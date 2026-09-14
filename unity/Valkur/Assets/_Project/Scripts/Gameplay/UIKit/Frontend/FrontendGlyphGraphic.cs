using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// One icon from <see cref="FrontendGlyphs"/>, drawn as geometry in its theme colour.
    /// Rebuilt only when the glyph, the accent or the glow changes.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrontendGlyphGraphic : MaskableGraphic
    {
        private readonly FrontendGlyphPainter _painter = new FrontendGlyphPainter();
        private FrontendGlyph _glyph;
        private Color _accent = FrontendPalette.GoldLight;
        private float _glow;

        public FrontendGlyph Glyph { get => _glyph; set { if (_glyph != value) { _glyph = value; SetVerticesDirty(); } } }
        public Color Accent { get => _accent; set { if (_accent != value) { _accent = value; SetVerticesDirty(); } } }

        /// <summary>0..1: lifts the icon toward white (hover, selection, a click).</summary>
        public float Glow { get => _glow; set { value = Mathf.Clamp01(value); if (Mathf.Abs(_glow - value) > 0.01f) { _glow = value; SetVerticesDirty(); } } }

        /// <summary>How many parts the current glyph is made of. For the tests: 0 means nothing is drawn.</summary>
        public int PartCount
        {
            get
            {
                _painter.Begin(new Rect(0f, 0f, 1f, 1f));
                FrontendGlyphs.Paint(_glyph, _painter);
                return _painter.ShapeCount;
            }
        }

        public static FrontendGlyphGraphic Create(Transform parent, string name, FrontendGlyph glyph)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<FrontendGlyphGraphic>();
            g.raycastTarget = false;
            g._glyph = glyph;
            g._accent = FrontendIconTheme.AccentOf(glyph);
            return g;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            _painter.Begin(rectTransform.rect);
            if (!FrontendGlyphs.Paint(_glyph, _painter)) return;
            _painter.Flush(vh, _accent, _glow);
            FrontendMesh.ApplyGraphicColor(vh, color);
        }
    }
}
