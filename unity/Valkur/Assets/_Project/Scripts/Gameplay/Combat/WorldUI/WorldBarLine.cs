using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// One drawn row of a <see cref="WorldBarRig"/>: a chamfered frame, the recess inside it, a
    /// delayed chip, the fill, and the quarter marks over the top.
    ///
    /// <para>Not a MonoBehaviour. A row is a piece of drawing owned by the rig, and making it a
    /// component would give it its own <c>Update</c>, its own enable/disable lifecycle and its own
    /// opinion about ordering — three things this readout has exactly one of, on purpose.</para>
    ///
    /// <para><b>Every renderer is <c>SpriteDrawMode.Sliced</c> and sized through
    /// <c>SpriteRenderer.size</c>, never through <c>transform.localScale</c>.</b> That is what
    /// keeps a texel a texel: a scaled quad resamples the source, so the old bars' 4x4 white
    /// square blown up to 64 x 8 screen pixels had no crisp edge to keep. Sliced sizing leaves the
    /// border texels alone and stretches only the middle, so the chamfered corners and the
    /// leading-edge highlight stay one texel wide at any width.</para>
    /// </summary>
    internal sealed class WorldBarLine
    {
        private const int NOTCH_COUNT = 3;   // quarter marks at 25 / 50 / 75 %

        private readonly Transform _root;
        private readonly SpriteRenderer _frame;
        private readonly SpriteRenderer _plate;
        private readonly SpriteRenderer _chip;
        private readonly SpriteRenderer _fill;
        private readonly SpriteRenderer[] _notches;

        private readonly int _rowTexels;
        private readonly WorldBarRow _row;

        private float _innerWidth;
        private float _innerHeight;

        private Color _fillColour = Color.white;
        private Color _lowColour = Color.white;
        private Color _chipColour = Color.white;
        private Color _frameColour = Color.black;
        private Color _plateColour = Color.black;
        private Color _notchColour = Color.black;

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

        /// <summary>Where the row's own centre sits, in the rig's local space.</summary>
        public float CentreY { get; private set; }

        /// <summary>The ratio this row is heading toward, 0..1.</summary>
        public float Target => _target;

        /// <summary>The ratio actually drawn this frame. What a test should assert on.</summary>
        public float Shown => _shown;

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
            _row = row;
            var go = new GameObject(name);
            _root = go.transform;
            _root.SetParent(parent, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            _frame = MakePart("Frame", WorldBarArt.Frame(row), sortingBase + 0);
            _plate = MakePart("Plate", WorldBarArt.Plate(row), sortingBase + 1);
            _chip = MakePart("Chip", WorldBarArt.Fill(row), sortingBase + 2);
            _fill = MakePart("Fill", WorldBarArt.Fill(row), sortingBase + 3);

            if (withNotches)
            {
                _notches = new SpriteRenderer[NOTCH_COUNT];
                for (int i = 0; i < NOTCH_COUNT; i++)
                    _notches[i] = MakePart("Notch" + i, WorldBarArt.Solid, sortingBase + 4);
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

        /// <summary>
        /// Whether the dark recess behind the fill is drawn at all.
        ///
        /// <para>Off is what makes a hollow frame read as a hollow frame: the empty part of the
        /// bar shows the world through it and the frame carries the scale. The renderer is
        /// deactivated rather than tinted clear, so an invisible plate costs nothing.</para>
        /// </summary>
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
            _frame.sortingOrder = order + 0;
            _plate.sortingOrder = order + 1;
            _chip.sortingOrder = order + 2;
            _fill.sortingOrder = order + 3;
            if (_notches != null)
                for (int i = 0; i < _notches.Length; i++)
                    _notches[i].sortingOrder = order + 4;
        }

        /// <summary>
        /// Place and size the row. <paramref name="width"/> is the OUTER width including the
        /// frame; the fill lives one texel inside it on every side. <paramref name="centreX"/>
        /// exists because the mana row is narrower than the health row — it gives up its right
        /// end to the dash pip — and a row that always centred itself would leave that end
        /// hanging off the stack.
        /// </summary>
        public void Layout(float width, float centreX, float centreY, bool notchesWanted)
        {
            CentreY = centreY;
            _root.localPosition = new Vector3(centreX, centreY, 0f);

            float rowHeight = WorldBarGeometry.Texels(_rowTexels);
            _innerWidth = Mathf.Max(0f, width - WorldBarGeometry.Texels(2));
            _innerHeight = Mathf.Max(WorldBarGeometry.TEXEL, rowHeight - WorldBarGeometry.Texels(2));

            _frame.size = new Vector2(width, rowHeight);
            _plate.size = new Vector2(_innerWidth, _innerHeight);

            if (_notches != null)
            {
                bool show = notchesWanted;
                for (int i = 0; i < _notches.Length; i++)
                {
                    var n = _notches[i];
                    if (n.gameObject.activeSelf != show) n.gameObject.SetActive(show);
                    if (!show) continue;
                    n.size = new Vector2(WorldBarGeometry.TEXEL, _innerHeight);
                    // Quarter marks sit ON the grid: the inner width is an even number of texels,
                    // so its quarters land on a texel boundary and the mark never straddles two.
                    float x = -_innerWidth * 0.5f + _innerWidth * ((i + 1) * 0.25f);
                    n.transform.localPosition = new Vector3(WorldBarGeometry.SnapToTexel(x), 0f, 0f);
                }
            }

            _dirty = true;
        }

        /// <summary>
        /// Set the palette. Colours are stored raw; the drawn alpha is the rig's fade.
        ///
        /// <para>Every colour goes through <see cref="WorldBarArt.TintFor"/> on the way in,
        /// because a style colour is an instruction to the GENERATOR and a painted piece has
        /// nothing left to colour — see that method for what the frame tint did to the painted
        /// sheet. Resolved HERE rather than in <c>Apply</c> so the lookup is paid once per
        /// palette change instead of once per frame.</para>
        /// </summary>
        public void SetColours(Color fill, Color low, Color chip, Color frame, Color plate,
                               Color notch, float lowThreshold)
        {
            string fillId = _row == WorldBarRow.Health
                ? WorldBarSheetLayout.FILL_HEALTH
                : WorldBarSheetLayout.FILL_RESOURCE;
            string frameId = _row == WorldBarRow.Health
                ? WorldBarSheetLayout.FRAME_HEALTH
                : WorldBarSheetLayout.FRAME_RESOURCE;
            string plateId = _row == WorldBarRow.Health
                ? WorldBarSheetLayout.PLATE_HEALTH
                : WorldBarSheetLayout.PLATE_RESOURCE;

            _fillColour = WorldBarArt.TintFor(fillId, fill);
            _lowColour = WorldBarArt.TintFor(fillId, low);
            _chipColour = WorldBarArt.TintFor(fillId, chip);
            _frameColour = WorldBarArt.TintFor(frameId, frame);
            _plateColour = WorldBarArt.TintFor(plateId, plate);
            _notchColour = WorldBarArt.TintFor(WorldBarSheetLayout.SOLID, notch);
            _lowThreshold = lowThreshold;
            _dirty = true;
        }

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
            if (heartbeat && _shown < _lowThreshold && _lowThreshold > 0f)
            {
                // The rhythm reports HOW bad it is: the closer to zero, the faster and deeper the
                // frame beats. A constant blink would only report that something is wrong.
                float severity = 1f - Mathf.Clamp01(_shown / _lowThreshold);
                float hz = style.heartbeatHz * Mathf.Lerp(0.45f, 1f, severity);
                float phase = Mathf.Sin(Time.time * hz * Mathf.PI * 2f) * 0.5f + 0.5f;
                wantedPulse = phase * style.heartbeatDepth * severity;
            }
            if (Mathf.Abs(wantedPulse - _framePulse) > 0.002f)
            {
                _framePulse = wantedPulse;
                moving = true;
            }

            if (!moving && !_dirty) return false;
            Apply(pixelsPerUnit, style);
            _dirty = false;
            return true;
        }

        private void Apply(float pixelsPerUnit, WorldBarStyle style)
        {
            float fillW = WorldBarGeometry.FillWidth(_shown, _innerWidth, pixelsPerUnit);
            float chipW = WorldBarGeometry.FillWidth(Mathf.Max(_chipShown, _shown), _innerWidth, pixelsPerUnit);

            ApplyBar(_fill, fillW);
            ApplyBar(_chip, chipW > fillW ? chipW : 0f);

            Color fill = _shown <= _lowThreshold ? _lowColour : _fillColour;
            if (_healPulseLeft > 0f && style.healPulseSeconds > 0f)
            {
                float t = _healPulseLeft / style.healPulseSeconds;
                fill = Color.Lerp(fill, Color.white, 0.55f * t);
            }
            Write(_fill, fill);
            Write(_chip, _chipColour);

            Color plate = _plateColour;
            if (_flashLeft > 0f && style.hitFlashSeconds > 0f)
            {
                float t = Mathf.Clamp01(_flashLeft / style.hitFlashSeconds);
                plate = Color.Lerp(plate, Color.white, 0.75f * t);
            }
            Write(_plate, plate);

            Color frame = _framePulse > 0f
                ? Color.Lerp(_frameColour, _lowColour, _framePulse)
                : _frameColour;
            Write(_frame, frame);

            if (_notches != null)
                for (int i = 0; i < _notches.Length; i++)
                    if (_notches[i].gameObject.activeSelf)
                        Write(_notches[i], _notchColour);
        }

        private void ApplyBar(SpriteRenderer sr, float width)
        {
            bool visible = width > 0f;
            if (sr.gameObject.activeSelf != visible) sr.gameObject.SetActive(visible);
            if (!visible) return;

            sr.size = new Vector2(width, _innerHeight);
            // Anchored on the LEFT inner edge. Centring the remainder instead would move the one
            // edge that must never move, and put it off the pixel grid on every odd width.
            sr.transform.localPosition =
                new Vector3(WorldBarGeometry.FillCentreX(width, _innerWidth), 0f, 0f);
        }

        private void Write(SpriteRenderer sr, Color c)
        {
            c.a *= _alpha;
            if (sr.color != c) sr.color = c;
        }
    }
}
