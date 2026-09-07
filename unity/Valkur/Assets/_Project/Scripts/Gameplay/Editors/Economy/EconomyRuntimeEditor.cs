using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Economy;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay.Editors.Economy
{
    /// <summary>
    /// Runtime Economy Editor, opened from the General Editor (ESC → Economy).
    ///
    /// <para>WHAT IT IS FOR. The economy's numbers are scattered across four kinds of home —
    /// constants in <c>MarketCycle</c> and <c>DeathDropSystem</c>, fields on five
    /// <c>VendorConfigDefinition</c> assets, margins on five <c>EconomyGroupDefinition</c>
    /// assets, and per-run state inside <c>MarketService</c> — and every one of them answers a
    /// question that can only be settled by PLAYING: is a 25 % swing something the player
    /// notices, is 400 coins a purse that ever runs out, is four coins a kill too mean. The
    /// stop-edit-recompile-walk-back loop destroys the only evidence that could answer them.
    /// Same argument the Camera editor makes about feel and the Skills editor about levels.</para>
    ///
    /// <para>WHAT IT EDITS, AND WHERE THAT LANDS. Three different lifetimes, and the panel says
    /// which is which because confusing them is how an author loses work:
    /// <list type="bullet">
    /// <item><b>Tuning</b> — <c>EconomyTuning</c> under <c>Resources/</c>. Design-time, shared
    /// by every save. Written to disk by SAVE.</item>
    /// <item><b>Vendors</b> — the shipped <c>VendorConfigDefinition</c> and
    /// <c>EconomyGroupDefinition</c> assets, live. Also written by SAVE.</item>
    /// <item><b>Market state</b> — the seed and the day, which live in the SAVE FILE, not in
    /// any asset. Changing them changes this run and nothing else.</item>
    /// </list></para>
    ///
    /// <para>THE BITCOIN PANEL IS A VIEWER, and the one button that reaches gameplay makes the
    /// architecture visible rather than hiding it: "sembrar" reads the latest close ONCE, turns
    /// it into a seed and writes it to the save. Nothing re-reads the feed while playing. That
    /// is the only shape in which a real-world index is safe here — a live price would make the
    /// world's prices client-authoritative (a proxy or a system clock moves them), impossible
    /// to reproduce from a bug report, and absent offline. The feed is OFF until the author
    /// switches it on, because a game reaching the network unasked is a game phoning home.</para>
    ///
    /// <para>NO HOTKEY, deliberately: the F-row is retired project-wide and the General Editor
    /// is the only way in, which is what <c>EditorReachabilityTests</c> pins — an editor with
    /// no menu entry cannot be opened at all and nothing throws to say so.</para>
    ///
    /// <para>IT DECLARES NO INPUT ACTIONS OF ITS OWN, so there is no <c>OwnerEditor</c>
    /// anywhere in this folder. Undo, redo, save and close are all in the shared
    /// <c>EditorShared</c> map and read through <see cref="EditorInput"/>. That sidesteps the
    /// trap this project has already paid for once, where an <c>OwnerEditor</c> spelled as the
    /// map slug rather than the exact <c>EditorName</c> silently killed all 35 tools of an
    /// editor.</para>
    /// </summary>
    public sealed partial class EconomyRuntimeEditor : SingletonMonoBehaviour<EconomyRuntimeEditor>,
        GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        /// <summary>
        /// How many edits the history keeps. Matches the Tile and Skills editors — deep enough
        /// that a tuning pass is fully reversible, bounded so a long session cannot grow it
        /// without limit.
        /// </summary>
        private const int HISTORY_DEPTH = 50;

        /// <summary>The tabs down the left panel. Each is a different LIFETIME, not just a group.</summary>
        internal enum Tab
        {
            Market = 0,
            Faucet = 1,
            Vendors = 2,
            Bitcoin = 3,
        }

        private bool _active;
        private bool _uiBuilt;

        private Canvas _canvas;
        private GameObject _root;

        private EconomyTuning _tuning;
        private readonly List<VendorConfigDefinition> _vendors = new List<VendorConfigDefinition>();
        private int _selectedVendor;

        private Tab _tab = Tab.Market;
        private BitcoinTimeframe _timeframe = BitcoinTimeframe.Day1;

        private bool _applyingHistory;

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

        public string EditorName => "Economy";
        public bool IsActive => _active;

        internal bool CanUndo => _undo.Count > 0;
        internal bool CanRedo => _redo.Count > 0;
        internal Tab ActiveTab => _tab;
        internal BitcoinTimeframe ActiveTimeframe => _timeframe;
        internal EconomyTuning Tuning => _tuning;

        /// <summary>The vendor config currently being edited, or null when none loaded.</summary>
        internal VendorConfigDefinition SelectedVendor =>
            _selectedVendor >= 0 && _selectedVendor < _vendors.Count ? _vendors[_selectedVendor] : null;

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
            ResolveContent();

            if (!_uiBuilt)
            {
                // Wrapped because a BuildUI that throws half way leaves an editor registered,
                // inactive and impossible to open again, with the exception the only clue. The
                // Camera and Skills editors carry the same guard for the same reason.
                try { BuildUI(); _uiBuilt = true; }
                catch (Exception ex)
                {
                    Debug.LogError($"[EconomyEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
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
        /// The shared editor verbs, read through <see cref="EditorInput"/> rather than raw keys
        /// so a rebind moves them and this editor cannot drift onto a different Undo from the
        /// other seventeen.
        /// </summary>
        private void Update()
        {
            if (!_active) return;

            if (EditorInput.UndoPressed()) UndoLast();
            else if (EditorInput.RedoPressed()) RedoLast();
            else if (EditorInput.SavePressed()) SaveAll();
            else TickBitcoinPanel();
        }

        /// <summary>
        /// Resolve the tuning asset and the vendor configs.
        ///
        /// <para>The tuning is resolved through <see cref="EconomyTuning.Active"/>, which hands
        /// back a throwaway instance carrying the shipped defaults when no asset exists — so
        /// the editor OPENS in a project that has never run the seeder, shows the real numbers,
        /// and says on its status line that they are not yet backed by a file. Refusing to
        /// open would be the worse failure: an editor that cannot be seen cannot explain what
        /// is missing.</para>
        /// </summary>
        private void ResolveContent()
        {
            _tuning = EconomyTuning.Active;

            _vendors.Clear();
#if UNITY_EDITOR
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:VendorConfigDefinition"))
            {
                var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<VendorConfigDefinition>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (cfg != null) _vendors.Add(cfg);
            }
            _vendors.Sort((a, b) => string.CompareOrdinal(a.vendorKey, b.vendorKey));
#else
            // In a build there is no asset database, so the only vendors reachable are the
            // ones a live VendorNPC is holding. That is fewer than the catalogue and it is the
            // honest answer: those are the configs a player's session actually has in memory.
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<VendorNPC>())
            {
                var cfg = npc != null ? npc.VendorConfig : null;
                if (cfg != null && !_vendors.Contains(cfg)) _vendors.Add(cfg);
            }
#endif
            ApplyPendingVendorSelection();
            if (_selectedVendor >= _vendors.Count) _selectedVendor = 0;
        }

        // ── History ─────────────────────────────────────────────────────────

        /// <summary>
        /// Record one reversible edit and apply it.
        ///
        /// <para>Applying THROUGH the history rather than beside it is what stops the two
        /// disagreeing: there is no path that changes a value without a matching inverse,
        /// because the change itself is the <paramref name="redo"/> closure. Copied from the
        /// Skills editor deliberately — a second history implementation is a second set of
        /// off-by-one bugs.</para>
        /// </summary>
        private void Commit(string label, Action undo, Action redo)
        {
            redo();

            if (_applyingHistory) return;

            _undo.Add(new EditStep(label, undo, redo));
            if (_undo.Count > HISTORY_DEPTH) _undo.RemoveAt(0);
            _redo.Clear();

            MarkTuningDirty();
            RefreshAfterEdit();
            SetStatus($"{label}");
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
            MarkTuningDirty();
            RefreshAll();
            SetStatus($"Deshecho: {step.Label}");
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
            MarkTuningDirty();
            RefreshAll();
            SetStatus($"Rehecho: {step.Label}");
        }

        /// <summary>
        /// Drops <see cref="EconomyTuning"/>'s cache so the live systems re-resolve.
        ///
        /// <para>Without it a change to the amplitude or the coin divisors would be visible in
        /// the panel and invisible in the world, because <c>MarketService.Shape</c> and
        /// <c>DeathDropSystem.ComputeCoinReward</c> both read through the cached instance. That
        /// is the exact "the panel reports a change it cannot make" defect this project has
        /// shipped before with the stance chips.</para>
        /// </summary>
        private void MarkTuningDirty()
        {
            if (_tuning == null) return;
#if UNITY_EDITOR
            // Marked dirty for EVERY commit, including the ones that edit a vendor or the
            // save's seed rather than the tuning. That is deliberate over-marking: SetDirty on
            // an unchanged asset costs a redundant write at SAVE and nothing else, while
            // tracking which field belongs to which asset would be a second model of the
            // panel's layout that could disagree with the first.
            UnityEditor.EditorUtility.SetDirty(_tuning);
#endif
            // The instance itself is unchanged, so the cache does NOT need dropping — Active
            // hands back this very object. Dropping it would reload from disk and discard the
            // unsaved edit the author is looking at.
            MarketService.Instance?.NotifyTuningChanged();
        }

        // ── Save ────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes every edited asset to disk.
        ///
        /// <para>In a build there is no asset database, and the status line SAYS SO rather than
        /// a button silently lying — same contract the Skills editor states. Everything the
        /// author changed is still live in memory for the session; it just will not survive a
        /// restart, which is the truth and is worth being told.</para>
        /// </summary>
        internal void SaveAll()
        {
#if UNITY_EDITOR
            if (_tuning != null && !string.IsNullOrEmpty(
                    UnityEditor.AssetDatabase.GetAssetPath(_tuning)))
                UnityEditor.EditorUtility.SetDirty(_tuning);

            for (int i = 0; i < _vendors.Count; i++)
                if (_vendors[i] != null) UnityEditor.EditorUtility.SetDirty(_vendors[i]);

            UnityEditor.AssetDatabase.SaveAssets();
            SetStatus("Guardado en disco.");
#else
            SetStatus("En una build no hay AssetDatabase: los cambios viven solo esta sesion.");
#endif
        }

        /// <summary>
        /// True when the tuning is a throwaway instance rather than a shipped asset — i.e. the
        /// project has never run <c>Valkur > Economy > Seed Economy Content</c>. Surfaced on
        /// the status line so an author does not tune for ten minutes into nothing.
        /// </summary>
        internal bool TuningIsUnbacked
        {
            get
            {
#if UNITY_EDITOR
                return _tuning == null ||
                       string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(_tuning));
#else
                return _tuning == null;
#endif
            }
        }
    }
}
