using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data.Feel;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.CameraFeelEditor
{
    /// <summary>
    /// Builds the Camera Editor: a menu bar, seven dockable panels, a status line and a
    /// tutorial, in the same chrome as every other runtime editor so the player learns one
    /// layout and reuses it everywhere.
    ///
    /// The tunable panels are generated from <see cref="CameraFeelProfile.Tunables"/> rather
    /// than hand-written. Twenty-four sliders written by hand is twenty-four chances for a
    /// label to disagree with the field it drives, plus a twenty-fifth tunable added later
    /// that nobody wires up; driving the UI off the same table the data declares means a new
    /// tunable appears in the editor by existing.
    /// </summary>
    internal static class CameraEditorUIBuilder
    {
        internal sealed class SliderRow
        {
            public CameraFeelTunable Id;
            public Slider Slider;
            public TextMeshProUGUI Value;
            public string Suffix;
        }

        internal sealed class CueRow
        {
            public string Field;
            public Slider Slider;
            public TextMeshProUGUI Value;
        }

        internal sealed class UIRefs
        {
            public readonly List<SliderRow> Rows = new List<SliderRow>();
            public readonly List<CueRow> CueRows = new List<CueRow>();
            public readonly Dictionary<CameraFeelCue, Image> CueButtons =
                new Dictionary<CameraFeelCue, Image>();

            /// <summary>Panel id → panel root, so the menu bar can show and hide each one.</summary>
            public readonly Dictionary<string, GameObject> Panels = new Dictionary<string, GameObject>();
            public readonly Dictionary<string, Image> MenuButtons = new Dictionary<string, Image>();
            public readonly Dictionary<string, TextMeshProUGUI> MenuLabels =
                new Dictionary<string, TextMeshProUGUI>();

            public TextMeshProUGUI Status;
            public TextMeshProUGUI Readout;
            public TextMeshProUGUI Diagnostics;
            public TextMeshProUGUI Help;
            public TextMeshProUGUI CueTitle;
        }

        internal sealed class Callbacks
        {
            public Action<CameraFeelTunable, float> OnTunable;
            public Action<string, float> OnCueField;
            public Action<CameraFeelCue> OnCueSelected;
            public Action<CameraFeelCue> OnCueTest;
            public Func<CameraFeelCue> CurrentCue;
            public Action<CameraFeelPreset> OnPreset;
            public Action<string> OnTogglePanel;
            public Action OnTutorial;
            public Action OnSave;
            public Action OnReset;
            public Action OnUndo;
            public Action OnRedo;
        }

        internal const string PANEL_FOLLOW = "follow";
        internal const string PANEL_LEAD = "lead";
        internal const string PANEL_SHAKE = "shake";
        internal const string PANEL_GLOBAL = "global";
        internal const string PANEL_CLASSIFY = "classify";
        internal const string PANEL_CUES = "cues";
        internal const string PANEL_LIVE = "live";

        /// <summary>
        /// Opened when the editor is first shown.
        ///
        /// Not all seven: the left column plus one right-hand panel is what fits a 790-pixel
        /// game view without overlapping, and a tuning session starts with movement. The
        /// other three are one menu-bar click away, and the button lights up when they are
        /// open, so nothing is hidden — just not all at once.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Immutable list of panel ids, built once from " +
            "const strings. Never written to and holds no Unity objects.")]
        internal static readonly string[] DefaultPanels =
        {
            PANEL_FOLLOW, PANEL_LEAD, PANEL_SHAKE, PANEL_LIVE,
        };

        [Valkur.Core.SelfHealingStatic("Immutable list of panel ids, built once from " +
            "const strings. Never written to and holds no Unity objects.")]
        internal static readonly string[] AllPanels =
        {
            PANEL_FOLLOW, PANEL_LEAD, PANEL_SHAKE, PANEL_GLOBAL,
            PANEL_CLASSIFY, PANEL_CUES, PANEL_LIVE,
        };

        private const float PANEL_W = 300f;
        private const float GAP = 8f;
        private const float MENUBAR_H = 26f;
        private const float STATUS_H = 20f;
        private const float TOP = MENUBAR_H + GAP;

        private static readonly Color MENU_BG = new Color(0.09f, 0.09f, 0.12f, 0.97f);
        private static readonly Color MENU_BTN = new Color(0.16f, 0.16f, 0.20f, 1f);
        private static readonly Color MENU_BTN_OPEN = new Color(0.22f, 0.30f, 0.42f, 1f);

        public static UIRefs BuildAll(Transform root, Callbacks cb)
        {
            var refs = new UIRefs();

            BuildMenuBar(root, refs, cb);

            AddTunablePanel(root, refs, cb, PANEL_FOLLOW, "Follow", CameraFeelGroup.Follow,
                            TileEditorUIHelpers.PanelDock.TopLeft, GAP, TOP, 122f);
            AddTunablePanel(root, refs, cb, PANEL_LEAD, "Lead", CameraFeelGroup.Lead,
                            TileEditorUIHelpers.PanelDock.TopLeft, GAP, TOP + 130f, 296f);
            AddTunablePanel(root, refs, cb, PANEL_SHAKE, "Shake", CameraFeelGroup.Shake,
                            TileEditorUIHelpers.PanelDock.TopLeft, GAP, TOP + 434f, 122f);
            AddTunablePanel(root, refs, cb, PANEL_GLOBAL, "Global", CameraFeelGroup.Global,
                            TileEditorUIHelpers.PanelDock.BottomLeft, GAP, GAP + STATUS_H, 122f);
            AddTunablePanel(root, refs, cb, PANEL_CLASSIFY, "Classification",
                            CameraFeelGroup.Classification,
                            TileEditorUIHelpers.PanelDock.TopRight, GAP, TOP, 244f);

            BuildCuePanel(root, refs, cb);
            BuildLivePanel(root, refs, cb);
            BuildStatusBar(root, refs);

            return refs;
        }

        // ── Menu bar ──────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, UIRefs refs, Callbacks cb)
        {
            var go = UIFactory.CreateUI("CameraMenuBar", canvasT);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(0f, MENUBAR_H);

            var bg = go.AddComponent<Image>();
            bg.color = MENU_BG;
            bg.raycastTarget = true;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 0, 0);
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            Transform t = go.transform;

            var brand = MakeText(t, "CAMERA", 11f, EditorUIHelpers.ACCENT,
                                 TextAlignmentOptions.Left, 74f, FontStyles.Bold);
            brand.characterSpacing = 2f;

            AddMenuButton(t, refs, cb, PANEL_FOLLOW, "Follow", 60f);
            AddMenuButton(t, refs, cb, PANEL_LEAD, "Lead", 52f);
            AddMenuButton(t, refs, cb, PANEL_SHAKE, "Shake", 56f);
            AddMenuButton(t, refs, cb, PANEL_GLOBAL, "Global", 58f);
            AddMenuButton(t, refs, cb, PANEL_CLASSIFY, "Classify", 64f);
            AddMenuButton(t, refs, cb, PANEL_CUES, "Cues", 50f);
            AddMenuButton(t, refs, cb, PANEL_LIVE, "Live", 50f);

            UIFactory.CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            MakeMenuButton(t, "Undo", 50f, () => cb.OnUndo?.Invoke(), out _);
            MakeMenuButton(t, "Redo", 50f, () => cb.OnRedo?.Invoke(), out _);
            MakeMenuButton(t, "?", 26f, () => cb.OnTutorial?.Invoke(), out _);
        }

        private static void AddMenuButton(Transform parent, UIRefs refs, Callbacks cb,
                                          string panelId, string label, float width)
        {
            var img = MakeMenuButton(parent, label, width,
                                     () => cb.OnTogglePanel?.Invoke(panelId),
                                     out TextMeshProUGUI tmp);
            refs.MenuButtons[panelId] = img;
            refs.MenuLabels[panelId] = tmp;
        }

        private static Image MakeMenuButton(Transform parent, string label, float width,
                                            Action onClick, out TextMeshProUGUI tmp)
        {
            var go = UIFactory.CreateUI($"Menu_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var img = go.AddComponent<Image>();
            img.color = MENU_BTN;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            tmp = MakeText(go.transform, label, 10f, EditorUIHelpers.TEXT_PRIMARY,
                           TextAlignmentOptions.Center, 0f, FontStyles.Normal);
            EditorUIHelpers.StretchFill(tmp.gameObject);
            return img;
        }

        public static void ApplyMenuButtonStyle(Image img, TextMeshProUGUI tmp, bool open)
        {
            if (img != null) img.color = open ? MENU_BTN_OPEN : MENU_BTN;
            if (tmp == null) return;
            tmp.color = open ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_PRIMARY;
            tmp.fontStyle = open ? FontStyles.Bold : FontStyles.Normal;
        }

        // ── Panels ────────────────────────────────────────────────────────────

        private static void AddTunablePanel(Transform canvasT, UIRefs refs, Callbacks cb,
                                            string panelId, string title, CameraFeelGroup group,
                                            TileEditorUIHelpers.PanelDock dock,
                                            float x, float y, float height)
        {
            var panel = EditorUIHelpers.MakeDropPanel($"Camera{group}Panel", canvasT, dock, x, y,
                                                     PANEL_W, height, title,
                                                     out Transform t, out _);
            refs.Panels[panelId] = panel;
            AddVertical(t);

            foreach (var info in CameraFeelProfile.Tunables)
            {
                if (info.Group != group) continue;
                refs.Rows.Add(AddSliderRow(t, info, refs, cb.OnTunable));
            }
        }

        private static SliderRow AddSliderRow(Transform parent, CameraFeelTunableInfo info,
                                              UIRefs refs,
                                              Action<CameraFeelTunable, float> onChanged)
        {
            var row = new SliderRow { Id = info.Id, Suffix = info.Suffix };

            var head = UIFactory.CreateUI($"Head_{info.Id}", parent);
            head.AddComponent<LayoutElement>().preferredHeight = 15f;
            var hl = head.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childForceExpandWidth = true;
            hl.childAlignment = TextAnchor.MiddleLeft;

            MakeText(head.transform, info.Label, 10f, EditorUIHelpers.TEXT_SECONDARY,
                     TextAlignmentOptions.Left, 0f, FontStyles.Normal);
            row.Value = MakeText(head.transform, "", 10f, EditorUIHelpers.ACCENT,
                                 TextAlignmentOptions.Right, 78f, FontStyles.Bold);

            var host = UIFactory.CreateUI($"Slider_{info.Id}", parent);
            host.AddComponent<LayoutElement>().preferredHeight = 13f;
            row.Slider = MakeSlider(host.transform, info.Min, info.Max,
                                    v => onChanged?.Invoke(info.Id, v));

            // Camera tuning is the one place where a number's consequence is genuinely not
            // self-evident, so every slider explains itself on hover. A knob nobody can
            // reason about gets moved on a hunch and moved back a week later.
            var hover = host.AddComponent<UIHoverHelp>();
            hover.Message = $"{info.Label} — {info.Help}";
            hover.Refs = refs;

            return row;
        }

        private static void BuildCuePanel(Transform canvasT, UIRefs refs, Callbacks cb)
        {
            var panel = EditorUIHelpers.MakeDropPanel("CameraCuePanel", canvasT,
                                                     TileEditorUIHelpers.PanelDock.TopRight,
                                                     GAP, TOP + 252f, PANEL_W, 400f, "Cues",
                                                     out Transform t, out _);
            refs.Panels[PANEL_CUES] = panel;
            AddVertical(t);

            var grid = UIFactory.CreateUI("CueGrid", t);
            grid.AddComponent<LayoutElement>().preferredHeight = 122f;
            var gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(90f, 17f);
            gl.spacing = new Vector2(3f, 3f);

            foreach (CameraFeelCue cue in Enum.GetValues(typeof(CameraFeelCue)))
            {
                CameraFeelCue captured = cue;
                var btn = EditorUIHelpers.MakeButton(grid.transform, cue.ToString(),
                                                     () => cb.OnCueSelected?.Invoke(captured),
                                                     17f, 8f);
                refs.CueButtons[cue] = btn.GetComponent<Image>();
            }

            refs.CueTitle = MakeText(t, "-", 11f, EditorUIHelpers.TEXT_PRIMARY,
                                     TextAlignmentOptions.Left, 0f, FontStyles.Bold);
            refs.CueTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 15f;

            AddCueRow(t, refs, "traumaAdd",            0f,   1f,   cb.OnCueField);
            AddCueRow(t, refs, "traumaDecayPerSecond", 0.1f, 6f,   cb.OnCueField);
            AddCueRow(t, refs, "shakeFrequencyHz",     0f,   40f,  cb.OnCueField);
            AddCueRow(t, refs, "kickAmplitudeWu",      0f,   1f,   cb.OnCueField);
            AddCueRow(t, refs, "kickOmega",            0f,   40f,  cb.OnCueField);
            AddCueRow(t, refs, "kickZeta",             0.2f, 1.5f, cb.OnCueField);
            AddCueRow(t, refs, "leadFreezeSeconds",    0f,   4f,   cb.OnCueField);
            AddCueRow(t, refs, "hitStopSeconds",       0f,   0.3f, cb.OnCueField);
            AddCueRow(t, refs, "minIntervalSeconds",   0f,   2f,   cb.OnCueField);

            // Firing the beat you are editing, on demand, is the whole difference between
            // tuning and waiting around to be hit by something.
            EditorUIHelpers.MakeButton(t, "TEST THIS CUE",
                () => cb.OnCueTest?.Invoke(cb.CurrentCue != null
                    ? cb.CurrentCue()
                    : CameraFeelCue.AttackConnect), 24f, 11f);
        }

        private static void AddCueRow(Transform parent, UIRefs refs, string field,
                                      float min, float max, Action<string, float> onChanged)
        {
            var head = UIFactory.CreateUI($"CueHead_{field}", parent);
            head.AddComponent<LayoutElement>().preferredHeight = 13f;
            var hl = head.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childForceExpandWidth = true;

            MakeText(head.transform, field, 9f, EditorUIHelpers.TEXT_MUTED,
                     TextAlignmentOptions.Left, 0f, FontStyles.Normal);
            var value = MakeText(head.transform, "", 9f, EditorUIHelpers.ACCENT,
                                 TextAlignmentOptions.Right, 58f, FontStyles.Bold);

            var host = UIFactory.CreateUI($"CueSlider_{field}", parent);
            host.AddComponent<LayoutElement>().preferredHeight = 11f;
            var slider = MakeSlider(host.transform, min, max, v => onChanged?.Invoke(field, v));

            refs.CueRows.Add(new CueRow { Field = field, Slider = slider, Value = value });
        }

        /// <summary>
        /// Live solver state, the derived readout, the presets and the file actions.
        ///
        /// The derived block is the important half. The follow spring settles behind a moving
        /// player and that lag subtracts from the lead, so "am I actually leading?" is a
        /// question the sliders cannot answer between them — and getting it wrong is the
        /// single easiest way to make this system feel bad.
        /// </summary>
        private static void BuildLivePanel(Transform canvasT, UIRefs refs, Callbacks cb)
        {
            var panel = EditorUIHelpers.MakeDropPanel("CameraLivePanel", canvasT,
                                                     TileEditorUIHelpers.PanelDock.BottomRight,
                                                     GAP, GAP + STATUS_H, PANEL_W, 340f,
                                                     "Live", out Transform t, out _);
            refs.Panels[PANEL_LIVE] = panel;
            AddVertical(t);

            AddSectionLabel(t, "SOLVER");
            refs.Diagnostics = MakeText(t, "", 9.5f, EditorUIHelpers.TEXT_PRIMARY,
                                        TextAlignmentOptions.TopLeft, 0f, FontStyles.Normal);
            refs.Diagnostics.gameObject.AddComponent<LayoutElement>().flexibleHeight = 2f;

            AddSectionLabel(t, "DERIVED");
            refs.Readout = MakeText(t, "", 9.5f, EditorUIHelpers.TEXT_PRIMARY,
                                    TextAlignmentOptions.TopLeft, 0f, FontStyles.Normal);
            refs.Readout.gameObject.AddComponent<LayoutElement>().flexibleHeight = 2f;

            AddSectionLabel(t, "PRESETS");
            var presetRow = UIFactory.CreateUI("Presets", t);
            presetRow.AddComponent<LayoutElement>().preferredHeight = 40f;
            var pg = presetRow.AddComponent<GridLayoutGroup>();
            pg.cellSize = new Vector2(90f, 18f);
            pg.spacing = new Vector2(3f, 3f);

            foreach (CameraFeelPreset preset in Enum.GetValues(typeof(CameraFeelPreset)))
            {
                CameraFeelPreset captured = preset;
                EditorUIHelpers.MakeButton(presetRow.transform, preset.ToString(),
                                           () => cb.OnPreset?.Invoke(captured), 18f, 9f);
            }

            AddSectionLabel(t, "HELP");
            refs.Help = MakeText(t, "Hover a slider for what it does.", 9f,
                                 EditorUIHelpers.TEXT_MUTED, TextAlignmentOptions.TopLeft,
                                 0f, FontStyles.Italic);
            refs.Help.gameObject.AddComponent<LayoutElement>().flexibleHeight = 3f;

            EditorUIHelpers.MakeButton(t, "SAVE TO ASSET", () => cb.OnSave?.Invoke(), 24f, 11f);
            EditorUIHelpers.MakeDangerButton(t, "RESET TO DEFAULTS",
                                             () => cb.OnReset?.Invoke(), 22f);
        }

        private static void BuildStatusBar(Transform canvasT, UIRefs refs)
        {
            var go = UIFactory.CreateUI("CameraStatusBar", canvasT);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(0f, STATUS_H);

            go.AddComponent<Image>().color = MENU_BG;

            refs.Status = MakeText(go.transform, "", 10f, EditorUIHelpers.TEXT_SECONDARY,
                                   TextAlignmentOptions.Left, 0f, FontStyles.Normal);
            var rt = refs.Status.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 0f);
            rt.offsetMax = new Vector2(-10f, 0f);
        }

        // ── Primitives ────────────────────────────────────────────────────────

        private static void AddSectionLabel(Transform parent, string text)
        {
            var tmp = MakeText(parent, text, 8.5f, EditorUIHelpers.TEXT_MUTED,
                               TextAlignmentOptions.Left, 0f, FontStyles.Bold);
            tmp.characterSpacing = 2f;
            tmp.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;
        }

        /// <summary>
        /// Tightens the layout on a panel's content area.
        ///
        /// MakeDropPanel already puts a VerticalLayoutGroup there, and LayoutGroup carries
        /// [DisallowMultipleComponent] — so adding a second one returns null rather than
        /// throwing, and the next line dereferences it. That NRE fired on the first panel and
        /// aborted BuildUI for all seven, leaving one empty header on screen and nothing else.
        /// Configure what is there; only add when it is genuinely absent.
        /// </summary>
        private static void AddVertical(Transform t)
        {
            if (t == null) return;

            var v = t.GetComponent<VerticalLayoutGroup>();
            if (v == null) v = t.gameObject.AddComponent<VerticalLayoutGroup>();
            if (v == null) return;

            v.spacing = 2f;
            v.padding = new RectOffset(6, 6, 4, 6);
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string text, float size,
                                                Color color, TextAlignmentOptions align,
                                                float preferredWidth, FontStyles style)
        {
            var go = UIFactory.CreateUI("Txt", parent);
            if (preferredWidth > 0f)
                go.AddComponent<LayoutElement>().preferredWidth = preferredWidth;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.enableWordWrapping = align == TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Slider MakeSlider(Transform parent, float min, float max,
                                         Action<float> onChanged)
        {
            var go = UIFactory.CreateUI("Slider", parent);
            var bg = go.AddComponent<Image>();
            bg.color = EditorUIHelpers.SLOT_BG;

            var slider = go.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.transition = Selectable.Transition.None;

            var fillGo = UIFactory.CreateUI("Fill", go.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.2f);
            fillRt.anchorMax = new Vector2(1f, 0.8f);
            fillRt.offsetMin = new Vector2(2f, 0f);
            fillRt.offsetMax = new Vector2(-2f, 0f);
            fillGo.AddComponent<Image>().color = EditorUIHelpers.ACCENT;

            var handleGo = UIFactory.CreateUI("Handle", go.transform);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(8f, 0f);
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = EditorUIHelpers.TEXT_PRIMARY;

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }
    }
}
