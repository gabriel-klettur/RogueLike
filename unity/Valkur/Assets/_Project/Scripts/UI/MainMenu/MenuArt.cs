using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Every bitmap the pre-game screens draw, generated in code into ONE atlas, plus the two
    /// soft pieces baked to their own bilinear texture because a vignette and a gradient are the
    /// only things here that must NOT be point-filtered.
    ///
    /// <para><b>Why generated.</b> Same argument as <c>HudArt</c> and <c>WorldBarArt</c>: every
    /// size comes from <see cref="MenuStyle"/>, so changing the row height regenerates a pill
    /// that fits it exactly instead of resampling one that does not. The menu this replaces drew
    /// every rectangle with a bare <c>Image</c> and no sprite at all, which is why nothing on it
    /// could have a corner, a bevel or an outline.</para>
    ///
    /// <para><b>Its own atlas, not <c>HudArt</c>'s.</b> The same decision <c>SpellBarArt</c>
    /// made: the shared HUD kit is sized in texels of the player panel's pixel space and is due
    /// to move; a menu piece is authored at canvas scale. One shared atlas would have to serve
    /// two grids and would grow a menu section nothing in the HUD ever draws.</para>
    ///
    /// <para><b>One texel of gutter</b> between pieces, because a stretched 9-slice samples to
    /// its rect edge and would otherwise pull a neighbour's texel into its end cap.</para>
    /// </summary>
    public sealed class MenuArt
    {
        /// <summary>Matches Canvas.referencePixelsPerUnit, so one atlas pixel is one canvas unit.</summary>
        public const float SpritePixelsPerUnit = 100f;

        private const int AtlasSize = 256;

        public Texture2D Atlas { get; private set; }

        /// <summary>The soft radial darkening at the edges of the screen. Bilinear, stretched.</summary>
        public Sprite Vignette { get; private set; }

        /// <summary>A vertical black-to-clear ramp. Bilinear. Puts a floor under the hint bar.</summary>
        public Sprite BottomScrim { get; private set; }

        /// <summary>
        /// A soft elliptical darkening with no edge at all. Bilinear, and that is the whole
        /// point: the press-to-start plate was <see cref="MoteGlow"/> (9 x 9) stretched fifty
        /// times on a POINT-filtered atlas, which resampled a radial fall-off into a hard grey
        /// rectangle. It also stands behind the title, so the word is legible over whatever the
        /// carousel happens to be showing.
        /// </summary>
        /// <summary>
        /// The title's own mote: a SOFT round dot, bilinear, on its own small texture.
        ///
        /// <para><b>Why it cannot come from the atlas.</b> The atlas is point-filtered on
        /// purpose — every panel, pill and chevron in the menu is crisp because of it — and the
        /// title draws its motes at about 3.9 canvas units from a 2x2 source. Point-sampling
        /// that upscale gives a hard square, so a word made of a few thousand of them reads as
        /// gravel however good its colour is. A dot with a real falloff is the difference
        /// between embers and confetti, and it costs the title one draw call.</para>
        /// </summary>
        public Sprite TitleMote { get; private set; }

        /// <summary>The glint inside the word: the same falloff with a four-point star cut into it.</summary>
        public Sprite TitleSpark { get; private set; }

        public Sprite SoftPlate { get; private set; }

        public MenuStyle Style { get; }

        public Sprite White { get; private set; }

        /// <summary>The panel: outline, bevelled top-left, chamfered corners, recessed fill.</summary>
        public Sprite Panel { get; private set; }

        /// <summary>The panel's title band, joined to the panel by sharing its outline row.</summary>
        public Sprite Header { get; private set; }

        /// <summary>The selection highlight. Chamfered, with a brighter top edge.</summary>
        public Sprite Pill { get; private set; }

        /// <summary>A row the pointer is over but that is not selected. Flatter than the pill.</summary>
        public Sprite Hover { get; private set; }

        /// <summary>The 4 px mark at the left of the selected row.</summary>
        public Sprite AccentBar { get; private set; }

        public Sprite Divider { get; private set; }
        public Sprite ArrowLeft { get; private set; }
        public Sprite ArrowRight { get; private set; }
        public Sprite KeyCap { get; private set; }
        public Sprite SliderTrack { get; private set; }
        public Sprite SliderFill { get; private set; }
        public Sprite SliderHandle { get; private set; }
        public Sprite Notch { get; private set; }
        public Sprite CheckOn { get; private set; }
        public Sprite CheckOff { get; private set; }
        public Sprite CardFrame { get; private set; }
        public Sprite MoteDot { get; private set; }
        public Sprite MoteSpark { get; private set; }
        public Sprite MoteGlow { get; private set; }
        public Sprite MoteRing { get; private set; }

        private readonly Dictionary<string, (RectInt rect, Vector4 border)> _pieces =
            new Dictionary<string, (RectInt, Vector4)>();

        private Color32[] _pixels;
        private int _cursorX = 1, _cursorY = 1, _rowHeight;

        // ── Cache ────────────────────────────────────────────────────────────

        private static MenuArt s_instance;

        /// <summary>
        /// Static mutable state with Domain Reload off. A direct assignment, because
        /// <c>DomainReloadStaticResetTests</c> reads this method's raw IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        /// <summary>The atlas for the active style, built on first use.</summary>
        public static MenuArt Get() => Get(MenuStyle.Active);

        /// <summary>The atlas for <paramref name="style"/>. Rebuilt only when the style changes.</summary>
        public static MenuArt Get(MenuStyle style)
        {
            if (style == null) style = MenuStyle.Active;
            if (s_instance != null && s_instance.Atlas != null && s_instance.Style == style)
                return s_instance;
            s_instance = new MenuArt(style);
            return s_instance;
        }

        private MenuArt(MenuStyle style)
        {
            Style = style;
            Build();
        }

        /// <summary>The pixel rect a named piece occupies. For the tests.</summary>
        public bool TryGetPieceRect(string name, out RectInt rect)
        {
            if (_pieces.TryGetValue(name, out var p)) { rect = p.rect; return true; }
            rect = default;
            return false;
        }

        // ── Build ────────────────────────────────────────────────────────────

        private void Build()
        {
            _pixels = new Color32[AtlasSize * AtlasSize];

            Add("white", 3, 3, (x, y) => Solid(255), Vector4.one);

            // 24x24 with a 9-texel border: big enough that the chamfer reads at a 560 px panel
            // and the bevel is two distinct rows rather than one ambiguous one.
            Add("panel", 24, 24, PanelPixel, new Vector4(9, 9, 9, 9));
            Add("header", 24, 16, HeaderPixel, new Vector4(9, 6, 9, 6));
            Add("pill", 16, 16, PillPixel, new Vector4(6, 6, 6, 6));
            Add("hover", 16, 16, HoverPixel, new Vector4(6, 6, 6, 6));
            Add("accent_bar", 5, 9, AccentBarPixel, new Vector4(0, 3, 0, 3));
            Add("divider", 1, 3, (x, y) => y == 1 ? Solid(255) : Solid(70), new Vector4(0, 1, 0, 1));
            Add("card", 20, 20, CardPixel, new Vector4(7, 7, 7, 7));
            Add("keycap", 14, 14, KeyCapPixel, new Vector4(5, 6, 5, 5));

            Add("slider_track", 9, 9, SliderTrackPixel, new Vector4(4, 4, 4, 4));
            Add("slider_fill", 7, 7, SliderFillPixel, new Vector4(3, 3, 3, 3));
            Add("slider_handle", 13, 17, SliderHandlePixel, Vector4.zero);
            Add("notch", 1, 4, (x, y) => Solid((byte)(y == 0 || y == 3 ? 90 : 190)), Vector4.zero);

            AddPattern("arrow_l", new[]
            {
                "   #",
                "  ##",
                " ###",
                "####",
                " ###",
                "  ##",
                "   #",
            });
            AddPattern("arrow_r", new[]
            {
                "#   ",
                "##  ",
                "### ",
                "####",
                "### ",
                "##  ",
                "#   ",
            });
            AddPattern("check_on", new[]
            {
                "       #",
                "      ##",
                "#    ###",
                "##  ### ",
                "######  ",
                " #####  ",
                "  ###   ",
                "   #    ",
            });
            AddPattern("check_off", new[]
            {
                "##    ##",
                "###  ###",
                " ###### ",
                "  ####  ",
                "  ####  ",
                " ###### ",
                "###  ###",
                "##    ##",
            });

            Add("mote_dot", 2, 2, (x, y) => Solid(255), Vector4.zero);
            Add("mote_spark", 5, 5, SparkPixel, Vector4.zero);
            Add("mote_glow", 9, 9, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(4f, 4f));
                float a = Mathf.Clamp01(1f - d / 4.6f);
                return Solid((byte)(a * a * 255f));
            }, Vector4.zero);
            Add("mote_ring", 11, 11, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(5f, 5f));
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 4f) / 1.6f);
                return Solid((byte)(a * a * 255f));
            }, Vector4.zero);

            Atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, false)
            {
                name = "MenuArt_Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Atlas.SetPixels32(_pixels);
            Atlas.Apply(false, false);
            _pixels = null;

            White = SpriteOf("white");
            Panel = SpriteOf("panel");
            Header = SpriteOf("header");
            Pill = SpriteOf("pill");
            Hover = SpriteOf("hover");
            AccentBar = SpriteOf("accent_bar");
            Divider = SpriteOf("divider");
            CardFrame = SpriteOf("card");
            KeyCap = SpriteOf("keycap");
            SliderTrack = SpriteOf("slider_track");
            SliderFill = SpriteOf("slider_fill");
            SliderHandle = SpriteOf("slider_handle");
            Notch = SpriteOf("notch");
            ArrowLeft = SpriteOf("arrow_l");
            ArrowRight = SpriteOf("arrow_r");
            CheckOn = SpriteOf("check_on");
            CheckOff = SpriteOf("check_off");
            MoteDot = SpriteOf("mote_dot");
            MoteSpark = SpriteOf("mote_spark");
            MoteGlow = SpriteOf("mote_glow");
            MoteRing = SpriteOf("mote_ring");

            Vignette = BakeSoft("MenuArt_Vignette", 96, 48, VignettePixel);
            SoftPlate = BakeSoft("MenuArt_SoftPlate", 64, 32, SoftPlatePixel);
            BakeTitleMotes();
            BottomScrim = BakeSoft("MenuArt_Scrim", 4, 64, ScrimPixel);
        }

        // ── The pieces ───────────────────────────────────────────────────────

        private Color32 PanelPixel(int x, int y)
        {
            const int n = 24;
            if (Chamfer(x, y, n, 3)) return Clear();
            int e = EdgeDistance(x, y, n);
            if (e == 0) return C(Style.PanelEdge);

            // A bevel is a LIGHT top-left and a DARK bottom-right, and it has to be asymmetric or
            // the panel reads as a flat outline with a halo. One row each, because two on a
            // 24 px piece is a frame rather than an edge.
            bool top = y >= n - 2, left = x <= 1, bottom = y <= 1, right = x >= n - 2;
            if (e == 1 && (top || left)) return C(Style.PanelBevel);
            if (e == 1 && (bottom || right)) return C(Mul(Style.PanelEdge, 1.6f));
            if (e == 1) return C(Lerp(Style.PanelEdge, Style.PanelBevel, 0.35f));

            var fill = Style.Panel;
            // A vertical ramp inside the fill so a tall panel is not one flat field of colour.
            float t = Mathf.Clamp01((y - 2f) / (n - 4f));
            fill = Lerp(Mul(fill, 0.86f), Mul(fill, 1.1f), t);
            return C(fill);
        }

        private Color32 HeaderPixel(int x, int y)
        {
            const int w = 24, h = 16;
            if (x <= 2 && y >= h - 3 && (h - 1 - y) + x < 3) return Clear();
            if (x >= w - 3 && y >= h - 3 && (h - 1 - y) + (w - 1 - x) < 3) return Clear();
            if (x == 0 || x == w - 1 || y == h - 1) return C(Style.PanelEdge);
            if (y == 0) return C(Mul(Style.Gold, 0.55f));           // the rule under the title
            if (y == 1) return C(Mul(Style.Gold, 0.22f));
            if (y == h - 2) return C(Style.PanelBevel);
            float t = Mathf.Clamp01((y - 2f) / (h - 4f));
            return C(Lerp(Mul(Style.header, 1.25f), Style.header, t));
        }

        private Color32 PillPixel(int x, int y)
        {
            const int n = 16;
            if (Chamfer(x, y, n, 4)) return Clear();
            int e = EdgeDistance(x, y, n);
            // WHITE, so the caller tints it. A pill baked in gold could only ever be gold, and
            // the same piece has to serve a danger row and a success row.
            if (e == 0) return Solid(255, 235);
            if (y >= n - 2) return Solid(255, 255);                  // lit top edge
            if (y <= 1) return Solid(170, 210);                      // shaded bottom
            return Solid(220, 230);
        }

        private Color32 HoverPixel(int x, int y)
        {
            const int n = 16;
            if (Chamfer(x, y, n, 4)) return Clear();
            int e = EdgeDistance(x, y, n);
            if (e == 0) return Solid(255, 120);
            return Solid(255, 34);
        }

        private Color32 AccentBarPixel(int x, int y)
        {
            const int w = 5, h = 9;
            if (y == 0 || y == h - 1) { if (x >= w - 1) return Clear(); }
            if (x == w - 1) return Solid(255, 90);
            if (x == 0) return Solid(255, 255);
            return Solid(255, 235);
        }

        private Color32 CardPixel(int x, int y)
        {
            const int n = 20;
            if (Chamfer(x, y, n, 4)) return Clear();
            int e = EdgeDistance(x, y, n);
            if (e == 0) return C(Style.PanelEdge);
            if (e == 1) return Solid(255, 255);                      // tinted by the caller
            if (e == 2) return C(Mul(Style.Panel, 1.15f));
            float t = Mathf.Clamp01((y - 3f) / (n - 6f));
            return C(Lerp(Mul(Style.Panel, 0.8f), Mul(Style.Panel, 1.18f), t));
        }

        private Color32 KeyCapPixel(int x, int y)
        {
            const int w = 14, h = 14;
            if (Chamfer(x, y, w, 3)) return Clear();
            int e = EdgeDistance(x, y, w);
            if (e == 0) return C(Style.PanelEdge);
            // A cap is a slab seen slightly from above: bright top, one dark row at the bottom.
            if (y >= h - 2) return C(Mul(Style.PanelBevel, 1.35f));
            if (y <= 1) return C(Mul(Style.PanelEdge, 2.2f));
            float t = Mathf.Clamp01((y - 2f) / (h - 4f));
            return C(Lerp(Mul(Style.PanelBevel, 0.72f), Mul(Style.PanelBevel, 1.05f), t));
        }

        private Color32 SliderTrackPixel(int x, int y)
        {
            const int n = 9;
            if (Chamfer(x, y, n, 2)) return Clear();
            int e = EdgeDistance(x, y, n);
            if (e == 0) return C(Style.PanelEdge);
            if (y >= n - 2) return C(Mul(Style.PanelEdge, 2.4f));    // the track is a groove:
            return C(Mul(Style.PanelEdge, 1.5f));                    // lit at the top, dark inside
        }

        private Color32 SliderFillPixel(int x, int y)
        {
            const int n = 7;
            if (Chamfer(x, y, n, 2)) return Clear();
            if (y >= n - 2) return Solid(255, 255);
            if (y <= 1) return Solid(150, 255);
            return Solid(215, 255);
        }

        private Color32 SliderHandlePixel(int x, int y)
        {
            const int w = 13, h = 17;
            float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
            float dx = Mathf.Abs(x - cx) / (cx + 0.5f);
            float dy = Mathf.Abs(y - cy) / (cy + 0.5f);
            float d = Mathf.Max(dx * 1.04f, dy);                     // a tall lozenge, not a disc
            if (d > 1f) return Clear();
            if (d > 0.82f) return C(Style.PanelEdge);
            if (y > cy) return Solid(255, 255);
            return Solid(190, 255);
        }

        private Color32 SparkPixel(int x, int y)
        {
            bool arm = x == 2 || y == 2;
            bool centre = x == 2 && y == 2;
            if (centre) return Solid(255, 255);
            if (arm) return Solid(255, 150);
            if (Mathf.Abs(x - 2) == 1 && Mathf.Abs(y - 2) == 1) return Solid(255, 60);
            return Clear();
        }

        private Color32 VignettePixel(int x, int y, int w, int h)
        {
            // Elliptical, biased so the darkening reaches further at the top and the bottom than
            // at the sides: the hint bar and the title both need a floor, the middle of the art
            // does not.
            float nx = (x + 0.5f) / w * 2f - 1f;
            float ny = (y + 0.5f) / h * 2f - 1f;
            float d = Mathf.Sqrt(nx * nx * 0.82f + ny * ny * 1.05f);
            float a = Mathf.Clamp01((d - 0.52f) / 0.62f);
            return new Color32(0, 0, 0, (byte)(a * a * 255f));
        }

        /// <summary>
        /// A round dot whose alpha falls off smoothly to nothing at the rim.
        ///
        /// <para>Squared-then-smoothed rather than linear: a linear falloff leaves a visible
        /// disc edge once a few thousand of them overlap, and the word gets a scaly texture.
        /// The core is held at full alpha across the middle third so the mote still has a
        /// BODY — a dot that is falloff all the way through reads as fog rather than as an
        /// ember.</para>
        /// </summary>
        private Color32 TitleMotePixel(int x, int y, int w, int h)
        {
            float cx = (x + 0.5f) / w * 2f - 1f;
            float cy = (y + 0.5f) / h * 2f - 1f;
            float d = Mathf.Sqrt(cx * cx + cy * cy);
            // 0.46, not 0.72: a wide falloff makes every mote a halo, and a few thousand
            // haloes overlapping turn a carved letterform back into a cloud. The core is
            // solid out to about half the radius and only the rim is soft, which is what
            // keeps the stroke EDGE while losing the square.
            float a = Mathf.Clamp01((1f - d) / 0.46f);
            a = a * a * (3f - 2f * a);
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }

        /// <summary>The same dot with two crossed rays, for the few motes drawn as glints.</summary>
        private Color32 TitleSparkPixel(int x, int y, int w, int h)
        {
            float cx = (x + 0.5f) / w * 2f - 1f;
            float cy = (y + 0.5f) / h * 2f - 1f;
            float d = Mathf.Sqrt(cx * cx + cy * cy);
            float core = Mathf.Clamp01((1f - d) / 0.55f);
            core = core * core * (3f - 2f * core);

            // The rays are a product of the two axes rather than a sum: a sum draws a cross with
            // a bright square where the arms meet, which is exactly where the core already is.
            float ax = Mathf.Clamp01(1f - Mathf.Abs(cx) / 0.16f);
            float ay = Mathf.Clamp01(1f - Mathf.Abs(cy) / 0.16f);
            float reach = Mathf.Clamp01(1f - d);
            float rays = Mathf.Max(ax, ay) * reach * reach;

            float a = Mathf.Clamp01(Mathf.Max(core, rays * 0.85f));
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }

        private Color32 SoftPlatePixel(int x, int y, int w, int h)
        {
            float nx = (x + 0.5f) / w * 2f - 1f;
            float ny = (y + 0.5f) / h * 2f - 1f;
            float d = Mathf.Sqrt(nx * nx + ny * ny);
            // A PLATEAU and then a fade, not a peak. A pure squared fall-off is at 0.25 of its
            // strength by the time it reaches the thing it is meant to be behind — measured on
            // the first live capture, the title's halo removed 8.6 of 134 luminance from the sky
            // and the word was still competing with it. Full alpha over the middle half, smooth
            // to exactly zero at the rect edge so the plate still has no border.
            float a = Mathf.Clamp01((1f - d) / 0.5f);
            a = a * a * (3f - 2f * a);
            return new Color32(0, 0, 0, (byte)(a * 255f));
        }

        private Color32 ScrimPixel(int x, int y, int w, int h)
        {
            float t = 1f - (y + 0.5f) / h;                            // 1 at the bottom
            float a = t * t * t;                                      // cubic: invisible by mid-panel
            return new Color32(0, 0, 0, (byte)(Mathf.Clamp01(a) * 255f));
        }

        // ── Packing ──────────────────────────────────────────────────────────

        private Sprite SpriteOf(string name)
        {
            if (!_pieces.TryGetValue(name, out var p)) return null;
            var sprite = Sprite.Create(Atlas, new Rect(p.rect.x, p.rect.y, p.rect.width, p.rect.height),
                                       new Vector2(0.5f, 0.5f), SpritePixelsPerUnit, 0,
                                       SpriteMeshType.FullRect, p.border);
            sprite.name = "Menu_" + name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>
        /// The dot and the glint on ONE page, because a <c>Graphic</c> draws from one texture.
        ///
        /// <para>Baking them as two textures compiles, runs, and silently loses the glint: the
        /// field can only bind one, so every spark falls back to the dot. Two shapes that have to
        /// appear in the same mesh belong in the same bake — the same reason the menu's other
        /// twelve pieces share an atlas.</para>
        /// </summary>
        private void BakeTitleMotes()
        {
            const int cell = 32;
            var tex = new Texture2D(cell * 2, cell, TextureFormat.RGBA32, false)
            {
                name = "MenuArt_TitleMotes",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[cell * 2 * cell];
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    pixels[y * cell * 2 + x] = TitleMotePixel(x, y, cell, cell);
                    pixels[y * cell * 2 + cell + x] = TitleSparkPixel(x, y, cell, cell);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            // FullRect on both: Tight would trace the alpha outline of a soft disc into a mesh
            // nothing reads, which is the 20 ms-per-call trap this project already records.
            TitleMote = Sprite.Create(tex, new Rect(0, 0, cell, cell), new Vector2(0.5f, 0.5f),
                                      cell, 0, SpriteMeshType.FullRect);
            TitleSpark = Sprite.Create(tex, new Rect(cell, 0, cell, cell), new Vector2(0.5f, 0.5f),
                                       cell, 0, SpriteMeshType.FullRect);
            TitleMote.name = "TitleMote";
            TitleSpark.name = "TitleSpark";
        }

        private static Sprite BakeSoft(string name, int w, int h, System.Func<int, int, int, int, Color32> pixel)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel(x, y, w, h);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                                       SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private RectInt Reserve(int w, int h)
        {
            if (_cursorX + w + 1 > AtlasSize)
            {
                _cursorX = 1;
                _cursorY += _rowHeight + 1;
                _rowHeight = 0;
            }
            if (_cursorY + h + 1 > AtlasSize)
                throw new System.InvalidOperationException("MenuArt atlas is full; raise AtlasSize.");
            var r = new RectInt(_cursorX, _cursorY, w, h);
            _cursorX += w + 1;
            _rowHeight = Mathf.Max(_rowHeight, h);
            return r;
        }

        private void Add(string name, int w, int h, System.Func<int, int, Color32> pixel, Vector4 border)
        {
            var r = Reserve(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = pixel(x, y);
            _pieces[name] = (r, border);
        }

        /// <summary>A white glyph from a top-down pattern, with a dark one-texel outline.</summary>
        private void AddPattern(string name, string[] rows)
        {
            var px = Valkur.UI.HUD.HudArt.Rasterise(rows,
                c => c == '#' ? (Color32?)Solid(255) : null, outline: true, out int w, out int h);
            var r = Reserve(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = px[y * w + x];
            _pieces[name] = (r, Vector4.zero);
        }

        // ── Pixel helpers ────────────────────────────────────────────────────

        private static bool Chamfer(int x, int y, int n, int cut)
        {
            int l = x, r = n - 1 - x, b = y, t = n - 1 - y;
            return l + b < cut || r + b < cut || l + t < cut || r + t < cut;
        }

        private static int EdgeDistance(int x, int y, int n)
            => Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y));

        private static Color32 Solid(byte v, byte a = 255) => new Color32(v, v, v, a);
        private static Color32 C(Color c) => c;
        private static Color Mul(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);
        private static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, t);
        private static Color32 Clear() => new Color32(0, 0, 0, 0);
    }
}
