using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Diagnostics;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The frame-time graph: one column per frame, height proportional to the frame's duration
    /// against a FIXED ceiling, coloured by the budget it fell in.
    ///
    /// <para><b>A ring in a texture, scrolled by UV.</b> Each frame writes ONE column at the
    /// write head and moves the <c>RawImage</c>'s <c>uvRect</c> so that column lands at the
    /// right edge; the texture wraps (<c>TextureWrapMode.Repeat</c>). No mesh grows with the
    /// history, and nothing is re-laid out: the alternative — an <c>Image</c> per column —
    /// is 140 graphics the canvas re-batches every frame.</para>
    ///
    /// <para><b>A hitch keeps its mark as the graph scrolls.</b> The mark is written INTO the
    /// column (a bright top texel), so it travels with the frame it belongs to and falls off the
    /// left edge with it; nothing has to remember where a hitch was drawn.</para>
    ///
    /// <para>Sits under its own nested <c>Canvas</c> so a per-frame uv change rebuilds this one
    /// quad and not the whole panel.</para>
    /// </summary>
    public sealed class DebugFrameGraph
    {
        private readonly Texture2D _tex;
        private readonly RawImage _image;
        private readonly Color32[] _column;
        private readonly int _w;
        private readonly int _h;
        private readonly Color32 _empty;
        private int _head;

        public RectTransform Root { get; }
        public int Width => _w;
        public int Height => _h;

        /// <summary>For the tests: how many columns have been written since the last repaint.</summary>
        public int Written { get; private set; }

        public DebugFrameGraph(Transform parent, string name, int x, int y, int w, int h, Color empty)
        {
            _w = Mathf.Max(2, w);
            _h = Mathf.Max(2, h);
            _empty = empty;
            _column = new Color32[_h];

            Root = HudRect.Make(name, parent, x, y, _w, _h);
            var canvas = Root.gameObject.AddComponent<Canvas>();
            canvas.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;

            _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false, false)
            {
                name = "DebugHud_" + name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave,
            };
            var fill = new Color32[_w * _h];
            for (int i = 0; i < fill.Length; i++) fill[i] = _empty;
            _tex.SetPixels32(fill);
            _tex.Apply(false, false);

            _image = Root.gameObject.AddComponent<RawImage>();
            _image.texture = _tex;
            _image.raycastTarget = false;
            ApplyUv();
        }

        public void Destroy()
        {
            if (_tex == null) return;
            if (Application.isPlaying) Object.Destroy(_tex);
            else Object.DestroyImmediate(_tex);
        }

        /// <summary>Appends one frame at the right edge.</summary>
        public void Push(float ms, bool hitch, DebugHudStyle style)
        {
            WriteColumn(_head, ms, hitch, style);
            _head = (_head + 1) % _w;
            _tex.Apply(false, false);
            ApplyUv();
            Written++;
        }

        /// <summary>Redraws every column from the monitor's history — used when the graph is shown
        /// again, so it opens on the last seconds instead of on an empty strip.</summary>
        public void Repaint(FrameTimeHistory history, DebugHudStyle style, float hitchFloorMs)
        {
            _head = 0;
            int n = history != null ? Mathf.Min(history.Count, _w) : 0;
            // Oldest shown first, so the newest lands at the right edge like a live push.
            for (int col = 0; col < _w; col++)
            {
                int age = _w - 1 - col;
                if (age < n)
                {
                    float ms = history.MsAt(age);
                    WriteColumn(col, ms, ms >= hitchFloorMs && ms >= style.warnMs, style);
                }
                else WriteEmpty(col);
            }
            _head = 0;
            _tex.Apply(false, false);
            ApplyUv();
            Written = 0;
        }

        /// <summary>Height in texels a frame of <paramref name="ms"/> fills. Pure, for the tests.</summary>
        public static int BarHeight(float ms, float ceilingMs, int height)
        {
            if (ms <= 0f || ceilingMs <= 0f) return 0;
            int h = Mathf.CeilToInt(ms / ceilingMs * height);
            return Mathf.Clamp(h, 1, height);
        }

        private void WriteColumn(int col, float ms, bool hitch, DebugHudStyle style)
        {
            int bar = BarHeight(ms, style.graphCeilingMs, _h);
            Color barColour = style.ForFrame(ms);
            Color32 body = Color.Lerp(barColour, (Color)_empty, 0.25f);
            Color32 top = barColour;
            bool over = ms > style.graphCeilingMs;
            for (int y = 0; y < _h; y++)
            {
                if (y < bar - 1) _column[y] = body;
                else if (y == bar - 1) _column[y] = top;          // the crest reads as a line
                else _column[y] = _empty;
            }
            // Past the ceiling, or a hitch: the top texel goes white-hot. It is the mark that
            // survives in the column while it scrolls.
            if (over || hitch) _column[_h - 1] = new Color32(255, 250, 240, 255);
            _tex.SetPixels32(col, 0, 1, _h, _column);
        }

        private void WriteEmpty(int col)
        {
            for (int y = 0; y < _h; y++) _column[y] = _empty;
            _tex.SetPixels32(col, 0, 1, _h, _column);
        }

        private void ApplyUv()
        {
            // The column just written is at _head - 1; put it at the right edge.
            _image.uvRect = new Rect((float)_head / _w, 0f, 1f, 1f);
        }
    }
}
