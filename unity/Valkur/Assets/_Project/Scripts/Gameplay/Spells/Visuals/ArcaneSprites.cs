using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The maps a woven barrier is built from, generated once at runtime: hexagonal membrane
    /// panels, their fractures, the anchor posts, the lattice edge, floating glyphs, the floor
    /// seal and the chips a failing panel throws.
    ///
    /// <para>EVERY MAP IS LUMINANCE ONLY — white with a varying alpha, never a hue.
    /// <see cref="IceSprites"/> bakes its colour in because an ice crystal runs a HUE ramp
    /// from deep blue to near-white, which one <c>SpriteRenderer.color</c> cannot express; a
    /// magic membrane is one hue with a LUMINANCE ramp, which one tint expresses exactly. That
    /// difference is the whole reason a barrier can be authored any colour from the F4 editor
    /// and an ice wall cannot. See <see cref="ArcaneBarrierPalette"/>.</para>
    /// </summary>
    internal static partial class ArcaneSprites
    {
        /// <summary>How many hexagon and fracture shapes exist. Panels pick one at random.</summary>
        public const int PanelVariants = 3;

        /// <summary>How many glyphs exist. A barrier draws several at once.</summary>
        public const int RuneVariants = 6;

        /// <summary>Height in world units a Post sprite covers at scale 1. Hidden by ScalePost.</summary>
        public const float PostUnitHeight = 2f;

        private const int PanelPx = 64;
        private const int PostW = 32, PostH = 160;
        private const int EdgeW = 128, EdgeH = 8;
        private const int RunePx = 48;

        private static Sprite[] _panel;
        private static Sprite[] _fracture;
        private static Sprite[] _rune;
        private static Sprite _post;
        private static Sprite _edge;
        private static Sprite _seal;
        private static Sprite _sigil;
        private static Sprite _shard;

        /// <summary>One hexagonal membrane cell: faint interior, bright rim. Centre-pivoted.</summary>
        public static Sprite Panel(int v) { EnsureAll(); return _panel[Wrap(v, PanelVariants)]; }

        /// <summary>Fracture lines clipped to the matching panel, for accumulated damage.</summary>
        public static Sprite Fracture(int v) { EnsureAll(); return _fracture[Wrap(v, PanelVariants)]; }

        /// <summary>An inscribed glyph. Drawn floating on the plane and drifting along it.</summary>
        public static Sprite Rune(int v) { EnsureAll(); return _rune[Wrap(v, RuneVariants)]; }

        /// <summary>A vertical shaft of force, base-pivoted, tapering out at the top.</summary>
        public static Sprite Post { get { EnsureAll(); return _post; } }

        /// <summary>A thin horizontal line with soft ends. The lattice contour. 2x1 units.</summary>
        public static Sprite Edge { get { EnsureAll(); return _edge; } }

        /// <summary>The band burnt into the floor along the barrier's axis. 2x1 units.</summary>
        public static Sprite Seal { get { EnsureAll(); return _seal; } }

        /// <summary>The ticked disc that turns under each anchor post. 1x1 unit.</summary>
        public static Sprite Sigil { get { EnsureAll(); return _sigil; } }

        /// <summary>An angular chip thrown by a failing panel. 1x1 unit.</summary>
        public static Sprite Shard { get { EnsureAll(); return _shard; } }

        /// <summary>
        /// Size a post child in world units, so no caller has to remember
        /// <see cref="PostUnitHeight"/>. The sprite is base-pivoted: this grows upward.
        /// </summary>
        public static void ScalePost(Transform t, float widthWu, float heightWu)
            => t.localScale = new Vector3(widthWu, heightWu / PostUnitHeight, 1f);

        /// <summary>
        /// Domain Reload is OFF, so these statics carry DESTROYED native objects into the next
        /// Play session. Each line is a plain <c>stsfld</c>, which is the only form
        /// <c>DomainReloadStaticResetTests</c> reads out of the IL — clearing an array in place
        /// passes the field as an ARGUMENT and registers as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _panel = null;
            _fracture = null;
            _rune = null;
            _post = null;
            _edge = null;
            _seal = null;
            _sigil = null;
            _shard = null;
        }

        public static void EnsureAll()
        {
            if (_panel != null && _panel.Length == PanelVariants && _panel[0] != null) return;

            _panel = new Sprite[PanelVariants];
            _fracture = new Sprite[PanelVariants];
            for (int v = 0; v < PanelVariants; v++)
            {
                _panel[v] = BuildPanel(v);
                _fracture[v] = BuildFracture(v);
            }

            _rune = new Sprite[RuneVariants];
            for (int v = 0; v < RuneVariants; v++) _rune[v] = BuildRune(v);

            _post = BuildPost();
            _edge = BuildEdge();
            _seal = BuildSeal(256, 64);
            _sigil = BuildSigil(96);
            _shard = BuildShard(32);
        }

        // ── shared raster helpers ────────────────────────────────────────────────────

        private static int Wrap(int v, int count) => ((v % count) + count) % count;

        private static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        private static Sprite Finish(Texture2D tex, Color[] px, Vector2 pivot, float ppu)
        {
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu);
        }

        /// <summary>White at the given coverage. Every map in this file is built from these.</summary>
        private static Color Lum(float a) => new Color(1f, 1f, 1f, Mathf.Clamp01(a));

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// Signed distance to a flat-top hexagon of circumradius <paramref name="r"/>, negative
        /// inside. Flat-top because the panels are stacked in offset COLUMNS along the wall,
        /// and a flat top is the edge that tiles cleanly against its neighbour above.
        /// </summary>
        private static float HexDistance(Vector2 p, float r)
        {
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            return Mathf.Max(p.x * 0.8660254f + p.y * 0.5f, p.y) - r;
        }
    }
}
