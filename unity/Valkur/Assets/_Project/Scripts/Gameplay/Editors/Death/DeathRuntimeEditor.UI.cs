using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Death
{
    public partial class DeathRuntimeEditor
    {
        private const float NAV_W = 250f;
        private const float BODY_W = 500f;
        private const float PANEL_H = 640f;

        /// <summary>
        /// Positive on purpose. <c>ApplyPanelDock</c> NEGATES the vertical offset it is given, so a
        /// negative here docks the panel above the canvas and takes its header off screen — exactly
        /// what shipped in the Controls editor and left two panels unreadable.
        /// </summary>
        private const float PANEL_TOP = TileEditorUIHelpers.PANEL_TOP_OFFSET;

        private const float ROW_H = 24f;
        private const float LABEL_W = 200f;

        /// <summary>Caption width on an enum row, where the options compete for the same line.</summary>
        private const float ENUM_LABEL_W = 120f;

        private Transform _navContent;
        private Transform _bodyContent;
        private RectTransform _bodyScrollContent;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _liveReadout;
        private GameObject _tutorial;

        private DraggablePanel _navPanel;
        private DraggablePanel _bodyPanel;

        private Button _undoButton;
        private Button _redoButton;
        private Button _helpButton;

        private readonly List<Button> _tabButtons = new List<Button>();

        /// <summary>
        /// One per numeric field: re-reads the model and writes it back into the box WITHOUT
        /// notifying.
        ///
        /// <para>This is what replaces rebuilding the body on every commit.
        /// <c>UIInputField.AddCommit</c> fires on focus loss as well as Enter, so a rebuild destroys
        /// the field the author has just tabbed INTO — the next box vanishes under the cursor.
        /// Re-syncing in place also shows a CLAMPED value immediately: type 9 into a fraction capped
        /// at 1 and the box snaps to 1 rather than keeping a number nothing ever took. Copied from
        /// the Skills and Economy editors, which paid for the lesson.</para>
        /// </summary>
        private readonly List<Action> _fieldResync = new List<Action>();

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("DeathEditorCanvas", 116);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            EditorUIHelpers.MakeDropPanel(
                "DeathNavPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopLeft, 16f, PANEL_TOP, NAV_W, PANEL_H,
                "Muerte", out _navContent, out _navPanel);

            EditorUIHelpers.MakeDropPanel(
                "DeathBodyPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopRight, 16f, PANEL_TOP, BODY_W, PANEL_H,
                "Parametros", out _bodyContent, out _bodyPanel);

            // NEITHER PANEL MAY BE CLOSED. Both ship closable by default, this editor has no
            // toolbar outside them, and EditorWorkspaceService persists `open:false` for an
            // inactive panel — so closing both would make the editor open empty forever with no way
            // back. That is not hypothetical: it is what shipped in the Controls editor.
            _navPanel.ShowCloseButton = false;
            _bodyPanel.ShowCloseButton = false;

            BuildNavPanel();
            BuildBodyPanel();
            BuildTutorial();
        }

        /// <summary>
        /// Heal a workspace document written before the close buttons were removed: a panel
        /// persisted as closed stays closed on restore, so one click on an X would leave the author
        /// opening an empty editor forever.
        /// </summary>
        private void ForcePanelsOpen()
        {
            Reopen(_navPanel);
            Reopen(_bodyPanel);
        }

        /// <summary>
        /// Both halves are needed and they are different statements: re-activating the GameObject
        /// puts the panel back on screen, and <c>MarkOpened</c> clears the REMEMBERED closed flag so
        /// the workspace does not close it again on the next restore.
        /// </summary>
        private static void Reopen(DraggablePanel panel)
        {
            if (panel == null) return;
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            panel.MarkOpened();
        }

        // ── Left panel ──────────────────────────────────────────────────────

        private void BuildNavPanel()
        {
            EditorUIHelpers.BuildSectionHeader(_navContent, "Seccion");

            var strip = EditorUIHelpers.CreateUI("TabStrip", _navContent);
            var layout = strip.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            strip.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _tabButtons.Clear();
            AddTab(strip.transform, Tab.Flow,   "Flujo y ritmo");
            AddTab(strip.transform, Tab.Spirit, "Forma espiritu");
            AddTab(strip.transform, Tab.Altars, "Altares y rescate");
            AddTab(strip.transform, Tab.Path,   "Camino y brujula");
            AddTab(strip.transform, Tab.Cost,   "Coste y cadaver");
            AddTab(strip.transform, Tab.Audio,  "Audio");

            EditorUIHelpers.BuildSeparator(_navContent);

            // TWO rows, not one. Four buttons across a 250 px panel gives each about 55 px, and
            // uGUI wraps a TextMeshPro label rather than shrinking it — the shipped first pass
            // rendered "GUARD / AR", "DESHA / CER", "REHAC / ER", which is the same defect the
            // Controls editor shipped when its two buttons collapsed to one character wide.
            // NoWrap is belt-and-braces: it makes a label that still does not fit truncate, which
            // is legible, instead of stacking, which is not.
            var saveBar = MakeRow(_navContent, "NavToolbarSave", 28f);
            NoWrap(EditorUIHelpers.MakeButton(saveBar.transform, "GUARDAR", SaveAsset, 28f, 11f));

            var bar = MakeRow(_navContent, "NavToolbar", 28f);
            _undoButton = NoWrap(EditorUIHelpers.MakeButton(bar.transform, "DESHACER", UndoLast, 28f, 10f));
            _redoButton = NoWrap(EditorUIHelpers.MakeButton(bar.transform, "REHACER", RedoLast, 28f, 10f));

            _helpButton = NoWrap(EditorUIHelpers.MakeButton(bar.transform, "?", ToggleTutorial, 28f, 12f));
            var helpElement = _helpButton.gameObject.AddComponent<LayoutElement>();
            helpElement.preferredWidth = 30f;
            helpElement.minWidth = 30f;
            helpElement.flexibleWidth = 0f;

            EditorUIHelpers.BuildSeparator(_navContent);
            EditorUIHelpers.BuildSectionHeader(_navContent, "Estado en vivo");

            // The live readout is the half of this editor that made the original bug findable at
            // all. Every one of these facts failed silently in the shipped build, and none of them
            // was answerable from inside the game.
            _liveReadout = EditorUIHelpers.AddLabel(_navContent, "", 10f);
            _liveReadout.color = UITheme.TEXT_SECONDARY;
            _liveReadout.enableWordWrapping = true;
            var readoutLayout = _liveReadout.gameObject.AddComponent<LayoutElement>();
            readoutLayout.preferredHeight = 120f;
            readoutLayout.minHeight = 120f;
            readoutLayout.flexibleHeight = 0f;

            EditorUIHelpers.BuildSeparator(_navContent);

            _status = EditorUIHelpers.MakeStatusText(_navContent);
            // Four lines' worth, reserved. This status carries sentences about what a setting
            // costs, and at one line's height every one of them is clipped mid-word.
            var statusLayout = _status.gameObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 72f;
            statusLayout.minHeight = 72f;
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

        // ── Right panel ─────────────────────────────────────────────────────

        private void BuildBodyPanel()
        {
            // The body SCROLLS. Without it the Altars tab's rows run past the panel edge and are
            // simply unreachable — invisible in EditMode, where uGUI runs no layout at all.
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
            _bodyScrollContent = content;
        }

        private void BuildTutorial()
        {
            // Every verb here is a SHARED editor binding, so the overlay names the same keys the
            // other eighteen editors do rather than inventing a vocabulary for this one.
            _tutorial = TutorialOverlay.Build(_root.transform, "MUERTE", new[]
            {
                ("Ctrl+Z",  "Deshacer el ultimo cambio"),
                ("Ctrl+Y",  "Rehacer"),
                ("Ctrl+S",  "Guardar DeathTuning"),
                ("Esc",     "Cerrar el editor"),
                ("Altares", "Sin altar el jugador NO puede revivir: el rescate es la unica salida"),
                ("Espiritu","'Atraviesa muros' y el modo de camino tienen que ir de la mano"),
                ("Coste",   "Sin persistencia, morir y recargar deshace la muerte entera"),
                ("Probar",  "Los botones de la pestana Flujo matan y reviven de verdad"),
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
            RefreshLiveReadout();
        }

        /// <summary>
        /// After an EDIT rather than a selection change: update what the edit could have moved and
        /// touch nothing else. Rebuilding here would destroy the focused field.
        /// </summary>
        private void RefreshAfterEdit()
        {
            for (int i = 0; i < _fieldResync.Count; i++) _fieldResync[i]?.Invoke();
            RefreshHistoryButtons();
        }

        private void RefreshTabHighlights()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                // SetTint, never targetGraphic.color: a Button's tint is its ColorBlock, the
                // CanvasRenderer MULTIPLIES the graphic colour by it, and assigning the block does
                // not repaint until Selectable evaluates a transition. Writing the graphic instead
                // once made an "active" row render darker than an inactive one.
                UIButton.SetTint(_tabButtons[i], (int)_tab == i ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
            }
        }

        private void RefreshHistoryButtons()
        {
            if (_undoButton != null) _undoButton.interactable = CanUndo;
            if (_redoButton != null) _redoButton.interactable = CanRedo;
        }

        /// <summary>
        /// The live state of the death flow, refreshed every frame the editor is open.
        ///
        /// <para>It is deliberately in the NAV panel rather than in a tab: "there are no altars in
        /// this world" is the fact that invalidates every other setting on screen, and a fact that
        /// only appears on one tab is a fact the author will be looking away from.</para>
        /// </summary>
        private void RefreshLiveReadout()
        {
            if (_liveReadout == null) return;

            var controller = ServiceLocator.Get<DeathSequenceController>();
            int altars = ResurrectionAltarRegistry.Count;
            bool usable = ResurrectionAltarRegistry.AnyUsable;

            var sb = new System.Text.StringBuilder();

            sb.Append("Fase: ");
            sb.AppendLine(controller == null ? "<color=#d08a3a>sin controlador</color>" : controller.CurrentPhase.ToString());

            sb.Append("Altares: ");
            sb.AppendLine(usable
                ? $"<color=#6fbf73>{altars}</color>"
                : "<color=#d05a5a>NINGUNO — el jugador no puede revivir</color>");

            if (controller != null && controller.CurrentPhase == DeathSequenceController.Phase.Spirit)
            {
                float eta = controller.RescueEta;
                sb.AppendLine($"Espiritu: {controller.SpiritElapsed:0.0} s");
                sb.AppendLine(float.IsPositiveInfinity(eta)
                    ? "<color=#d05a5a>Rescate: nunca</color>"
                    : $"Rescate en: {eta:0.0} s");
            }

            var path = ServiceLocator.Get<SpiritAltarPathHighlighter>();
            if (path != null)
                sb.AppendLine($"Rastros: altar {path.AltarTrailLength}, cadaver {path.CorpseTrailLength}");

            sb.Append($"Botin caido: {DeathLitter.Count}");

            _liveReadout.text = sb.ToString();
        }

        internal void SetStatus(string message)
        {
            if (_status == null) return;

            // The unbacked-asset warning is APPENDED to whatever the caller said rather than
            // replacing it, because it is a standing condition and not an event: an author who has
            // never saved needs to be told on every message, not once.
            string suffix = TuningIsUnbacked
                ? "\n<color=#d08a3a>Sin asset DeathTuning: pulsa GUARDAR o los cambios mueren con la sesion.</color>"
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
            // Explicit zero. A LayoutElement that sets only preferredHeight does NOT stop the row
            // expanding: uGUI resolves each property independently and takes flexibleHeight from
            // the HorizontalLayoutGroup on this same object, which reports 1 whenever
            // childForceExpandHeight is on. The chat input row was clipped for exactly this.
            element.flexibleHeight = 0f;
            return row;
        }

        /// <summary>
        /// A labelled numeric field bound to a getter/setter pair, committed through the history so
        /// it is undoable.
        ///
        /// <para>Parsed with <see cref="CultureInfo.InvariantCulture"/>: on a machine with a comma
        /// decimal separator "0.25" would otherwise parse as 25, and a fraction of 25 clamps to 1 —
        /// silently turning a 25 % XP penalty into a total wipe.</para>
        /// </summary>
        private void AddFloatField(Transform parent, string label, string hint,
                                   Func<float> get, Action<float> set, float min, float max)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            var caption = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            var captionElement = caption.gameObject.AddComponent<LayoutElement>();
            captionElement.preferredWidth = LABEL_W;
            captionElement.flexibleWidth = 0f;

            var field = EditorUIHelpers.AddInputField(row.transform, Fmt(get()), text =>
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                { RefreshAfterEdit(); return; }

                float clamped = Mathf.Clamp(v, min, max);
                float before = get();
                if (Mathf.Approximately(before, clamped)) { RefreshAfterEdit(); return; }

                Commit($"{label}: {Fmt(before)} -> {Fmt(clamped)}", () => set(before), () => set(clamped));
            });

            if (!string.IsNullOrEmpty(hint)) AddHint(parent, hint);
            _fieldResync.Add(() => field.SetTextWithoutNotify(Fmt(get())));
        }

        private void AddIntField(Transform parent, string label, string hint,
                                 Func<int> get, Action<int> set, int min, int max)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            var caption = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            var captionElement = caption.gameObject.AddComponent<LayoutElement>();
            captionElement.preferredWidth = LABEL_W;
            captionElement.flexibleWidth = 0f;

            var field = EditorUIHelpers.AddInputField(row.transform,
                get().ToString(CultureInfo.InvariantCulture), text =>
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                { RefreshAfterEdit(); return; }

                int clamped = Mathf.Clamp(v, min, max);
                int before = get();
                if (before == clamped) { RefreshAfterEdit(); return; }

                Commit($"{label}: {before} -> {clamped}", () => set(before), () => set(clamped));
            });

            if (!string.IsNullOrEmpty(hint)) AddHint(parent, hint);
            _fieldResync.Add(() => field.SetTextWithoutNotify(get().ToString(CultureInfo.InvariantCulture)));
        }

        private void AddTextField(Transform parent, string label, string hint,
                                  Func<string> get, Action<string> set)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            var caption = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            var captionElement = caption.gameObject.AddComponent<LayoutElement>();
            captionElement.preferredWidth = LABEL_W;
            captionElement.flexibleWidth = 0f;

            var field = EditorUIHelpers.AddInputField(row.transform, get() ?? string.Empty, text =>
            {
                string before = get() ?? string.Empty;
                string next = text ?? string.Empty;
                if (before == next) return;
                Commit($"{label}: '{before}' -> '{next}'", () => set(before), () => set(next));
            });

            if (!string.IsNullOrEmpty(hint)) AddHint(parent, hint);
            _fieldResync.Add(() => field.SetTextWithoutNotify(get() ?? string.Empty));
        }

        /// <summary>
        /// A boolean as a full-width button that reads as its own state.
        ///
        /// <para>A button rather than a uGUI <c>Toggle</c> because the label has to CHANGE — "el
        /// espiritu atraviesa muros" and "el espiritu es solido" are two different sentences, and a
        /// checkbox beside one fixed sentence makes the author read the negation in their head
        /// every time. It also keeps every control in this panel on the one tint path that is known
        /// to repaint (<c>UIButton.SetTint</c>).</para>
        /// </summary>
        private void AddBoolField(Transform parent, string label, string hint,
                                  Func<bool> get, Action<bool> set,
                                  string whenTrue = null, string whenFalse = null)
        {
            Button button = null;
            TextMeshProUGUI caption = null;

            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            caption = EditorUIKit.AddCaption(row.transform, label, LABEL_W);

            button = NoWrap(EditorUIHelpers.MakeButton(row.transform, "", () =>
            {
                bool before = get();
                Commit($"{label}: {Describe(before, whenTrue, whenFalse)} -> {Describe(!before, whenTrue, whenFalse)}",
                    () => set(before), () => set(!before));
            }, ROW_H + 4f, 11f));

            void Sync()
            {
                bool on = get();
                var tmp = button.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) tmp.text = Describe(on, whenTrue, whenFalse);
                UIButton.SetTint(button, on ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
            }

            Sync();
            if (!string.IsNullOrEmpty(hint)) AddHint(parent, hint);
            _fieldResync.Add(Sync);

            // Referenced so the compiler cannot warn about an unused local on a future edit that
            // drops the caption; also the seam a hover-help pass would use.
            if (caption == null) return;
        }

        private static string Describe(bool value, string whenTrue, string whenFalse)
        {
            if (value) return string.IsNullOrEmpty(whenTrue) ? "SI" : whenTrue;
            return string.IsNullOrEmpty(whenFalse) ? "NO" : whenFalse;
        }

        /// <summary>An enum as a row of buttons — every option visible, so nothing is hidden behind a click.</summary>
        private void AddEnumField<T>(Transform parent, string label, string hint,
                                     Func<T> get, Action<T> set, params (T value, string caption)[] options)
            where T : struct, Enum
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);

            // A NARROWER caption than every other row, because the options have to share what is
            // left: four buttons behind a 200 px caption get about 60 px each, and uGUI wraps a
            // TextMeshPro label rather than shrinking it — the first pass rendered "NINGUN / O" and
            // "DONDE / MORI". The caption is the half a reader can infer from the section header;
            // the options are the half they cannot.
            EditorUIKit.AddCaption(row.transform, label, ENUM_LABEL_W);

            var buttons = new List<(T value, Button button)>();
            for (int i = 0; i < options.Length; i++)
            {
                var option = options[i];
                Button button = null;
                button = NoWrap(EditorUIHelpers.MakeButton(row.transform, option.caption, () =>
                {
                    T before = get();
                    if (EqualityComparer<T>.Default.Equals(before, option.value)) return;
                    Commit($"{label}: {before} -> {option.value}",
                        () => set(before), () => set(option.value));
                }, ROW_H + 4f, 10f));
                buttons.Add((option.value, button));
            }

            void Sync()
            {
                T current = get();
                for (int i = 0; i < buttons.Count; i++)
                {
                    bool on = EqualityComparer<T>.Default.Equals(buttons[i].value, current);
                    UIButton.SetTint(buttons[i].button, on ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
                }
            }

            Sync();
            if (!string.IsNullOrEmpty(hint)) AddHint(parent, hint);
            _fieldResync.Add(Sync);
        }

        /// <summary>
        /// A dim explanatory line under a field. Not a tooltip: this editor's numbers are judgements
        /// with consequences an author cannot guess ("sin esto, morir y recargar deshace la muerte"),
        /// and a consequence hidden behind a hover is one nobody reads.
        /// </summary>
        private static void AddHint(Transform parent, string text)
        {
            var hint = EditorUIHelpers.AddLabel(parent, text, 9.5f);
            hint.color = UITheme.TEXT_MUTED;
            hint.enableWordWrapping = true;
            var element = hint.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 30f;
            element.minHeight = 14f;
            element.flexibleHeight = 0f;
        }

        /// <summary>
        /// Stop a button's label wrapping, and truncate it instead.
        ///
        /// <para>A wrapped label on a 28 px button renders both halves clipped and reads as two
        /// broken words; a truncated one reads as one word that ran out of room, which at least
        /// says what it is. Applied to every button in the nav toolbar, where the widths are
        /// tightest.</para>
        /// </summary>
        private static Button NoWrap(Button button)
        {
            if (button == null) return null;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Truncate;
            }
            return button;
        }

        private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The two-line widgets this editor needed and <c>EditorUIHelpers</c> does not have. Kept in
    /// this file rather than pushed into the shared helper because both are shaped by decisions
    /// specific to this panel — one fixed caption width, and a caption that never wraps.
    /// </summary>
    internal static class EditorUIKit
    {
        internal static TextMeshProUGUI AddCaption(Transform parent, string text, float width)
        {
            var caption = EditorUIHelpers.AddLabel(parent, text, 11f);
            var element = caption.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.flexibleWidth = 0f;
            caption.enableWordWrapping = false;
            caption.overflowMode = TextOverflowModes.Truncate;
            return caption;
        }
    }
}
