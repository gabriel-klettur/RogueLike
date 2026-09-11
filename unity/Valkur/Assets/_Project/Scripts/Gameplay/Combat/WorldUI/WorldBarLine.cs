using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// One drawn row of a <see cref="WorldBarRig"/>, built from the outside in: a rank halo, the
    /// black outline, a translucent navy plate, two metal end caps, and inside them the fill —
    /// drawn in four hue-shifted tones with a delayed chip behind it and motes of light drifting
    /// through it.
    ///
    /// <para>Not a MonoBehaviour. A row is a piece of drawing owned by the rig, and making it a
    /// component would give it its own <c>Update</c>, its own enable/disable lifecycle and its own
    /// opinion about ordering — three things this readout has exactly one of, on purpose.</para>
    ///
    /// <para><b>Every renderer is <c>SpriteDrawMode.Sliced</c> and sized through
    /// <c>SpriteRenderer.size</c>, never through <c>transform.localScale</c>.</b> That is what
    /// keeps a texel a texel: a scaled quad resamples the source, so the old bars' 4x4 white
    /// square blown up to 64 x 8 screen pixels had no crisp edge to keep.</para>
    ///
    /// <para><b>Why the rank is in the CAPS and the HALO and never in the outline.</b> The outline
    /// is the one thing separating the bar from whatever ground it is drawn over, pale cobblestone
    /// and dark foliage alike, so it has to be near black for every rank. The painted colour
    /// frames tried to carry rank and separation at once and could do neither: tinting them was
    /// destructive, so elite and boss bars were drawn identical to an ordinary monster's.</para>
    /// </summary>
    internal sealed class WorldBarLine
    {
        /// <summary>How many consecutive sorting orders one row claims.</summary>
        internal const int SLOT_COUNT = 10;

        private const int O_HALO = 0, O_PLATE = 1, O_CHIP = 2, O_FILL = 3, O_SHADE = 4,
                          O_EDGE = 5, O_MOTE = 6, O_CAPS = 7, O_FRAME = 8, O_NOTCH = 9;

        private const int NOTCH_COUNT = 3;   // quarter marks at 25 / 50 / 75 %
        private const int MAX_MOTES = 6;

        private readonly Transform _root;
        private readonly SpriteRenderer _halo;
        private readonly SpriteRenderer _frame;
        private readonly SpriteRenderer _plate;
        private readonly SpriteRenderer _caps;
        private readonly SpriteRenderer _chip;
        private readonly SpriteRenderer _fill;
        private readonly SpriteRenderer _fillHi;
        private readonly SpriteRenderer _fillLo;
        private readonly SpriteRenderer _edge;
        private readonly SpriteRenderer[] _notches;
        private readonly SpriteRenderer[] _motes;

        private readonly float[] _moteAt = new float[MAX_MOTES];     // texels along the fill
        private readonly int[] _moteRow = new int[MAX_MOTES];
        private readonly float[] _motePhase = new float[MAX_MOTES];

        private readonly int _rowTexels;
        private readonly string _fillId, _frameId, _plateId, _capsId;

        private float _innerWidth;     // the span the fill may occupy, between the two caps
        private float _innerHeight;
        private float _fillLeft;       // x of the fill's fixed left edge, in the row's space
        private float _lastFillWidth;

        private WorldBarRamp _ramp;
        private WorldBarRamp _lowRamp;
        private Color _chipColour = Color.white;
        private Color _outlineColour = Color.black;
        private Color _plateColour = Color.black;
        private Color _notchColour = Color.black;
        private Color _capColour = Color.gray;
        private Color _haloColour = Color.clear;
        private Color _lowPulseColour = Color.red;

        private float _target = 1f;
        private float _shown = 1f;
        private float _chipShown;
        private float _chipHoldLeft;
        private float _lowThreshold;
        private float _alpha = 1f;
        private float _flashLeft;
        private float _healPulseLeft;
        private float _framePulse;
        private bool _dirty = true;

        private bool _motesBoosted;
        private int _moteCountShown = -1;

        /// <summary>Where the row's own centre sits, in the rig's local space.</summary>
        public float CentreY { get; private set; }

        /// <summary>Where the row's own centre sits on X, in the rig's local space.</summary>
        public float CentreX { get; private set; }

        /// <summary>The ratio this row is heading toward, 0..1.</summary>
        public float Target => _target;

        /// <summary>The ratio actually drawn this frame. What a test should assert on.</summary>
        public float Shown => _shown;

        /// <summary>Width the fill may occupy — the interior between the two caps.</summary>
        public float InnerWidth => _innerWidth;

        /// <summary>Height of the interior.</summary>
        public float InnerHeight => _innerHeight;

        /// <summary>X of the fill's fixed left edge, in the rig's local space.</summary>
        public float FillLeftX => CentreX + _fillLeft;

        /// <summary>How many fill motes are drawn right now. For the tests.</summary>
        public int MotesShown => Mathf.Max(0, _moteCountShown);

        /// <summary>The colours the fill is being drawn in (not counting the low switch).</summary>
        public WorldBarRamp Ramp => _ramp;

        /// <summary>
        /// Whether the outline is beating. The DECISION, separate from the phase: the phase rides
        /// <c>Time.time</c> and can legitimately be at zero on the frame a test looks.
        /// </summary>
        public bool HeartbeatActive { get; private set; }

        /// <summary>
        /// True from the moment a blow arms the chip until it has finished catching up with the
        /// fill. Includes the HOLD, which is the part a test can observe without a frame: right
        /// after the blow the chip and the fill are still the same width, and what makes the chip
        /// exist is that it has been told to wait.
        /// </summary>
        public bool ChipActive => _chipHoldLeft > 0f || _chipShown > _shown + 1e-4f;

        public WorldBarLine(Transform parent, string name, WorldBarRow row, int rowTexels,
                            int sortingBase, bool withNotches)
        {
            _rowTexels = Mathf.Max(3, rowTexels);
            bool health = row == WorldBarRow.Health;
            _fillId = health ? WorldBarSheetLayout.FILL_HEALTH : WorldBarSheetLayout.FILL_RESOURCE;
            _frameId = health ? WorldBarSheetLayout.FRAME_HEALTH : WorldBarSheetLayout.FRAME_RESOURCE;
            _plateId = health ? WorldBarSheetLayout.PLATE_HEALTH : WorldBarSheetLayout.PLATE_RESOURCE;
            _capsId = health ? WorldBarSheetLayout.CAPS_HEALTH : WorldBarSheetLayout.CAPS_RESOURCE;

            var go = new GameObject(name);
            _root = go.transform;
            _root.SetParent(parent, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            _halo = MakePart("Halo", WorldBarArt.Frame(row), sortingBase + O_HALO);
            _plate = MakePart("Plate", WorldBarArt.Plate(row), sortingBase + O_PLATE);
            _chip = MakePart("Chip", WorldBarArt.Fill(row), sortingBase + O_CHIP);
            _fill = MakePart("Fill", WorldBarArt.Fill(row), sortingBase + O_FILL);
            _fillHi = MakePart("FillHighlight", WorldBarArt.Solid, sortingBase + O_SHADE);
            _fillLo = MakePart("FillShadow", WorldBarArt.Solid, sortingBase + O_SHADE);
            _edge = MakePart("LeadingEdge", WorldBarArt.Solid, sortingBase + O_EDGE);
            _caps = MakePart("Caps", WorldBarArt.Caps(row), sortingBase + O_CAPS);
            _frame = MakePart("Frame", WorldBarArt.Frame(row), sortingBase + O_FRAME);

            _motes = new SpriteRenderer[MAX_MOTES];
            for (int i = 0; i < MAX_MOTES; i++)
            {
                _motes[i] = MakePart("Mote" + i, WorldBarArt.Solid, sortingBase + O_MOTE);
                _motes[i].gameObject.SetActive(false);
                // Deterministic spread, so two rigs spawned together do not twinkle in lockstep
                // and a test sees the same row every run.
                _moteAt[i] = i * 3.7f + (name.Length % 5);
                _motePhase[i] = i * 0.61f + name.Length * 0.13f;
                _moteRow[i] = i;
            }

            if (withNotches)
            {
                _notches = new SpriteRenderer[NOTCH_COUNT];
                for (int i = 0; i < NOTCH_COUNT; i++)
                    _notches[i] = MakePart("Notch" + i, WorldBarArt.Solid, sortingBase + O_NOTCH);
            }
        }

        private SpriteRenderer MakePart(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            // sharedMaterial, never material: reading or assigning `.material` is what clones a
            // material per renderer, and this project has already measured that leak twice.
            sr.sharedMaterial = WorldBarArt.Material;
            sr.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Whether the translucent recess behind the fill is drawn at all.</summary>
        public void SetPlateVisible(bool visible)
        {
            if (_plate.gameObject.activeSelf != visible) _plate.gameObject.SetActive(visible);
        }

        /// <summary>Show or hide the whole row without destroying it.</summary>
        public void SetActive(bool on)
        {
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        public bool IsActive => _root.gameObject.activeSelf;

        /// <summary>Re-point every renderer's sorting order after the owner moved on Y.</summary>
        public void SetSortingBase(int order)
        {
            _halo.sortingOrder = order + O_HALO;
            _plate.sortingOrder = order + O_PLATE;
            _chip.sortingOrder = order + O_CHIP;
            _fill.sortingOrder = order + O_FILL;
            _fillHi.sortingOrder = order + O_SHADE;
            _fillLo.sortingOrder = order + O_SHADE;
            _edge.sortingOrder = order + O_EDGE;
            for (int i = 0; i < _motes.Length; i++) _motes[i].sortingOrder = order + O_MOTE;
            _caps.sortingOrder = order + O_CAPS;
            _frame.sortingOrder = order + O_FRAME;
            if (_notches != null)
                for (int i = 0; i < _notches.Length; i++)
                    _notches[i].sortingOrder = order + O_NOTCH;
        }

        /// <summary>
        /// Place and size the row. <paramref name="width"/> is the OUTER width including the
        /// outline; inside it the plate takes one texel off every side and the fill a further
        /// texel off each end for the metal caps. <paramref name="centreX"/> exists because the
        /// mana row is narrower than the health row — it gives up its right end to the dash pip.
        /// </summary>
        public void Layout(float width, float centreX, float centreY, bool notchesWanted)
        {
            float t = WorldBarGeometry.TEXEL;
            CentreX = centreX;
            CentreY = centreY;
            _root.localPosition = new Vector3(centreX, centreY, 0f);

            float rowHeight = WorldBarGeometry.Texels(_rowTexels);
            _innerHeight = Mathf.Max(t, rowHeight - 2f * t);
            float capInset = 2f * t;   // outline + cap column
            _innerWidth = Mathf.Max(t, width - 2f * capInset);
            _fillLeft = -width * 0.5f + capInset;

            _frame.size = new Vector2(width, rowHeight);
            _halo.size = new Vector2(width + 2f * t, rowHeight + 2f * t);
            _plate.size = new Vector2(Mathf.Max(t, width - 2f * t), _innerHeight);
            _caps.size = new Vector2(Mathf.Max(2f * t, width - 2f * t), _innerHeight);

            if (_notches != null)
            {
                bool show = notchesWanted;
                for (int i = 0; i < _notches.Length; i++)
                {
                    var n = _notches[i];
                    if (n.gameObject.activeSelf != show) n.gameObject.SetActive(show);
                    if (!show) continue;
                    n.size = new Vector2(t, _innerHeight);
                    float x = _fillLeft + _innerWidth * ((i + 1) * 0.25f);
                    n.transform.localPosition = new Vector3(WorldBarGeometry.SnapToTexel(x), 0f, 0f);
                }
            }

            _moteCountShown = -1;
            _dirty = true;
        }

        /// <summary>
        /// Set the palette. Each colour goes through <see cref="WorldBarArt.TintFor"/>, because a
        /// style colour is an instruction to the GENERATED art and a painted piece has nothing
        /// left to colour. The fill gets its whole ramp from <see cref="WorldBarPalette"/> here,
        /// once per palette change rather than once per frame.
        /// </summary>
        public void SetColours(Color fill, Color low, Color chip, Color outline, Color plate,
                               Color notch, float lowThreshold)
        {
            _ramp = WorldBarPalette.Ramp(WorldBarArt.TintFor(_fillId, fill));
            _lowRamp = WorldBarPalette.Ramp(WorldBarArt.TintFor(_fillId, low));
            _chipColour = WorldBarArt.TintFor(_fillId, chip);
            _outlineColour = WorldBarArt.TintFor(_frameId, outline);
            _plateColour = WorldBarArt.TintFor(_plateId, plate);
            _notchColour = WorldBarArt.TintFor(WorldBarSheetLayout.SOLID, notch);
            _lowThreshold = lowThreshold;
            _dirty = true;
        }

        /// <summary>The rank's metal for the end caps, its halo (clear for none) and the low pulse.</summary>
        public void SetRankColours(Color cap, Color halo, Color lowPulse)
        {
            _capColour = WorldBarArt.TintFor(_capsId, cap);
            _haloColour = WorldBarArt.TintFor(_frameId, halo);
            _lowPulseColour = lowPulse;
            _dirty = true;
        }

        /// <summary>Faster motes while the resource is regenerating.</summary>
        public void SetMotesBoosted(bool boosted) => _motesBoosted = boosted;

        /// <summary>
        /// Point the row at a new ratio. <paramref name="leaveChip"/> asks for the delayed chunk —
        /// wanted for a blow, and deliberately not for a heal, a max-HP change or a restore.
        /// </summary>
        public void SetRatio(float ratio, bool instant, bool leaveChip, WorldBarStyle style)
        {
            ratio = Mathf.Clamp01(ratio);
            bool fell = ratio < _target - 1e-4f;
            bool rose = ratio > _target + 1e-4f;
            _target = ratio;

            if (instant)
            {
                _shown = ratio;
                _chipShown = ratio;
                _chipHoldLeft = 0f;
            }
            else if (leaveChip && fell)
            {
                // The chip keeps whatever the bar was showing, which is the honest quantity: it
                // is the width the player last saw, not the width the model held.
                if (_chipShown < _shown) _chipShown = _shown;
                _chipHoldLeft = style.chipHoldSeconds;
            }
            else if (rose)
            {
                // A rise retires the chip outright: it is the ghost of a chunk that was LOST, and
                // there is no longer one. Snapping it to the width currently DRAWN rather than to
                // the new target matters — setting it to the target would leave a bright band
                // running ahead of the fill for the whole heal, which reads as a preview of
                // health the creature does not have yet.
                _chipShown = _shown;
                _chipHoldLeft = 0f;
                _healPulseLeft = style.healPulseSeconds;
            }

            _dirty = true;
        }

        /// <summary>Flash the plate. Called on a blow, never on a heal.</summary>
        public void Flash(float seconds)
        {
            _flashLeft = Mathf.Max(_flashLeft, seconds);
            _dirty = true;
        }

        /// <summary>The rig's fade, 0..1, multiplied into every part's alpha.</summary>
        public void SetAlpha(float alpha)
        {
            if (Mathf.Abs(alpha - _alpha) < 0.002f) return;
            _alpha = alpha;
            _dirty = true;
        }

        /// <summary>X, in the rig's local space, of a point <paramref name="ratio"/> along the fill.</summary>
        public float XAtRatio(float ratio) => FillLeftX + Mathf.Clamp01(ratio) * _innerWidth;

        /// <summary>
        /// Advance the row. Returns true when something was written, so the rig can tell an idle
        /// frame from a live one.
        /// </summary>
        public bool Tick(float dt, float pixelsPerUnit, WorldBarStyle style, bool heartbeat)
        {
            bool moving = false;

            if (Mathf.Abs(_shown - _target) > 1e-4f)
            {
                _shown = Mathf.Lerp(_shown, _target, 1f - Mathf.Exp(-style.fillLerpSpeed * dt));
                if (Mathf.Abs(_shown - _target) < 0.0008f) _shown = _target;
                moving = true;
            }

            if (_chipHoldLeft > 0f)
            {
                _chipHoldLeft -= dt;
                moving = true;
            }
            else if (_chipShown > _shown + 1e-4f)
            {
                float rate = style.chipDrainSeconds > 0f ? 1f / style.chipDrainSeconds : 100f;
                _chipShown = Mathf.MoveTowards(_chipShown, _shown, rate * dt);
                moving = true;
            }
            else if (_chipShown < _shown)
            {
                _chipShown = _shown;
                moving = true;
            }

            if (_flashLeft > 0f) { _flashLeft -= dt; moving = true; }
            if (_healPulseLeft > 0f) { _healPulseLeft -= dt; moving = true; }

            float wantedPulse = 0f;
            HeartbeatActive = heartbeat && _shown < _lowThreshold && _lowThreshold > 0f;
            if (HeartbeatActive)
            {
                // The rhythm reports HOW bad it is: the closer to zero, the faster and deeper the
                // outline beats. A constant blink would only report that something is wrong.
                float severity = 1f - Mathf.Clamp01(_shown / _lowThreshold);
                float hz = style.heartbeatHz * Mathf.Lerp(0.45f, 1f, severity);
                float phase = Mathf.Sin(Time.time * hz * Mathf.PI * 2f) * 0.5f + 0.5f;
                wantedPulse = phase * Mathf.Lerp(0.35f, 1f, severity) * style.heartbeatDepth;
            }
            if (Mathf.Abs(wantedPulse - _framePulse) > 0.002f)
            {
                _framePulse = wantedPulse;
                moving = true;
            }

            bool wrote = false;
            if (moving || _dirty)
            {
                Apply(pixelsPerUnit, style);
                _dirty = false;
                wrote = true;
            }

            if (TickMotes(dt, pixelsPerUnit, style)) wrote = true;
            return wrote;
        }

        private void Apply(float pixelsPerUnit, WorldBarStyle style)
        {
            float t = WorldBarGeometry.TEXEL;
            float fillW = WorldBarGeometry.FillWidth(_shown, _innerWidth, pixelsPerUnit);
            float chipW = WorldBarGeometry.FillWidth(Mathf.Max(_chipShown, _shown), _innerWidth, pixelsPerUnit);
            _lastFillWidth = fillW;

            var ramp = _shown <= _lowThreshold ? _lowRamp : _ramp;
            if (_healPulseLeft > 0f && style.healPulseSeconds > 0f)
            {
                float k = 0.55f * (_healPulseLeft / style.healPulseSeconds);
                ramp.Base = Color.Lerp(ramp.Base, Color.white, k);
                ramp.Highlight = Color.Lerp(ramp.Highlight, Color.white, k);
                ramp.Shadow = Color.Lerp(ramp.Shadow, Color.white, k * 0.6f);
                ramp.Edge = Color.Lerp(ramp.Edge, Color.white, k);
            }

            float edgeW = Mathf.Min(t, fillW);
            float bodyW = Mathf.Max(0f, fillW - edgeW);

            Span(_chip, 0f, chipW > fillW ? chipW : 0f, 0f, _innerHeight);
            Span(_fill, 0f, fillW, 0f, _innerHeight);
            Span(_edge, fillW - edgeW, edgeW, 0f, _innerHeight);

            // Four rows or more: highlight on top, shadow at the foot, body between. TWO rows (the
            // mana bar) take the shadow only — measured, a highlight there is half the bar and it
            // reads as a two-tone stripe rather than as light on a tube.
            bool hasHi = _innerHeight >= 3f * t - 1e-4f;
            bool hasLo = _innerHeight >= 2f * t - 1e-4f;
            Span(_fillHi, 0f, hasHi ? bodyW : 0f, _innerHeight * 0.5f - t * 0.5f, t);
            Span(_fillLo, 0f, hasLo ? bodyW : 0f, -_innerHeight * 0.5f + t * 0.5f, t);

            Write(_fill, ramp.Base);
            Write(_fillHi, ramp.Highlight);
            Write(_fillLo, ramp.Shadow);
            Write(_edge, ramp.Edge);
            Write(_chip, _chipColour);

            Color plate = _plateColour;
            if (_flashLeft > 0f && style.hitFlashSeconds > 0f)
            {
                float k = Mathf.Clamp01(_flashLeft / style.hitFlashSeconds);
                plate = Color.Lerp(plate, new Color(1f, 0.95f, 0.9f, 0.9f), 0.8f * k);
            }
            Write(_plate, plate);

            Write(_frame, _framePulse > 0f
                ? Color.Lerp(_outlineColour, _lowPulseColour, _framePulse)
                : _outlineColour);
            Write(_caps, _capColour);

            bool halo = _haloColour.a > 0.01f;
            if (_halo.gameObject.activeSelf != halo) _halo.gameObject.SetActive(halo);
            if (halo) Write(_halo, _haloColour);

            if (_notches != null)
                for (int i = 0; i < _notches.Length; i++)
                    if (_notches[i].gameObject.activeSelf)
                        Write(_notches[i], _notchColour);
        }

        /// <summary>
        /// The motes: single texels of the highlight tone drifting along the fill toward its
        /// leading edge and twinkling. They are the "particle background" the fill was asked to
        /// have, built from the same atlas and material as the bar so they cost no extra batch —
        /// a ParticleSystem per bar would be a draw call and a simulation per creature.
        ///
        /// <para>They exist only where the fill does: the count follows the drawn width, so an
        /// emptying bar visibly loses its light, and they never cross into the chip or the
        /// leading edge.</para>
        /// </summary>
        private bool TickMotes(float dt, float pixelsPerUnit, WorldBarStyle style)
        {
            float t = WorldBarGeometry.TEXEL;
            int innerTexels = Mathf.Max(1, WorldBarGeometry.TexelsOf(_innerHeight));
            float usable = _lastFillWidth - 2f * t;     // keep clear of both ends and the edge
            int count = 0;
            if (style.fillMotes && _alpha > 0.01f && usable > t)
                count = Mathf.Clamp(Mathf.FloorToInt(WorldBarGeometry.TexelsOf(_lastFillWidth)
                                                     / (float)Mathf.Max(1, style.moteEveryTexels)),
                                    0, MAX_MOTES);

            if (count == 0 && _moteCountShown == 0) return false;

            float speed = _motesBoosted ? style.moteBoostSpeedTexels : style.moteSpeedTexels;
            float usableTexels = usable / t;
            var ramp = _shown <= _lowThreshold ? _lowRamp : _ramp;

            for (int i = 0; i < MAX_MOTES; i++)
            {
                var sr = _motes[i];
                bool on = i < count;
                if (sr.gameObject.activeSelf != on) sr.gameObject.SetActive(on);
                if (!on) continue;

                _moteAt[i] += speed * dt;
                if (usableTexels > 0f && _moteAt[i] > usableTexels) _moteAt[i] %= usableTexels;

                // Rows the mote may travel: the body rows, never the highlight row (a light speck
                // on a light row is invisible) — or the one row there is on a thin bar.
                int row;
                if (innerTexels >= 3) row = 1 + (_moteRow[i] % (innerTexels - 2));
                else row = 0;

                float x = _fillLeft + t + _moteAt[i] * t;
                float y = -_innerHeight * 0.5f + t * (row + 0.5f);
                x = WorldBarGeometry.QuantizeToPixel(x, pixelsPerUnit);
                sr.size = new Vector2(t, t);
                sr.transform.localPosition = new Vector3(x, y, 0f);

                _motePhase[i] += dt * style.moteTwinkleHz;
                float tw = Mathf.Sin((_motePhase[i]) * Mathf.PI * 2f) * 0.5f + 0.5f;
                // Peak 0.7, not full: caught in a still frame a mote at full strength is a single
                // white texel in the middle of the fill and reads as a dead pixel.
                var c = ramp.Edge;
                c.a *= Mathf.Lerp(0.1f, 0.7f, tw * tw);
                Write(sr, c);
            }
            _moteCountShown = count;
            return count > 0;
        }

        /// <summary>
        /// Size and place a span of the fill. Anchored on the fill's LEFT edge: centring the
        /// remainder instead would move the one edge that must never move.
        /// </summary>
        private void Span(SpriteRenderer sr, float x0, float width, float centreY, float height)
        {
            bool visible = width > 1e-5f;
            if (sr.gameObject.activeSelf != visible) sr.gameObject.SetActive(visible);
            if (!visible) return;
            sr.size = new Vector2(width, height);
            sr.transform.localPosition = new Vector3(_fillLeft + x0 + width * 0.5f, centreY, 0f);
        }

        private void Write(SpriteRenderer sr, Color c)
        {
            c.a *= _alpha;
            if (sr.color != c) sr.color = c;
        }
    }
}
