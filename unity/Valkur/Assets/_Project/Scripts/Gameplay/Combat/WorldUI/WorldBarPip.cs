using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The dash charge, drawn as a square that fills from the bottom.
    ///
    /// <para><b>Why not a third bar.</b> It was one: a full-width strip identical in shape to the
    /// mana bar and separated from it only by hue — cyan against blue, which is close to the same
    /// channel and closer still for a colour-blind player. A charge is a COUNT, not a quantity,
    /// so it gets a shape a quantity does not have and sits on the same row as the mana it shares
    /// a decision with. The stack loses a whole row for it.</para>
    ///
    /// <para>The flash when the charge returns is the point of the widget. The old one drew the
    /// charging ramp and then simply stopped moving, so the one instant a dash readout exists
    /// for — the moment the ability is available again — produced no pixel at all.</para>
    /// </summary>
    internal sealed class WorldBarPip
    {
        private readonly Transform _root;
        private readonly SpriteRenderer _frame;
        private readonly SpriteRenderer _core;
        private readonly float _side;
        private readonly float _coreSide;

        private float _shown = -1f;
        private bool _wasReady;
        private float _flashLeft;
        private float _alpha = 1f;
        private Color _readyColour = Color.cyan;
        private Color _chargingColour = Color.gray;
        private Color _frameColour = Color.black;
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

            _frame = MakePart("Frame", WorldBarArt.PipFrame, sortingBase);
            _core = MakePart("Core", WorldBarArt.PipCore, sortingBase + 1);
            _frame.size = new Vector2(_side, _side);
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

        public void SetActive(bool on)
        {
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        public bool IsActive => _root.gameObject.activeSelf;

        public void SetSortingBase(int order)
        {
            _frame.sortingOrder = order;
            _core.sortingOrder = order + 1;
        }

        public void Layout(float centreX, float centreY)
        {
            _root.localPosition = new Vector3(centreX, centreY, 0f);
            _dirty = true;
        }

        /// <summary>
        /// Set the pip's palette, resolved through <see cref="WorldBarArt.TintFor"/> for the
        /// reason that method records: a style colour multiplies, so it colours the generated
        /// pip and destroys a painted one.
        /// </summary>
        public void SetColours(Color ready, Color charging, Color frame)
        {
            _readyColour = WorldBarArt.TintFor(WorldBarSheetLayout.PIP_CORE, ready);
            _chargingColour = WorldBarArt.TintFor(WorldBarSheetLayout.PIP_CORE, charging);
            _frameColour = WorldBarArt.TintFor(WorldBarSheetLayout.PIP_FRAME, frame);
            _dirty = true;
        }

        public void SetAlpha(float alpha)
        {
            if (Mathf.Abs(alpha - _alpha) < 0.002f) return;
            _alpha = alpha;
            _dirty = true;
        }

        /// <summary>
        /// Report the charge, 0..1. Returns true when the charge just completed, so the rig can
        /// treat that as activity worth un-fading for.
        /// </summary>
        public bool SetCharge(float charge, WorldBarStyle style)
        {
            charge = Mathf.Clamp01(charge);
            bool ready = charge >= 1f;
            bool becameReady = ready && !_wasReady;
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

            float charge = Mathf.Clamp01(_shown);
            bool any = charge > 0f;
            if (_core.gameObject.activeSelf != any) _core.gameObject.SetActive(any);

            if (any)
            {
                // Grows upward from the pip's floor: a charge that grew from the middle would
                // read as a pulse rather than as something filling.
                float h = _coreSide * charge;
                _core.size = new Vector2(_coreSide, h);
                _core.transform.localPosition = new Vector3(0f, -_coreSide * 0.5f + h * 0.5f, 0f);
            }

            Color core = charge >= 1f ? _readyColour : _chargingColour;
            Color frame = _frameColour;
            if (_flashLeft > 0f && style.dashReadyFlashSeconds > 0f)
            {
                float t = Mathf.Clamp01(_flashLeft / style.dashReadyFlashSeconds);
                core = Color.Lerp(core, Color.white, t);
                frame = Color.Lerp(frame, _readyColour, t);
            }

            Write(_core, core);
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
