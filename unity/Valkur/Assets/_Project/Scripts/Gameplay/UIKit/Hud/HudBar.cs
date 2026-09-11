using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>What moved a bar, which decides how it moves.</summary>
    public enum HudBarChange
    {
        /// <summary>Glide there with no event. Regeneration, restores, max changes.</summary>
        Silent = 0,

        /// <summary>A loss. The fill drops at once; the lost part stays as a pale chip, then drains.</summary>
        Damage = 1,

        /// <summary>A gain. The gained part lights up first and the fill grows into it.</summary>
        Heal = 2,
    }

    /// <summary>
    /// One bar of the player panel: a 9-slice frame with an inner recess, and inside it the
    /// chip, the heal preview, the fill with its lit top rows and shaded bottom rows, a bright
    /// leading edge, quarter notches, a hit flash, a value label and an outer glow for the
    /// heartbeat.
    ///
    /// <para><b>A blow and a heal are different events.</b> The bar it replaces listened to
    /// <c>OnHpChanged</c> alone and lerped both ways identically, so a hit and a potion looked the
    /// same. Here a loss DROPS the fill and leaves the lost span as a chip that holds, then drains
    /// — the eye reads how much was taken — while a gain lights the incoming span first and grows
    /// into it. Same model as <c>WorldBarLine</c> over the head.</para>
    ///
    /// <para><b>Widths are whole texels.</b> The ratio animates continuously; the drawn width is
    /// rounded, so the leading edge steps a pixel at a time instead of boiling.</para>
    ///
    /// <para>A plain class, ticked by <see cref="PlayerHUD"/>, so an EditMode test can drive it
    /// frame by frame without Unity's player loop.</para>
    /// </summary>
    public sealed class HudBar
    {
        public RectTransform Root { get; }

        private readonly Image _glow;
        private readonly Image _chip;
        private readonly Image _gain;
        private readonly Image _fill;
        private readonly Image _shine;
        private readonly Image _shade;
        private readonly Image _edge;
        private readonly Image _flash;
        private readonly Image _glint;
        private readonly Image[] _notches;
        private readonly HudPixelText _label;
        private readonly int _innerW;
        private readonly int _innerH;

        private Color _colour = Color.white;
        private Color _lowColour = Color.red;
        private Color _chipColour = Color.white;
        private Color _gainColour = Color.white;
        private Color _glowColour = Color.red;
        private float _lowThreshold = -1f;

        private float _target = 1f;
        private float _shown = 1f;
        private float _chipRatio = 1f;
        private float _chipHold;
        private float _gainFrom;
        private float _gainLeft;
        private float _flashLeft;
        private float _flashSeconds = 0.1f;
        private Color _flashColour = Color.white;
        private float _lowT;
        private float _glintT = -1f;
        private bool _heartbeat;
        private float _heartbeatDepth;
        private float _beatPhase;
        private bool _seeded;

        private int _valueCurrent;
        private int _valueMax;
        private bool _hasValue;
        private int _drawnFill = -1;
        private int _drawnChip = -1;
        private int _drawnGainL = -1, _drawnGainR = -1;

        /// <summary>The ratio the bar is heading to.</summary>
        public float Target => _target;

        /// <summary>The ratio being DRAWN, which trails the model on a heal.</summary>
        public float Shown => _shown;

        /// <summary>True while the pale chip still stands behind the fill.</summary>
        public bool ChipActive => _chipRatio > _shown + 0.0001f;

        /// <summary>Drawn fill width in texels.</summary>
        public int FillTexels => Mathf.Max(0, _drawnFill);

        /// <summary>Width of the inside of the frame, in texels.</summary>
        public int InnerWidth => _innerW;

        /// <summary>True while the chip IMAGE is drawn — what the player sees, which the model's
        /// <see cref="ChipActive"/> once disagreed with for the whole life of a blow.</summary>
        public bool ChipDrawn => _chip.enabled && _chip.rectTransform.sizeDelta.x > FillTexels;

        /// <summary>True while a flash is running over the inside of the bar.</summary>
        public bool Flashing => _flashLeft > 0f;

        /// <summary>True while the heal preview is lit.</summary>
        public bool GainActive => _gainLeft > 0f;

        /// <summary>True while a glint is sweeping the fill.</summary>
        public bool Glinting => _glintT >= 0f;

        /// <summary>True while the outer glow is beating.</summary>
        public bool HeartbeatActive => _heartbeat;

        /// <summary>The label's current text.</summary>
        public string Label => _label != null ? _label.Text : "";

        /// <summary>The colour the fill is drawn in right now.</summary>
        public Color FillColour => _fill.color;

        public HudBar(Transform parent, string name, HudArt art, int x, int y, int w, int h,
                      HudFontFace face, bool withLabel, int notchSegments, Material additive)
        {
            Root = HudRect.Make(name, parent, x, y, w, h);
            _innerW = w - 2;
            _innerH = h - 2;

            // Outer glow sits one texel outside the frame and is only ever lit by the heartbeat.
            // Only built where its 9-slice fits: squashed borders would sit off the texel grid.
            if (h + 2 >= 8)
            {
                _glow = HudRect.MakeImage("Glow", Root, art.BarGlow, -1, -1, w + 2, h + 2, Image.Type.Sliced);
                _glow.material = additive;
                _glow.color = Color.clear;
                _glow.enabled = false;
            }

            HudRect.MakeImage("Frame", Root, h >= 7 ? art.BarFrame : art.BarFrameThin, 0, 0, w, h, Image.Type.Sliced);

            var inner = HudRect.Make("Inner", Root, 1, 1, _innerW, _innerH);
            _chip = HudRect.MakeImage("Chip", inner, art.White, 0, 0, 0, _innerH);
            _gain = HudRect.MakeImage("Gain", inner, art.White, 0, 0, 0, _innerH);
            _fill = HudRect.MakeImage("Fill", inner, art.White, 0, 0, 0, _innerH);
            _shade = HudRect.MakeImage("Shade", inner, art.Shade, 0, 0, 0, Mathf.Min(2, _innerH));
            _shine = HudRect.MakeImage("Shine", inner, art.Shine, 0, Mathf.Max(0, _innerH - 2), 0, Mathf.Min(2, _innerH));
            _edge = HudRect.MakeImage("Edge", inner, art.White, 0, 0, 1, _innerH);
            _edge.color = new Color(1f, 1f, 1f, 0.55f);

            int n = Mathf.Max(0, notchSegments - 1);
            _notches = new Image[n];
            for (int i = 0; i < n; i++)
            {
                int nx = Mathf.RoundToInt(_innerW * (i + 1) / (float)notchSegments);
                _notches[i] = HudRect.MakeImage("Notch" + i, inner, art.White, nx, 0, 1, _innerH);
            }

            _flash = HudRect.MakeImage("Flash", inner, art.White, 0, 0, _innerW, _innerH);
            _flash.color = Color.clear;
            _flash.enabled = false;

            _glint = HudRect.MakeImage("Glint", inner, art.White, 0, 0, 2, _innerH);
            _glint.material = additive;
            _glint.color = Color.clear;
            _glint.enabled = false;

            if (withLabel)
                _label = HudPixelText.Create(Root, "Label", art, face, HudTextAlign.Centre, 1, 1, _innerW, _innerH);

            _chip.enabled = _gain.enabled = false;
        }

        // -- Configuration -------------------------------------------------------

        /// <summary>
        /// The fill's colour, the colour it turns below <paramref name="lowThreshold"/> (negative
        /// for never), the chip's, the heal preview's, and the heartbeat glow's.
        /// </summary>
        public void SetColours(Color fill, Color low, float lowThreshold, Color chip, Color gain, Color glow)
        {
            _colour = fill;
            _lowColour = low;
            _lowThreshold = lowThreshold;
            _chipColour = chip;
            _gainColour = gain;
            _glowColour = glow;
            ApplyFillColour(force: true);
            _chip.color = _chipColour;
        }

        public void SetNotchColour(Color c)
        {
            for (int i = 0; i < _notches.Length; i++) _notches[i].color = c;
        }

        public void SetLabelColour(Color c) => _label?.SetColour(c);

        // -- Values ------------------------------------------------------------------

        /// <summary>Sets the readout from a current and a max. Writes "cur/max" into the label.</summary>
        public void SetValue(int current, int max, HudBarChange change)
        {
            _valueCurrent = current;
            _valueMax = max;
            _hasValue = true;
            SetRatio(max > 0 ? (float)current / max : 0f, change);
            UpdateLabel();
        }

        /// <summary>
        /// The number follows the FILL on a heal — it counts up as the bar grows into the gain —
        /// and jumps at once on a loss, where the fill already dropped. A bar with no maximum has
        /// nothing to say and prints nothing, rather than "0/0".
        /// </summary>
        private void UpdateLabel()
        {
            if (_label == null || !_hasValue) return;
            if (_valueMax <= 0)
            {
                _label.SetText("");
                return;
            }
            int shown = _valueCurrent;
            if (_shown < _target - 0.0001f)
                shown = Mathf.Min(_valueCurrent, Mathf.RoundToInt(_shown * _valueMax));
            _label.SetText(shown + "/" + _valueMax);
        }

        /// <summary>Moves the bar to <paramref name="ratio"/>, animated according to what moved it.</summary>
        public void SetRatio(float ratio, HudBarChange change, bool instant = false)
        {
            ratio = Mathf.Clamp01(ratio);
            // The first report is the starting value and must not animate up from nothing.
            if (!_seeded)
            {
                _seeded = true;
                instant = true;
            }

            float before = _shown;
            _target = ratio;

            if (instant)
            {
                _shown = _chipRatio = ratio;
                _chipHold = 0f;
                _gainLeft = 0f;
                ApplyFillColour(force: false);
                return;
            }

            if (change == HudBarChange.Damage && ratio < before)
            {
                // The chip keeps whichever is further right: the fill as it was drawn, or a chip
                // still standing from the blow before. Two quick blows then read as one long loss.
                _chipRatio = Mathf.Max(_chipRatio, before);
                _shown = ratio;
                _chipHold = PlayerHudStyle.Active.chipHoldSeconds;
            }
            else if (change == HudBarChange.Heal && ratio > before)
            {
                _gainFrom = before;
                _gainLeft = 1f;
                // A heal retires any chip it grows past — by collapsing it onto the fill, never by
                // pushing it AHEAD of the fill: a chip in front of a growing fill is drawn pale over
                // exactly the span the heal preview owns, and reads as health lost.
                if (_chipRatio <= ratio) _chipRatio = _shown;
            }
        }

        /// <summary>Flashes the inside of the bar in <paramref name="c"/> for <paramref name="seconds"/>.</summary>
        public void Flash(Color c, float seconds)
        {
            if (seconds <= 0f) return;
            _flashColour = c;
            _flashSeconds = seconds;
            _flashLeft = seconds;
            _flash.enabled = true;
        }

        /// <summary>A bright column sweeps along the fill once — "something was added".</summary>
        public void Glint() => _glintT = 0f;

        /// <summary>Starts or stops the outer glow's heartbeat; <paramref name="depth"/> 0..1.</summary>
        public void SetHeartbeat(bool on, float depth)
        {
            _heartbeat = on && _glow != null;
            _heartbeatDepth = Mathf.Clamp01(depth);
            if (!on && _glow != null && _glow.enabled)
            {
                _glow.color = Color.clear;
                _glow.enabled = false;
            }
        }

        /// <summary>Panel-local x (relative to the bar root) of a ratio along the inside.</summary>
        public float XAtRatio(float ratio) => 1f + Mathf.Clamp01(ratio) * _innerW;

        /// <summary>Height of the inside of the frame, in texels.</summary>
        public int InnerHeight => _innerH;

        // -- Frame ---------------------------------------------------------------

        public void Tick(float dt, PlayerHudStyle style)
        {
            // Fill: a loss already dropped it; a heal or a silent change glides.
            if (!Mathf.Approximately(_shown, _target))
            {
                float k = 1f - Mathf.Exp(-style.fillLerpSpeed * dt);
                _shown = Mathf.Lerp(_shown, _target, k);
                if (Mathf.Abs(_shown - _target) < 0.0015f) _shown = _target;
            }

            // Chip: hold, then drain at a constant rate — a lerp would spend most of its time
            // crawling over the last few texels.
            if (_chipRatio > _shown)
            {
                if (_chipHold > 0f) _chipHold -= dt;
                else
                {
                    float rate = dt / Mathf.Max(0.01f, style.chipDrainSeconds);
                    _chipRatio = Mathf.Max(_shown, _chipRatio - rate);
                }
            }
            else _chipRatio = _shown;

            if (_gainLeft > 0f) _gainLeft = Mathf.Max(0f, _gainLeft - dt / Mathf.Max(0.01f, style.healGlowSeconds));

            // Low colour glides in rather than switching, so crossing the line reads as a change
            // of state instead of a flicker.
            float lowGoal = _lowThreshold >= 0f && _target <= _lowThreshold ? 1f : 0f;
            _lowT = Mathf.MoveTowards(_lowT, lowGoal, dt * 4f);

            DrawWidths();
            UpdateLabel();
            ApplyFillColour(force: false);
            TickFlash(dt);
            TickGlint(dt);
            TickHeartbeat(dt, style);
        }

        private void DrawWidths()
        {
            int fillW = Mathf.RoundToInt(_shown * _innerW);
            if (_target > 0f && fillW == 0) fillW = 1;          // a sliver is not zero
            if (_target <= 0f && _shown < 0.5f / _innerW) fillW = 0;
            fillW = Mathf.Clamp(fillW, 0, _innerW);

            if (fillW != _drawnFill)
            {
                _drawnFill = fillW;
                SetWidth(_fill, fillW);
                SetWidth(_shine, fillW);
                SetWidth(_shade, fillW);
                bool edge = fillW > 1 && fillW < _innerW;
                _edge.enabled = edge;
                if (edge) _edge.rectTransform.anchoredPosition = new Vector2(fillW - 1, 0f);
                _fill.enabled = _shine.enabled = _shade.enabled = fillW > 0;
            }

            // Visibility depends on BOTH widths: after a blow the chip keeps its old width while
            // the fill drops under it, so a test on the chip's width alone never turned it on —
            // which is exactly how the first build shipped, measured in a live capture.
            int chipW = Mathf.Clamp(Mathf.RoundToInt(_chipRatio * _innerW), 0, _innerW);
            bool chipOn = chipW > fillW;
            if (chipW != _drawnChip || chipOn != _chip.enabled)
            {
                _drawnChip = chipW;
                _chip.enabled = chipOn;
                if (chipOn) SetWidth(_chip, chipW);
            }

            int gl = 0, gr = 0;
            if (_gainLeft > 0f)
            {
                gl = Mathf.Clamp(Mathf.RoundToInt(_gainFrom * _innerW), 0, _innerW);
                gr = Mathf.Clamp(Mathf.RoundToInt(_target * _innerW), 0, _innerW);
            }
            if (gl != _drawnGainL || gr != _drawnGainR)
            {
                _drawnGainL = gl;
                _drawnGainR = gr;
                bool on = gr > gl;
                _gain.enabled = on;
                if (on)
                {
                    _gain.rectTransform.anchoredPosition = new Vector2(gl, 0f);
                    SetWidth(_gain, gr - gl);
                }
            }
            if (_gain.enabled)
            {
                var c = _gainColour;
                c.a *= Mathf.SmoothStep(0f, 1f, _gainLeft);
                if (_gain.color != c) _gain.color = c;
            }
        }

        private void ApplyFillColour(bool force)
        {
            var c = Color.Lerp(_colour, _lowColour, _lowT);
            if (force || _fill.color != c) _fill.color = c;
        }

        private void TickFlash(float dt)
        {
            if (_flashLeft <= 0f) return;
            _flashLeft -= dt;
            if (_flashLeft <= 0f)
            {
                _flash.color = Color.clear;
                _flash.enabled = false;
                return;
            }
            var c = _flashColour;
            c.a *= Mathf.Clamp01(_flashLeft / _flashSeconds);
            _flash.color = c;
        }

        private void TickGlint(float dt)
        {
            if (_glintT < 0f) return;
            _glintT += dt / 0.42f;
            int fillW = Mathf.Max(0, _drawnFill);
            if (_glintT >= 1f || fillW < 2)
            {
                _glintT = -1f;
                _glint.enabled = false;
                return;
            }
            _glint.enabled = true;
            int x = Mathf.Clamp(Mathf.RoundToInt(_glintT * fillW) - 1, 0, fillW - 2);
            _glint.rectTransform.anchoredPosition = new Vector2(x, 0f);
            _glint.color = new Color(1f, 1f, 1f, 0.8f * Mathf.Sin(_glintT * Mathf.PI));
        }

        private void TickHeartbeat(float dt, PlayerHudStyle style)
        {
            if (!_heartbeat) return;
            // Lub-dub: two pulses a fifth of a cycle apart, the second weaker. A plain sine reads
            // as a warning light; the double beat reads as a pulse.
            float hz = style.heartbeatHz * Mathf.Lerp(1f, 1.6f, _heartbeatDepth);
            _beatPhase = Mathf.Repeat(_beatPhase + dt * hz, 1f);
            float a = Pulse(_beatPhase, 0.08f) + 0.55f * Pulse(_beatPhase, 0.28f);
            var c = _glowColour;
            c.a *= Mathf.Clamp01(a) * Mathf.Lerp(0.45f, 1f, _heartbeatDepth);
            _glow.enabled = true;
            _glow.color = c;
        }

        private static float Pulse(float phase, float centre)
        {
            float d = (phase - centre) / 0.07f;
            return Mathf.Exp(-d * d);
        }

        private static void SetWidth(Graphic g, int w)
        {
            var rt = g.rectTransform;
            var s = rt.sizeDelta;
            if ((int)s.x == w) return;
            rt.sizeDelta = new Vector2(w, s.y);
        }
    }
}
