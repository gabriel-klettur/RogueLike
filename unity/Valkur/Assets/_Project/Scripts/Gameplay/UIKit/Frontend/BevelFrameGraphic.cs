using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The housing of every pre-game surface: soft shadow, bevelled gold frame lit from above,
    /// a recessed channel (or a tinted face, for a button), corner brackets, and optionally a
    /// header band with a notched rule and a gem where the rule meets the frame, or a gem on a
    /// spike at each end.
    ///
    /// <para><b>The loading bar's frame is this graphic</b> with side gems and no header; a menu
    /// panel is this graphic with a header; a class card, a load-panel slot and a button are
    /// this graphic smaller. That is the whole claim of "one piece": the same method call with a
    /// different rect, never a copy of it.</para>
    ///
    /// <para>Rebuilt only when something it draws changes. The gems' breathing and the glow are
    /// the only moving parts, and each setter ignores a change too small to see.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BevelFrameGraphic : MaskableGraphic
    {
        private const float SideGemOffset = 17f;

        private float _thickness = 4f;
        private float _glow;
        private Color _tint = new Color(0.95f, 0.75f, 0.35f, 1f);
        private Color _face = new Color(0f, 0f, 0f, 0f);
        private float _shadowScale = 1f;
        private bool _brackets = true;
        private bool _sideGems;
        private float _startGemLit = 1f;
        private float _endGemLit;
        private float _headerHeight;
        private float _headerGemLit;

        public float Thickness { get => _thickness; set => Set(ref _thickness, value); }

        /// <summary>0..1: extra light on the bevel (a stage completing, a hover, a card chosen).</summary>
        public float Glow { get => _glow; set => Set(ref _glow, Mathf.Clamp01(value)); }

        /// <summary>The state colour, which the gems borrow so a warning says so at both ends.</summary>
        public Color Tint { get => _tint; set { if (_tint != value) { _tint = value; SetVerticesDirty(); } } }

        /// <summary>A tinted face instead of the recessed channel (a button). Alpha 0 = recess.</summary>
        public Color Face { get => _face; set { if (_face != value) { _face = value; SetVerticesDirty(); } } }

        /// <summary>How far the soft shadow reaches, as a multiple of the bar's. 0 = none.</summary>
        public float ShadowScale { get => _shadowScale; set => Set(ref _shadowScale, Mathf.Max(0f, value)); }

        public bool Brackets { get => _brackets; set { if (_brackets != value) { _brackets = value; SetVerticesDirty(); } } }

        /// <summary>A gem on a spike beyond each end, centred vertically — the loading bar's.</summary>
        public bool SideGems { get => _sideGems; set { if (_sideGems != value) { _sideGems = value; SetVerticesDirty(); } } }

        public float StartGemLit { get => _startGemLit; set => Set(ref _startGemLit, Mathf.Clamp01(value)); }
        public float EndGemLit { get => _endGemLit; set => Set(ref _endGemLit, Mathf.Clamp01(value)); }

        /// <summary>Height of the header band inside the top of the frame. 0 = no header.</summary>
        public float HeaderHeight { get => _headerHeight; set => Set(ref _headerHeight, Mathf.Max(0f, value)); }

        /// <summary>0..1: how lit the two gems at the ends of the header rule are.</summary>
        public float HeaderGemLit { get => _headerGemLit; set => Set(ref _headerGemLit, Mathf.Clamp01(value)); }

        public static BevelFrameGraphic Create(Transform parent, string name = "Frame")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var g = go.AddComponent<BevelFrameGraphic>();
            g.raycastTarget = false;
            return g;
        }

        private void Set(ref float field, float value)
        {
            if (Mathf.Abs(field - value) <= 0.002f) return;
            field = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Build(vh);
            FrontendMesh.ApplyGraphicColor(vh, color);
        }

        private void Build(VertexHelper vh)
        {
            var r = rectTransform.rect;
            float x = r.xMin, y = r.yMin, w = r.width, h = r.height, t = _thickness;
            if (w <= 2f * t || h <= 2f * t) return;
            float cy = y + h * 0.5f;

            if (_shadowScale > 0f) FrontendDraw.Shadow(vh, x, y, w, h, _shadowScale);

            float gemR = h * 0.46f + 2f;
            if (_sideGems)
            {
                // Behind the frame, so the frame's edge overlaps the spike's root.
                FrontendDraw.Spike(vh, x - SideGemOffset, x + 1f, cy);
                FrontendDraw.Spike(vh, x + w - 1f, x + w + SideGemOffset, cy);
            }

            FrontendDraw.BevelFrame(vh, x, y, w, h, t, _glow);
            if (_face.a > 0.001f) DrawFace(vh, x + t, y + t, w - 2f * t, h - 2f * t);
            if (_headerHeight > 0f) DrawHeader(vh, x, y, w, h, t);
            if (_brackets) FrontendDraw.Brackets(vh, x, y, w, h, _glow);

            if (_sideGems)
            {
                FrontendDraw.Gem(vh, x - SideGemOffset, cy, gemR, _startGemLit, _tint);
                FrontendDraw.Gem(vh, x + w + SideGemOffset, cy, gemR, _endGemLit, _tint);
            }
        }

        /// <summary>A button's face: the row's light profile in the face colour, over the recess.</summary>
        private void DrawFace(VertexHelper vh, float x, float y, float w, float h)
        {
            const int bands = 6;
            for (int i = 0; i < bands; i++)
            {
                float v0 = i / (float)bands, v1 = (i + 1) / (float)bands;
                Color b = FrontendRamp.Shade(_face, FrontendRamp.Row(v0)); b.a = _face.a;
                Color tp = FrontendRamp.Shade(_face, FrontendRamp.Row(v1)); tp.a = _face.a;
                FrontendMesh.QuadV(vh, x, y + h * v0, w, h * (v1 - v0), b, tp);
            }
            FrontendMesh.Quad(vh, x, y + h - 1f, w, 1f, FrontendMesh.WithAlpha(Color.white, 0.28f + 0.3f * _glow));
            FrontendMesh.Quad(vh, x, y, w, 1f, FrontendMesh.WithAlpha(FrontendPalette.Outline, 0.45f));
        }

        /// <summary>
        /// A header band a shade lighter than the channel, a notched rule under it, and a gem
        /// where that rule meets each side of the frame.
        /// </summary>
        private void DrawHeader(VertexHelper vh, float x, float y, float w, float h, float t)
        {
            float top = y + h - t;
            float hh = Mathf.Min(_headerHeight, h - 2f * t);
            float by = top - hh;
            Color light = FrontendPalette.RecessBottom * 1.35f; light.a = 1f;
            FrontendMesh.QuadV(vh, x + t, by, w - 2f * t, hh, FrontendPalette.RecessTop, light);
            FrontendMesh.Quad(vh, x + t, top - 1f, w - 2f * t, 1f, FrontendPalette.LipLight);
            FrontendDraw.Rule(vh, x + t + 10f, by - 1f, w - 2f * t - 20f, _tint);
            float gr = Mathf.Clamp(hh * 0.18f, 5f, 8f);
            FrontendDraw.Gem(vh, x + t * 0.5f, by, gr, _headerGemLit, _tint);
            FrontendDraw.Gem(vh, x + w - t * 0.5f, by, gr, _headerGemLit, _tint);
        }
    }
}
