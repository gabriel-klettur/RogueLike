using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The furniture of the constellation, generated once at runtime: the socket a node sits
    /// in, the plate under its icon, the halo behind a selected one, the channel a connector
    /// is drawn with, and the placeholder that stands in until real art arrives.
    ///
    /// <para>EVERY MAP IS LUMINANCE ONLY — white with a varying alpha, never a hue — for the
    /// reason <c>ArcaneSprites</c> records: colour arriving through <c>Image.color</c> is what
    /// lets one socket serve a learned node, an available one and a locked one, and lets a
    /// school tint its own constellation without nine sets of textures.</para>
    ///
    /// <para>WHY GENERATED RATHER THAN AUTHORED. These are the SCAFFOLD, and the scaffold is
    /// the part that must not need art to exist — 58 of the 104 shipped spells have no icon
    /// at all, so a graph that needed one per node would be blank on the majority of them.
    /// Every one of these is replaceable by dropping a sprite into the same slot; see
    /// <c>SpellGraphView.ResolveNodeIcon</c> for the chain that finds it.</para>
    /// </summary>
    internal static class SpellGraphSprites
    {
        private const int SocketPx = 128;
        private const int PlatePx = 96;
        private const int GlowPx = 128;
        private const int LinkPx = 64;
        private const int MarkPx = 64;

        private static Sprite _socket;
        private static Sprite _socketCapstone;
        private static Sprite _plate;
        private static Sprite _glow;
        private static Sprite _link;
        private static Sprite _linkFlow;
        private static Sprite[] _marks;

        /// <summary>The ring a node sits in. Bevelled, with four cardinal notches.</summary>
        public static Sprite Socket { get { EnsureAll(); return _socket; } }

        /// <summary>A heavier, faceted ring for the deepest node of a branch.</summary>
        public static Sprite SocketCapstone { get { EnsureAll(); return _socketCapstone; } }

        /// <summary>The dark plate an icon is laid on, inside the socket.</summary>
        public static Sprite Plate { get { EnsureAll(); return _plate; } }

        /// <summary>Soft halo behind a selected or hovered node.</summary>
        public static Sprite Glow { get { EnsureAll(); return _glow; } }

        /// <summary>A connector channel: bright down the middle, soft at both long edges.</summary>
        public static Sprite Link { get { EnsureAll(); return _link; } }

        /// <summary>A short bright dash tiled along a link, so a connector has direction.</summary>
        public static Sprite LinkFlow { get { EnsureAll(); return _linkFlow; } }

        /// <summary>
        /// The stand-in for a node with no art, one glyph per <see cref="SpellRole"/>.
        ///
        /// <para>Deliberately a FLAT SYMBOL rather than a question mark or an empty socket: an
        /// author looking at a school needs to see at a glance which nodes still need drawing
        /// AND what each one is for, and a wall of identical question marks answers only the
        /// first. It is also deliberately not pretty — a placeholder that looks finished is one
        /// nobody replaces.</para>
        /// </summary>
        public static Sprite Mark(SpellRole role) { EnsureAll(); return _marks[Wrap((int)role)]; }

        /// <summary>
        /// Domain Reload is OFF, so these statics carry DESTROYED native objects into the next
        /// Play session. Each line is a plain <c>stsfld</c>, the only form
        /// <c>DomainReloadStaticResetTests</c> reads out of the IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _socket = null;
            _socketCapstone = null;
            _plate = null;
            _glow = null;
            _link = null;
            _linkFlow = null;
            _marks = null;
        }

        private const int RoleCount = 7;

        private static int Wrap(int v) => ((v % RoleCount) + RoleCount) % RoleCount;

        public static void EnsureAll()
        {
            if (_socket != null && _marks != null && _marks[0] != null) return;

            _socket = BuildSocket(facets: 0);
            _socketCapstone = BuildSocket(facets: 8);
            _plate = BuildPlate();
            _glow = BuildGlow();
            _link = BuildLink();
            _linkFlow = BuildLinkFlow();

            _marks = new Sprite[RoleCount];
            for (int i = 0; i < RoleCount; i++) _marks[i] = BuildMark((SpellRole)i);
        }

        // ── the socket ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A bevelled ring. The inner and outer walls are lit from opposite sides, which is
        /// the whole of what makes a flat circle read as a socket cut into something rather
        /// than as an outline drawn on it.
        /// </summary>
        private static Sprite BuildSocket(int facets)
        {
            var tex = NewTexture(SocketPx, SocketPx);
            var px = new Color[SocketPx * SocketPx];

            const float outer = 0.94f;
            const float inner = 0.66f;
            float mid = (outer + inner) * 0.5f;
            float half = (outer - inner) * 0.5f;
            float aa = 2.4f / SocketPx;

            for (int y = 0; y < SocketPx; y++)
            {
                float ny = (y + 0.5f) / SocketPx * 2f - 1f;
                for (int x = 0; x < SocketPx; x++)
                {
                    float nx = (x + 0.5f) / SocketPx * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);

                    // Faceting pinches the ring at N angles, which is what separates a
                    // capstone from the plain nodes at a glance.
                    float ringOuter = outer;
                    if (facets > 0)
                    {
                        float angle = Mathf.Atan2(ny, nx);
                        ringOuter = outer * (0.93f + 0.07f * Mathf.Cos(angle * facets));
                    }

                    float band = 1f - Mathf.Clamp01(Mathf.Abs(r - mid) / half);
                    if (r > ringOuter + aa || band <= 0f) { px[y * SocketPx + x] = Color.clear; continue; }

                    // Bevel: the upper-inner wall catches the light, the lower-outer falls away.
                    float side = (r - mid) / half;                       // -1 inner .. +1 outer
                    float lift = 0.5f - 0.5f * side * (ny > 0f ? 1f : -1f);
                    float shade = Mathf.Lerp(0.42f, 1f, Mathf.Clamp01(lift));

                    float edge = Mathf.Clamp01((ringOuter + aa - r) / aa);
                    float body = Mathf.SmoothStep(0f, 1f, band);
                    px[y * SocketPx + x] = Lum(body * shade * edge);
                }
            }

            return Finish(tex, px, SocketPx, facets > 0 ? "SocketCapstone" : "Socket");
        }

        /// <summary>The recessed plate an icon sits on: darkest at the rim, open in the middle.</summary>
        private static Sprite BuildPlate()
        {
            var tex = NewTexture(PlatePx, PlatePx);
            var px = new Color[PlatePx * PlatePx];
            float aa = 2.2f / PlatePx;

            for (int y = 0; y < PlatePx; y++)
            {
                float ny = (y + 0.5f) / PlatePx * 2f - 1f;
                for (int x = 0; x < PlatePx; x++)
                {
                    float nx = (x + 0.5f) / PlatePx * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 1f) { px[y * PlatePx + x] = Color.clear; continue; }

                    float fill = Mathf.Clamp01((1f - r) / aa);
                    // A rim shadow, so the icon reads as sunk in rather than pasted on.
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.16f);
                    px[y * PlatePx + x] = Lum(fill * (0.72f + 0.28f * rim));
                }
            }

            return Finish(tex, px, PlatePx, "Plate");
        }

        private static Sprite BuildGlow()
        {
            var tex = NewTexture(GlowPx, GlowPx);
            var px = new Color[GlowPx * GlowPx];

            for (int y = 0; y < GlowPx; y++)
            {
                float ny = (y + 0.5f) / GlowPx * 2f - 1f;
                for (int x = 0; x < GlowPx; x++)
                {
                    float nx = (x + 0.5f) / GlowPx * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    px[y * GlowPx + x] = Lum(Mathf.Exp(-Mathf.Pow(r / 0.42f, 2f)));
                }
            }

            return Finish(tex, px, GlowPx, "Glow");
        }

        // ── connectors ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A channel rather than a hairline: bright along its spine and fading at both long
        /// edges, so a connector reads as something power runs THROUGH. The sprite is square
        /// and stretched to length by the caller, so only the cross-section matters.
        /// </summary>
        private static Sprite BuildLink()
        {
            var tex = NewTexture(LinkPx, LinkPx);
            var px = new Color[LinkPx * LinkPx];

            for (int y = 0; y < LinkPx; y++)
            {
                float ny = (y + 0.5f) / LinkPx * 2f - 1f;
                float spine = Mathf.Exp(-Mathf.Pow(ny / 0.30f, 2f));
                float body = Mathf.Exp(-Mathf.Pow(ny / 0.78f, 2f)) * 0.34f;
                for (int x = 0; x < LinkPx; x++) px[y * LinkPx + x] = Lum(spine + body);
            }

            return Finish(tex, px, LinkPx, "Link");
        }

        /// <summary>A lozenge, tiled along a link to give the connection a direction.</summary>
        private static Sprite BuildLinkFlow()
        {
            var tex = NewTexture(LinkPx, LinkPx);
            var px = new Color[LinkPx * LinkPx];

            for (int y = 0; y < LinkPx; y++)
            {
                float ny = (y + 0.5f) / LinkPx * 2f - 1f;
                for (int x = 0; x < LinkPx; x++)
                {
                    float nx = (x + 0.5f) / LinkPx * 2f - 1f;
                    // |x| + |y| < 1 is a diamond; softened so it does not alias when scaled.
                    float d = Mathf.Abs(nx) * 0.62f + Mathf.Abs(ny);
                    px[y * LinkPx + x] = Lum(Mathf.Clamp01((0.86f - d) / 0.34f));
                }
            }

            return Finish(tex, px, LinkPx, "LinkFlow");
        }

        // ── placeholders ─────────────────────────────────────────────────────────────

        /// <summary>
        /// One flat glyph per role, drawn from primitives: a chevron for damage, a ring for
        /// control, a shield for protection, a cross for healing, an arrow for mobility, three
        /// dots for summon, a bar for utility.
        /// </summary>
        private static Sprite BuildMark(SpellRole role)
        {
            var tex = NewTexture(MarkPx, MarkPx);
            var px = new Color[MarkPx * MarkPx];

            for (int y = 0; y < MarkPx; y++)
            {
                float ny = (y + 0.5f) / MarkPx * 2f - 1f;
                for (int x = 0; x < MarkPx; x++)
                {
                    float nx = (x + 0.5f) / MarkPx * 2f - 1f;
                    px[y * MarkPx + x] = Lum(MarkCoverage(role, nx, ny));
                }
            }

            return Finish(tex, px, MarkPx, "Mark_" + role);
        }

        private static float MarkCoverage(SpellRole role, float nx, float ny)
        {
            const float w = 0.16f;   // stroke half-width, normalized

            switch (role)
            {
                case SpellRole.Damage:      // a downward chevron
                    return Stroke(Mathf.Abs(nx) - (0.42f - ny * 0.55f), w * 0.9f) *
                           Inside(Mathf.Abs(nx) < 0.72f && ny > -0.62f && ny < 0.66f);

                case SpellRole.Control:     // a broken ring
                    return Stroke(Mathf.Sqrt(nx * nx + ny * ny) - 0.56f, w * 0.75f) *
                           Inside(!(ny > 0.34f && Mathf.Abs(nx) < 0.26f));

                case SpellRole.Protection:  // a shield outline
                {
                    float shield = Mathf.Max(Mathf.Abs(nx) - 0.52f,
                        ny < 0f ? (Mathf.Abs(nx) * 0.9f + ny + 0.72f) * -1f + 0f : ny - 0.62f);
                    return Stroke(shield, w * 0.7f);
                }

                case SpellRole.Healing:     // a cross
                    return Mathf.Max(
                        Inside(Mathf.Abs(nx) < w && Mathf.Abs(ny) < 0.60f),
                        Inside(Mathf.Abs(ny) < w && Mathf.Abs(nx) < 0.60f));

                case SpellRole.Mobility:    // an arrow pointing right
                    return Mathf.Max(
                        Inside(Mathf.Abs(ny) < w * 0.8f && nx > -0.62f && nx < 0.34f),
                        Stroke(Mathf.Abs(ny) - (0.46f - nx * 0.9f), w * 0.8f) *
                            Inside(nx > 0.10f && nx < 0.62f));

                case SpellRole.Summon:      // three rising dots
                    return Mathf.Max(Mathf.Max(
                        Dot(nx + 0.42f, ny + 0.22f, 0.19f),
                        Dot(nx, ny + 0.02f, 0.19f)),
                        Dot(nx - 0.42f, ny + 0.26f, 0.19f));

                default:                    // Utility — a plain bar
                    return Inside(Mathf.Abs(ny) < w * 0.85f && Mathf.Abs(nx) < 0.56f);
            }
        }

        private static float Stroke(float signedDistance, float halfWidth)
            => Mathf.Clamp01(1f - Mathf.Abs(signedDistance) / halfWidth);

        private static float Inside(bool test) => test ? 1f : 0f;

        private static float Dot(float dx, float dy, float radius)
            => Mathf.Clamp01((radius - Mathf.Sqrt(dx * dx + dy * dy)) / (radius * 0.55f));

        // ── shared raster helpers ────────────────────────────────────────────────────

        private static Color Lum(float a) => new Color(1f, 1f, 1f, Mathf.Clamp01(a));

        private static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        /// <summary>
        /// Wrap the pixels in a sprite, NAMED. The name is what the hierarchy shows for the
        /// Image holding it, and this whole file exists to be replaced piece by piece — an
        /// author picking a node apart to find what to swap should not be reading a row of
        /// blank sprite fields.
        /// </summary>
        private static Sprite Finish(Texture2D tex, Color[] px, int size, string name)
        {
            tex.name = "SpellGraph_" + name;
            tex.SetPixels(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = "SpellGraph_" + name;
            return sprite;
        }
    }
}
