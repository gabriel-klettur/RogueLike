using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.Economy;
using Valkur.Gameplay.NPC;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Economy
{
    public partial class EconomyRuntimeEditor
    {
        private const float NAV_W = 250f;
        private const float BODY_W = 470f;
        private const float PANEL_H = 620f;

        /// <summary>
        /// Positive on purpose. <c>ApplyPanelDock</c> NEGATES the vertical offset it is given,
        /// so a negative here docks the panel above the canvas and takes its header off screen
        /// — exactly what shipped in the Controls editor and left two panels unreadable.
        /// </summary>
        private const float PANEL_TOP = TileEditorUIHelpers.PANEL_TOP_OFFSET;

        private const float ROW_H = 24f;

        private Transform _navContent;
        private Transform _bodyContent;
        private TextMeshProUGUI _status;
        private GameObject _tutorial;

        private DraggablePanel _navPanel;
        private DraggablePanel _bodyPanel;

        private Button _undoButton;
        private Button _redoButton;
        private Button _helpButton;

        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<GameObject> _bodyObjects = new List<GameObject>();

        /// <summary>
        /// One per numeric field: re-reads the model and writes it back into the box WITHOUT
        /// notifying.
        ///
        /// <para>This is what replaces rebuilding the body on every commit.
        /// <c>UIInputField.AddCommit</c> fires on focus loss as well as Enter, so a rebuild
        /// destroys the field the author has just tabbed INTO — the next box vanishes under the
        /// cursor. Re-syncing in place also shows a CLAMPED value immediately: type 9 into an
        /// amplitude capped at 0.6 and the box snaps to 0.6 rather than keeping a number
        /// nothing ever took. Copied from the Skills editor, which paid for the lesson.</para>
        /// </summary>
        private readonly List<Action> _fieldResync = new List<Action>();

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("EconomyEditorCanvas", 115);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            EditorUIHelpers.MakeDropPanel(
                "EconomyNavPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopLeft, 16f, PANEL_TOP, NAV_W, PANEL_H,
                "Economia", out _navContent, out _navPanel);

            EditorUIHelpers.MakeDropPanel(
                "EconomyBodyPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopRight, 16f, PANEL_TOP, BODY_W, PANEL_H,
                "Parametros", out _bodyContent, out _bodyPanel);

            // NEITHER PANEL MAY BE CLOSED. Both ship closable by default, this editor has no
            // toolbar outside them, and EditorWorkspaceService persists `open:false` for an
            // inactive panel — so closing both would make the editor open empty forever with no
            // way back. That is not hypothetical: it is what shipped in the Controls editor.
            _navPanel.ShowCloseButton = false;
            _bodyPanel.ShowCloseButton = false;

            BuildNavPanel();
            BuildBodyPanel();
            BuildTutorial();
        }

        /// <summary>
        /// Heal a workspace document written before the close buttons were removed: a panel
        /// persisted as closed stays closed on restore, so one click on an X would leave the
        /// author opening an empty editor forever.
        /// </summary>
        private void ForcePanelsOpen()
        {
            Reopen(_navPanel);
            Reopen(_bodyPanel);
        }

        /// <summary>
        /// Both halves are needed and they are different statements: re-activating the
        /// GameObject puts the panel back on screen, and <c>MarkOpened</c> clears the
        /// REMEMBERED closed flag so the workspace does not close it again on the next restore.
        /// </summary>
        private static void Reopen(DraggablePanel panel)
        {
            if (panel == null) return;
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            panel.MarkOpened();
        }

        // ── Left panel: the tabs, and what each one costs ───────────────────

        private void BuildNavPanel()
        {
            EditorUIHelpers.BuildSectionHeader(_navContent, "Seccion");

            var strip = EditorUIHelpers.CreateUI("TabStrip", _navContent);
            var layout = strip.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            strip.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _tabButtons.Clear();
            AddTab(strip.transform, Tab.Market, "Ciclo de mercado");
            AddTab(strip.transform, Tab.Faucet, "Grifo de monedas");
            AddTab(strip.transform, Tab.Vendors, "Vendedores");
            AddTab(strip.transform, Tab.Bitcoin, "Bitcoin");

            EditorUIHelpers.BuildSeparator(_navContent);

            var bar = MakeRow(_navContent, "NavToolbar", 28f);
            EditorUIHelpers.MakeButton(bar.transform, "GUARDAR", SaveAll, 28f, 11f);
            _undoButton = EditorUIHelpers.MakeButton(bar.transform, "DESHACER", UndoLast, 28f, 11f);
            _redoButton = EditorUIHelpers.MakeButton(bar.transform, "REHACER", RedoLast, 28f, 11f);

            _helpButton = EditorUIHelpers.MakeButton(bar.transform, "?", ToggleTutorial, 28f, 12f);
            var helpElement = _helpButton.gameObject.AddComponent<LayoutElement>();
            helpElement.preferredWidth = 30f;
            helpElement.minWidth = 30f;
            helpElement.flexibleWidth = 0f;

            EditorUIHelpers.BuildSeparator(_navContent);

            _status = EditorUIHelpers.MakeStatusText(_navContent);
            // Four lines' worth, reserved. This status carries sentences about which LIFETIME an
            // edit lands in ("el ciclo vive en el save, no en un asset"), and at one line's
            // height every one of them is clipped mid-word.
            var statusLayout = _status.gameObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 64f;
            statusLayout.minHeight = 64f;
            statusLayout.flexibleHeight = 0f;
        }

        private void AddTab(Transform parent, Tab tab, string label)
        {
            var button = EditorUIHelpers.MakeButton(parent, label, () => SelectTab(tab), ROW_H + 4f, 12f);
            _tabButtons.Add(button);
        }

        internal void SelectTab(Tab tab)
        {
            _tab = tab;
            RefreshAll();
        }

        // ── Right panel: the body, rebuilt per tab ──────────────────────────

        private void BuildBodyPanel()
        {
            // The body SCROLLS. Without it the vendor tab's rows run past the panel edge and
            // are simply unreachable — invisible in EditMode, where uGUI runs no layout at all.
            var (scroll, content) = EditorUIHelpers.MakeScrollView(_bodyContent, "BodyScroll");
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
            _bodyContent2 = content;
        }

        private RectTransform _bodyContent2;

        private void BuildTutorial()
        {
            // Every verb here is a SHARED editor binding, so the overlay names the same keys the
            // other seventeen editors do rather than inventing a vocabulary for this one.
            _tutorial = TutorialOverlay.Build(_root.transform, "ECONOMIA", new[]
            {
                ("Ctrl+Z", "Deshacer el ultimo cambio"),
                ("Ctrl+Y", "Rehacer"),
                ("Ctrl+S", "Guardar los assets"),
                ("Esc",    "Cerrar el editor"),
                ("Ciclo",  "Semilla y dia viven en el SAVE, no en un asset"),
                ("Tuning", "Amplitud y grifo viven en EconomyTuning, compartido por todos los saves"),
                ("BTC",    "Solo mira. La unica via al juego es sembrar la semilla, una vez"),
            });
            _tutorial.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            bool show = !_tutorial.activeSelf;
            _tutorial.SetActive(show);
            UIButton.SetTint(_helpButton, show ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
        }

        // ── Refresh ─────────────────────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshTabHighlights();
            RebuildBody();
            RefreshHistoryButtons();
        }

        /// <summary>
        /// After an EDIT rather than a selection change: update what the edit could have moved
        /// and touch nothing else. Rebuilding here would destroy the focused field.
        /// </summary>
        private void RefreshAfterEdit()
        {
            for (int i = 0; i < _fieldResync.Count; i++) _fieldResync[i]?.Invoke();
            RefreshDerivedLabels();
            RefreshHistoryButtons();
        }

        private void RefreshTabHighlights()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                // SetTint, never targetGraphic.color: a Button's tint is its ColorBlock, the
                // CanvasRenderer MULTIPLIES the graphic colour by it, and assigning the block
                // does not repaint until Selectable evaluates a transition. Writing the graphic
                // instead once made an "active" row render darker than an inactive one.
                UIButton.SetTint(_tabButtons[i], (int)_tab == i ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
            }
        }

        private void RefreshHistoryButtons()
        {
            if (_undoButton != null) _undoButton.interactable = CanUndo;
            if (_redoButton != null) _redoButton.interactable = CanRedo;
        }

        internal void SetStatus(string message)
        {
            if (_status == null) return;

            // The unbacked-tuning warning is APPENDED to whatever the caller said rather than
            // replacing it, because it is a standing condition and not an event: an author who
            // has never run the seeder needs to be told on every message, not once.
            string suffix = TuningIsUnbacked
                ? "\n<color=#d08a3a>Sin asset EconomyTuning: ejecuta Valkur > Economy > Seed Economy Content o los cambios no sobreviven.</color>"
                : string.Empty;
            _status.text = message + suffix;
        }

        // ── Row helpers ─────────────────────────────────────────────────────

        private static GameObject MakeRow(Transform parent, string name, float height)
        {
            var row = EditorUIHelpers.CreateUI(name, parent);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            var element = row.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            // Explicit zero. A LayoutElement that sets only preferredHeight does NOT stop the
            // row expanding: uGUI resolves each property independently and takes flexibleHeight
            // from the HorizontalLayoutGroup on this same object, which reports 1 whenever
            // childForceExpandHeight is on. The chat input row was clipped for exactly this.
            element.flexibleHeight = 0f;
            return row;
        }

        /// <summary>
        /// A labelled numeric field bound to a getter/setter pair, committed through the
        /// history so it is undoable.
        ///
        /// <para>Parsed with <see cref="CultureInfo.InvariantCulture"/>: on a machine with a
        /// comma decimal separator "0.25" would otherwise parse as 25, and an amplitude of 25
        /// clamps to the ceiling and silently doubles every price swing in the world.</para>
        /// </summary>
        private void AddFloatField(Transform parent, string label, string tooltip,
                                   Func<float> get, Action<float> set, float min, float max)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            var caption = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            var captionElement = caption.gameObject.AddComponent<LayoutElement>();
            captionElement.preferredWidth = 210f;
            captionElement.flexibleWidth = 0f;

            var field = EditorUIHelpers.AddInputField(row.transform, Fmt(get()), text =>
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                { RefreshAfterEdit(); return; }

                float clamped = Mathf.Clamp(v, min, max);
                float before = get();
                if (Mathf.Approximately(before, clamped)) { RefreshAfterEdit(); return; }

                Commit($"{label}: {Fmt(before)} -> {Fmt(clamped)}",
                    () => set(before), () => set(clamped));
            });

            if (!string.IsNullOrEmpty(tooltip)) AddHint(parent, tooltip);
            _fieldResync.Add(() => field.SetTextWithoutNotify(Fmt(get())));
        }

        private void AddIntField(Transform parent, string label, string tooltip,
                                 Func<int> get, Action<int> set, int min, int max)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            var caption = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            var captionElement = caption.gameObject.AddComponent<LayoutElement>();
            captionElement.preferredWidth = 210f;
            captionElement.flexibleWidth = 0f;

            var field = EditorUIHelpers.AddInputField(row.transform, get().ToString(CultureInfo.InvariantCulture), text =>
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                { RefreshAfterEdit(); return; }

                int clamped = Mathf.Clamp(v, min, max);
                int before = get();
                if (before == clamped) { RefreshAfterEdit(); return; }

                Commit($"{label}: {before} -> {clamped}", () => set(before), () => set(clamped));
            });

            if (!string.IsNullOrEmpty(tooltip)) AddHint(parent, tooltip);
            _fieldResync.Add(() => field.SetTextWithoutNotify(get().ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// A dim explanatory line under a field. Not a tooltip: this editor's numbers are
        /// judgements with consequences an author cannot guess ("below 0.10 nobody notices"),
        /// and a consequence hidden behind a hover is one nobody reads.
        /// </summary>
        private static void AddHint(Transform parent, string text)
        {
            var hint = EditorUIHelpers.AddLabel(parent, text, 9.5f);
            hint.color = UITheme.TEXT_MUTED;
            hint.enableWordWrapping = true;
            var element = hint.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 28f;
            element.minHeight = 14f;
            element.flexibleHeight = 0f;
        }

        private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
