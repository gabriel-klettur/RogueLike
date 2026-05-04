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
    /// Lighting Editor panel builders. Each panel mirrors a Python lighting_editor
    /// sub-panel: Modes (toolbar) / Cycle (day-night) / Presets (catalog) / Instances.
    /// </summary>
    public static partial class LightingEditorUIBuilder
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void EnsureFlexibleHeight(GameObject go, float flex = 1f)
        {
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = flex;
        }

        private static Slider AddSlider(Transform parent, float min, float max, float initial,
            Action<float> onChanged, float height = 20f)
        {
            var go = CreateUI("Slider", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var bg          = go.AddComponent<Image>();
            bg.color        = new Color(0.10f, 0.10f, 0.13f, 1f);
            bg.raycastTarget = true;

            var slider = go.AddComponent<Slider>();
            slider.minValue       = min;
            slider.maxValue       = max;
            slider.wholeNumbers   = false;
            slider.value          = Mathf.Clamp(initial, min, max);
            slider.transition     = Selectable.Transition.None;

            // Background image needs explicit fill / handle / target setup so the
            // slider actually reacts to clicks. UGUI requires a fill rect for the
            // value to map visually.
            var fillRectGo = CreateUI("Fill", go.transform);
            var fillRt     = fillRectGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.25f);
            fillRt.anchorMax = new Vector2(1f, 0.75f);
            fillRt.offsetMin = new Vector2(2f, 0f);
            fillRt.offsetMax = new Vector2(-2f, 0f);
            var fillImg = fillRectGo.AddComponent<Image>();
            fillImg.color = ACCENT;

            var handleGo = CreateUI("Handle", go.transform);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(10f, 0f);
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            var handleImg     = handleGo.AddComponent<Image>();
            handleImg.color   = TEXT_PRIMARY;
            slider.targetGraphic = handleImg;
            slider.fillRect      = fillRt;
            slider.handleRect    = handleRt;
            slider.direction     = Slider.Direction.LeftToRight;

            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        private static (TextMeshProUGUI label, Slider slider, TextMeshProUGUI valueTmp)
            AddLabeledSlider(Transform parent, string label, float min, float max, float initial,
                             string suffix, Action<float, TextMeshProUGUI> onChanged)
        {
            var row = CreateUI($"LabRow_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 18f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing                = 6f;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth      = true;
            hl.childControlHeight     = true;
            hl.childAlignment         = TextAnchor.MiddleLeft;

            var lblGo                          = CreateUI("Lbl", row.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 90f;
            var lblTmp                         = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text                        = label;
            lblTmp.fontSize                    = 10f;
            lblTmp.color                       = TEXT_SECONDARY;
            lblTmp.alignment                   = TextAlignmentOptions.Left;
            lblTmp.enableWordWrapping          = false;

            var valGo                          = CreateUI("Val", row.transform);
            valGo.AddComponent<LayoutElement>().preferredWidth = 64f;
            var valTmp                         = valGo.AddComponent<TextMeshProUGUI>();
            valTmp.text                        = $"{initial:0.##}{suffix}";
            valTmp.fontSize                    = 10f;
            valTmp.fontStyle                   = FontStyles.Bold;
            valTmp.color                       = ACCENT;
            valTmp.alignment                   = TextAlignmentOptions.Right;

            var sliderGo = CreateUI("SliderHost", parent);
            var sliderLE = sliderGo.AddComponent<LayoutElement>();
            sliderLE.preferredHeight = 16f;
            var slider = AddSlider(sliderGo.transform, min, max, initial, v => onChanged?.Invoke(v, valTmp));
            return (lblTmp, slider, valTmp);
        }

        // ── Modes Panel (60 px narrow, top-left) ──────────────────────────────────

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onModeSelect, Action onModeSpawn, Action onModeDelete,
            Action onToggleAmbient, Action onTogglePointLights,
            Action onSave, Action onUndo, Action onRedo)
        {
            refs.ModesDropdown = MakeDrop("LightingModesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "Modes",
                out var t, out refs.ModesPanelDrag, narrowPanel: true);

            refs.SelectBtnImg = AddToolBtn(t, "Sel", "ect",     BTN_H, onModeSelect);
            refs.SpawnBtnImg  = AddToolBtn(t, "Spwn", "+",      BTN_H, onModeSpawn);
            refs.DeleteBtnImg = AddDangerToolBtn(t, "Del", "X", BTN_H, onModeDelete);

            AddInlineSeparator(t);
            AddSectionLabel(t, "FX");

            refs.AmbientToggleImg     = AddToolBtn(t, "Amb",   "global",  BTN_H, onToggleAmbient);
            refs.PointLightsToggleImg = AddToolBtn(t, "Lights","points",  BTN_H, onTogglePointLights);

            AddInlineSeparator(t);
            AddSectionLabel(t, "EDIT");

            AddActionBtn(t, "Save", 24f, onSave);
            AddActionBtn(t, "Undo", 24f, onUndo);
            AddActionBtn(t, "Redo", 24f, onRedo);

            refs.ModesDropdown.SetActive(false);
        }

        // ── Cycle Panel (280 px, top-left after Modes) ────────────────────────────

        private static void BuildCyclePanel(Transform canvasT, ref UIRefs refs,
            Action<float> onScrubTime, Action onPause,
            Action<float> onDayLengthChanged, Action<float> onMinIntensityChanged,
            Action onToggleLightsWindow,
            Action<float> onLightsWindowStart, Action<float> onLightsWindowEnd,
            Action onJumpDawn, Action onJumpNoon, Action onJumpDusk, Action onJumpMidnight)
        {
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.CycleDropdown = MakeDrop("LightingCyclePanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET,
                CYCLE_W, CYCLE_H, "Day / Night Cycle",
                out var t, out refs.CyclePanelDrag);

            BuildCycleClockSection(t, ref refs);
            AddInlineSeparator(t);
            BuildCycleScrubAndPauseSection(t, ref refs,
                onScrubTime, onPause, onJumpDawn, onJumpNoon, onJumpDusk, onJumpMidnight);
            AddInlineSeparator(t);
            AddSectionLabel(t, "TIMING");
            BuildCycleTimingSliders(t, ref refs, onDayLengthChanged, onMinIntensityChanged);
            AddInlineSeparator(t);
            AddSectionLabel(t, "POINT-LIGHT WINDOW (off during daytime)");
            BuildCycleLightsWindowSection(t, ref refs,
                onToggleLightsWindow, onLightsWindowStart, onLightsWindowEnd);

            refs.CycleDropdown.SetActive(false);
        }

        private static void BuildCycleClockSection(Transform t, ref UIRefs refs)
        {
            var clockGo = CreateUI("Clock", t);
            clockGo.AddComponent<LayoutElement>().preferredHeight = 30f;
            var clockTmp                      = clockGo.AddComponent<TextMeshProUGUI>();
            clockTmp.text                     = "12:00 — Day";
            clockTmp.fontSize                 = 18f;
            clockTmp.fontStyle                = FontStyles.Bold;
            clockTmp.alignment                = TextAlignmentOptions.Center;
            clockTmp.color                    = ACCENT;
            refs.ClockText = clockTmp;

            var hintGo = CreateUI("PhaseHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var hintTmp                       = hintGo.AddComponent<TextMeshProUGUI>();
            hintTmp.text                      = "Bright daylight phase";
            hintTmp.fontSize                  = 9f;
            hintTmp.fontStyle                 = FontStyles.Italic;
            hintTmp.alignment                 = TextAlignmentOptions.Center;
            hintTmp.color                     = TEXT_MUTED;
            refs.PhaseHintText = hintTmp;
        }

        private static void BuildCycleScrubAndPauseSection(Transform t, ref UIRefs refs,
            Action<float> onScrubTime, Action onPause,
            Action onJumpDawn, Action onJumpNoon, Action onJumpDusk, Action onJumpMidnight)
        {
            AddSectionLabel(t, "TIME OF DAY (00:00 → 23:59)");
            refs.TimeScrubSlider = AddSlider(t, 0f, 1f, 0.5f, v => onScrubTime?.Invoke(v), 18f);

            var jumpRow = CreateUI("JumpRow", t);
            jumpRow.AddComponent<LayoutElement>().preferredHeight = 26f;
            var jhl = jumpRow.AddComponent<HorizontalLayoutGroup>();
            jhl.spacing               = 4f;
            jhl.childForceExpandWidth = true;
            jhl.childAlignment        = TextAnchor.MiddleCenter;
            AddJumpBtn(jumpRow.transform, "Dawn",     onJumpDawn);
            AddJumpBtn(jumpRow.transform, "Noon",     onJumpNoon);
            AddJumpBtn(jumpRow.transform, "Dusk",     onJumpDusk);
            AddJumpBtn(jumpRow.transform, "Midnight", onJumpMidnight);

            var pauseHost = CreateUI("PauseRow", t);
            pauseHost.AddComponent<LayoutElement>().preferredHeight = 28f;
            var phl = pauseHost.AddComponent<HorizontalLayoutGroup>();
            phl.spacing               = 0f;
            phl.childForceExpandWidth = true;
            phl.childAlignment        = TextAnchor.MiddleCenter;

            var pauseGo = CreateUI("PauseBtn", pauseHost.transform);
            pauseGo.AddComponent<LayoutElement>().preferredHeight = 26f;
            var pauseImg               = pauseGo.AddComponent<Image>();
            pauseImg.color             = BTN_NORMAL;
            var pauseBtn               = pauseGo.AddComponent<Button>();
            var pc                     = pauseBtn.colors;
            pc.normalColor             = BTN_NORMAL;
            pc.highlightedColor        = BTN_HOVER;
            pc.pressedColor            = BTN_ACTIVE;
            pauseBtn.colors            = pc;
            pauseBtn.targetGraphic     = pauseImg;
            if (onPause != null) pauseBtn.onClick.AddListener(() => onPause.Invoke());
            var pauseTmp               = AddCenteredText(pauseGo.transform, "PAUSE CYCLE", 11f, FontStyles.Bold, TEXT_PRIMARY);
            pauseTmp.alignment         = TextAlignmentOptions.Center;
            refs.PauseBtnImg           = pauseImg;
            refs.PauseBtnTmp           = pauseTmp;
        }

        private static void BuildCycleTimingSliders(Transform t, ref UIRefs refs,
            Action<float> onDayLengthChanged, Action<float> onMinIntensityChanged)
        {
            // Day length slider (60..7200 real seconds per day)
            var dl = AddLabeledSlider(t, "Day length", 60f, 7200f, 3600f, "s/day",
                (v, tmp) => { tmp.text = $"{v:0}s"; onDayLengthChanged?.Invoke(v); });
            refs.DayLengthSlider = dl.slider;
            refs.DayLengthTmp    = dl.valueTmp;

            var mi = AddLabeledSlider(t, "Min intensity", 0f, 1f, 0.20f, "",
                (v, tmp) => { tmp.text = $"{v:0.00}"; onMinIntensityChanged?.Invoke(v); });
            refs.MinIntensitySlider = mi.slider;
            refs.MinIntensityTmp    = mi.valueTmp;
        }

        private static void BuildCycleLightsWindowSection(Transform t, ref UIRefs refs,
            Action onToggleLightsWindow,
            Action<float> onLightsWindowStart, Action<float> onLightsWindowEnd)
        {
            var twGo                          = CreateUI("WindowToggle", t);
            twGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var twImg                         = twGo.AddComponent<Image>();
            twImg.color                       = CYCLE_TOGGLE_ON;
            var twBtn                         = twGo.AddComponent<Button>();
            var twC                           = twBtn.colors;
            twC.normalColor                   = twImg.color;
            twC.highlightedColor              = BTN_HOVER;
            twC.pressedColor                  = BTN_ACTIVE;
            twBtn.colors                      = twC;
            twBtn.targetGraphic               = twImg;
            if (onToggleLightsWindow != null) twBtn.onClick.AddListener(() => onToggleLightsWindow.Invoke());
            AddCenteredText(twGo.transform, "Window enabled", 10f, FontStyles.Bold, TEXT_PRIMARY);
            refs.LightsWindowToggleImg = twImg;

            var rangeGo                       = CreateUI("WindowRange", t);
            rangeGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var rangeTmp                      = rangeGo.AddComponent<TextMeshProUGUI>();
            rangeTmp.text                     = "08:45 → 20:45";
            rangeTmp.fontSize                 = 10f;
            rangeTmp.alignment                = TextAlignmentOptions.Center;
            rangeTmp.color                    = TEXT_SECONDARY;
            refs.LightsWindowRangeTmp         = rangeTmp;

            refs.LightsWindowStartSlider = AddSlider(t, 0f, 1f, DayNightCycle.DefaultLightsOffStartNormalized,
                v => onLightsWindowStart?.Invoke(v), 16f);
            refs.LightsWindowEndSlider   = AddSlider(t, 0f, 1f, DayNightCycle.DefaultLightsOffEndNormalized,
                v => onLightsWindowEnd?.Invoke(v), 16f);
        }

        private static void AddJumpBtn(Transform parent, string label, Action onClick)
        {
            var go = CreateUI($"Jump_{label}", parent);
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());
            AddCenteredText(go.transform, label, 9f, FontStyles.Bold, TEXT_PRIMARY).alignment = TextAlignmentOptions.Center;
        }

        // ── Presets Panel (256 px, top-right) ─────────────────────────────────────

        private static void BuildPresetsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            refs.PresetsDropdown = MakeDrop("LightingPresetsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PRESETS_W, PRESETS_H, "Light Presets",
                out var t, out refs.PresetsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search presets…", onSearchChanged);

            // Preset list (vertical buttons rather than a grid: presets are few and have long names).
            var listHost = CreateUI("PresetList", t);
            EnsureFlexibleHeight(listHost, 1f);
            var listLE = listHost.GetComponent<LayoutElement>();
            listLE.minHeight = 120f;
            var listImg              = listHost.AddComponent<Image>();
            listImg.color            = new Color(0f, 0f, 0f, 0.18f);

            var (scroll, listContent) = EditorUIHelpers.MakeScrollView(listHost.transform, "PresetScroll");
            EnsureFlexibleHeight(scroll.gameObject, 1f);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            // Add a VLG to the scroll content so dynamically-added preset buttons stack.
            var contentVlg = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing                = 2f;
            contentVlg.childForceExpandWidth  = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.childControlWidth      = true;
            contentVlg.childControlHeight     = true;
            contentVlg.padding                = new RectOffset(4, 4, 4, 4);
            var contentFitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            refs.PresetGrid = listContent;

            AddInlineSeparator(t);

            // Selected preset properties
            var titleGo                  = CreateUI("PresetTitle", t);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var titleTmp                 = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text                = "(no preset selected)";
            titleTmp.fontSize            = 12f;
            titleTmp.fontStyle           = FontStyles.Bold;
            titleTmp.alignment           = TextAlignmentOptions.Center;
            titleTmp.color               = ACCENT;
            titleTmp.enableWordWrapping  = false;
            titleTmp.overflowMode        = TextOverflowModes.Ellipsis;
            refs.PresetTitle             = titleTmp;

            var bodyGo                   = CreateUI("PresetBody", t);
            var bodyLE                   = bodyGo.AddComponent<LayoutElement>();
            bodyLE.preferredHeight       = 170f;
            bodyLE.minHeight             = 120f;
            var bodyTmp                  = bodyGo.AddComponent<TextMeshProUGUI>();
            bodyTmp.text                 = "Pick a preset above to inspect its properties.";
            bodyTmp.fontSize             = 11f;
            bodyTmp.alignment            = TextAlignmentOptions.TopLeft;
            bodyTmp.color                = TEXT_PRIMARY;
            bodyTmp.enableWordWrapping   = true;
            bodyTmp.richText             = true;
            bodyTmp.margin               = new Vector4(6f, 4f, 6f, 4f);
            refs.PresetBody              = bodyTmp;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);
            refs.StatusText.text = "Lighting Editor active. Pick a preset and click on the map to drop a light.";

            refs.PresetsDropdown.SetActive(false);
        }

        // ── Instances Panel (280 px, bottom-right) ────────────────────────────────

        private static void BuildInstancesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.InstancesDropdown = MakeDrop("LightingInstancesPanel", canvasT,
                PanelDock.BottomRight, PANEL_GAP, PANEL_GAP,
                INSTANCES_W, INSTANCES_H, "Instances",
                out var t, out refs.InstancesPanelDrag);

            // Count strip
            var countGo                          = CreateUI("InstancesCount", t);
            countGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var countTmp                         = countGo.AddComponent<TextMeshProUGUI>();
            countTmp.text                        = "0 lights spawned";
            countTmp.fontSize                    = 10f;
            countTmp.alignment                   = TextAlignmentOptions.Center;
            countTmp.color                       = TEXT_SECONDARY;
            refs.InstancesCountTmp               = countTmp;

            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "InstancesScroll");
            EnsureFlexibleHeight(scroll.gameObject, 1f);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            // Add VLG + ContentSizeFitter so dynamically added rows stack and the
            // scroll content grows with them.
            var contentVlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing                = 2f;
            contentVlg.childForceExpandWidth  = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.childControlWidth      = true;
            contentVlg.childControlHeight     = true;
            contentVlg.padding                = new RectOffset(4, 4, 4, 4);
            var contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            refs.InstancesListContent = content;

            // Empty-state hint
            var hintGo                                            = CreateUI("InstancesHint", content);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 80f;
            var hintTmp                                          = hintGo.AddComponent<TextMeshProUGUI>();
            hintTmp.text                                         = "(no lights placed yet)\n\nUse Spawn mode to drop lights.\nClick a row to focus the camera on it.";
            hintTmp.fontSize                                     = 10f;
            hintTmp.fontStyle                                    = FontStyles.Italic;
            hintTmp.alignment                                    = TextAlignmentOptions.Center;
            hintTmp.color                                        = TEXT_MUTED;
            hintTmp.enableWordWrapping                           = true;
            refs.InstancesHint = hintTmp;

            refs.InstancesDropdown.SetActive(false);
        }
    }
}
