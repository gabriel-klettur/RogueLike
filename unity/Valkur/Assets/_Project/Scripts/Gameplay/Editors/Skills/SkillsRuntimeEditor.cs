using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;

namespace Valkur.Gameplay.Editors.Skills
{
    /// <summary>
    /// Runtime Skills Editor, opened from the General Editor (ESC → Skills).
    ///
    /// <para>WHAT IT IS FOR. Every trade in the game — cooking, blacksmithing, mining,
    /// woodcutting and the generic crafting bucket — is configured here: the profession's own
    /// level curve and station vocabulary, and every recipe's requirements. Before this the
    /// only way to change any of it was to edit a Python table, re-run a generator and
    /// re-import, which is the right loop for a bulk retune of 36 recipes and the wrong one for
    /// the question this editor exists to answer: "is level 5 too high for this?" That is a
    /// judgement about play, and the stop-edit-play-walk-back loop destroys the only thing that
    /// can settle it. Same argument the Camera editor makes about feel.</para>
    ///
    /// <para>IT EDITS THE LIVE ASSETS, IMMEDIATELY, AND IS UNDOABLE. A change is visible in the
    /// crafting panel on the next open with no apply step, because a tuning surface with an
    /// apply button makes the author guess whether they are looking at what they just typed.
    /// That is only safe BECAUSE of the history below: live editing without an undo means a
    /// mistyped level is unrecoverable except by remembering the old number. SAVE writes the
    /// ScriptableObjects to disk; in a build there is no asset database and the status line
    /// says so rather than a button silently lying.</para>
    ///
    /// <para>WHY IT DOES NOT DUPLICATE THE GENERATOR. The Python side owns BALANCE — the
    /// derivation from one ingredient price table that keeps 36 dishes priced consistently.
    /// This owns EXCEPTIONS: the single recipe that should need a station, the one trade whose
    /// curve is too steep. A re-import rewrites the derived numbers and would discard those, so
    /// the two are not interchangeable and the editor says which fields the generator will
    /// reclaim — see <c>CraftingContentImporter</c> on the derived/prose split.</para>
    ///
    /// <para>NO HOTKEY, deliberately. The F-row was retired project-wide and the General Editor
    /// is the only way in, which is what <c>EditorReachabilityTests</c> pins: an editor with no
    /// menu entry cannot be opened at all and nothing throws to say so.</para>
    ///
    /// <para>IT DECLARES NO INPUT ACTIONS OF ITS OWN, which is why there is no
    /// <c>OwnerEditor</c> anywhere in this folder. Every gesture it needs — undo, redo, save,
    /// close — is in the shared <c>EditorShared</c> map and read through <see cref="EditorInput"/>.
    /// That sidesteps the trap this project has already paid for once, where an
    /// <c>OwnerEditor</c> spelled as the map slug rather than the exact <c>EditorName</c>
    /// silently killed all 35 tools of an editor.</para>
    /// </summary>
    public sealed partial class SkillsRuntimeEditor : SingletonMonoBehaviour<SkillsRuntimeEditor>,
        GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        /// <summary>
        /// How many edits the history keeps. Matches the Tile editor's depth — deep enough
        /// that a tuning pass is fully reversible, bounded so a long session cannot grow it
        /// without limit.
        /// </summary>
        private const int HISTORY_DEPTH = 50;

        private bool _active;
        private bool _uiBuilt;

        private Canvas _canvas;
        private GameObject _root;
        private RecipeCatalog _catalog;

        private int _selectedProfession;
        private string _selectedRecipeId;
        private string _search = string.Empty;

        /// <summary>
        /// One reversible edit. Two closures rather than a value snapshot, because the fields
        /// edited here live on two different asset types and a generic snapshot would have to
        /// reflect over both.
        /// </summary>
        private readonly struct EditStep
        {
            public readonly string Label;
            public readonly Action Undo;
            public readonly Action Redo;

            public EditStep(string label, Action undo, Action redo)
            {
                Label = label;
                Undo = undo;
                Redo = redo;
            }
        }

        private readonly List<EditStep> _undo = new List<EditStep>(HISTORY_DEPTH);
        private readonly List<EditStep> _redo = new List<EditStep>(HISTORY_DEPTH);

        /// <summary>
        /// True while an undo or redo is being applied, so the setters it drives do not record
        /// themselves as new history. Without it, undoing pushes the inverse edit onto the undo
        /// stack and the author can never get further back than one step.
        /// </summary>
        private bool _applyingHistory;

        public string EditorName => "Skills";
        public bool IsActive => _active;

        internal bool CanUndo => _undo.Count > 0;
        internal bool CanRedo => _redo.Count > 0;

        /// <summary>The trade currently being edited, or null before the catalog resolves.</summary>
        internal ProfessionDefinition SelectedProfession =>
            _catalog != null && _selectedProfession >= 0
            && _selectedProfession < _catalog.Professions.Count
                ? _catalog.Professions[_selectedProfession]
                : null;

        /// <summary>The recipe currently selected in the list, or null.</summary>
        internal RecipeDefinition SelectedRecipe =>
            _catalog != null && !string.IsNullOrEmpty(_selectedRecipeId)
                ? _catalog.GetById(_selectedRecipeId)
                : null;

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        public void Activate()
        {
            if (!ResolveCatalog())
            {
                Debug.LogWarning("[SkillsEditor] No RecipeCatalog at Resources/" +
                                 RecipeCatalog.ResourcePath + " — run " +
                                 "Valkur > Crafting > Import Crafting Content.");
                return;
            }

            if (!_uiBuilt)
            {
                // Wrapped because a BuildUI that throws half way leaves an editor registered,
                // inactive and impossible to open again, with the exception the only clue. The
                // Camera editor carries the same guard for the same reason.
                try { BuildUI(); _uiBuilt = true; }
                catch (Exception ex)
                {
                    Debug.LogError($"[SkillsEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }

            _active = true;
            if (_root != null) _root.SetActive(true);
            ForcePanelsOpen();
            RefreshAll();
            SetStatus("Editando en vivo. Ctrl+Z deshace, GUARDAR escribe los assets.");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        /// <summary>
        /// The shared editor verbs. Read through <see cref="EditorInput"/> rather than a raw
        /// key so a rebind moves them, and so this editor cannot drift onto a different Undo
        /// from the other sixteen.
        /// </summary>
        private void Update()
        {
            if (!_active) return;

            if (EditorInput.UndoPressed()) UndoLast();
            else if (EditorInput.RedoPressed()) RedoLast();
            else if (EditorInput.SavePressed()) SaveAll();
        }

        private bool ResolveCatalog()
        {
            if (_catalog != null) return true;
            _catalog = Resources.Load<RecipeCatalog>(RecipeCatalog.ResourcePath);
            return _catalog != null;
        }

        // ── History ─────────────────────────────────────────────────────────

        /// <summary>
        /// Record one reversible edit and apply it.
        ///
        /// <para>Applying THROUGH the history rather than beside it is what stops the two
        /// disagreeing: there is no path that changes an asset without a matching inverse,
        /// because the change itself is the <paramref name="redo"/> closure.</para>
        /// </summary>
        private void Commit(string label, Action undo, Action redo)
        {
            redo();

            if (_applyingHistory) return;

            _undo.Add(new EditStep(label, undo, redo));
            if (_undo.Count > HISTORY_DEPTH) _undo.RemoveAt(0);

            // A new edit invalidates the redo branch — keeping it would let the author redo
            // their way onto a value the current state never passed through.
            _redo.Clear();

            RefreshAfterEdit();
        }

        internal void UndoLast()
        {
            if (_undo.Count == 0) { SetStatus("Nada que deshacer."); return; }

            var step = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            _applyingHistory = true;
            try { step.Undo(); }
            finally { _applyingHistory = false; }

            _redo.Add(step);
            // The WHOLE list, not just the selected row: a history step can target a recipe the
            // author has since navigated away from, and refreshing only the selection would
            // leave that row showing the value it had before the undo.
            RefreshRecipeList();
            RefreshAfterEdit();
            SetStatus("Deshecho: " + step.Label);
        }

        internal void RedoLast()
        {
            if (_redo.Count == 0) { SetStatus("Nada que rehacer."); return; }

            var step = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);

            _applyingHistory = true;
            try { step.Redo(); }
            finally { _applyingHistory = false; }

            _undo.Add(step);
            RefreshRecipeList();
            RefreshAfterEdit();
            SetStatus("Rehecho: " + step.Label);
        }

        // ── Editing ─────────────────────────────────────────────────────────
        //
        // Every setter goes through Commit, so every one of them is undoable and none can
        // change an asset without recording how to put it back. They deliberately do NOT
        // rebuild the detail panel: AddCommit fires on focus loss as well as Enter, so a
        // rebuild would destroy the very field the author just tabbed INTO. RefreshAfterEdit
        // updates the derived readouts and re-syncs the field texts in place instead.

        internal void SetProfessionMaxLevel(ProfessionDefinition p, int value)
        {
            if (p == null) return;
            int next = Mathf.Max(1, value), prev = p.maxLevel;
            if (next == prev) return;
            Commit($"nivel maximo de {p.displayName}",
                () => { p.maxLevel = prev; MarkDirty(p); },
                () => { p.maxLevel = next; MarkDirty(p); });
        }

        internal void SetProfessionBaseXp(ProfessionDefinition p, int value)
        {
            if (p == null) return;
            int next = Mathf.Max(1, value), prev = p.baseXpPerLevel;
            if (next == prev) return;
            Commit($"XP base de {p.displayName}",
                () => { p.baseXpPerLevel = prev; MarkDirty(p); },
                () => { p.baseXpPerLevel = next; MarkDirty(p); });
        }

        internal void SetProfessionGrowth(ProfessionDefinition p, float value)
        {
            if (p == null) return;
            // Below 1 the curve INVERTS — level 10 would cost less than level 2 — which is not
            // a tuning anybody wants and is easy to type by accident.
            float next = Mathf.Max(1f, value), prev = p.xpGrowth;
            if (Mathf.Approximately(next, prev)) return;
            Commit($"crecimiento de {p.displayName}",
                () => { p.xpGrowth = prev; MarkDirty(p); },
                () => { p.xpGrowth = next; MarkDirty(p); });
        }

        internal void SetProfessionStationName(ProfessionDefinition p, string value)
        {
            if (p == null) return;
            string next = value ?? string.Empty, prev = p.stationName ?? string.Empty;
            if (next == prev) return;
            Commit($"estacion de {p.displayName}",
                () => { p.stationName = prev; MarkDirty(p); },
                () => { p.stationName = next; MarkDirty(p); });
        }

        internal void SetRecipeRequiredLevel(RecipeDefinition r, int value)
        {
            if (r == null) return;
            // Clamped to the trade's own cap, because a recipe requiring a level the profession
            // can never reach is unreachable content that looks perfectly valid in the
            // Inspector — the authored-and-inert shape this project has shipped a dozen times.
            int cap = r.profession != null ? Mathf.Max(1, r.profession.maxLevel) : 1;
            int next = Mathf.Clamp(value, 1, cap), prev = r.requiredLevel;
            if (next == prev) return;
            Commit($"nivel de {r.displayName}",
                () => { r.requiredLevel = prev; MarkDirty(r); },
                () => { r.requiredLevel = next; MarkDirty(r); });
        }

        internal void SetRecipeXpReward(RecipeDefinition r, int value)
        {
            if (r == null) return;
            int next = Mathf.Max(0, value), prev = r.xpReward;
            if (next == prev) return;
            Commit($"XP de {r.displayName}",
                () => { r.xpReward = prev; MarkDirty(r); },
                () => { r.xpReward = next; MarkDirty(r); });
        }

        internal void SetRecipeRequiresStation(RecipeDefinition r, bool value)
        {
            if (r == null || r.requiresStation == value) return;
            bool prev = r.requiresStation;
            Commit($"estacion de {r.displayName}",
                () => { r.requiresStation = prev; MarkDirty(r); },
                () => { r.requiresStation = value; MarkDirty(r); });
        }

        internal void SetRecipeCraftSeconds(RecipeDefinition r, float value)
        {
            if (r == null) return;
            float next = Mathf.Max(0f, value), prev = r.craftSeconds;
            if (Mathf.Approximately(next, prev)) return;
            Commit($"duracion de {r.displayName}",
                () => { r.craftSeconds = prev; MarkDirty(r); },
                () => { r.craftSeconds = next; MarkDirty(r); });
        }

        internal void SelectRecipe(string recipeId)
        {
            _selectedRecipeId = recipeId;
            RefreshRecipeList();
            RefreshDetail();
            RevealSelectedRecipe();
        }

        internal void SelectProfession(int index)
        {
            if (index == _selectedProfession) return;
            _selectedProfession = index;
            // Cleared rather than kept: a recipe id from the previous trade would leave the
            // detail panel showing something that is not in the list beside it.
            _selectedRecipeId = null;
            RefreshAll();
        }

        internal void SetSearch(string text)
        {
            _search = text ?? string.Empty;
            RefreshRecipeList();
        }

        /// <summary>
        /// Mark an edited asset dirty so Unity persists it on the next save.
        ///
        /// <para><c>SetDirty</c> and never <c>Undo.RecordObject</c>: this editor writes many
        /// assets in a session and the global undo stack is what reverted 193 building
        /// templates to their creation state the first time anything popped it. Its own
        /// history above is what makes that safe — the edits are reversible HERE, without
        /// putting them somewhere the EditMode suite can pop.</para>
        /// </summary>
        private static void MarkDirty(UnityEngine.Object asset)
        {
#if UNITY_EDITOR
            if (asset != null) UnityEditor.EditorUtility.SetDirty(asset);
#endif
        }

        /// <summary>
        /// Persist every edited asset.
        ///
        /// <para>In a build there is no asset database, and the live edit still applies for the
        /// session — saying so beats a button that silently lies. Same split the Camera editor
        /// makes.</para>
        /// </summary>
        internal void SaveAll()
        {
#if UNITY_EDITOR
            if (_catalog != null) UnityEditor.EditorUtility.SetDirty(_catalog);
            UnityEditor.AssetDatabase.SaveAssets();
            SetStatus("Guardado en los assets de oficios y recetas.");
#else
            SetStatus("Aplicado para esta sesion. Guardar requiere el Editor.");
#endif
        }

        /// <summary>
        /// Recipes of the selected trade, filtered by the search box, grouped by their
        /// <c>group</c> tag and ordered inside each group.
        ///
        /// <para>Unlike the player's panel, this deliberately includes MALFORMED recipes. That
        /// panel hides them because a broken row sends the player looking for an ingredient
        /// that does not exist; this is the one screen where somebody can actually see the
        /// breakage and fix it, so hiding it here would make the defect invisible everywhere.</para>
        /// </summary>
        internal List<RecipeDefinition> RecipesForSelected()
        {
            var result = new List<RecipeDefinition>();
            var profession = SelectedProfession;
            if (_catalog == null || profession == null) return result;

            for (int i = 0; i < _catalog.Recipes.Count; i++)
            {
                var r = _catalog.Recipes[i];
                if (r == null || r.profession != profession) continue;
                if (!MatchesSearch(r)) continue;
                result.Add(r);
            }

            // Grouped first, then by name inside the group. A flat alphabetical list mixes six
            // cuisines together, which is the one ordering that makes a 36-row list hard to
            // scan — the data already carries the grouping.
            result.Sort((a, b) =>
            {
                int g = string.Compare(a.group ?? "", b.group ?? "",
                    StringComparison.OrdinalIgnoreCase);
                return g != 0 ? g : string.Compare(a.displayName ?? "", b.displayName ?? "",
                    StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>
        /// Matches the recipe's NAME, its id and its group, so an author can find "paella" by
        /// typing it and every Spanish dish by typing "spain".
        /// </summary>
        private bool MatchesSearch(RecipeDefinition r)
        {
            if (string.IsNullOrWhiteSpace(_search)) return true;
            string q = _search.Trim();
            return Contains(r.displayName, q) || Contains(r.recipeId, q) || Contains(r.group, q);
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack)
            && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        // Built in the UI partial.
        private partial void BuildUI();
        private partial void RefreshAll();
        private partial void RefreshRecipeList();
        private partial void RefreshDetail();
        private partial void RefreshAfterEdit();
        private partial void RevealSelectedRecipe();
        private partial void ForcePanelsOpen();
        private partial void SetStatus(string message);
    }
}
