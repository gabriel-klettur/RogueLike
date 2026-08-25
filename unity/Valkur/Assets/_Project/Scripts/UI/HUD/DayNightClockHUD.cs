using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Sundial-style HUD widget that surfaces the live <see cref="DayNightCycle"/>
    /// to the player. Anchored top-left of the screen, always visible during
    /// gameplay. Shows:
    ///   • A 24-hour ring whose colored arc grows clockwise from 00:00 → current time.
    ///   • A central icon (sun during 06:00–18:00, moon otherwise) tinted by phase.
    ///   • A digital "HH:MM" readout.
    ///   • A short phase label (Día / Amanecer / Atardecer / Noche).
    ///
    /// All *modifying* controls (speed slider, phase shortcuts, weather toggles,
    /// phase tuning) live inside the F2 TimeWeatherEditor — this widget is
    /// read-only so it can stay always-visible without intercepting clicks.
    ///
    /// Reads <see cref="DayNightCycle.Instance"/> by polling each frame — no
    /// event subscription, so no teardown / unsubscribe surface to maintain.
    /// </summary>
    public sealed partial class DayNightClockHUD : MonoBehaviour
    {
        // ── Layout constants ─────────────────────────────────────────────────
        private const float WIDGET_SIZE   = 110f;
        private const float MARGIN_LEFT   = 24f;
        private const float MARGIN_TOP    = 24f;
        private const float DIAL_SIZE     = 92f;
        private const float ICON_SIZE     = 44f;
        private const float CLOCK_ROW_H   = 22f;
        private const float PHASE_ROW_H   = 14f;
        private const float BOTTOM_BG_PAD = 4f;
        private static readonly float BOTTOM_BG_H    = CLOCK_ROW_H + PHASE_ROW_H + BOTTOM_BG_PAD * 2f;
        private static readonly float WIDGET_TOTAL_H = WIDGET_SIZE + BOTTOM_BG_H;

        // ── Phase palette (sundial ring + icon tint) ─────────────────────────
        // Independent of the live Light2D color so the HUD reads as a stable
        // ambient indicator regardless of how aggressive the global tint is.
        private static readonly Color RING_BG    = new Color(0.10f, 0.12f, 0.18f, 0.85f);
        private static readonly Color RING_DAY   = new Color(1.00f, 0.95f, 0.65f, 1f);   // bright sunrise yellow
        private static readonly Color RING_DAWN  = new Color(0.85f, 0.88f, 0.98f, 1f);   // soft cool grey-blue
        private static readonly Color RING_DUSK  = new Color(0.92f, 0.85f, 0.95f, 1f);   // soft warm grey-lilac
        private static readonly Color RING_NIGHT = new Color(0.55f, 0.65f, 1.00f, 1f);   // cool blue
        private static readonly Color ICON_DAY   = new Color(1.00f, 0.95f, 0.70f, 1f);
        private static readonly Color ICON_DAWN  = new Color(0.95f, 0.97f, 1.00f, 1f);
        private static readonly Color ICON_DUSK  = new Color(1.00f, 0.95f, 0.92f, 1f);
        private static readonly Color ICON_NIGHT = new Color(0.86f, 0.92f, 1.00f, 1f);
        private static readonly Color BG_PANEL   = new Color(0.04f, 0.05f, 0.08f, 0.55f);
        private static readonly Color BG_BOTTOM  = new Color(0.04f, 0.05f, 0.08f, 0.65f);

        // ── UI handles ───────────────────────────────────────────────────────
        private Canvas        _canvas;
        private RectTransform _root;
        private Image _bgPanel;
        private Image _bgBottom;
        private Image _ringBg;
        private Image _ringFill;
        private Image _icon;
        private TextMeshProUGUI _clockTmp;
        private TextMeshProUGUI _phaseTmp;

        private void Start() => BuildUI();

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
            // No GraphicRaycaster — the clock is read-only, never intercepts
            // gameplay clicks. Modifying controls live in the F2 editor.

            _root = NewUI("Root", canvasGo.transform).GetComponent<RectTransform>();
            _root.anchorMin        = new Vector2(0f, 1f);
            _root.anchorMax        = new Vector2(0f, 1f);
            _root.pivot            = new Vector2(0f, 1f);
            _root.anchoredPosition = new Vector2(MARGIN_LEFT, -MARGIN_TOP);
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

        // Bottom half: HH:MM + phase label, on a shared semi-transparent BG so
        // the small text stays readable against any world tint.
        private void BuildBottomBlock()
        {
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
        /// <summary>
        /// Sun or moon. Derived from the cycle's own band constants rather than the 0.20/0.80
        /// literals this used to carry: those disagreed with DAWN_START 0.18 and NIGHT_START 0.84,
        /// so the icon flipped up to 58 in-game minutes before the world did.
        /// </summary>
        private static bool IsDaytime(float t)
            => t >= Valkur.Gameplay.World.DayNightCycle.DAWN_START
            && t <  Valkur.Gameplay.World.DayNightCycle.NIGHT_START;

        private static Color RingColorFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Dawn  => RING_DAWN,
            DayNightCycle.DayPhase.Dusk  => RING_DUSK,
            DayNightCycle.DayPhase.Night => RING_NIGHT,
            _                             => RING_DAY,   // Day + legacy enum values
        };

        private static Color IconColorFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Dawn  => ICON_DAWN,
            DayNightCycle.DayPhase.Dusk  => ICON_DUSK,
            DayNightCycle.DayPhase.Night => ICON_NIGHT,
            _                             => ICON_DAY,
        };
    }
}
