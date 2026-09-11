using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The song's progress: a sunken groove three texels tall, filled in pale stone up to a gold
    /// bead — the one piece of gold on the plaque besides the medallion's flash, because where
    /// the song IS is the thing this panel exists to say.
    ///
    /// <para><b>The hit area is taller than the groove</b> (the whole row), so it is easy to
    /// grab; the old panel got that right and it is kept. Dragging CAPTURES the pointer, so a
    /// seek never drags the window. While the player drags, the groove shows where they are
    /// pointing, not where the song is, and the song only moves on release — seeking a
    /// streamed track on every drag frame stutters the audio.</para>
    ///
    /// <para><b>The fill is quantised to whole texels</b>, so the bead's leading edge steps a
    /// texel at a time instead of boiling between two.</para>
    /// </summary>
    public sealed class MusicGroove
    {
        public RectTransform Root { get; }
        public MusicHudPointer Pointer { get; }

        private readonly Image _fill;
        private readonly Image _bead;
        private readonly MusicHudArt _art;
        private readonly int _innerX, _innerW, _grooveY;
        private float _progress;
        private bool _seeking, _hot, _enabled = true;
        private float _seekFraction;
        private int _drawnFill = -1;
        private bool _drawnBig, _drawnEnabled = true;

        /// <summary>Raised on release after a click or drag, with the fraction to seek to.</summary>
        public event Action<float> SeekCommitted;

        /// <summary>Raised while hovering, with the fraction under the pointer (NaN when it leaves).</summary>
        public event Action<float> Hovered;

        public bool Seeking => _seeking;
        public float DisplayedFraction => _seeking ? _seekFraction : _progress;

        /// <summary>Texels of fill currently drawn. For the tests.</summary>
        public int FillTexels => Mathf.RoundToInt(_fill.rectTransform.sizeDelta.x);

        /// <summary>The bead's centre in the parent's texels.</summary>
        public Vector2 BeadCentre => Root.anchoredPosition + _bead.rectTransform.anchoredPosition
                                     + _bead.rectTransform.sizeDelta * 0.5f;

        public MusicGroove(Transform parent, MusicHudArt art, int x, int y, int w, int h, Color fill)
        {
            _art = art;
            Root = HudRect.Make("Groove", parent, x, y, w, h);
            var hit = Root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            _grooveY = (h - 3) / 2;
            HudRect.MakeImage("Well", Root, art.WellThin, 0, _grooveY, w, 3, Image.Type.Sliced);
            _innerX = 1;
            _innerW = w - 2;
            _fill = HudRect.MakeImage("Fill", Root, art.White, _innerX, _grooveY + 1, 0, 1);
            _fill.color = fill;
            _bead = HudRect.MakeImage("Bead", Root, art.Bead, 0, 0, 5, 5);

            Pointer = Root.gameObject.AddComponent<MusicHudPointer>();
            Pointer.CapturesDrag = true;
            Pointer.Enter = () => { _hot = true; Draw(); };
            Pointer.Exit = () => { _hot = false; Hovered?.Invoke(float.NaN); Draw(); };
            Pointer.Down = p => { if (!_enabled) return; _seeking = true; _seekFraction = FractionAt(p.x); Draw(); };
            Pointer.Drag = p => { if (!_seeking) return; _seekFraction = FractionAt(p.x); Hovered?.Invoke(_seekFraction); Draw(); };
            Pointer.Up = p => Commit(FractionAt(p.x));
            Pointer.Move = UpdateHover;
            Draw();
        }

        /// <summary>The fraction of the song at a texel x inside the groove's row.</summary>
        public float FractionAt(float localX) => Mathf.Clamp01((localX - _innerX) / Mathf.Max(1f, _innerW));

        /// <summary>Hover feedback while the pointer moves over the groove.</summary>
        public void UpdateHover(Vector2 localTexel)
        {
            if (_hot && !_seeking) Hovered?.Invoke(FractionAt(localTexel.x));
        }

        /// <summary>Releases a seek at <paramref name="fraction"/>. Public so a test can seek.</summary>
        public void Commit(float fraction)
        {
            if (!_seeking) return;
            _seeking = false;
            _seekFraction = Mathf.Clamp01(fraction);
            _progress = _seekFraction;
            SeekCommitted?.Invoke(_seekFraction);
            Draw();
        }

        /// <summary>Starts a seek the way a press would. For the tests.</summary>
        public void BeginSeek(float fraction)
        {
            if (!_enabled) return;
            _seeking = true;
            _seekFraction = Mathf.Clamp01(fraction);
            Draw();
        }

        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            if (!enabled) _seeking = false;
            Draw();
        }

        public void SetProgress(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Approximately(fraction, _progress)) return;
            _progress = fraction;
            if (!_seeking) Draw();
        }

        private void Draw()
        {
            float f = DisplayedFraction;
            int fillW = _enabled ? Mathf.RoundToInt(_innerW * f) : 0;
            bool big = (_hot || _seeking) && _enabled;
            // A rect write dirties the canvas whether or not the value changed; the song moves
            // a texel every second or so, so almost every frame writes nothing.
            if (fillW == _drawnFill && big == _drawnBig && _enabled == _drawnEnabled) return;
            _drawnFill = fillW;
            _drawnBig = big;
            _drawnEnabled = _enabled;
            HudRect.Place(_fill.rectTransform, _innerX, _grooveY + 1, fillW, 1);
            var sprite = big ? _art.BeadHot : _art.Bead;
            int size = big ? 7 : 5;
            if (_bead.sprite != sprite) _bead.sprite = sprite;
            int cx = _innerX + fillW;
            int cy = _grooveY + 1;
            HudRect.Place(_bead.rectTransform, cx - size / 2, cy - size / 2, size, size);
            _bead.enabled = _enabled;
        }
    }
}
