using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Skills
{
    public partial class SkillsRuntimeEditor
    {
        private const float LIST_W = 360f;
        private const float DETAIL_W = 400f;
        private const float PANEL_H = 620f;

        /// <summary>
        /// Positive on purpose. <c>ApplyPanelDock</c> NEGATES the vertical offset it is given,
        /// so passing a negative here docks the panel above the canvas and takes its header off
        /// screen — which is exactly what shipped in the Controls editor and left two panels
        /// unreadable. Every other editor passes a positive gap.
        /// </summary>
        private const float PANEL_TOP = TileEditorUIHelpers.PANEL_TOP_OFFSET;

        private const float ROW_H = 24f;

        private Transform _listContent;
        private Transform _detailContent;
        private Transform _detailBody;
        private RectTransform _recipeRows;
        private ScrollRect _recipeScroll;
        private TextMeshProUGUI _status;
        private GameObject _tutorial;

        private DraggablePanel _listPanel;
        private DraggablePanel _detailPanel;

        private readonly List<GameObject> _rowObjects = new List<GameObject>();
        private readonly List<GameObject> _detailObjects = new List<GameObject>();
        private readonly List<Button> _professionButtons = new List<Button>();

        /// <summary>Recipe id -> its row button, so a selection repaint costs no rebuild.</summary>
        private readonly Dictionary<string, Button> _rowByRecipe =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Order the rows are drawn in, used to scroll the selection into view.</summary>
        private readonly List<string> _rowOrder = new List<string>();

        /// <summary>
        /// One per input field: re-reads the model and writes it back into the box WITHOUT
        /// notifying.
        ///
        /// <para>This is what replaced rebuilding the detail panel on every commit.
        /// <c>UIInputField.AddCommit</c> fires on focus loss as well as Enter, so a rebuild
        /// destroyed the field the author had just tabbed or clicked INTO — the second field
        /// vanished under the cursor. Re-syncing in place also shows a CLAMPED value
        /// immediately: type 999 into a level the trade caps at 20 and the box snaps to 20
        /// rather than keeping a number the asset never took.</para>
        /// </summary>
        private readonly List<Action> _fieldResync = new List<Action>();

        private partial void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("SkillsEditorCanvas", 114);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            EditorUIHelpers.MakeDropPanel(
                "SkillsListPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopLeft, 16f, PANEL_TOP, LIST_W, PANEL_H,
                "Oficios y recetas", out _listContent, out _listPanel);

            EditorUIHelpers.MakeDropPanel(
                "SkillsDetailPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopRight, 16f, PANEL_TOP, DETAIL_W, PANEL_H,
                "Configuracion", out _detailContent, out _detailPanel);

            // NEITHER PANEL MAY BE CLOSED, and this is the whole reason DraggablePanel exposes
            // the flag. Both ship closable by default, this editor has no toolbar outside them,
            // and EditorWorkspaceService.CollectPanels persists `open:false` for INACTIVE
            // panels — so closing both would make the editor open empty forever, with no way
            // back. That is not hypothetical: it is exactly what shipped in the Controls
            // editor and had to be undone there.
            _listPanel.ShowCloseButton = false;
            _detailPanel.ShowCloseButton = false;

            BuildListPanel();
            BuildDetailPanel();
            BuildTutorial();
        }

        /// <summary>
        /// Heal a workspace document written before the close buttons were removed. A panel
        /// persisted as closed stays closed on restore, so an author who hit the X once would
        /// go on opening an empty editor even after the fix.
        /// </summary>
        private partial void ForcePanelsOpen()
        {
            Reopen(_listPanel);
            Reopen(_detailPanel);
        }

        /// <summary>
        /// Both halves are needed and they are different statements: re-activating the
        /// GameObject puts the panel back on screen, and <c>MarkOpened</c> clears the
        /// REMEMBERED closed flag so the workspace does not close it again on the next
        /// restore. Same pair the Controls editor uses for the same reason.
        /// </summary>
        private static void Reopen(DraggablePanel panel)
        {
            if (panel == null) return;
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            panel.MarkOpened();
        }

        // ── Left panel ──────────────────────────────────────────────────────

        private void BuildListPanel()
        {
            EditorUIHelpers.BuildSectionHeader(_listContent, "Oficio");

            var strip = EditorUIHelpers.CreateUI("ProfessionStrip", _listContent);
            var stripLayout = strip.AddComponent<VerticalLayoutGroup>();
            stripLayout.spacing = 2f;
            stripLayout.childControlHeight = true;
            stripLayout.childForceExpandHeight = false;
            strip.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // One button per profession, built from the CATALOG. That is the point of
            // professions being assets: a new trade is a file plus a catalog row, and this
            // strip grows without a code change here.
            _professionButtons.Clear();
            for (int i = 0; i < _catalog.Professions.Count; i++)
            {
                var profession = _catalog.Professions[i];
                if (profession == null) continue;
                int index = i;
                string label = string.IsNullOrEmpty(profession.displayName)
                    ? profession.professionKey
                    : profession.displayName;
                _professionButtons.Add(EditorUIHelpers.MakeButton(
                    strip.transform, label, () => SelectProfession(index), ROW_H + 2f, 12f));
            }

            EditorUIHelpers.BuildSeparator(_listContent);
            EditorUIHelpers.BuildSectionHeader(_listContent, "Recetas");

            // 36 rows in one scroll is exactly the size at which a filter stops being a luxury.
            var search = SearchBox.Create(_listContent, "Buscar receta o cocina...", SetSearch, 26f);

            // A restored filter has to be VISIBLE in the box. Without this the workspace would
            // reopen the editor with the list already filtered and the field blank, so the
            // author sees recipes missing with nothing on screen explaining why — worse than
            // not restoring it at all. SetTextWithoutNotify because the value is already in
            // _search and notifying would re-enter SetSearch mid-build.
            if (!string.IsNullOrEmpty(_search)) search.SetTextWithoutNotify(_search);

            var (scroll, content) = EditorUIHelpers.MakeScrollView(_listContent, "RecipeScroll");
            var scrollLayout = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLayout.minHeight = 120f;
            // flexibleHeight is what makes the list absorb the panel's spare space instead of a
            // hardcoded number that would leave a gap or overflow when the panel is resized.
            scrollLayout.flexibleHeight = 1f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            _recipeScroll = scroll;
            _recipeRows = content;

            var group = content.GetComponent<VerticalLayoutGroup>();
            if (group != null)
            {
                group.spacing = 2f;
                group.padding = new RectOffset(2, 2, 2, 2);
                group.childControlHeight = true;
                group.childForceExpandHeight = false;
            }

            _status = EditorUIHelpers.MakeStatusText(_listContent);
            // Two lines' worth, reserved. The status carries sentences ("Deshecho: nivel de
            // Paella"), and at one line's height a two-line message is clipped mid-word.
            var statusLayout = _status.gameObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 34f;
            statusLayout.minHeight = 34f;
            statusLayout.flexibleHeight = 0f;
        }

        // ── Right panel ─────────────────────────────────────────────────────

        private void BuildDetailPanel()
        {
            // A compact toolbar rather than one full-width SAVE. The old layout gave a single
            // button the entire panel width and then left ~250 px of dead space below the
            // fields; three verbs in one row is the shape every other editor's toolbar uses.
            var bar = EditorUIHelpers.CreateUI("DetailToolbar", _detailContent);
            var barLayout = bar.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 4f;
            barLayout.childControlWidth = true;
            barLayout.childForceExpandWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandHeight = true;
            var barElement = bar.AddComponent<LayoutElement>();
            barElement.preferredHeight = 28f;
            barElement.minHeight = 28f;
            barElement.flexibleHeight = 0f;

            EditorUIHelpers.MakeButton(bar.transform, "GUARDAR", SaveAll, 28f, 11f);
            _undoButton = EditorUIHelpers.MakeButton(bar.transform, "DESHACER", UndoLast, 28f, 11f);
            _redoButton = EditorUIHelpers.MakeButton(bar.transform, "REHACER", RedoLast, 28f, 11f);

            // The overlay is built hidden, so WITHOUT this button it is unreachable — an
            // authored-and-inert surface of exactly the kind this project has shipped a dozen
            // times. It is narrow because it is a modifier on the panel, not a fourth verb.
            _helpButton = EditorUIHelpers.MakeButton(bar.transform, "?", ToggleTutorial, 28f, 12f);
            var helpElement = _helpButton.gameObject.AddComponent<LayoutElement>();
            helpElement.preferredWidth = 30f;
            helpElement.minWidth = 30f;
            helpElement.flexibleWidth = 0f;

            EditorUIHelpers.BuildSeparator(_detailContent);

            // The body SCROLLS. Without it a recipe with a long ingredient line pushes the
            // last rows past the panel edge and they are simply unreachable — invisible in
            // EditMode, where uGUI runs no layout at all.
            var (scroll, content) = EditorUIHelpers.MakeScrollView(_detailContent, "DetailScroll");
            var scrollLayout = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLayout.minHeight = 120f;
            scrollLayout.flexibleHeight = 1f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);

            var group = content.GetComponent<VerticalLayoutGroup>();
            if (group != null)
            {
                group.spacing = 3f;
                group.padding = new RectOffset(4, 4, 4, 4);
                group.childControlHeight = true;
                group.childForceExpandHeight = false;
            }
            _detailBody = content;
        }

        private Button _undoButton;
        private Button _redoButton;
        private Button _helpButton;

        /// <summary>
        /// Show or hide the shortcut overlay.
        ///
        /// <para>The button is TINTED to its state rather than relabelled: "?" is what the
        /// author looks for whether the panel is open or shut, and a caption that flips to "X"
        /// makes them hunt for it the second time.</para>
        /// </summary>
        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            bool show = !_tutorial.activeSelf;
            _tutorial.SetActive(show);
            UIButton.SetTint(_helpButton, show ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
        }

        private void BuildTutorial()
        {
            // Every verb here is a SHARED editor binding, so the overlay names the same keys
            // the other sixteen editors do rather than inventing a vocabulary for this one.
            _tutorial = TutorialOverlay.Build(_root.transform, "SKILLS", new[]
            {
                ("Ctrl+Z", "Deshacer el ultimo cambio"),
                ("Ctrl+Y", "Rehacer"),
                ("Ctrl+S", "Guardar los assets"),
                ("Esc",    "Cerrar el editor"),
                ("Buscar", "Filtra por receta o por cocina"),
                ("Aviso",  "Reimportar reescribe nivel, XP, segundos y estacion"),
            });
            _tutorial.SetActive(false);
        }

        // ── Refresh ─────────────────────────────────────────────────────────

        private partial void RefreshAll()
        {
            RefreshProfessionHighlights();
            RefreshRecipeList();
            RefreshDetail();
        }

        /// <summary>
        /// After an EDIT, as opposed to a selection change: update what the edit could have
        /// moved and touch nothing else. Rebuilding here would destroy the focused field — see
        /// <see cref="_fieldResync"/>.
        /// </summary>
        private partial void RefreshAfterEdit()
        {
            for (int i = 0; i < _fieldResync.Count; i++) _fieldResync[i]?.Invoke();
            RefreshDerivedLabels();
            RefreshRowLabel(SelectedRecipe);
            RefreshHistoryButtons();
        }

        private void RefreshHistoryButtons()
        {
            // Greyed rather than hidden: a control that disappears when it is unavailable
            // teaches the author it does not exist.
            SetHistoryButtonState(_undoButton, CanUndo);
            SetHistoryButtonState(_redoButton, CanRedo);
        }

        /// <summary>
        /// `interactable` alone is NOT a visible state here, which is why the label is dimmed
        /// too. Measured off a rendered frame: with an empty history both buttons sat at
        /// (33,33,46) against an enabled GUARDAR at (34,34,46) — one unit apart, so "you cannot
        /// undo yet" and "press me" looked identical. Unity's default disabledColor multiplies
        /// into an already-dark button and effectively cancels out; the LABEL is the part with
        /// enough contrast left to carry the state.
        /// </summary>
        private static void SetHistoryButtonState(Button button, bool available)
        {
            if (button == null) return;
            button.interactable = available;

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.color = available ? UITheme.TEXT_PRIMARY : UITheme.TEXT_MUTED;
        }

        private void RefreshProfessionHighlights()
        {
            for (int i = 0; i < _professionButtons.Count; i++)
                UIButton.SetTint(_professionButtons[i],
                    i == _selectedProfession ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
        }

        private partial void RefreshRecipeList()
        {
            if (_recipeRows == null) return;
            ClearRows();
            _rowByRecipe.Clear();
            _rowOrder.Clear();

            var recipes = RecipesForSelected();
            if (recipes.Count == 0)
            {
                var empty = EditorUIHelpers.AddLabel(_recipeRows,
                    string.IsNullOrWhiteSpace(_search)
                        ? "Este oficio no tiene recetas todavia."
                        : $"Sin resultados para '{_search}'.", 11f);
                empty.color = UITheme.TEXT_MUTED;
                _rowObjects.Add(empty.gameObject);
                return;
            }

            string currentGroup = null;
            for (int i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];

                // A group heading every time the cuisine changes. The list is sorted by group,
                // so this splits 36 flat rows into six scannable blocks without a tree widget.
                string group = string.IsNullOrWhiteSpace(recipe.group) ? "otros" : recipe.group;
                if (!string.Equals(group, currentGroup, StringComparison.OrdinalIgnoreCase))
                {
                    currentGroup = group;
                    var heading = EditorUIHelpers.AddLabel(_recipeRows, group.ToUpperInvariant(), 9.5f);
                    heading.color = UITheme.ACCENT_DIM;
                    heading.fontStyle = FontStyles.Bold;
                    heading.characterSpacing = 1.5f;
                    _rowObjects.Add(heading.gameObject);
                }

                string id = recipe.recipeId;
                var btn = EditorUIHelpers.MakeButton(
                    _recipeRows, RowLabel(recipe), () => SelectRecipe(id), ROW_H, 11f);
                _rowObjects.Add(btn.gameObject);
                _rowByRecipe[id] = btn;
                _rowOrder.Add(id);
                PaintRow(recipe, btn);
            }
        }

        /// <summary>
        /// The row's own text. The station flag and the level live HERE, not only in the detail
        /// panel: the question this editor is opened to answer is usually "which of these needs
        /// a stove", and answering it by clicking thirty rows one at a time is the same as not
        /// answering it.
        /// </summary>
        private static string RowLabel(RecipeDefinition recipe)
        {
            string suffix = recipe.requiresStation ? "  ·estacion" : "";
            if (recipe.requiredLevel > 1) suffix += $"  ·nv{recipe.requiredLevel}";
            if (!recipe.IsWellFormed) suffix += "  ·ROTA";
            return recipe.displayName + suffix;
        }

        private void PaintRow(RecipeDefinition recipe, Button btn)
        {
            bool selected = string.Equals(recipe.recipeId, _selectedRecipeId,
                StringComparison.OrdinalIgnoreCase);

            // Through UIButton.SetTint, never targetGraphic.color — a Button's ColorTint
            // transition multiplies the graphic by its own ColorBlock, so a direct write
            // renders as the PRODUCT and the selection reads backwards. Measured on the first
            // build of this editor: the active row came out (32,31,29) against a (31,33,42)
            // panel, invisible, while untouched rows sat at near-black.
            UIButton.SetTint(btn,
                selected ? UITheme.SLOT_SELECTED
                : recipe.IsWellFormed ? UITheme.BTN_NORMAL
                : UITheme.DANGER_IDLE);
        }

        private void RefreshRowLabel(RecipeDefinition recipe)
        {
            if (recipe == null) return;
            if (!_rowByRecipe.TryGetValue(recipe.recipeId, out var btn) || btn == null) return;

            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = RowLabel(recipe);
            PaintRow(recipe, btn);
        }

        /// <summary>
        /// Scroll the selected row into view.
        ///
        /// <para>Without it, selecting a recipe from anywhere but the visible window — a search
        /// result, a restored workspace — leaves the list sitting where it was, so the panel on
        /// the right describes something the author cannot see highlighted on the left.</para>
        /// </summary>
        private partial void RevealSelectedRecipe()
        {
            if (_recipeScroll == null || string.IsNullOrEmpty(_selectedRecipeId)) return;

            int index = _rowOrder.IndexOf(_selectedRecipeId);
            if (index < 0 || _rowOrder.Count <= 1) return;

            float t = 1f - (index / (float)(_rowOrder.Count - 1));
            _recipeScroll.verticalNormalizedPosition = Mathf.Clamp01(t);
        }

        /// <summary>
        /// <c>Object.Destroy</c> is an ERROR in Edit Mode, not a warning — seven
        /// ControlsEditorTests went red on the log line alone with every assertion passing. Any
        /// editor path that tears down UI needs this branch.
        /// </summary>
        private static void DestroyUI(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rowObjects.Count; i++) DestroyUI(_rowObjects[i]);
            _rowObjects.Clear();
        }

        // ── Detail ──────────────────────────────────────────────────────────

        private TextMeshProUGUI _curveLabel;
        private TextMeshProUGUI _ingredientsLabel;
        private TextMeshProUGUI _producesLabel;
        private Button _stationToggle;

        private partial void RefreshDetail()
        {
            if (_detailBody == null) return;

            for (int i = 0; i < _detailObjects.Count; i++) DestroyUI(_detailObjects[i]);
            _detailObjects.Clear();
            _fieldResync.Clear();
            _curveLabel = _ingredientsLabel = _producesLabel = null;
            _stationToggle = null;

            var profession = SelectedProfession;
            if (profession == null) return;

            Header("Oficio: " + profession.displayName);
            IntField("Nivel maximo", () => profession.maxLevel,
                v => SetProfessionMaxLevel(profession, v));
            IntField("XP del nivel 1", () => profession.baseXpPerLevel,
                v => SetProfessionBaseXp(profession, v));
            FloatField("Crecimiento XP", () => profession.xpGrowth,
                v => SetProfessionGrowth(profession, v));
            TextField("Nombre de la estacion", () => profession.stationName,
                v => SetProfessionStationName(profession, v));

            // The resolved curve, not just its two inputs. "growth 1.25" is not a number
            // anybody can picture; "nivel 10 cuesta 596" is the thing being decided.
            _curveLabel = Note(CurveText(profession));

            EditorUIHelpers.BuildSeparator(_detailBody);

            var recipe = SelectedRecipe;
            if (recipe == null || recipe.profession != profession)
            {
                Note("Selecciona una receta de la lista para configurarla.");
                RefreshHistoryButtons();
                return;
            }

            Header("Receta: " + recipe.displayName);
            IntField("Nivel requerido", () => recipe.requiredLevel,
                v => SetRecipeRequiredLevel(recipe, v));
            IntField("XP por fabricacion", () => recipe.xpReward,
                v => SetRecipeXpReward(recipe, v));
            FloatField("Segundos", () => recipe.craftSeconds,
                v => SetRecipeCraftSeconds(recipe, v));

            // A LABELLED ROW, not a full-width centred button. The old shape read as a section
            // heading rather than a control, so the one toggle on the screen did not look
            // clickable at all.
            var row = MakeFieldRow("Requiere estacion");
            _stationToggle = EditorUIHelpers.MakeButton(row, StationToggleText(recipe),
                () => SetRecipeRequiresStation(recipe, !recipe.requiresStation), 22f, 11f);
            StretchControl(_stationToggle.gameObject);
            PaintStationToggle(recipe);

            _ingredientsLabel = Note("Ingredientes: " + DescribeIngredients(recipe));
            _producesLabel = Note("Produce: " + DescribeOutput(recipe));

            // Says out loud which of the fields above a re-import will take back. Without it an
            // author retunes a level, runs the importer for an unrelated reason and silently
            // loses the edit — the derived/prose split is real and invisible from in here.
            var warn = Note("Aviso: reimportar desde el manifiesto reescribe nivel, XP, " +
                            "segundos y estacion. Lo editado aqui son excepciones, no balance.");
            warn.color = UITheme.WARNING;

            RefreshHistoryButtons();
        }

        /// <summary>Update only what an edit can move, leaving every input field alive.</summary>
        private void RefreshDerivedLabels()
        {
            var profession = SelectedProfession;
            if (_curveLabel != null && profession != null) _curveLabel.text = CurveText(profession);

            var recipe = SelectedRecipe;
            if (recipe == null) return;
            if (_ingredientsLabel != null)
                _ingredientsLabel.text = "Ingredientes: " + DescribeIngredients(recipe);
            if (_producesLabel != null) _producesLabel.text = "Produce: " + DescribeOutput(recipe);
            if (_stationToggle != null)
            {
                var label = _stationToggle.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = StationToggleText(recipe);
                PaintStationToggle(recipe);
            }
        }

        private static string StationToggleText(RecipeDefinition r) => r.requiresStation ? "SI" : "NO";

        /// <summary>Lit for on, neutral for off — the state readable without reading the word.</summary>
        private void PaintStationToggle(RecipeDefinition r)
            => UIButton.SetTint(_stationToggle,
                r.requiresStation ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);

        private static string CurveText(ProfessionDefinition p) =>
            $"Curva: nv2 {p.XpForNextLevel(1)} · nv5 {p.XpForNextLevel(4)} · " +
            $"nv10 {p.XpForNextLevel(9)} · nv{p.maxLevel} {p.XpForNextLevel(p.maxLevel - 1)}";

        private static string DescribeOutput(RecipeDefinition recipe) =>
            recipe.output != null
                ? $"{recipe.outputQuantity}x {recipe.output.displayName}"
                : "NADA — receta rota";

        private static string DescribeIngredients(RecipeDefinition recipe)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                var line = recipe.ingredients[i];
                if (sb.Length > 0) sb.Append(", ");
                if (!line.IsValid) { sb.Append("<ROTO>"); continue; }
                sb.Append(line.quantity).Append("x ").Append(line.item.displayName);
            }
            return sb.Length == 0 ? "ninguno" : sb.ToString();
        }

        // ── Field helpers ───────────────────────────────────────────────────
        //
        // Each takes a GETTER as well as a setter, which is what lets RefreshAfterEdit re-read
        // the model and push the stored value back into the box without rebuilding it.

        private TextMeshProUGUI Header(string text)
        {
            var label = EditorUIHelpers.AddLabel(_detailBody, text, 13f);
            label.color = UITheme.ACCENT;
            label.fontStyle = FontStyles.Bold;
            _detailObjects.Add(label.gameObject);
            return label;
        }

        private TextMeshProUGUI Note(string text)
        {
            var label = EditorUIHelpers.AddLabel(_detailBody, text, 10.5f);
            label.color = UITheme.TEXT_MUTED;
            _detailObjects.Add(label.gameObject);
            return label;
        }

        private void IntField(string label, Func<int> get, Action<int> set)
        {
            var row = MakeFieldRow(label);
            var input = EditorUIHelpers.AddInputField(row, get().ToString(CultureInfo.InvariantCulture),
                s =>
                {
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                        set(v);
                    else RefreshAfterEdit();   // reject: put the stored value back
                });
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            StretchControl(input.gameObject);
            _fieldResync.Add(() =>
            {
                if (input != null)
                    input.SetTextWithoutNotify(get().ToString(CultureInfo.InvariantCulture));
            });
        }

        private void FloatField(string label, Func<float> get, Action<float> set)
        {
            var row = MakeFieldRow(label);
            var input = EditorUIHelpers.AddInputField(row,
                get().ToString("0.###", CultureInfo.InvariantCulture),
                s =>
                {
                    // InvariantCulture on purpose: this machine's locale uses a comma decimal
                    // separator, and parsing under the current culture would read "1.25" as
                    // 125 — a growth curve twenty times too steep, from a field that looked
                    // right when it was typed.
                    if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                        set(v);
                    else RefreshAfterEdit();
                });
            StretchControl(input.gameObject);
            _fieldResync.Add(() =>
            {
                if (input != null)
                    input.SetTextWithoutNotify(get().ToString("0.###", CultureInfo.InvariantCulture));
            });
        }

        private void TextField(string label, Func<string> get, Action<string> set)
        {
            var row = MakeFieldRow(label);
            var input = EditorUIHelpers.AddInputField(row, get() ?? "", set);
            StretchControl(input.gameObject);
            _fieldResync.Add(() =>
            {
                if (input != null) input.SetTextWithoutNotify(get() ?? "");
            });
        }

        private Transform MakeFieldRow(string label)
        {
            var row = EditorUIHelpers.CreateUI("Field_" + label, _detailBody);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            var element = row.AddComponent<LayoutElement>();
            element.preferredHeight = 22f;
            element.minHeight = 22f;
            // Both numbers: a LayoutElement that sets only preferredHeight does NOT stop the
            // row expanding, because uGUI takes flexibleHeight from whatever supplies one and
            // an unset element supplies -1. That is how the chat's input row ended up 80 px
            // tall against a 32 px preference.
            element.flexibleHeight = 0f;

            var text = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            var textElement = text.gameObject.AddComponent<LayoutElement>();
            textElement.preferredWidth = 150f;
            textElement.minWidth = 150f;
            textElement.flexibleWidth = 0f;

            _detailObjects.Add(row);
            return row.transform;
        }

        /// <summary>
        /// A child of a HorizontalLayoutGroup with childControlWidth and no
        /// childForceExpandWidth is laid out at its MINIMUM unless it declares a width — the
        /// defect that printed the Controls editor's buttons one letter per line down a
        /// one-character column. The control takes whatever the label leaves.
        /// </summary>
        private static void StretchControl(GameObject go)
        {
            if (go == null) return;
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.minWidth = 60f;
            element.flexibleWidth = 1f;
        }

        private partial void SetStatus(string message)
        {
            if (_status != null) _status.text = message;
        }
    }
}
