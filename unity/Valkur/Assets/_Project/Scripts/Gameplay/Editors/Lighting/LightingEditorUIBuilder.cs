using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Builds all UI panels for the Lighting Runtime Editor (Ctrl+F3) using the
    /// professional menu-bar + floating-panel architecture shared with Tile (F8),
    /// Buildings (F10), Items (F7) and friends.
    ///
    /// Layout (mirrors Python <c>lighting_editor</c>):
    ///   • 30 px menu bar          — brand + Modes / Cycle / Presets / Instances + ?
    ///   • Modes panel  (60 px)    — Select / Spawn / Delete / Toggle ambient + point lights
    ///   • Cycle panel  (260 px)   — Day-night controls (clock, jump buttons, time scale,
    ///                               pause, min intensity slider, lights-disable window)
    ///   • Presets panel (256 px)  — Search + grid catalog + selected-preset inspector
    ///   • Instances panel (280 px)— Live light list with delete-from-list buttons
    /// </summary>
    public static partial class LightingEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            ModesMenuBtnImg;     public TextMeshProUGUI ModesMenuBtnTmp;
            public Image            CycleMenuBtnImg;     public TextMeshProUGUI CycleMenuBtnTmp;
            public Image            PresetsMenuBtnImg;   public TextMeshProUGUI PresetsMenuBtnTmp;
            public Image            InstancesMenuBtnImg; public TextMeshProUGUI InstancesMenuBtnTmp;

            // Panel roots + drag components
            public GameObject       ModesDropdown;     public DraggablePanel ModesPanelDrag;
            public GameObject       CycleDropdown;     public DraggablePanel CyclePanelDrag;
            public GameObject       PresetsDropdown;   public DraggablePanel PresetsPanelDrag;
            public GameObject       InstancesDropdown; public DraggablePanel InstancesPanelDrag;

            // Modes panel
            public Image            SelectBtnImg;
            public Image            SpawnBtnImg;
            public Image            DeleteBtnImg;
            public Image            AmbientToggleImg;
            public Image            PointLightsToggleImg;

            // Cycle panel
            public TextMeshProUGUI  ClockText;          // big "HH:MM — Phase" label
            public TextMeshProUGUI  PhaseHintText;      // small descriptor
            public Image            PauseBtnImg;        // colour reflects paused state
            public TextMeshProUGUI  PauseBtnTmp;
            public Slider           TimeScrubSlider;    // 0..1 normalised time
            public Slider           DayLengthSlider;    // realSecondsPerDay
            public TextMeshProUGUI  DayLengthTmp;
            public Slider           MinIntensitySlider;
            public TextMeshProUGUI  MinIntensityTmp;
            public Image            LightsWindowToggleImg;
            public Slider           LightsWindowStartSlider;
            public Slider           LightsWindowEndSlider;
            public TextMeshProUGUI  LightsWindowRangeTmp;

            // Presets panel
            public TMP_InputField   SearchBox;
            public RectTransform    PresetGrid;          // VerticalLayoutGroup of preset buttons
            public TextMeshProUGUI  PresetTitle;         // "(no preset selected)" / preset key
            public TextMeshProUGUI  PresetBody;          // multi-line property table
            public TextMeshProUGUI  StatusText;

            // Instances panel
            public RectTransform    InstancesListContent;
            public TextMeshProUGUI  InstancesHint;
            public TextMeshProUGUI  InstancesCountTmp;
        }

        // ── Panel sizes ───────────────────────────────────────────────────────────

        private const float MODES_W     = TOOLS_DROP_W;            // 60 px
        private const float MODES_H     = 360f + PANEL_HDR_H;
        private const float CYCLE_W     = 280f;
        private const float CYCLE_H     = 480f + PANEL_HDR_H;
        private const float PRESETS_W   = TILES_DROP_W;            // 256 px
        private const float PRESETS_H   = TILES_DROP_H;            // 564 px
        private const float INSTANCES_W = 280f;
        private const float INSTANCES_H = 320f + PANEL_HDR_H;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W     = 138f;
        private const float MODES_BTN_W     = 70f;
        private const float CYCLE_BTN_W     = 68f;
        private const float PRESETS_BTN_W   = 80f;
        private const float INSTANCES_BTN_W = 92f;
        private const float TUTORIAL_BTN_W  = 40f;

        private const float BTN_H = 38f;

        // Shared danger-button palette (delete / destructive). Defined once so the
        // Modes panel, the per-row Delete buttons, and the AddDangerToolBtn helper
        // never drift apart on a colour tweak.
        internal static readonly Color DANGER_NORMAL  = new Color(0.55f, 0.15f, 0.15f, 1f);
        internal static readonly Color DANGER_HOVER   = new Color(0.70f, 0.20f, 0.20f, 1f);
        internal static readonly Color DANGER_PRESSED = new Color(0.90f, 0.30f, 0.30f, 1f);
        // Cycle "window enabled" / pause-active highlight.
        internal static readonly Color CYCLE_TOGGLE_ON = new Color(0.30f, 0.70f, 0.40f, 1f);
        internal static readonly Color CYCLE_PAUSED    = new Color(0.85f, 0.45f, 0.15f, 1f);

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            // Modes
            Action onModeSelect, Action onModeSpawn, Action onModeDelete,
            Action onToggleAmbient, Action onTogglePointLights,
            // Cycle
            Action<float> onScrubTime, Action onPause,
            Action<float> onDayLengthChanged, Action<float> onMinIntensityChanged,
            Action onToggleLightsWindow,
            Action<float> onLightsWindowStart, Action<float> onLightsWindowEnd,
            Action onJumpDawn, Action onJumpNoon, Action onJumpDusk, Action onJumpMidnight,
            // Presets / instances
            Action<string> onSearchChanged,
            // Toolbar (Save / Undo / Redo)
            Action onSave, Action onUndo, Action onRedo,
            // Tutorial
            Action onToggleTutorial)
        {
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial);
            BuildModesPanel(canvasT, ref refs,
                onModeSelect, onModeSpawn, onModeDelete,
                onToggleAmbient, onTogglePointLights,
                onSave, onUndo, onRedo);
            BuildCyclePanel(canvasT, ref refs,
                onScrubTime, onPause,
                onDayLengthChanged, onMinIntensityChanged,
                onToggleLightsWindow, onLightsWindowStart, onLightsWindowEnd,
                onJumpDawn, onJumpNoon, onJumpDusk, onJumpMidnight);
            BuildPresetsPanel(canvasT, ref refs, onSearchChanged);
            BuildInstancesPanel(canvasT, ref refs);
            return refs;
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial)
        {
            var go = CreateUI("LightingMenuBar", canvasT);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0f, 1f);
            r.anchorMax        = new Vector2(1f, 1f);
            r.pivot            = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta        = new Vector2(0f, MENUBAR_HEIGHT);
            refs.MenuBar       = go;

            var bg           = go.AddComponent<Image>();
            bg.color         = MENUBAR_BG;
            bg.raycastTarget = true;

            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = BORDER;
            ol.effectDistance = new Vector2(0f, -1f);

            var chrome           = go.AddComponent<MenuBarChrome>();
            chrome.BgImage       = bg;
            chrome.BorderOutline = ol;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding             = new RectOffset((int)MENUBAR_PAD_H, (int)MENUBAR_PAD_H, 0, 0);
            hlg.spacing             = MENUBAR_SPACING;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var t = go.transform;

            // Brand
            var brand = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp              = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "LIGHTING EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ModesMenuBtnImg     = AddMenuBtn(t, "Modes v",     MODES_BTN_W,
                () => onToggle?.Invoke("modes"),     out refs.ModesMenuBtnTmp);
            refs.CycleMenuBtnImg     = AddMenuBtn(t, "Cycle v",     CYCLE_BTN_W,
                () => onToggle?.Invoke("cycle"),     out refs.CycleMenuBtnTmp);
            refs.PresetsMenuBtnImg   = AddMenuBtn(t, "Presets v",   PRESETS_BTN_W,
                () => onToggle?.Invoke("presets"),   out refs.PresetsMenuBtnTmp);
            refs.InstancesMenuBtnImg = AddMenuBtn(t, "Instances v", INSTANCES_BTN_W,
                () => onToggle?.Invoke("instances"), out refs.InstancesMenuBtnTmp);

            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
        }

        // ── Public helpers (called from LightingRuntimeEditor) ────────────────────

        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT      : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        public static void ApplyToolBtnStyle(Image img, bool active, bool danger = false)
        {
            if (img == null) return;
            if (danger)
            {
                img.color = active ? DANGER_PRESSED : DANGER_NORMAL;
            }
            else
            {
                img.color = active ? BTN_ACTIVE : BTN_NORMAL;
            }
        }

        public static void ApplyToggleBtnStyle(Image img, bool on)
        {
            if (img == null) return;
            img.color = on ? CYCLE_TOGGLE_ON : BTN_NORMAL;
        }

        // ── Internal helpers ──────────────────────────────────────────────────────

        private static void AddMenuDivider(Transform parent)
        {
            var go = CreateUI("Div", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 1f;
            go.AddComponent<Image>().color = BORDER;
        }

        private static Image AddMenuBtn(Transform parent, string label, float width,
            UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI tmp)
        {
            var go = CreateUI($"MenuBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;

            var img   = go.AddComponent<Image>();
            img.color = MENU_BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = MENU_BTN_NORMAL;
            c.highlightedColor = MENU_BTN_HOVER;
            c.pressedColor     = MENU_BTN_OPEN;
            c.selectedColor    = MENU_BTN_NORMAL;
            c.fadeDuration     = 0.08f;
            btn.colors        = c;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            tmp           = AddCenteredText(go.transform, label, 11f, FontStyles.Normal, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        // Tool button used inside the Modes panel (label + small subtitle).
        private static Image AddToolBtn(Transform parent, string label, string sub,
            float height, Action onClick)
        {
            var go = CreateUI($"ToolBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment         = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth      = true;
            vl.childControlHeight     = true;
            vl.spacing                = 0f;
            vl.padding                = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var lblTmp       = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text      = label;
            lblTmp.fontSize  = 10f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color     = TEXT_PRIMARY;

            if (!string.IsNullOrEmpty(sub))
            {
                var subGo = CreateUI("Sub", go.transform);
                subGo.AddComponent<LayoutElement>().preferredHeight = 10f;
                var subTmp       = subGo.AddComponent<TextMeshProUGUI>();
                subTmp.text      = sub;
                subTmp.fontSize  = 8f;
                subTmp.alignment = TextAlignmentOptions.Center;
                subTmp.color     = TEXT_MUTED;
            }
            return img;
        }

        private static Image AddDangerToolBtn(Transform parent, string label, string sub,
            float height, Action onClick)
        {
            var img   = AddToolBtn(parent, label, sub, height, onClick);
            img.color = DANGER_NORMAL;
            var btn   = img.GetComponent<Button>();
            var c     = btn.colors;
            c.normalColor      = DANGER_NORMAL;
            c.highlightedColor = DANGER_HOVER;
            c.pressedColor     = DANGER_PRESSED;
            btn.colors         = c;
            return img;
        }

        // Compact full-width button (Save / Undo / Redo at the bottom of Modes).
        private static void AddActionBtn(Transform parent, string label, float height, Action onClick)
        {
            var go = CreateUI($"Act_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp       = AddCenteredText(go.transform, label, 9f, FontStyles.Bold, TEXT_SECONDARY);
            tmp.alignment = TextAlignmentOptions.Center;
        }

        private static void AddInlineSeparator(Transform parent)
        {
            var go = CreateUI("InlineSep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 6f;
            var img = go.AddComponent<Image>();
            img.color = SEPARATOR;
        }

        private static void AddSectionLabel(Transform parent, string text)
        {
            var go = CreateUI($"SecLbl_{text}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
            var tmp       = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 9f;
            tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = TEXT_MUTED;
        }

        // ── MakeDrop (delegates to the shared EditorUIHelpers floating-panel factory) ──

        private static GameObject MakeDrop(
            string name, Transform canvasT,
            PanelDock dock, float xOff, float yOff, float width, float height,
            string title, out Transform contentOut, out DraggablePanel dragOut,
            bool narrowPanel = false)
            => EditorUIHelpers.MakeDropPanel(name, canvasT, dock, xOff, yOff, width, height,
                title, out contentOut, out dragOut, narrowPanel);
    }
}
