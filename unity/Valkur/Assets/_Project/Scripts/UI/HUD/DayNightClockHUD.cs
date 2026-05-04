using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Sundial-style HUD widget that surfaces the live <see cref="DayNightCycle"/>
    /// to the player. Anchored top-right of the screen, it shows:
    ///   • A 24-hour ring whose colored arc grows clockwise from 00:00 → current time.
    ///   • A central icon (sun during 06:00–18:00, moon otherwise) tinted by phase.
    ///   • A digital "HH:MM" readout.
    ///   • A short phase label (Dawn / Day / Dusk / Night).
    ///   • A horizontal speed slider with 7 discrete steps (1× / 2× / 5× / 10× /
    ///     20× / 50× / 100×) that scales the cycle's
    ///     <see cref="DayNightCycle.RealSecondsPerDay"/>. Only the day/night clock
    ///     is accelerated — gameplay timing (combat, AI, animations) stays at 1×.
    ///
    /// Reads <see cref="DayNightCycle.Instance"/> by polling each frame — no event
    /// subscription, so no teardown / unsubscribe surface to maintain.
    /// </summary>
    public sealed partial class DayNightClockHUD : MonoBehaviour
    {
        // ── Layout constants ─────────────────────────────────────────────────
        private const float WIDGET_SIZE       = 110f;
        private const float MARGIN_RIGHT      = 24f;
        private const float MARGIN_TOP        = 24f;
        private const float DIAL_SIZE         = 92f;
        private const float ICON_SIZE         = 44f;
        private const float CLOCK_ROW_H       = 22f;
        private const float PHASE_ROW_H       = 14f;
        private const float SLIDER_ROW_H      = 22f;
        private const float SPEED_LABEL_H     = 14f;
        private const float SLIDER_GAP_TOP    = 4f;
        private const float BOTTOM_BG_PAD     = 4f;
        private const float SLIDER_INSET_X    = 12f;     // keeps the handle inside the BG edges
        private static readonly float BOTTOM_BG_H = CLOCK_ROW_H + PHASE_ROW_H +
            SLIDER_GAP_TOP + SLIDER_ROW_H + SPEED_LABEL_H + BOTTOM_BG_PAD * 2f;
        private static readonly float WIDGET_TOTAL_H = WIDGET_SIZE + BOTTOM_BG_H;

        // ── Speed presets ────────────────────────────────────────────────────
        // Multipliers map to fractions of the Python-parity baseline (3600 s/day).
        // 1× keeps the canonical 60 real-min day; 100× compresses it to 36 real-sec.
        private const float BASELINE_REAL_SECONDS_PER_DAY = 3600f;
        private static readonly int[] SPEED_MULTIPLIERS = { 1, 2, 5, 10, 20, 50, 100 };

        // ── Phase palette (sundial ring + icon tint) ─────────────────────────
        // Warmer than the global Light2D color so the HUD reads as "ambient
        // weather" without competing with the actual world tint.
        private static readonly Color RING_BG          = new Color(0.10f, 0.12f, 0.18f, 0.85f);
        private static readonly Color RING_DAY         = new Color(1.00f, 0.95f, 0.65f, 1f);
        private static readonly Color RING_DAWN        = new Color(0.78f, 0.78f, 0.92f, 1f);
        private static readonly Color RING_GOLDEN_M    = new Color(1.00f, 0.78f, 0.45f, 1f);
        private static readonly Color RING_GOLDEN_E    = new Color(1.00f, 0.62f, 0.30f, 1f);
        private static readonly Color RING_DUSK        = new Color(0.95f, 0.45f, 0.30f, 1f);
        private static readonly Color RING_BLUE_HOUR   = new Color(0.40f, 0.50f, 0.95f, 1f);
        private static readonly Color RING_NIGHT       = new Color(0.55f, 0.62f, 1.00f, 1f);
        private static readonly Color ICON_DAY         = new Color(1.00f, 0.95f, 0.70f, 1f);
        private static readonly Color ICON_DAWN        = new Color(0.92f, 0.92f, 1.00f, 1f);
        private static readonly Color ICON_GOLDEN_M    = new Color(1.00f, 0.85f, 0.55f, 1f);
        private static readonly Color ICON_GOLDEN_E    = new Color(1.00f, 0.72f, 0.45f, 1f);
        private static readonly Color ICON_DUSK        = new Color(1.00f, 0.55f, 0.35f, 1f);
        private static readonly Color ICON_BLUE_HOUR   = new Color(0.78f, 0.85f, 1.00f, 1f);
        private static readonly Color ICON_NIGHT       = new Color(0.86f, 0.92f, 1.00f, 1f);
        private static readonly Color BG_PANEL    = new Color(0.04f, 0.05f, 0.08f, 0.55f);
        private static readonly Color BG_BOTTOM   = new Color(0.04f, 0.05f, 0.08f, 0.65f);
        // Slider palette
        private static readonly Color TRACK_COLOR = new Color(0.50f, 0.54f, 0.62f, 0.95f);
        private static readonly Color TICK_COLOR  = new Color(0.65f, 0.69f, 0.78f, 0.85f);
        private static readonly Color HANDLE_COLOR = new Color(0.95f, 0.78f, 0.40f, 1.00f);
        private static readonly Color HANDLE_HOVER = new Color(1.00f, 0.85f, 0.50f, 1.00f);
        private static readonly Color HANDLE_PRESS = new Color(1.00f, 0.92f, 0.60f, 1.00f);
        private static readonly Color SPEED_LABEL  = new Color(0.95f, 0.78f, 0.40f, 0.95f);

        // ── UI handles ───────────────────────────────────────────────────────
        private Canvas       _canvas;
        private RectTransform _root;
        private Image _bgPanel;
        private Image _bgBottom;
        private Image _ringBg;
        private Image _ringFill;
        private Image _icon;
        private TextMeshProUGUI _clockTmp;
        private TextMeshProUGUI _phaseTmp;
        private Slider _speedSlider;
        private Image  _speedHandleImg;
        private TextMeshProUGUI _speedLabelTmp;
        private bool   _suppressSpeedEvents;
        private int    _activeSpeedIdx = -1;

        private void Start()
        {
            BuildUI();
        }

        private void Update()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            // 24-hour sundial: arc grows from 00:00 (12 o'clock position) clockwise.
            if (_ringFill != null)
            {
                _ringFill.fillAmount = cycle.TimeNormalized;
                _ringFill.color      = RingColorFor(cycle.CurrentPhase);
            }

            if (_icon != null)
            {
                _icon.sprite = IsDaytime(cycle.TimeNormalized) ? SunSprite() : MoonSprite();
                _icon.color  = IconColorFor(cycle.CurrentPhase);
            }

            if (_clockTmp != null)
            {
                int totalMin = cycle.MinuteOfDay;
                _clockTmp.text = $"{totalMin / 60:D2}:{totalMin % 60:D2}";
            }
            if (_phaseTmp != null)
                _phaseTmp.text = cycle.CurrentPhase.ToString().ToUpperInvariant();

            // Keep the slider position in sync with whatever set the cycle's
            // RealSecondsPerDay (this widget, the Lighting Editor scrubber, or
            // an inspector tweak). Snaps to the nearest preset.
            SyncSpeedFromCycle(cycle.RealSecondsPerDay);
        }

        // ── UI build ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            // Own canvas: sort just above the gameplay HUD so it sits over the
            // PlayerHUD bars but below modal panels (editors / pause menu).
            var canvasGo = new GameObject("DayNightClockCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 105;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = NewUI("Root", canvasGo.transform).GetComponent<RectTransform>();
            _root.anchorMin        = new Vector2(1f, 1f);
            _root.anchorMax        = new Vector2(1f, 1f);
            _root.pivot            = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-MARGIN_RIGHT, -MARGIN_TOP);
            _root.sizeDelta        = new Vector2(WIDGET_SIZE, WIDGET_TOTAL_H);

            BuildDial();
            BuildBottomBlock();
        }

        // Top half: circular dial (sundial ring + center icon).
        private void BuildDial()
        {
            // BG panel: faint dark wash so the dial reads against bright skies.
            _bgPanel = AddImage(_root, "Bg", CircleSprite());
            var bgRt = _bgPanel.rectTransform;
            bgRt.anchorMin = new Vector2(0.5f, 1f);
            bgRt.anchorMax = new Vector2(0.5f, 1f);
            bgRt.pivot     = new Vector2(0.5f, 1f);
            bgRt.sizeDelta = new Vector2(WIDGET_SIZE, WIDGET_SIZE);
            bgRt.anchoredPosition = Vector2.zero;
            _bgPanel.color = BG_PANEL;

            // Ring backdrop (full circle, dark).
            _ringBg = AddImage(_root, "RingBg", RingSprite());
            CenterOnDial(_ringBg.rectTransform);
            _ringBg.color = RING_BG;

            // Ring fill (radial 360 from the top, clockwise).
            _ringFill = AddImage(_root, "RingFill", RingSprite());
            CenterOnDial(_ringFill.rectTransform);
            _ringFill.type            = Image.Type.Filled;
            _ringFill.fillMethod      = Image.FillMethod.Radial360;
            _ringFill.fillOrigin      = (int)Image.Origin360.Top;
            _ringFill.fillClockwise   = true;
            _ringFill.fillAmount      = 0.5f;
            _ringFill.color           = RING_DAY;

            // Center icon (sun by day, moon by night).
            _icon = AddImage(_root, "Icon", SunSprite());
            var iconRt = _icon.rectTransform;
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot     = new Vector2(0.5f, 1f);
            iconRt.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);
            iconRt.anchoredPosition = new Vector2(0f, -(WIDGET_SIZE - ICON_SIZE) * 0.5f);
            _icon.color      = ICON_DAY;
        }

        // Bottom half: HH:MM + phase + speed slider, all on a shared
        // semi-transparent BG so the small text + slider stay readable
        // against any world tint.
        private void BuildBottomBlock()
        {
            // Backplate that wraps the HH:MM + phase + slider rows.
            _bgBottom = AddImage(_root, "BottomBg", SolidSprite());
            var bgBRt = _bgBottom.rectTransform;
            bgBRt.anchorMin = new Vector2(0f, 1f);
            bgBRt.anchorMax = new Vector2(1f, 1f);
            bgBRt.pivot     = new Vector2(0.5f, 1f);
            bgBRt.sizeDelta = new Vector2(0f, BOTTOM_BG_H);
            bgBRt.anchoredPosition = new Vector2(0f, -WIDGET_SIZE);
            _bgBottom.color = BG_BOTTOM;

            // Clock label (HH:MM)
            _clockTmp                      = AddText(_root, "Clock", "12:00", 18f, FontStyles.Bold);
            _clockTmp.alignment            = TextAlignmentOptions.Center;
            _clockTmp.color                = new Color(1f, 1f, 1f, 0.95f);
            var clockRt                    = _clockTmp.rectTransform;
            clockRt.anchorMin              = new Vector2(0f, 1f);
            clockRt.anchorMax              = new Vector2(1f, 1f);
            clockRt.pivot                  = new Vector2(0.5f, 1f);
            clockRt.sizeDelta              = new Vector2(0f, CLOCK_ROW_H);
            clockRt.anchoredPosition       = new Vector2(0f, -WIDGET_SIZE - BOTTOM_BG_PAD);

            // Phase label (small italics)
            _phaseTmp                      = AddText(_root, "Phase", "DAY", 10f, FontStyles.Italic);
            _phaseTmp.alignment            = TextAlignmentOptions.Center;
            _phaseTmp.color                = new Color(0.85f, 0.88f, 0.95f, 0.8f);
            var phaseRt                    = _phaseTmp.rectTransform;
            phaseRt.anchorMin              = new Vector2(0f, 1f);
            phaseRt.anchorMax              = new Vector2(1f, 1f);
            phaseRt.pivot                  = new Vector2(0.5f, 1f);
            phaseRt.sizeDelta              = new Vector2(0f, PHASE_ROW_H);
            phaseRt.anchoredPosition       = new Vector2(0f, -WIDGET_SIZE - BOTTOM_BG_PAD - CLOCK_ROW_H);

            BuildSpeedSlider();
            BuildSpeedLabel();
        }

        // ── Speed slider ─────────────────────────────────────────────────────

        private void BuildSpeedSlider()
        {
            // SliderContainer: full-width row with horizontal inset so the handle
            // never clips the background edges. The Slider component sits on
            // this container and drives the handle relative to it.
            var sliderGo = NewUI("SpeedSlider", _root);
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin        = new Vector2(0f, 1f);
            sliderRt.anchorMax        = new Vector2(1f, 1f);
            sliderRt.pivot            = new Vector2(0.5f, 1f);
            sliderRt.sizeDelta        = new Vector2(-SLIDER_INSET_X * 2f, SLIDER_ROW_H);
            sliderRt.anchoredPosition = new Vector2(0f, -WIDGET_SIZE - BOTTOM_BG_PAD - CLOCK_ROW_H - PHASE_ROW_H - SLIDER_GAP_TOP);

            // Track: thin horizontal line spanning the full slider width.
            var trackGo = NewUI("Track", sliderGo.transform);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin        = new Vector2(0f, 0.5f);
            trackRt.anchorMax        = new Vector2(1f, 0.5f);
            trackRt.pivot            = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta        = new Vector2(0f, 1.5f);
            trackRt.anchoredPosition = Vector2.zero;
            var trackImg              = trackGo.AddComponent<Image>();
            trackImg.sprite           = SolidSprite();
            trackImg.color            = TRACK_COLOR;
            trackImg.raycastTarget    = false;

            // Tick marks at each preset position. Their X is a normalized anchor
            // matching the slider's value/maxValue ratio so they line up with the
            // handle exactly when value == i.
            int n = SPEED_MULTIPLIERS.Length;
            for (int i = 0; i < n; i++)
            {
                float t = (n == 1) ? 0.5f : (float)i / (n - 1);
                var tickGo = NewUI($"Tick_{SPEED_MULTIPLIERS[i]}x", sliderGo.transform);
                var tickRt = tickGo.GetComponent<RectTransform>();
                tickRt.anchorMin        = new Vector2(t, 0.5f);
                tickRt.anchorMax        = new Vector2(t, 0.5f);
                tickRt.pivot            = new Vector2(0.5f, 0.5f);
                tickRt.sizeDelta        = new Vector2(1.5f, 6f);
                tickRt.anchoredPosition = Vector2.zero;
                var tickImg              = tickGo.AddComponent<Image>();
                tickImg.sprite           = SolidSprite();
                tickImg.color            = TICK_COLOR;
                tickImg.raycastTarget    = false;
            }

            // HandleSlideArea: the rect Unity slides the handle within. Stretches
            // across the slider so the handle covers the full inset range.
            var slideAreaGo = NewUI("HandleSlideArea", sliderGo.transform);
            var slideAreaRt = slideAreaGo.GetComponent<RectTransform>();
            slideAreaRt.anchorMin = new Vector2(0f, 0f);
            slideAreaRt.anchorMax = new Vector2(1f, 1f);
            slideAreaRt.offsetMin = Vector2.zero;
            slideAreaRt.offsetMax = Vector2.zero;

            // Handle: down-pointing triangle (apex sits on the track).
            var handleGo = NewUI("Handle", slideAreaGo.transform);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(13f, 14f);
            _speedHandleImg            = handleGo.AddComponent<Image>();
            _speedHandleImg.sprite     = TrianglePointerSprite();
            _speedHandleImg.color      = HANDLE_COLOR;
            _speedHandleImg.raycastTarget = true;

            // Slider component on the container.
            _speedSlider                  = sliderGo.AddComponent<Slider>();
            _speedSlider.fillRect         = null;
            _speedSlider.handleRect       = handleRt;
            _speedSlider.targetGraphic    = _speedHandleImg;
            _speedSlider.direction        = Slider.Direction.LeftToRight;
            _speedSlider.minValue         = 0;
            _speedSlider.maxValue         = n - 1;
            _speedSlider.wholeNumbers     = true;
            _speedSlider.value            = 0;

            var c = _speedSlider.colors;
            c.normalColor      = HANDLE_COLOR;
            c.highlightedColor = HANDLE_HOVER;
            c.pressedColor     = HANDLE_PRESS;
            c.selectedColor    = HANDLE_COLOR;
            c.fadeDuration     = 0.06f;
            _speedSlider.colors = c;

            _speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
        }

        // Live "Nx" readout under the slider. Value mirrors whichever step is
        // currently active so designers + players always have an explicit cue.
        private void BuildSpeedLabel()
        {
            _speedLabelTmp = AddText(_root, "SpeedLabel", "1x", 10f, FontStyles.Bold);
            _speedLabelTmp.alignment       = TextAlignmentOptions.Center;
            _speedLabelTmp.color           = SPEED_LABEL;
            var rt                         = _speedLabelTmp.rectTransform;
            rt.anchorMin                   = new Vector2(0f, 1f);
            rt.anchorMax                   = new Vector2(1f, 1f);
            rt.pivot                       = new Vector2(0.5f, 1f);
            rt.sizeDelta                   = new Vector2(0f, SPEED_LABEL_H);
            rt.anchoredPosition            = new Vector2(0f, -WIDGET_SIZE - BOTTOM_BG_PAD - CLOCK_ROW_H - PHASE_ROW_H - SLIDER_GAP_TOP - SLIDER_ROW_H);
        }

        private void OnSpeedSliderChanged(float v)
        {
            if (_suppressSpeedEvents) return;
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            int idx  = Mathf.Clamp(Mathf.RoundToInt(v), 0, SPEED_MULTIPLIERS.Length - 1);
            int mult = SPEED_MULTIPLIERS[idx];
            cycle.RealSecondsPerDay = BASELINE_REAL_SECONDS_PER_DAY / mult;
            UpdateSpeedLabel(idx);
            _activeSpeedIdx = idx;
        }

        // Pick the preset whose ratio is closest (in log-space) to the live
        // RealSecondsPerDay. Log-space avoids the 1× ↔ 100× spread biasing the
        // nearest-match toward the slowest preset on small differences.
        private void SyncSpeedFromCycle(float realSecondsPerDay)
        {
            if (_speedSlider == null || realSecondsPerDay <= 0f) return;
            float liveMult = BASELINE_REAL_SECONDS_PER_DAY / realSecondsPerDay;
            int   bestIdx  = 0;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < SPEED_MULTIPLIERS.Length; i++)
            {
                float d = Mathf.Abs(Mathf.Log(SPEED_MULTIPLIERS[i] / Mathf.Max(0.0001f, liveMult)));
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            if (bestIdx == _activeSpeedIdx) return;
            _activeSpeedIdx = bestIdx;
            // Move the slider without retriggering OnSpeedSliderChanged
            // (which would otherwise lock the cycle to the snapped value).
            _suppressSpeedEvents = true;
            try { _speedSlider.value = bestIdx; }
            finally { _suppressSpeedEvents = false; }
            UpdateSpeedLabel(bestIdx);
        }

        private void UpdateSpeedLabel(int idx)
        {
            if (_speedLabelTmp == null) return;
            int mult = SPEED_MULTIPLIERS[Mathf.Clamp(idx, 0, SPEED_MULTIPLIERS.Length - 1)];
            _speedLabelTmp.text = $"{mult}x";
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void CenterOnDial(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(DIAL_SIZE, DIAL_SIZE);
            rt.anchoredPosition = new Vector2(0f, -(WIDGET_SIZE - DIAL_SIZE) * 0.5f);
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Image AddImage(Transform parent, string name, Sprite sprite)
        {
            var go  = NewUI(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, string text,
            float fontSize, FontStyles style)
        {
            var go  = NewUI(name, parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = text;
            tmp.fontSize      = fontSize;
            tmp.fontStyle     = style;
            tmp.raycastTarget = false;
            return tmp;
        }

        // ── Phase mapping ────────────────────────────────────────────────────

        // Daytime icon window: sun shown when sun would actually be in the sky.
        // Matches the cycle's normalized boundaries (0.20 dawn → 0.80 dusk).
        private static bool IsDaytime(float t) => t >= 0.20f && t < 0.80f;

        private static Color RingColorFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Dawn          => RING_DAWN,
            DayNightCycle.DayPhase.GoldenMorning => RING_GOLDEN_M,
            DayNightCycle.DayPhase.GoldenEvening => RING_GOLDEN_E,
            DayNightCycle.DayPhase.Dusk          => RING_DUSK,
            DayNightCycle.DayPhase.BlueHour      => RING_BLUE_HOUR,
            DayNightCycle.DayPhase.Night         => RING_NIGHT,
            _                                     => RING_DAY,
        };

        private static Color IconColorFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Dawn          => ICON_DAWN,
            DayNightCycle.DayPhase.GoldenMorning => ICON_GOLDEN_M,
            DayNightCycle.DayPhase.GoldenEvening => ICON_GOLDEN_E,
            DayNightCycle.DayPhase.Dusk          => ICON_DUSK,
            DayNightCycle.DayPhase.BlueHour      => ICON_BLUE_HOUR,
            DayNightCycle.DayPhase.Night         => ICON_NIGHT,
            _                                     => ICON_DAY,
        };
    }
}
