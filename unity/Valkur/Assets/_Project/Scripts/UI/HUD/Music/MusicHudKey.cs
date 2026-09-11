using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// One stone key of the music panel: a 9-sliced frame and a pixel glyph centred on whole
    /// texels. Four looks — at rest, under the pointer, pressed, and unavailable — plus a LATCHED
    /// look for a key that toggles something open (the resonance), which stays down while the
    /// thing it opened is open: a switch reads its own state.
    ///
    /// <para>Pressing moves the glyph one texel DOWN and inverts the bevel, so the key goes in
    /// rather than changing colour. An unavailable key keeps its shape and loses its glyph's
    /// light: skip with no playlist must look like a key that cannot be pressed, not like a
    /// missing one.</para>
    /// </summary>
    public sealed class MusicHudKey
    {
        public RectTransform Root { get; }
        public MusicHudPointer Pointer { get; }

        private readonly MusicHudArt _art;
        private readonly Image _frame;
        private readonly Image _glyph;
        private readonly int _w, _h;
        private Color _ink = Color.white;
        private Color _inkDim = Color.grey;
        private bool _hot, _down, _enabled = true, _latched;
        private int _glyphW, _glyphH;

        /// <summary>Raised on a click while the key is enabled.</summary>
        public event Action Clicked;

        /// <summary>Raised when the pointer enters (true) or leaves (false) the key.</summary>
        public event Action<bool> HoverChanged;

        public bool Enabled => _enabled;
        public bool Latched => _latched;
        public bool Hot => _hot;
        public bool Pressed => _down || _latched;

        /// <summary>Centre of the key in the parent's texels, for tooltips and motes.</summary>
        public Vector2 Centre => Root.anchoredPosition + new Vector2(_w * 0.5f, _h * 0.5f);

        /// <summary>The glyph image. For the tests.</summary>
        public Image Glyph => _glyph;

        public MusicHudKey(Transform parent, MusicHudArt art, string name, int x, int y, int w, int h,
                           Sprite glyph)
        {
            _art = art;
            _w = w;
            _h = h;
            Root = HudRect.Make(name, parent, x, y, w, h);
            _frame = Root.gameObject.AddComponent<Image>();
            _frame.sprite = art.Key;
            _frame.type = Image.Type.Sliced;
            _frame.pixelsPerUnitMultiplier = 1f;
            _frame.raycastTarget = true;
            _glyph = HudRect.MakeImage("Glyph", Root, glyph, 0, 0, 1, 1);
            Pointer = Root.gameObject.AddComponent<MusicHudPointer>();
            Pointer.Enter = () => { _hot = true; Refresh(); HoverChanged?.Invoke(true); };
            Pointer.Exit = () => { _hot = false; _down = false; Refresh(); HoverChanged?.Invoke(false); };
            Pointer.Down = _ => { _down = _enabled; Refresh(); };
            Pointer.Up = _ => { _down = false; Refresh(); };
            Pointer.Click = Press;
            SetGlyph(glyph);
        }

        /// <summary>Clicks the key as the pointer would. Public so a test can press it.</summary>
        public void Press()
        {
            if (!_enabled) return;
            Clicked?.Invoke();
        }

        public void SetGlyph(Sprite glyph)
        {
            if (glyph == null) return;
            if (_glyph.sprite == glyph && _glyphW > 0) return;
            _glyph.sprite = glyph;
            var r = glyph.rect;
            _glyphW = Mathf.RoundToInt(r.width);
            _glyphH = Mathf.RoundToInt(r.height);
            PlaceGlyph();
        }

        public void SetInk(Color ink, Color inkDim)
        {
            _ink = ink;
            _inkDim = inkDim;
            Refresh();
        }

        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            if (!enabled) _down = false;
            Refresh();
        }

        public void SetLatched(bool latched)
        {
            if (_latched == latched) return;
            _latched = latched;
            Refresh();
        }

        private void PlaceGlyph()
        {
            int gx = (_w - _glyphW) / 2;
            int gy = (_h - _glyphH) / 2 + (Pressed ? -1 : 0) + (_h - _glyphH) % 2;
            HudRect.Place(_glyph.rectTransform, gx, gy, _glyphW, _glyphH);
        }

        private void Refresh()
        {
            var sprite = Pressed ? _art.KeyDown : (_hot && _enabled ? _art.KeyHot : _art.Key);
            if (_frame.sprite != sprite) _frame.sprite = sprite;
            var c = _enabled ? (_hot || _latched ? Color.Lerp(_ink, Color.white, 0.25f) : _ink) : _inkDim;
            if (_glyph.color != c) _glyph.color = c;
            PlaceGlyph();
        }
    }
}
