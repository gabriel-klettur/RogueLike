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
    /// Panel builders for the Time &amp; Weather editor (F2). Each panel mirrors
    /// what previously lived as a standalone HUD widget but now embedded in
    /// the editor's canonical menu-bar + draggable-panel chrome.
    /// </summary>
    public static partial class TimeWeatherEditorUIBuilder
    {
        // ── Palette ───────────────────────────────────────────────────────────────

        private static readonly Color TRACK_COLOR     = new Color(0.50f, 0.54f, 0.62f, 0.95f);
        private static readonly Color TICK_COLOR      = new Color(0.65f, 0.69f, 0.78f, 0.85f);
        private static readonly Color HANDLE_COLOR    = new Color(0.95f, 0.78f, 0.40f, 1.00f);
        private static readonly Color HANDLE_HOVER    = new Color(1.00f, 0.85f, 0.50f, 1.00f);
        private static readonly Color HANDLE_PRESS    = new Color(1.00f, 0.92f, 0.60f, 1.00f);
        private static readonly Color VALUE_COLOR     = new Color(0.95f, 0.78f, 0.40f, 1.00f);

        private static readonly Color ROW_BG          = new Color(0.10f, 0.12f, 0.18f, 0.85f);
        private static readonly Color ROW_BG_HOVER    = new Color(0.20f, 0.22f, 0.30f, 0.95f);
        private static readonly Color ROW_BG_ACTIVE   = new Color(0.95f, 0.78f, 0.40f, 1.00f);
        private static readonly Color ROW_LABEL       = new Color(0.92f, 0.94f, 0.98f, 1.00f);
        private static readonly Color ROW_LABEL_ON    = new Color(0.10f, 0.10f, 0.12f, 1.00f);
        private static readonly Color OFF_ROW_ON      = new Color(0.55f, 0.58f, 0.65f, 1.00f);

        // Per-phase swatch colors used in the cycle and settings panels.
        private static readonly Color PHASE_DAWN_TINT  = new Color(0.85f, 0.88f, 0.98f, 1f);
        private static readonly Color PHASE_DAY_TINT   = new Color(1.00f, 1.00f, 1.00f, 1f);
        private static readonly Color PHASE_DUSK_TINT  = new Color(0.92f, 0.85f, 0.95f, 1f);
        private static readonly Color PHASE_NIGHT_TINT = new Color(0.55f, 0.65f, 1.00f, 1f);

        // Per-weather accent (used by the toggle row swatch + ON-state tint).
        private static readonly Color WEATHER_WIND_TINT = new Color(0.78f, 0.85f, 0.95f, 1f);
        private static readonly Color WEATHER_RAIN_TINT = new Color(0.45f, 0.65f, 0.95f, 1f);
        private static readonly Color WEATHER_SNOW_TINT = new Color(0.95f, 0.95f, 1.00f, 1f);

        // ── Cycle / Weather row entries ───────────────────────────────────────────
        // Order is the source of truth shared with the editor's logic partials.

        public struct CycleRow { public string Label; public string HourText; public Color Swatch; }
        public struct WeatherRow { public string Label; public string Icon; public Color Accent; }

        public static readonly CycleRow[] CYCLE_ROWS = new[]
        {
            new CycleRow { Label = "Dawn",     HourText = "05:30", Swatch = PHASE_DAWN_TINT  },
            new CycleRow { Label = "Morning",  HourText = "09:00", Swatch = PHASE_DAY_TINT   },
            new CycleRow { Label = "Noon",     HourText = "12:00", Swatch = PHASE_DAY_TINT   },
            new CycleRow { Label = "Dusk",     HourText = "18:30", Swatch = PHASE_DUSK_TINT  },
            new CycleRow { Label = "Midnight", HourText = "00:00", Swatch = PHASE_NIGHT_TINT },
        };

        public static readonly WeatherRow[] WEATHER_ROWS = new[]
        {
            new WeatherRow { Label = "Wind", Icon = "≈", Accent = WEATHER_WIND_TINT },
            new WeatherRow { Label = "Rain", Icon = "/", Accent = WEATHER_RAIN_TINT },
            new WeatherRow { Label = "Snow", Icon = "*", Accent = WEATHER_SNOW_TINT },
        };

        // ── Settings tabs / sliders metadata (shared with the logic partial) ─────

        public struct SettingsTab { public string Label; public Color Swatch; }

        public static readonly SettingsTab[] SETTINGS_TABS = new[]
        {
            new SettingsTab { Label = "Day",   Swatch = PHASE_DAY_TINT   },
            new SettingsTab { Label = "Night", Swatch = PHASE_NIGHT_TINT },
        };

        public static readonly string[] SETTINGS_SLIDER_LABELS = { "Hue", "Saturation", "Brightness", "Warmth", "Vignette" };
        public static readonly float[]  SETTINGS_SLIDER_MINS   = {  0f,    0f,           0f,       -1f,       0f    };
        public static readonly float[]  SETTINGS_SLIDER_MAXS   = {  360f,  1f,           1.5f,      1f,       1f    };
        public static readonly string[] SETTINGS_SLIDER_FORMAT = { "{0:0}°", "{0:0.00}",  "{0:0.00}", "{0:+0.00;-0.00;0.00}", "{0:0.00}" };

        // ── Panel: Speed (top-left) ───────────────────────────────────────────────

        private static void BuildSpeedPanel(Transform canvasT, ref UIRefs refs, Action<float> onChanged)
        {
            refs.SpeedDropdown = MakeDrop("TimeWeatherSpeedPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                SPEED_W, SPEED_H, "Speed",
                out var t, out refs.SpeedPanelDrag);

            // Slider host (full width minus padding inside the content area).
            var sliderGo = CreateUI("SpeedSliderHost", t);
            sliderGo.AddComponent<LayoutElement>().preferredHeight = 26f;

            // Track
            var trackGo = CreateUI("Track", sliderGo.transform);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin        = new Vector2(0f, 0.5f);
            trackRt.anchorMax        = new Vector2(1f, 0.5f);
            trackRt.pivot            = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta        = new Vector2(0f, 1.5f);
            trackRt.anchoredPosition = Vector2.zero;
            var trackImg              = trackGo.AddComponent<Image>();
            trackImg.color            = TRACK_COLOR;
            trackImg.raycastTarget    = false;

            // Tick marks at 7 preset positions.
            int n = 7;
            for (int i = 0; i < n; i++)
            {
                float u = (n == 1) ? 0.5f : (float)i / (n - 1);
                var tickGo = CreateUI($"Tick_{i}", sliderGo.transform);
                var tickRt = tickGo.GetComponent<RectTransform>();
                tickRt.anchorMin        = new Vector2(u, 0.5f);
                tickRt.anchorMax        = new Vector2(u, 0.5f);
                tickRt.pivot            = new Vector2(0.5f, 0.5f);
                tickRt.sizeDelta        = new Vector2(1.5f, 6f);
                tickRt.anchoredPosition = Vector2.zero;
                var tickImg              = tickGo.AddComponent<Image>();
                tickImg.color            = TICK_COLOR;
                tickImg.raycastTarget    = false;
            }

            // Slide area + handle.
            var slideAreaGo = CreateUI("HandleSlideArea", sliderGo.transform);
            var slideAreaRt = slideAreaGo.GetComponent<RectTransform>();
            slideAreaRt.anchorMin = new Vector2(0f, 0f);
            slideAreaRt.anchorMax = new Vector2(1f, 1f);
            slideAreaRt.offsetMin = Vector2.zero;
            slideAreaRt.offsetMax = Vector2.zero;

            var handleGo = CreateUI("Handle", slideAreaGo.transform);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta      = new Vector2(13f, 14f);
            refs.SpeedHandleImg     = handleGo.AddComponent<Image>();
            refs.SpeedHandleImg.color        = HANDLE_COLOR;
            refs.SpeedHandleImg.raycastTarget = true;

            refs.SpeedSlider                  = sliderGo.AddComponent<Slider>();
            refs.SpeedSlider.fillRect         = null;
            refs.SpeedSlider.handleRect       = handleRt;
            refs.SpeedSlider.targetGraphic    = refs.SpeedHandleImg;
            refs.SpeedSlider.direction        = Slider.Direction.LeftToRight;
            refs.SpeedSlider.minValue         = 0;
            refs.SpeedSlider.maxValue         = n - 1;
            refs.SpeedSlider.wholeNumbers     = true;
            refs.SpeedSlider.value            = 0;

            var c = refs.SpeedSlider.colors;
            c.normalColor      = HANDLE_COLOR;
            c.highlightedColor = HANDLE_HOVER;
            c.pressedColor     = HANDLE_PRESS;
            c.selectedColor    = HANDLE_COLOR;
            c.fadeDuration     = 0.06f;
            refs.SpeedSlider.colors = c;
            refs.SpeedSlider.onValueChanged.AddListener(v => onChanged?.Invoke(v));

            // "Nx" value readout.
            var valGo = CreateUI("Value", t);
            valGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.SpeedValueTmp                = valGo.AddComponent<TextMeshProUGUI>();
            refs.SpeedValueTmp.text           = "1x";
            refs.SpeedValueTmp.fontSize       = 11f;
            refs.SpeedValueTmp.fontStyle      = FontStyles.Bold;
            refs.SpeedValueTmp.alignment      = TextAlignmentOptions.Center;
            refs.SpeedValueTmp.color          = VALUE_COLOR;
            refs.SpeedValueTmp.raycastTarget  = false;

            refs.SpeedDropdown.SetActive(false);
        }

        // ── Panel: Cycle (top-left, beside speed) ────────────────────────────────

        private static void BuildCyclePanel(Transform canvasT, ref UIRefs refs,
            Action[] onRowClicked, Action onOffClicked)
        {
            float xOff = PANEL_GAP + SPEED_W + PANEL_GAP;
            refs.CycleDropdown = MakeDrop("TimeWeatherCyclePanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET,
                CYCLE_W, CYCLE_H, "Phases",
                out var t, out refs.CyclePanelDrag);

            int n = CYCLE_ROWS.Length;
            refs.CycleRowBgs    = new Image[n];
            refs.CycleRowLabels = new TextMeshProUGUI[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                BuildPhaseRow(t, CYCLE_ROWS[i], () => onRowClicked?[idx]?.Invoke(),
                    out var img, out var lbl);
                refs.CycleRowBgs[i]    = img;
                refs.CycleRowLabels[i] = lbl;
            }

            AddInlineSeparator(t);

            BuildOffRow(t, "OFF · NO FILTER", onOffClicked,
                out refs.CycleOffImg, out refs.CycleOffTmp);

            refs.CycleDropdown.SetActive(false);
        }

        // ── Panel: Weather (top-right) ────────────────────────────────────────────

        private static void BuildWeatherPanel(Transform canvasT, ref UIRefs refs,
            Action[] onRowClicked, Action onOffClicked)
        {
            refs.WeatherDropdown = MakeDrop("TimeWeatherWeatherPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                WEATHER_W, WEATHER_H, "Weather",
                out var t, out refs.WeatherPanelDrag);

            // Which zone the rows are editing. Not decoration: a click applies to the zone the
            // player is standing in, so without this the author sets "Rain HEAVY", walks thirty
            // units, watches the world clear itself and reads it as a bug.
            refs.WeatherZoneTmp = BuildWeatherZoneLine(t);
            AddInlineSeparator(t);

            int n = WEATHER_ROWS.Length;
            refs.WeatherRowBgs    = new Image[n];
            refs.WeatherRowLabels = new TextMeshProUGUI[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                BuildWeatherRow(t, WEATHER_ROWS[i], () => onRowClicked?[idx]?.Invoke(),
                    out var img, out var lbl);
                refs.WeatherRowBgs[i]    = img;
                refs.WeatherRowLabels[i] = lbl;
            }

            AddInlineSeparator(t);

            BuildOffRow(t, "OFF · CLEAR", onOffClicked,
                out refs.WeatherOffImg, out refs.WeatherOffTmp);

            refs.WeatherDropdown.SetActive(false);
        }

        // ── Panel: Settings (bottom-right — phase tuning) ────────────────────────

        private static void BuildSettingsPanel(Transform canvasT, ref UIRefs refs,
            Action<int> onTabClicked, Action<int, float> onSliderChanged,
            Action onResetClicked, Action onNeutroClicked)
        {
            refs.SettingsDropdown = MakeDrop("TimeWeatherSettingsPanel", canvasT,
                PanelDock.BottomRight, PANEL_GAP, PANEL_GAP,
                SETTINGS_W, SETTINGS_H, "Phase Settings",
                out var t, out refs.SettingsPanelDrag);

            // Tabs row
            var tabsGo = CreateUI("Tabs", t);
            tabsGo.AddComponent<LayoutElement>().preferredHeight = 24f;
            var tabsHl = tabsGo.AddComponent<HorizontalLayoutGroup>();
            tabsHl.spacing                = 4f;
            tabsHl.childAlignment         = TextAnchor.MiddleCenter;
            tabsHl.childForceExpandWidth  = true;
            tabsHl.childForceExpandHeight = true;
            tabsHl.childControlWidth      = true;
            tabsHl.childControlHeight     = true;

            int nTabs = SETTINGS_TABS.Length;
            refs.SettingsTabImgs = new Image[nTabs];
            for (int i = 0; i < nTabs; i++)
            {
                int idx = i;
                BuildSettingsTab(tabsGo.transform, SETTINGS_TABS[i],
                    () => onTabClicked?.Invoke(idx),
                    out refs.SettingsTabImgs[i]);
            }

            // Selected name + swatch
            var nameRow = CreateUI("NameRow", t);
            nameRow.AddComponent<LayoutElement>().preferredHeight = 18f;
            var nameHl = nameRow.AddComponent<HorizontalLayoutGroup>();
            nameHl.spacing                = 6f;
            nameHl.padding                = new RectOffset(4, 4, 0, 0);
            nameHl.childForceExpandWidth  = false;
            nameHl.childForceExpandHeight = true;
            nameHl.childControlWidth      = true;
            nameHl.childControlHeight     = true;
            nameHl.childAlignment         = TextAnchor.MiddleLeft;

            var swGo = CreateUI("Swatch", nameRow.transform);
            swGo.AddComponent<LayoutElement>().preferredWidth = 14f;
            refs.SettingsSwatchImg       = swGo.AddComponent<Image>();
            refs.SettingsSwatchImg.color = Color.white;

            var nameGo = CreateUI("Name", nameRow.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.SettingsNameTmp                = nameGo.AddComponent<TextMeshProUGUI>();
            refs.SettingsNameTmp.text           = "—";
            refs.SettingsNameTmp.fontSize       = 12f;
            refs.SettingsNameTmp.fontStyle      = FontStyles.Bold;
            refs.SettingsNameTmp.color          = VALUE_COLOR;
            refs.SettingsNameTmp.alignment      = TextAlignmentOptions.MidlineLeft;
            refs.SettingsNameTmp.enableWordWrapping = false;

            AddInlineSeparator(t);

            // 5 sliders
            int nSliders = SETTINGS_SLIDER_LABELS.Length;
            refs.SettingsSliders = new Slider[nSliders];
            refs.SettingsValues  = new TextMeshProUGUI[nSliders];
            for (int i = 0; i < nSliders; i++)
            {
                int idx = i;
                BuildSettingsSlider(t, i, v => onSliderChanged?.Invoke(idx, v),
                    out refs.SettingsSliders[i], out refs.SettingsValues[i]);
            }

            AddInlineSeparator(t);

            // Preset buttons
            var presetRow = CreateUI("Presets", t);
            presetRow.AddComponent<LayoutElement>().preferredHeight = 22f;
            var presetHl = presetRow.AddComponent<HorizontalLayoutGroup>();
            presetHl.spacing                = 4f;
            presetHl.childAlignment         = TextAnchor.MiddleCenter;
            presetHl.childForceExpandWidth  = true;
            presetHl.childForceExpandHeight = true;
            presetHl.childControlWidth      = true;
            presetHl.childControlHeight     = true;

            BuildPresetBtn(presetRow.transform, "DEFAULT", onResetClicked);
            BuildPresetBtn(presetRow.transform, "NEUTRAL", onNeutroClicked);

            refs.SettingsDropdown.SetActive(false);
        }

        // ── Helper: phase shortcut row ────────────────────────────────────────────

        private static void BuildPhaseRow(Transform parent, CycleRow row, Action onClick,
            out Image img, out TextMeshProUGUI lbl)
        {
            var rowGo = CreateUI($"Row_{row.Label}", parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 26f;

            img = rowGo.AddComponent<Image>();
            img.color         = ROW_BG;
            img.raycastTarget = true;

            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_BG;
            c.highlightedColor = ROW_BG_HOVER;
            c.pressedColor     = ROW_BG_ACTIVE;
            c.selectedColor    = ROW_BG;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            // Swatch
            var swGo = CreateUI("Swatch", rowGo.transform);
            var swRt = swGo.GetComponent<RectTransform>();
            swRt.anchorMin = new Vector2(0f, 0f);
            swRt.anchorMax = new Vector2(0f, 1f);
            swRt.pivot     = new Vector2(0f, 0.5f);
            swRt.sizeDelta = new Vector2(4f, 0f);
            swRt.anchoredPosition = Vector2.zero;
            var swImg     = swGo.AddComponent<Image>();
            swImg.color   = row.Swatch;
            swImg.raycastTarget = false;

            // Label
            var lblGo = CreateUI("Label", rowGo.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.offsetMin = new Vector2(10f, 0f);
            lblRt.offsetMax = new Vector2(-44f, 0f);
            lbl                       = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text                  = row.Label;
            lbl.fontSize              = 11f;
            lbl.fontStyle             = FontStyles.Bold;
            lbl.alignment             = TextAlignmentOptions.MidlineLeft;
            lbl.color                 = ROW_LABEL;
            lbl.enableWordWrapping    = false;
            lbl.overflowMode          = TextOverflowModes.Ellipsis;
            lbl.raycastTarget         = false;

            // Hour suffix
            var hrGo = CreateUI("Hour", rowGo.transform);
            var hrRt = hrGo.GetComponent<RectTransform>();
            hrRt.anchorMin = new Vector2(1f, 0f);
            hrRt.anchorMax = new Vector2(1f, 1f);
            hrRt.pivot     = new Vector2(1f, 0.5f);
            hrRt.sizeDelta = new Vector2(40f, 0f);
            hrRt.anchoredPosition = new Vector2(-6f, 0f);
            var hrTmp     = hrGo.AddComponent<TextMeshProUGUI>();
            hrTmp.text    = row.HourText;
            hrTmp.fontSize = 9f;
            hrTmp.alignment = TextAlignmentOptions.MidlineRight;
            hrTmp.color   = new Color(0.78f, 0.82f, 0.88f, 0.85f);
            hrTmp.raycastTarget = false;
        }

        // ── Helper: weather toggle row ────────────────────────────────────────────

        /// <summary>The "editing: &lt;zone&gt;" line at the top of the weather panel.</summary>
        private static TextMeshProUGUI BuildWeatherZoneLine(Transform parent)
        {
            var go = CreateUI("ZoneLine", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 16f;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text                = "—";
            tmp.fontSize            = 10f;
            tmp.fontStyle           = FontStyles.Bold;
            tmp.alignment           = TextAlignmentOptions.MidlineLeft;
            tmp.color               = ROW_LABEL;
            tmp.margin              = new Vector4(8f, 0f, 4f, 0f);
            tmp.enableWordWrapping  = false;
            tmp.overflowMode        = TextOverflowModes.Ellipsis;
            tmp.raycastTarget       = false;
            return tmp;
        }

        private static void BuildWeatherRow(Transform parent, WeatherRow row, Action onClick,
            out Image img, out TextMeshProUGUI lbl)
        {
            var rowGo = CreateUI($"Row_{row.Label}", parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 36f;

            img = rowGo.AddComponent<Image>();
            img.color         = ROW_BG;
            img.raycastTarget = true;

            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_BG;
            c.highlightedColor = ROW_BG_HOVER;
            c.pressedColor     = ROW_BG_ACTIVE;
            c.selectedColor    = ROW_BG;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            // Accent swatch
            var swGo = CreateUI("Swatch", rowGo.transform);
            var swRt = swGo.GetComponent<RectTransform>();
            swRt.anchorMin = new Vector2(0f, 0f);
            swRt.anchorMax = new Vector2(0f, 1f);
            swRt.pivot     = new Vector2(0f, 0.5f);
            swRt.sizeDelta = new Vector2(4f, 0f);
            swRt.anchoredPosition = Vector2.zero;
            var swImg     = swGo.AddComponent<Image>();
            swImg.color   = row.Accent;
            swImg.raycastTarget = false;

            // Icon
            var icoGo = CreateUI("Icon", rowGo.transform);
            var icoRt = icoGo.GetComponent<RectTransform>();
            icoRt.anchorMin = new Vector2(0f, 0f);
            icoRt.anchorMax = new Vector2(0f, 1f);
            icoRt.pivot     = new Vector2(0f, 0.5f);
            icoRt.sizeDelta = new Vector2(28f, 0f);
            icoRt.anchoredPosition = new Vector2(8f, 0f);
            var icoTmp     = icoGo.AddComponent<TextMeshProUGUI>();
            icoTmp.text    = row.Icon;
            icoTmp.fontSize = 18f;
            icoTmp.fontStyle = FontStyles.Bold;
            icoTmp.alignment = TextAlignmentOptions.Center;
            icoTmp.color   = row.Accent;
            icoTmp.raycastTarget = false;

            // Label
            var lblGo = CreateUI("Label", rowGo.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.offsetMin = new Vector2(40f, 0f);
            lblRt.offsetMax = new Vector2(-6f, 0f);
            lbl             = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text        = row.Label;
            lbl.fontSize    = 12f;
            lbl.fontStyle   = FontStyles.Bold;
            lbl.alignment   = TextAlignmentOptions.MidlineLeft;
            lbl.color       = ROW_LABEL;
            lbl.raycastTarget = false;
        }

        // ── Helper: OFF row (used by Cycle and Weather panels) ────────────────────

        private static void BuildOffRow(Transform parent, string label, Action onClick,
            out Image img, out TextMeshProUGUI tmp)
        {
            var go = CreateUI("OffRow", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 22f;

            img = go.AddComponent<Image>();
            img.color         = ROW_BG;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_BG;
            c.highlightedColor = ROW_BG_HOVER;
            c.pressedColor     = OFF_ROW_ON;
            c.selectedColor    = ROW_BG;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            tmp                  = AddCenteredText(go.transform, label, 10f, FontStyles.Bold, ROW_LABEL);
            tmp.alignment        = TextAlignmentOptions.Center;
            tmp.characterSpacing = 1.5f;
        }

        // ── Helper: settings tab ──────────────────────────────────────────────────

        private static void BuildSettingsTab(Transform parent, SettingsTab tab, Action onClick,
            out Image img)
        {
            var go = CreateUI($"Tab_{tab.Label}", parent);
            img = go.AddComponent<Image>();
            img.color         = ROW_BG;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_BG;
            c.highlightedColor = ROW_BG_HOVER;
            c.pressedColor     = ROW_BG_ACTIVE;
            c.selectedColor    = ROW_BG;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var lbl              = AddCenteredText(go.transform, tab.Label, 10f, FontStyles.Bold, ROW_LABEL);
            lbl.alignment        = TextAlignmentOptions.Center;
            lbl.characterSpacing = 1.5f;
            lbl.raycastTarget    = false;
        }

        // ── Helper: settings slider with label + value ────────────────────────────

        private static void BuildSettingsSlider(Transform parent, int idx, Action<float> onChanged,
            out Slider slider, out TextMeshProUGUI valTmp)
        {
            // Label row
            var lblGo = CreateUI($"Slider_{idx}_Lbl", parent);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var hlg = lblGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var nameGo = CreateUI("Name", lblGo.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameTmp     = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text    = SETTINGS_SLIDER_LABELS[idx];
            nameTmp.fontSize = 10f;
            nameTmp.color   = ROW_LABEL;
            nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
            nameTmp.raycastTarget = false;

            var valGo = CreateUI("Val", lblGo.transform);
            valGo.AddComponent<LayoutElement>().preferredWidth = 60f;
            valTmp           = valGo.AddComponent<TextMeshProUGUI>();
            valTmp.text      = "—";
            valTmp.fontSize  = 10f;
            valTmp.fontStyle = FontStyles.Bold;
            valTmp.color     = VALUE_COLOR;
            valTmp.alignment = TextAlignmentOptions.MidlineRight;
            valTmp.raycastTarget = false;

            // Slider
            var sliderHostGo = CreateUI($"Slider_{idx}", parent);
            sliderHostGo.AddComponent<LayoutElement>().preferredHeight = 16f;

            var trackGo = CreateUI("Track", sliderHostGo.transform);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin        = new Vector2(0f, 0.5f);
            trackRt.anchorMax        = new Vector2(1f, 0.5f);
            trackRt.pivot            = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta        = new Vector2(0f, 1.5f);
            trackRt.anchoredPosition = Vector2.zero;
            var trackImg              = trackGo.AddComponent<Image>();
            trackImg.color            = TRACK_COLOR;
            trackImg.raycastTarget    = false;

            var slideAreaGo = CreateUI("HandleSlideArea", sliderHostGo.transform);
            var slideAreaRt = slideAreaGo.GetComponent<RectTransform>();
            slideAreaRt.anchorMin = new Vector2(0f, 0f);
            slideAreaRt.anchorMax = new Vector2(1f, 1f);
            slideAreaRt.offsetMin = Vector2.zero;
            slideAreaRt.offsetMax = Vector2.zero;

            var handleGo = CreateUI("Handle", slideAreaGo.transform);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta      = new Vector2(10f, 12f);
            var handleImg           = handleGo.AddComponent<Image>();
            handleImg.color         = HANDLE_COLOR;
            handleImg.raycastTarget = true;

            slider                  = sliderHostGo.AddComponent<Slider>();
            slider.fillRect         = null;
            slider.handleRect       = handleRt;
            slider.targetGraphic    = handleImg;
            slider.direction        = Slider.Direction.LeftToRight;
            slider.minValue         = SETTINGS_SLIDER_MINS[idx];
            slider.maxValue         = SETTINGS_SLIDER_MAXS[idx];
            slider.wholeNumbers     = false;
            slider.value            = SETTINGS_SLIDER_MINS[idx];
            slider.onValueChanged.AddListener(v => onChanged?.Invoke(v));
        }

        // ── Helper: preset button (DEFECTO / NEUTRO) ──────────────────────────────

        private static void BuildPresetBtn(Transform parent, string label, Action onClick)
        {
            var go = CreateUI($"Preset_{label}", parent);
            var img = go.AddComponent<Image>();
            img.color         = ROW_BG;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_BG;
            c.highlightedColor = ROW_BG_HOVER;
            c.pressedColor     = ROW_BG_ACTIVE;
            c.selectedColor    = ROW_BG;
            c.fadeDuration     = 0.05f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var lbl              = AddCenteredText(go.transform, label, 10f, FontStyles.Bold, ROW_LABEL);
            lbl.alignment        = TextAlignmentOptions.Center;
            lbl.characterSpacing = 1.5f;
            lbl.raycastTarget    = false;
        }

        private static void AddInlineSeparator(Transform parent)
        {
            var go = CreateUI("InlineSep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 6f;
            var img = go.AddComponent<Image>();
            img.color = SEPARATOR;
        }

        // ── Style helpers exposed to the editor for live highlighting ─────────────

        public static void ApplyPhaseRowStyle(Image img, TextMeshProUGUI lbl, bool active)
        {
            if (img == null) return;
            img.color = active ? ROW_BG_ACTIVE : ROW_BG;
            if (lbl != null) lbl.color = active ? ROW_LABEL_ON : ROW_LABEL;
            var btn = img.GetComponent<Button>();
            var c = btn.colors;
            c.normalColor   = active ? ROW_BG_ACTIVE : ROW_BG;
            c.selectedColor = active ? ROW_BG_ACTIVE : ROW_BG;
            btn.colors      = c;
        }

        public static void ApplyWeatherRowStyle(Image img, TextMeshProUGUI lbl, bool active, Color accent)
        {
            if (img == null) return;
            var on = Color.Lerp(ROW_BG_ACTIVE, accent, 0.35f);
            img.color = active ? on : ROW_BG;
            if (lbl != null) lbl.color = active ? ROW_LABEL_ON : ROW_LABEL;
            var btn = img.GetComponent<Button>();
            var c = btn.colors;
            c.normalColor   = active ? on : ROW_BG;
            c.selectedColor = active ? on : ROW_BG;
            btn.colors      = c;
        }

        public static void ApplyOffRowStyle(Image img, TextMeshProUGUI tmp, bool noneActive)
        {
            if (img == null) return;
            img.color = noneActive ? OFF_ROW_ON : ROW_BG;
            if (tmp != null) tmp.color = noneActive ? ROW_LABEL_ON : ROW_LABEL;
            var btn = img.GetComponent<Button>();
            var c = btn.colors;
            c.normalColor   = noneActive ? OFF_ROW_ON : ROW_BG;
            c.selectedColor = noneActive ? OFF_ROW_ON : ROW_BG;
            btn.colors      = c;
        }

        public static void ApplySettingsTabStyle(Image img, bool active)
        {
            if (img == null) return;
            img.color = active ? ROW_BG_ACTIVE : ROW_BG;
            var btn = img.GetComponent<Button>();
            var c = btn.colors;
            c.normalColor   = active ? ROW_BG_ACTIVE : ROW_BG;
            c.selectedColor = active ? ROW_BG_ACTIVE : ROW_BG;
            btn.colors      = c;
        }
    }
}
