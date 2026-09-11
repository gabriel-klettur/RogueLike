using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The dash charge, drawn as a gem that fills from the bottom.
    ///
    /// <para><b>Why not a third bar.</b> It was one: a full-width strip identical in shape to the
    /// mana bar and separated from it only by hue. A charge is a COUNT, not a quantity, so it gets
    /// a shape a quantity does not have and sits on the same row as the mana it shares a decision
    /// with.</para>
    ///
    /// <para><b>Why it is generated and not the painted pill.</b> The painted pip was four brass
    /// corner brackets and a gold square: at six texels the brackets reduced to four dots and the
    /// ring was all but invisible, and — painted art being drawn white — "ready" and "charging"
    /// became the same colour and the ready flash interpolated white into white, i.e. did nothing.
    /// The pip keeps the sheet's GOLD and gets back the three states it exists to show: empty,
    /// charging (dim, rising), ready (bright, with a flash and a burst of sparks from the rig).</para>
    ///
    /// <para><b>Why it is shaded like a stone and not filled like a bar.</b> Measured on the first
    /// live capture, a flat square of the ready gold was the brightest thing over the character —
    /// brighter than the health it sits beside, in the state the pip spends most of its life in.
    /// A resting readout has to be the quiet one. It keeps the gold and gets a shadow row at its
    /// foot, the light row on top and one glint texel in the corner: the same four tones as the
    /// fill, which is what makes it read as a gem set into the frame rather than as a lamp.</para>
    /// </summary>
    internal sealed class WorldBarPip
    {
        /// <summary>How many consecutive sorting orders the pip claims.</summary>
        internal const int SLOT_COUNT = 6;

        private readonly Transform _root;
        private readonly SpriteRenderer _plate;
        private readonly SpriteRenderer _core;
        private readonly SpriteRenderer _coreLo;
        private readonly SpriteRenderer _coreHi;
        private readonly SpriteRenderer _glint;
        private readonly SpriteRenderer _frame;
        private readonly float _side;
        private readonly float _coreSide;

        private float _shown = -1f;
        private bool _wasReady;
        private float _flashLeft;
        private float _alpha = 1f;
        private WorldBarRamp _ready;
        private WorldBarRamp _charging;
        private Color _outline = Color.black;
        private Color _plateColour = Color.black;
        private bool _dirty = true;

        public WorldBarPip(Transform parent, int sideTexels, int sortingBase)
        {
            _side = WorldBarGeometry.Texels(Mathf.Max(3, sideTexels));
            _coreSide = WorldBarGeometry.Texels(Mathf.Max(1, sideTexels - 2));

            var go = new GameObject("DashPip");
            _root = go.transform;
            _root.SetParent(parent, false);
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            _plate = MakePart("Plate", WorldBarArt.Solid, sortingBase);
            _core = MakePart("Core", WorldBarArt.PipCore, sortingBase + 1);
            _coreLo = MakePart("CoreShadow", WorldBarArt.Solid, sortingBase + 2);
            _coreHi = MakePart("CoreHighlight", WorldBarArt.Solid, sortingBase + 3);
            _glint = MakePart("Glint", WorldBarArt.Solid, sortingBase + 4);
            _frame = MakePart("Frame", WorldBarArt.PipFrame, sortingBase + 5);
            _frame.size = new Vector2(_side, _side);
            _plate.size = new Vector2(_coreSide, _coreSide);
        }

        private SpriteRenderer MakePart(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.sharedMaterial = WorldBarArt.Material;
            sr.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Outer side of the pip in world units — what the row's layout has to reserve.</summary>
        public float Side => _side;

        /// <summary>The pip's centre in the rig's local space. Where the ready burst comes from.</summary>
        public Vector2 Centre => _root.localPosition;

        /// <summary>True while the charge is full.</summary>
        public bool IsReady => _wasReady;

        public void SetActive(bool on)
        {
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        public bool IsActive => _root.gameObject.activeSelf;

        public void SetSortingBase(int order)
        {
            _plate.sortingOrder = order;
            _core.sortingOrder = order + 1;
            _coreLo.sortingOrder = order + 2;
            _coreHi.sortingOrder = order + 3;
            _glint.sortingOrder = order + 4;
            _frame.sortingOrder = order + 5;
        }

        public void Layout(float centreX, float centreY)
        {
            _root.localPosition = new Vector3(centreX, centreY, 0f);
            _dirty = true;
        }

        /// <summary>The pip's palette: ready and charging each get a hue-shifted ramp.</summary>
        public void SetColours(Color ready, Color charging, Color outline, Color plate)
        {
            _ready = WorldBarPalette.Ramp(WorldBarArt.TintFor(WorldBarSheetLayout.PIP_CORE, ready));
            _charging = WorldBarPalette.Ramp(WorldBarArt.TintFor(WorldBarSheetLayout.PIP_CORE, charging));
            _outline = WorldBarArt.TintFor(WorldBarSheetLayout.PIP_FRAME, outline);
            _plateColour = plate;
            _dirty = true;
        }

        public void SetAlpha(float alpha)
        {
            if (Mathf.Abs(alpha - _alpha) < 0.002f) return;
            _alpha = alpha;
            _dirty = true;
        }

        /// <summary>
        /// Report the charge, 0..1. Returns true on the frame the charge completes, so the rig can
        /// un-fade and throw the ready burst.
        /// </summary>
        public bool SetCharge(float charge, WorldBarStyle style)
        {
            charge = Mathf.Clamp01(charge);
            bool ready = charge >= 1f;
            // The first report is the state at spawn, not an event: a pip that "became ready" on
            // the frame the character appeared would flash and spark on every load.
            bool becameReady = ready && !_wasReady && _shown >= 0f;
            if (becameReady) _flashLeft = style.dashReadyFlashSeconds;
            _wasReady = ready;

            if (Mathf.Abs(charge - _shown) > 0.002f)
            {
                _shown = charge;
                _dirty = true;
            }
            return becameReady;
        }

        public bool Tick(float dt, WorldBarStyle style)
        {
            bool moving = false;
            if (_flashLeft > 0f) { _flashLeft -= dt; moving = true; }
            if (!moving && !_dirty) return false;

            float t = WorldBarGeometry.TEXEL;
            float charge = Mathf.Clamp01(_shown);
            bool any = charge > 0f;
            // Grows upward from the pip's floor, in whole texels: a charge that grew from the
            // middle would read as a pulse, and a sub-texel height is a soft edge.
            float h = any ? Mathf.Max(t, WorldBarGeometry.SnapToTexel(_coreSide * charge)) : 0f;
            bool ready = charge >= 1f;
            if (_core.gameObject.activeSelf != any) _core.gameObject.SetActive(any);
            // The light row needs a body under it and the shadow row a body over it: at two rows
            // the shadow alone, at three or more both. The glint only on a full stone — a glint on
            // a charging one would say "ready" in the corner of something that is not.
            bool hi = any && h > t * 2.5f;
            bool lo = any && h > t * 1.5f;
            bool glint = ready && _coreSide > t * 2.5f;
            if (_coreHi.gameObject.activeSelf != hi) _coreHi.gameObject.SetActive(hi);
            if (_coreLo.gameObject.activeSelf != lo) _coreLo.gameObject.SetActive(lo);
            if (_glint.gameObject.activeSelf != glint) _glint.gameObject.SetActive(glint);
            float floor = -_coreSide * 0.5f;
            if (any)
            {
                _core.size = new Vector2(_coreSide, h);
                _core.transform.localPosition = new Vector3(0f, floor + h * 0.5f, 0f);
            }
            if (lo)
            {
                _coreLo.size = new Vector2(_coreSide, t);
                _coreLo.transform.localPosition = new Vector3(0f, floor + t * 0.5f, 0f);
            }
            if (hi)
            {
                _coreHi.size = new Vector2(_coreSide, t);
                _coreHi.transform.localPosition = new Vector3(0f, floor + h - t * 0.5f, 0f);
            }
            if (glint)
            {
                _glint.size = new Vector2(t, t);
                _glint.transform.localPosition =
                    new Vector3(-_coreSide * 0.5f + t * 0.5f, floor + h - t * 0.5f, 0f);
            }

            var ramp = ready ? _ready : _charging;
            Color core = ramp.Base, top = ramp.Highlight, foot = ramp.Shadow, shine = ramp.Edge;
            Color frame = _outline;
            if (_flashLeft > 0f && style.dashReadyFlashSeconds > 0f)
            {
                float k = Mathf.Clamp01(_flashLeft / style.dashReadyFlashSeconds);
                core = Color.Lerp(core, Color.white, 0.8f * k);
                top = Color.Lerp(top, Color.white, k);
                foot = Color.Lerp(foot, Color.white, 0.6f * k);
                frame = Color.Lerp(frame, _ready.Highlight, k);
            }

            Write(_plate, _plateColour);
            Write(_core, core);
            Write(_coreLo, foot);
            Write(_coreHi, top);
            Write(_glint, shine);
            Write(_frame, frame);
            _dirty = false;
            return true;
        }

        private void Write(SpriteRenderer sr, Color c)
        {
            c.a *= _alpha;
            if (sr.color != c) sr.color = c;
        }
    }
}
