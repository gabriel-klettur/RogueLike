using System;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.Frontend;

namespace Valkur.UI.MainMenu.Kit
{
    /// <summary>
    /// The menu's slider: the loading bar in miniature — a bevelled housing, a recessed channel,
    /// a molten fill with a white-hot leading edge, diamond notches for its steps, and a gem for
    /// a handle.
    ///
    /// <para><b>Why not <c>UISlider.MakeSlimTrack</c>.</b> That one is the EDITORS' slider and is
    /// drawn in flat colours with no sprite — correct there, and a fifth palette here (the
    /// shipped audio panel's cyan track appears nowhere else in the game). The geometry idea is
    /// the same and is the good part: the visible track stays slim while the whole ROW height
    /// accepts the click, so nobody needs pixel-perfect aim.</para>
    ///
    /// <para><b>The value is snapped to the step on the way IN, not on the way out.</b> A slider
    /// that stores 0.3719 and displays 37 is a control whose two halves disagree, and the
    /// disagreement surfaces the first time the value is written to disk and read back.</para>
    ///
    /// <para><b>Sparks answer MOTION</b>, exactly as the loading bar's leading edge does: none
    /// at rest, a few from a keyboard nudge, and a spray in proportion to how fast a drag moves
    /// the fill. The fill is driven by <see cref="FrontendFillGraphic.Amount"/> rather than by the
    /// Slider's <c>fillRect</c>, so the notches stay where the steps are instead of being
    /// squeezed along with the filled width.</para>
    /// </summary>
    public sealed class MenuSlider
    {
        private const float TrackHeight = 14f;
        private const float TrackFrame = 3f;

        public readonly Slider Slider;
        private readonly float _step;
        private readonly Action<float> _onChanged;
        private readonly FrontendFillGraphic _fill;
        private readonly FrontendGemGraphic _gem;
        private readonly MenuFxLayer _motes;
        private readonly Color _tint;
        private bool _suppress;
        private float _lastDrawn;
        private float _sparkDebt;
        private float _gemFlash;

        /// <summary>The live value, already snapped.</summary>
        public float Value => Slider != null ? Slider.value : 0f;

        /// <summary>How much of the channel is drawn filled, 0..1. For the tests.</summary>
        public float FillAmount => _fill != null ? _fill.Amount : 0f;

        public MenuSlider(RectTransform parent, MenuArt art, MenuStyle style,
                          float min, float max, float initial, float step,
                          Action<float> onChanged, int notches = 0, MenuFxLayer motes = null)
        {
            _step = step;
            _onChanged = onChanged;
            _motes = motes;
            _tint = style.Gold;

            var host = MenuUIKit.Rect("Slider", parent);
            // Stops short of the value column. Content starts at 0.54 of the row and the value
            // at 0.56, so a host reaching 1.0 of Content would run to 1.0 of the row and put the
            // track under the number it is being read against.
            host.anchorMin = new Vector2(0f, 0.5f);
            host.anchorMax = new Vector2(0.66f, 0.5f);
            host.pivot = new Vector2(0.5f, 0.5f);
            host.offsetMin = new Vector2(0f, -style.rowHeight * 0.5f);
            host.offsetMax = new Vector2(0f, style.rowHeight * 0.5f);

            // Transparent, raycast-on: the whole row-height band is the drag area.
            var hit = host.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            Slider = host.gameObject.AddComponent<Slider>();
            Slider.direction = Slider.Direction.LeftToRight;
            Slider.minValue = min;
            Slider.maxValue = max;
            Slider.wholeNumbers = false;
            Slider.transition = Selectable.Transition.None;

            var trackRt = MenuUIKit.Rect("Track", host);
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta = new Vector2(0f, TrackHeight);
            trackRt.anchoredPosition = Vector2.zero;

            var frame = BevelFrameGraphic.Create(trackRt, "Frame");
            frame.Thickness = TrackFrame;
            frame.ShadowScale = 0.35f;
            frame.Brackets = false;
            frame.Tint = _tint;

            _fill = FrontendFillGraphic.Create(trackRt, "Fill");
            _fill.Profile = FrontendFillProfile.Bar;
            _fill.Tint = _tint;
            _fill.LeadingCore = true;
            // Notches make an unlabelled slider legible at a glance, which is what turns
            // "somewhere past the middle" into "three of five".
            _fill.Notches = notches > 1 ? notches : 0;
            var fillRt = _fill.rectTransform;
            fillRt.offsetMin = new Vector2(TrackFrame, TrackFrame);
            fillRt.offsetMax = new Vector2(-TrackFrame, -TrackFrame);

            var handleArea = MenuUIKit.Rect("HandleArea", host);
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.pivot = new Vector2(0.5f, 0.5f);
            handleArea.offsetMin = new Vector2(TrackFrame, -11f);
            handleArea.offsetMax = new Vector2(-TrackFrame, 11f);

            _gem = FrontendGemGraphic.Create(handleArea, "Handle", _tint);
            _gem.Lit = 0.7f;
            var handleRt = _gem.rectTransform;
            handleRt.sizeDelta = new Vector2(22f, 22f);

            Slider.handleRect = handleRt;
            Slider.targetGraphic = _gem;
            Slider.SetValueWithoutNotify(Snap(Mathf.Clamp(initial, min, max)));
            Slider.onValueChanged.AddListener(OnSliderMoved);
            _lastDrawn = Normalized;
            _fill.Amount = _lastDrawn;
        }

        private float Normalized => Slider == null || Slider.maxValue <= Slider.minValue
            ? 0f
            : Mathf.InverseLerp(Slider.minValue, Slider.maxValue, Slider.value);

        /// <summary>Sets the value without calling back — how a nudge from the keyboard lands.</summary>
        public void SetValue(float v)
        {
            if (Slider == null) return;
            _suppress = true;
            Slider.SetValueWithoutNotify(Snap(Mathf.Clamp(v, Slider.minValue, Slider.maxValue)));
            _suppress = false;
            // A value SET is not motion: only a drag measures speed, so a panel refreshing its
            // sliders on open throws nothing, and a nudge throws only the handful it asks for.
            _lastDrawn = Normalized;
            _fill.Amount = _lastDrawn;
        }

        /// <summary>Moves by one step in <paramref name="dir"/>. Returns the new value.</summary>
        public float Nudge(int dir)
        {
            if (Slider == null) return 0f;
            float v = Snap(Mathf.Clamp(Slider.value + dir * _step, Slider.minValue, Slider.maxValue));
            SetValue(v);
            _onChanged?.Invoke(v);
            // A step is a small event: a handful off the edge and a blink of the handle.
            _sparkDebt += 5f;
            _gemFlash = 1f;
            return v;
        }

        /// <summary>
        /// Advances the handle's glow and emits in proportion to how far the fill moved since the
        /// last tick. Own delta, so a fixture can drive it; a slider nobody ticks is simply still.
        /// </summary>
        public void Tick(float dt, bool reduceMotion)
        {
            if (Slider == null || dt <= 0f) return;
            float now = Normalized;
            float speed = Mathf.Abs(now - _lastDrawn) / dt;          // track widths per second
            _lastDrawn = now;
            _fill.Amount = now;

            _gemFlash = Mathf.Max(0f, _gemFlash - dt / 0.35f);
            if (speed > 0.01f) _gemFlash = Mathf.Max(_gemFlash, Mathf.Clamp01(speed * 0.6f));
            _gem.Lit = 0.7f + 0.3f * _gemFlash;
            _fill.Glow = _gemFlash * 0.6f;

            if (reduceMotion || _motes == null) { _sparkDebt = 0f; return; }
            _sparkDebt += Mathf.Min(speed, 8f) * 22f * dt;
            var fillRt = _fill.rectTransform;
            var r = fillRt.rect;
            float ex = r.xMin + r.width * now;
            Color hot = FrontendMotes.Hot(_tint), warm = FrontendMotes.Warm(_tint);
            while (_sparkDebt >= 1f)
            {
                _sparkDebt -= 1f;
                float a = UnityEngine.Random.Range(40f, 140f) * Mathf.Deg2Rad;
                float v = UnityEngine.Random.Range(50f, 130f);
                FrontendMotes.EmitFrom(_motes, fillRt, new Vector2(ex, UnityEngine.Random.Range(r.yMin, r.yMax)),
                                       new Vector2(Mathf.Cos(a) * v, Mathf.Sin(a) * v),
                                       Color.Lerp(hot, warm, UnityEngine.Random.value), UnityEngine.Random.Range(0.25f, 0.5f),
                                       MenuMoteShape.Spark, gravity: 260f, drag: 1.2f,
                                       size: UnityEngine.Random.Range(0.14f, 0.28f));
            }
        }

        private void OnSliderMoved(float raw)
        {
            if (_suppress) return;
            float v = Snap(raw);
            if (!Mathf.Approximately(v, raw))
            {
                _suppress = true;
                Slider.SetValueWithoutNotify(v);
                _suppress = false;
            }
            _fill.Amount = Normalized;
            _onChanged?.Invoke(v);
        }

        private float Snap(float v)
        {
            if (_step <= 0f) return v;
            float snapped = Mathf.Round((v - Slider.minValue) / _step) * _step + Slider.minValue;
            return Mathf.Clamp(snapped, Slider.minValue, Slider.maxValue);
        }
    }
}
