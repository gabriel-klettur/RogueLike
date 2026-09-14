using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>The light profile a fill is drawn with. See <see cref="FrontendRamp"/>.</summary>
    public enum FrontendFillProfile
    {
        /// <summary>The loading bar's molten body: deep floor, bright band. Nothing is written on it.</summary>
        Bar = 0,

        /// <summary>A selected row's: the same band over a floor dark ink stays legible on.</summary>
        Row = 1,
    }

    /// <summary>
    /// A fill in the loading bar's language, as geometry: the vertical light profile in the
    /// tint, a gloss on the upper half, a lit top edge, a bright border, slanted bands of light
    /// flowing along it, a white-hot core at the leading edge, and diamond notches for steps.
    ///
    /// <para><b>One graphic for three jobs</b> — the selected row of every list, the filled part
    /// of a slider, and a class card's stat bar — so "the selection fills like the bar" is a
    /// statement about one mesh rather than three that resemble each other.</para>
    ///
    /// <para><b>The flow is the only thing that needs a rebuild every frame</b>, and it only runs
    /// while <see cref="Flow"/> is on; a stat bar at rest is a mesh built once.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrontendFillGraphic : MaskableGraphic
    {
        private const int Bands = 8;
        private const float StripeSpacing = 22f;
        private const float StripeWidth = 8f;
        private const float StripeSpeed = 42f;

        private FrontendFillProfile _profile = FrontendFillProfile.Bar;
        private float _amount = 1f;
        private Color _tint = new Color(0.95f, 0.75f, 0.35f, 1f);
        private float _clock;
        private bool _flow;
        private bool _border;
        private bool _leadingCore;
        private bool _startCore;
        private int _notches;
        private float _glow;

        public FrontendFillProfile Profile { get => _profile; set { if (_profile != value) { _profile = value; SetVerticesDirty(); } } }

        /// <summary>0..1 of the rect's width that is filled, from the left.</summary>
        public float Amount { get => _amount; set { value = Mathf.Clamp01(value); if (Mathf.Abs(_amount - value) > 0.0005f) { _amount = value; SetVerticesDirty(); } } }

        public Color Tint { get => _tint; set { if (_tint != value) { _tint = value; SetVerticesDirty(); } } }

        /// <summary>Seconds, driving the flow. Ignored while <see cref="Flow"/> is off.</summary>
        public float Clock { get => _clock; set { _clock = value; if (_flow) SetVerticesDirty(); } }

        public bool Flow { get => _flow; set { if (_flow != value) { _flow = value; SetVerticesDirty(); } } }

        /// <summary>A one-unit outline and a bright border in the tint — the selected row's edge.</summary>
        public bool Border { get => _border; set { if (_border != value) { _border = value; SetVerticesDirty(); } } }

        /// <summary>A white-hot line at the leading edge of the fill (a slider, the bar's edge).</summary>
        public bool LeadingCore { get => _leadingCore; set { if (_leadingCore != value) { _leadingCore = value; SetVerticesDirty(); } } }

        /// <summary>A bright bar at the start — the selected row's accent, where the fill "comes from".</summary>
        public bool StartCore { get => _startCore; set { if (_startCore != value) { _startCore = value; SetVerticesDirty(); } } }

        /// <summary>Evenly spaced diamond notches along the top and bottom edges, including both ends. &lt; 2 = none.</summary>
        public int Notches { get => _notches; set { if (_notches != value) { _notches = value; SetVerticesDirty(); } } }

        /// <summary>0..1 extra light (a confirm, a drag).</summary>
        public float Glow { get => _glow; set { value = Mathf.Clamp01(value); if (Mathf.Abs(_glow - value) > 0.002f) { _glow = value; SetVerticesDirty(); } } }

        public static FrontendFillGraphic Create(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var g = go.AddComponent<FrontendFillGraphic>();
            g.raycastTarget = false;
            return g;
        }

        /// <summary>The luminance factor the profile gives at height <paramref name="v"/>. For the tests.</summary>
        public float LuminanceAt(float v) => _profile == FrontendFillProfile.Row ? FrontendRamp.Row(v) : FrontendRamp.Bar(v);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Build(vh);
            FrontendMesh.ApplyGraphicColor(vh, color);
        }

        private void Build(VertexHelper vh)
        {
            var r = rectTransform.rect;
            float x = r.xMin, y = r.yMin, h = r.height;
            float w = r.width * _amount;
            if (h <= 0f) return;

            if (w > 0.5f)
            {
                if (_border) FrontendMesh.Quad(vh, x - 1f, y - 1f, w + 2f, h + 2f, FrontendPalette.Outline);

                for (int i = 0; i < Bands; i++)
                {
                    float v0 = i / (float)Bands, v1 = (i + 1) / (float)Bands;
                    Color b = FrontendRamp.Shade(_tint, LuminanceAt(v0)); b.a = _tint.a;
                    Color t = FrontendRamp.Shade(_tint, LuminanceAt(v1)); t.a = _tint.a;
                    FrontendMesh.QuadV(vh, x, y + h * v0, w, h * (v1 - v0), b, t);
                }
                // Gloss: the upper half catches the light. Alpha-blended, so it stays inside the tint.
                float g0 = h * 0.55f;
                FrontendMesh.QuadV(vh, x, y + g0, w, h * 0.3f, FrontendMesh.WithAlpha(Color.white, 0f),
                                   FrontendMesh.WithAlpha(Color.white, 0.10f + 0.12f * _glow));
                FrontendMesh.Quad(vh, x, y + h - 1f, w, 1f, FrontendMesh.WithAlpha(Color.white, 0.45f + 0.4f * _glow));
                FrontendMesh.Quad(vh, x, y, w, 1f, FrontendMesh.WithAlpha(FrontendPalette.Outline, 0.35f));

                if (_flow) DrawFlow(vh, x, y, h, w);

                if (_border)
                {
                    Color rim = Color.Lerp(_tint, Color.white, 0.45f + 0.35f * _glow);
                    rim.a = 0.85f;
                    FrontendMesh.Quad(vh, x, y + h - 1f, w, 1f, rim);
                    FrontendMesh.Quad(vh, x + w - 1f, y, 1f, h, FrontendMesh.WithAlpha(rim, 0.55f));
                }

                if (_startCore)
                {
                    FrontendMesh.Quad(vh, x, y, 4f, h, FrontendPalette.Outline);
                    FrontendMesh.QuadH(vh, x + 1f, y + 1f, 3f, h - 2f, FrontendPalette.WarmWhite,
                                       FrontendMesh.WithAlpha(Color.Lerp(_tint, Color.white, 0.5f), 0.9f));
                }

                if (_leadingCore && _amount < 0.999f)
                    FrontendMesh.Quad(vh, x + w - 1f, y - 1f, 2f, h + 2f, FrontendMesh.WithAlpha(FrontendPalette.WarmWhite, 0.9f));
            }

            if (_notches >= 2)
            {
                float fullW = r.width;
                for (int i = 0; i < _notches; i++)
                {
                    float f = i / (float)(_notches - 1);
                    bool passed = f <= _amount + 0.0005f;
                    float nx = x + fullW * f;
                    FrontendDraw.Notch(vh, nx, y + h + 2.5f, 2.4f, passed, _tint);
                    FrontendDraw.Notch(vh, nx, y - 2.5f, 2.4f, passed, _tint);
                }
            }
        }

        /// <summary>Slanted bands of light sliding along the filled part, fading before the edge so none is cut.</summary>
        private void DrawFlow(VertexHelper vh, float x, float y, float h, float fillW)
        {
            if (fillW < 6f) return;
            float slant = h * 0.9f;
            float offset = Mathf.Repeat(_clock * StripeSpeed, StripeSpacing);
            for (float sx = -StripeSpacing - slant + offset; sx < fillW; sx += StripeSpacing)
            {
                float right = sx + slant + StripeWidth;
                if (right > fillW || sx < 0f) continue;
                float edge = Mathf.Clamp01((fillW - right) / 26f) * Mathf.Clamp01(sx / 12f);
                if (edge <= 0.01f) continue;
                FrontendMesh.Parallelogram(vh, x + sx, y, StripeWidth, h, slant,
                                           FrontendMesh.WithAlpha(Color.white, 0.075f * edge));
            }
        }
    }
}
