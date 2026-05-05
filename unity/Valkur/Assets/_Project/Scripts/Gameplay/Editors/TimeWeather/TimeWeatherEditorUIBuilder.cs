using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Builds all UI panels for the Time &amp; Weather Runtime Editor (F2)
    /// using the canonical menu-bar + floating-panel architecture shared with
    /// the Tile (F8), Buildings (F10), Items (F7) and Lighting (Ctrl+F3)
    /// editors.
    ///
    /// Layout:
    ///   • 30 px menu bar          — brand + Speed / Cycle / Weather / Settings dropdowns + ?
    ///   • Speed panel    (220 px) — slider 1× / 2× / 5× / 10× / 20× / 50× / 100×
    ///   • Cycle panel    (180 px) — phase shortcut buttons + OFF (sin filtro)
    ///   • Weather panel  (180 px) — Wind / Rain / Snow toggles + OFF (despejado)
    ///   • Settings panel (260 px) — phase tuning sliders (Tono/Sat/Brillo/Calidez/Viñeta)
    ///                                with Día / Noche tabs and DEFECTO / NEUTRO presets
    /// </summary>
    public static partial class TimeWeatherEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            SpeedMenuBtnImg;    public TextMeshProUGUI SpeedMenuBtnTmp;
            public Image            CycleMenuBtnImg;    public TextMeshProUGUI CycleMenuBtnTmp;
            public Image            WeatherMenuBtnImg;  public TextMeshProUGUI WeatherMenuBtnTmp;
            public Image            SettingsMenuBtnImg; public TextMeshProUGUI SettingsMenuBtnTmp;

            // Panel roots + drag components
            public GameObject       SpeedDropdown;    public DraggablePanel SpeedPanelDrag;
            public GameObject       CycleDropdown;    public DraggablePanel CyclePanelDrag;
            public GameObject       WeatherDropdown;  public DraggablePanel WeatherPanelDrag;
            public GameObject       SettingsDropdown; public DraggablePanel SettingsPanelDrag;

            // Speed panel
            public Slider            SpeedSlider;
            public Image             SpeedHandleImg;
            public TextMeshProUGUI   SpeedValueTmp;

            // Cycle panel
            public Image[]           CycleRowBgs;
            public TextMeshProUGUI[] CycleRowLabels;
            public Image             CycleOffImg;
            public TextMeshProUGUI   CycleOffTmp;

            // Weather panel
            public Image[]           WeatherRowBgs;
            public TextMeshProUGUI[] WeatherRowLabels;
            public Image             WeatherOffImg;
            public TextMeshProUGUI   WeatherOffTmp;

            // Settings panel
            public Image[]           SettingsTabImgs;
            public TextMeshProUGUI   SettingsNameTmp;
            public Image             SettingsSwatchImg;
            public Slider[]          SettingsSliders;
            public TextMeshProUGUI[] SettingsValues;

            public TextMeshProUGUI   StatusText;
        }

        // ── Panel sizes ───────────────────────────────────────────────────────────

        private const float SPEED_W    = 220f;
        private const float SPEED_H    = 110f;
        private const float CYCLE_W    = 196f;
        private const float CYCLE_H    = 230f;
        private const float WEATHER_W  = 168f;
        private const float WEATHER_H  = 198f;
        private const float SETTINGS_W = 260f;
        private const float SETTINGS_H = 320f;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W     = 156f;
        private const float SPEED_BTN_W     = 78f;
        private const float CYCLE_BTN_W     = 64f;
        private const float WEATHER_BTN_W   = 68f;
        private const float SETTINGS_BTN_W  = 76f;
        private const float TUTORIAL_BTN_W  = 40f;

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            // Speed
            Action<float> onSpeedChanged,
            // Cycle
            Action[] onCycleRowClicked, Action onCycleOffClicked,
            // Weather
            Action[] onWeatherRowClicked, Action onWeatherOffClicked,
            // Settings
            Action<int> onSettingsTabClicked,
            Action<int, float> onSettingsSliderChanged,
            Action onSettingsResetClicked,
            Action onSettingsNeutroClicked,
            // Tutorial
            Action onToggleTutorial)
        {
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial);
            BuildSpeedPanel   (canvasT, ref refs, onSpeedChanged);
            BuildCyclePanel   (canvasT, ref refs, onCycleRowClicked, onCycleOffClicked);
            BuildWeatherPanel (canvasT, ref refs, onWeatherRowClicked, onWeatherOffClicked);
            BuildSettingsPanel(canvasT, ref refs, onSettingsTabClicked, onSettingsSliderChanged,
                                                  onSettingsResetClicked, onSettingsNeutroClicked);
            return refs;
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial)
        {
            var go = CreateUI("TimeWeatherMenuBar", canvasT);
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
            brandTmp.text             = "TIME & WEATHER";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.SpeedMenuBtnImg    = AddMenuBtn(t, "Speed v",    SPEED_BTN_W,
                () => onToggle?.Invoke("speed"),    out refs.SpeedMenuBtnTmp);
            refs.CycleMenuBtnImg    = AddMenuBtn(t, "Phases v",   CYCLE_BTN_W,
                () => onToggle?.Invoke("cycle"),    out refs.CycleMenuBtnTmp);
            refs.WeatherMenuBtnImg  = AddMenuBtn(t, "Weather v",  WEATHER_BTN_W,
                () => onToggle?.Invoke("weather"),  out refs.WeatherMenuBtnTmp);
            refs.SettingsMenuBtnImg = AddMenuBtn(t, "Settings v", SETTINGS_BTN_W,
                () => onToggle?.Invoke("settings"), out refs.SettingsMenuBtnTmp);

            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
        }

        // ── Public helpers (called from TimeWeatherEditor) ────────────────────────

        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT      : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
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

        // ── MakeDrop (mirrors LightingEditorUIBuilder.MakeDrop) ───────────────────

        private static GameObject MakeDrop(
            string name, Transform canvasT,
            PanelDock dock, float xOff, float yOff, float width, float height,
            string title, out Transform contentOut, out DraggablePanel dragOut)
        {
            var go = CreateUI(name, canvasT);
            var r  = go.GetComponent<RectTransform>();
            ApplyDock(r, dock, xOff, yOff, width, height);

            var img           = go.AddComponent<Image>();
            img.color         = TileEditorTheme.PanelBg;
            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // Header
            var hdrGo          = CreateUI("PanelHeader", go.transform);
            var hdrRt          = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin        = new Vector2(0f, 1f);
            hdrRt.anchorMax        = new Vector2(1f, 1f);
            hdrRt.pivot            = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = Vector2.zero;
            hdrRt.sizeDelta        = new Vector2(0f, PANEL_HDR_H);

            var hdrImg           = hdrGo.AddComponent<Image>();
            hdrImg.color         = TileEditorTheme.HeaderBg;
            hdrImg.raycastTarget = true;

            var hdrHlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.padding                = new RectOffset(8, 8, 0, 0);
            hdrHlg.spacing                = 0f;
            hdrHlg.childForceExpandWidth  = false;
            hdrHlg.childForceExpandHeight = true;
            hdrHlg.childControlWidth      = true;
            hdrHlg.childControlHeight     = true;
            hdrHlg.childAlignment         = TextAnchor.MiddleLeft;

            var titleGo                 = CreateUI("Title", hdrGo.transform);
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var titleTmp                = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text               = title.ToUpper();
            titleTmp.fontSize           = 10f;
            titleTmp.fontStyle          = FontStyles.Bold;
            titleTmp.color              = TileEditorTheme.HeaderTitle;
            titleTmp.characterSpacing   = 1.5f;
            titleTmp.alignment          = TextAlignmentOptions.Left;
            titleTmp.enableWordWrapping = false;
            titleTmp.overflowMode       = TextOverflowModes.Truncate;
            titleTmp.raycastTarget      = false;

            // Separator
            var sepGo              = CreateUI("HdrSep", go.transform);
            var sepRt              = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin        = new Vector2(0f, 1f);
            sepRt.anchorMax        = new Vector2(1f, 1f);
            sepRt.pivot            = new Vector2(0f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -PANEL_HDR_H);
            sepRt.sizeDelta        = new Vector2(0f, 1f);
            var sepImg             = sepGo.AddComponent<Image>();
            sepImg.color           = TileEditorTheme.Separator;

            // Content area
            var contentGo       = CreateUI("Content", go.transform);
            var contentRt       = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(0f, 0f);
            contentRt.offsetMax = new Vector2(0f, -(PANEL_HDR_H + 1f));

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding                = new RectOffset(8, 8, 6, 6);
            layout.spacing                = 4f;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            contentGo.AddComponent<CanvasGroup>();

            var drag         = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;
            go.AddComponent<CanvasGroup>();

            var chrome             = go.AddComponent<PanelChrome>();
            chrome.PanelBgImage    = img;
            chrome.PanelOutline    = ol;
            chrome.HeaderBgImage   = hdrImg;
            chrome.HeaderSeparator = sepImg;
            chrome.HeaderTitle     = titleTmp;

            contentOut = contentGo.transform;
            dragOut    = drag;
            return go;
        }

        private static void ApplyDock(RectTransform r, PanelDock dock,
            float xOff, float yOff, float width, float height)
        {
            switch (dock)
            {
                case PanelDock.TopLeft:
                    r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f);
                    r.pivot     = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
                case PanelDock.TopRight:
                    r.anchorMin = new Vector2(1f, 1f); r.anchorMax = new Vector2(1f, 1f);
                    r.pivot     = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-xOff, -yOff);
                    break;
                case PanelDock.BottomLeft:
                    r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 0f);
                    r.pivot     = new Vector2(0f, 0f);
                    r.anchoredPosition = new Vector2(xOff, yOff);
                    break;
                case PanelDock.BottomRight:
                    r.anchorMin = new Vector2(1f, 0f); r.anchorMax = new Vector2(1f, 0f);
                    r.pivot     = new Vector2(1f, 0f);
                    r.anchoredPosition = new Vector2(-xOff, yOff);
                    break;
            }
            r.sizeDelta = new Vector2(width, height);
        }
    }
}
