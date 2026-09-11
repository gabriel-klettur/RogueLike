using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>Horizontal placement of a pixel label inside its rect.</summary>
    public enum HudTextAlign
    {
        Left = 0,
        Centre = 1,
        Right = 2,
    }

    /// <summary>
    /// A label drawn in the panel's bitmap face: one quad per glyph from the <see cref="HudArt"/>
    /// atlas, each carrying its own baked one-texel dark outline.
    ///
    /// <para><b>Every quad lands on a whole texel.</b> The label's origin is floored in its own
    /// local space, and every rect in the panel is placed bottom-left on integer coordinates
    /// (<see cref="HudRect"/>), so a centred "70/200" never sits half a texel off the frame
    /// around it — at 3 screen pixels per texel, half a texel is a smeared glyph.</para>
    ///
    /// <para>Characters the face has no glyph for are skipped rather than drawn as boxes; the
    /// owner decides whether a label is spellable (<see cref="HudPixelFont.CanSpell"/>) and
    /// falls back to TMP when it is not.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudPixelText : MaskableGraphic
    {
        private string _text = "";
        private HudFontFace _face = HudFontFace.Small;
        private HudTextAlign _align = HudTextAlign.Centre;
        private int _yNudge;
        private HudArt _art;

        /// <summary>The string being drawn.</summary>
        public string Text => _text;

        /// <summary>Ink width of the current label in texels. For layout and the tests.</summary>
        public int InkWidth => Art != null ? Art.Measure(_text, _face) : 0;

        private HudArt Art => _art ?? (_art = HudArt.Get());

        public override Texture mainTexture => Art != null && Art.Atlas != null ? Art.Atlas : s_WhiteTexture;

        /// <summary>Creates a label under <paramref name="parent"/>, placed on whole texels.</summary>
        public static HudPixelText Create(Transform parent, string name, HudArt art, HudFontFace face,
                                          HudTextAlign align, int x, int y, int w, int h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            HudRect.Place((RectTransform)go.transform, x, y, w, h);
            var t = go.AddComponent<HudPixelText>();
            t._art = art;
            t._face = face;
            t._align = align;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Changes the label. A write that changes nothing rebuilds nothing.</summary>
        public void SetText(string text)
        {
            text = text ?? "";
            if (text == _text) return;
            _text = text;
            SetVerticesDirty();
        }

        public void SetFace(HudFontFace face)
        {
            if (face == _face) return;
            _face = face;
            SetVerticesDirty();
        }

        /// <summary>Moves the ink up or down by whole texels, for optical centring.</summary>
        public void SetVerticalNudge(int texels)
        {
            if (texels == _yNudge) return;
            _yNudge = texels;
            SetVerticesDirty();
        }

        /// <summary>Sets the colour only when it changed — a colour write dirties the canvas.</summary>
        public void SetColour(Color c)
        {
            if (color != c) color = c;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var art = Art;
            if (art == null || string.IsNullOrEmpty(_text)) return;

            var rect = GetPixelAdjustedRect();
            int inkW = art.Measure(_text, _face);
            int inkH = HudPixelFont.HeightOf(_face);

            float x0 = rect.xMin;
            if (_align == HudTextAlign.Centre) x0 += Mathf.Floor((rect.width - inkW) * 0.5f);
            else if (_align == HudTextAlign.Right) x0 += rect.width - inkW;
            float y0 = rect.yMin + Mathf.Floor((rect.height - inkH) * 0.5f) + _yNudge;
            x0 = Mathf.Floor(x0);
            y0 = Mathf.Floor(y0);

            Color32 c = color;
            float pen = x0;
            bool first = true;
            for (int i = 0; i < _text.Length; i++)
            {
                char ch = _text[i];
                if (ch == ' ')
                {
                    if (!first) pen += HudPixelFont.Tracking;
                    pen += HudPixelFont.SpaceWidthOf(_face);
                    first = false;
                    continue;
                }
                if (!art.TryGetGlyph(_face, ch, out var g)) continue;
                if (!first) pen += HudPixelFont.Tracking;
                first = false;

                // The cell carries one texel of outline on every side, so it starts one texel
                // left of and below the ink.
                float qx = pen - 1f, qy = y0 - 1f;
                AddQuad(vh, qx, qy, g.CellWidth, g.CellHeight, g.Uv, c);
                pen += g.InkWidth;
            }
        }

        private static void AddQuad(VertexHelper vh, float x, float y, float w, float h, Rect uv, Color32 c)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), c, new Vector2(uv.xMin, uv.yMin));
            vh.AddVert(new Vector3(x, y + h), c, new Vector2(uv.xMin, uv.yMax));
            vh.AddVert(new Vector3(x + w, y + h), c, new Vector2(uv.xMax, uv.yMax));
            vh.AddVert(new Vector3(x + w, y), c, new Vector2(uv.xMax, uv.yMin));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
