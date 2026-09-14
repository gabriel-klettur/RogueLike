using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>One baked bitmap glyph: where it sits in the atlas and how big its cell is.</summary>
    public readonly struct HudGlyph
    {
        public readonly Rect Uv;

        /// <summary>Cell width INCLUDING the one-texel outline on each side.</summary>
        public readonly int CellWidth;

        /// <summary>Cell height INCLUDING the outline.</summary>
        public readonly int CellHeight;

        public HudGlyph(Rect uv, int cellWidth, int cellHeight)
        {
            Uv = uv;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
        }

        /// <summary>The glyph's own width, which is what the advance is measured from.</summary>
        public int InkWidth => CellWidth - 2;
    }

    /// <summary>
    /// Every bitmap the player panel draws, generated in code into ONE point-filtered atlas, plus
    /// the three pieces baked to their own texture because they are one-offs at an exact size
    /// (the panel's stone, the portrait's backdrop, the red screen edge).
    ///
    /// <para><b>Why generated.</b> Every size comes from <see cref="PlayerHudStyle"/>, so changing
    /// a row height regenerates art that fits it exactly instead of resampling art that does not
    /// — the same argument <c>WorldBarArt</c> makes for the bars over the head. The old panel was
    /// a 4x4 white texture stretched to every rectangle, which is why nothing on it could have a
    /// corner, a bevel or an outline.</para>
    ///
    /// <para><b>One atlas</b> so frame, slots, glyphs, text and motes batch. A 1-texel gutter
    /// separates the pieces: a stretched 9-slice samples to its rect edge and would otherwise pull
    /// a neighbour's texel into its end cap.</para>
    ///
    /// <para>Sprites are created at 100 pixels per unit, which with the panel canvas's reference
    /// of 100 makes one atlas pixel exactly one panel texel under <c>Image.Type.Sliced</c>.</para>
    ///
    /// <para>Built in two phases because a sprite needs a finished texture: every piece is first
    /// written into a pixel buffer and its rect recorded by NAME, then the texture is applied and
    /// the sprites are made from the recorded rects.</para>
    /// </summary>
    public sealed class HudArt
    {
        /// <summary>Sprite pixels per unit. Matches Canvas.referencePixelsPerUnit, so 1 px = 1 texel.</summary>
        public const float SpritePixelsPerUnit = 100f;

        private const int AtlasSize = 256;

        public Texture2D Atlas { get; private set; }
        public PlayerHudStyle Style { get; }

        public Sprite White { get; private set; }
        public Sprite BarFrame { get; private set; }

        /// <summary>
        /// The frame for a bar under 7 texels tall. A 9-slice whose borders add up to more than
        /// the rect is SQUASHED by uGUI, which lands every border row on half a texel — measured on
        /// the 5-texel XP line, the only row of the whole panel off the pixel grid.
        /// </summary>
        public Sprite BarFrameThin { get; private set; }
        public Sprite BarGlow { get; private set; }
        public Sprite Shine { get; private set; }
        public Sprite Shade { get; private set; }
        public Sprite Slot { get; private set; }
        public Sprite SlotGlow { get; private set; }
        public Sprite PortraitFrame { get; private set; }
        public Sprite Medallion { get; private set; }
        public Sprite MedallionGlow { get; private set; }
        public Sprite PipFrame { get; private set; }
        public Sprite PipFill { get; private set; }
        public Sprite StatusTile { get; private set; }
        public Sprite Heart { get; private set; }
        public Sprite Drop { get; private set; }
        public Sprite Boot { get; private set; }
        public Sprite Lock { get; private set; }
        public Sprite MouseLeft { get; private set; }
        public Sprite MouseRight { get; private set; }
        public Sprite MouseMiddle { get; private set; }
        public Sprite MoteDot { get; private set; }
        public Sprite MotePlus { get; private set; }
        public Sprite MoteStar { get; private set; }
        public Sprite MoteGlow { get; private set; }
        public Sprite MoteNote { get; private set; }
        public Sprite[] StatusGlyph { get; private set; }

        private readonly Dictionary<char, HudGlyph> _small = new Dictionary<char, HudGlyph>();
        private readonly Dictionary<char, HudGlyph> _large = new Dictionary<char, HudGlyph>();
        private readonly Dictionary<char, string[]> _smallPatterns;
        private readonly Dictionary<char, string[]> _largePatterns;
        private readonly Dictionary<string, (RectInt rect, Vector4 border)> _pieces =
            new Dictionary<string, (RectInt, Vector4)>();

        private Color32[] _pixels;
        private int _cursorX = 1, _cursorY = 1, _rowHeight;

        // -- Cache -------------------------------------------------------------

        private static HudArt s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHudArtStatics() => s_instance = null;

        /// <summary>The atlas for the active style, built on first use.</summary>
        public static HudArt Get() => Get(PlayerHudStyle.Active);

        /// <summary>The atlas for <paramref name="style"/>. Rebuilt only when the style changes.</summary>
        public static HudArt Get(PlayerHudStyle style)
        {
            if (style == null) style = PlayerHudStyle.Active;
            if (s_instance != null && s_instance.Atlas != null && s_instance.Style == style)
                return s_instance;
            s_instance = new HudArt(style);
            return s_instance;
        }

        private HudArt(PlayerHudStyle style)
        {
            Style = style;
            _smallPatterns = HudPixelFont.Glyphs(HudFontFace.Small);
            _largePatterns = HudPixelFont.Glyphs(HudFontFace.Large);
            Build();
        }

        // -- Glyph access --------------------------------------------------------

        /// <summary>The baked glyph for <paramref name="c"/>, if the face has one.</summary>
        public bool TryGetGlyph(HudFontFace face, char c, out HudGlyph glyph)
        {
            var table = face == HudFontFace.Large ? _large : _small;
            return table.TryGetValue(char.ToUpperInvariant(c), out glyph);
        }

        /// <summary>The raw patterns of a face, for measuring before any quad exists.</summary>
        public Dictionary<char, string[]> Patterns(HudFontFace face)
            => face == HudFontFace.Large ? _largePatterns : _smallPatterns;

        /// <summary>Ink width of a label in a face, in texels.</summary>
        public int Measure(string text, HudFontFace face)
            => HudPixelFont.MeasureWidth(text, face, Patterns(face));

        /// <summary>The pixel rect a named piece occupies. For the tests.</summary>
        public bool TryGetPieceRect(string name, out RectInt rect)
        {
            if (_pieces.TryGetValue(name, out var p)) { rect = p.rect; return true; }
            rect = default;
            return false;
        }

        // -- Build ---------------------------------------------------------------

        private void Build()
        {
            _pixels = new Color32[AtlasSize * AtlasSize];
            var s = Style;

            Add("white", 3, 3, (x, y) => Grey(255, 255), Vector4.one);
            Add("bar_frame", 7, 7, (x, y) => BarFramePixel(x, y, 7, s), new Vector4(3, 3, 3, 3));
            Add("bar_frame_thin", 3, 3, (x, y) => Chamfered(x, y, 3) ? Clear()
                : EdgeDistance(x, y, 3) == 0 ? C(s.outline) : C(s.recess), new Vector4(1, 1, 1, 1));
            Add("bar_glow", 9, 9, (x, y) => RingPixel(x, y, 9, new[] { 0.45f, 1f, 0.35f }), new Vector4(4, 4, 4, 4));
            Add("shine", 1, 2, (x, y) => Grey(255, y == 1 ? 140 : 56), Vector4.zero);
            Add("shade", 1, 2, (x, y) => new Color32(0, 0, 0, (byte)(y == 0 ? 92 : 40)), Vector4.zero);
            Add("slot", 9, 9, (x, y) => SlotPixel(x, y, 9, s), new Vector4(4, 4, 4, 4));
            Add("slot_glow", 9, 9, (x, y) => RingPixel(x, y, 9, new[] { 0.55f, 1f, 0.45f, 0.12f }), new Vector4(4, 4, 4, 4));
            Add("portrait_frame", 13, 13, (x, y) => PortraitFramePixel(x, y, 13, s), new Vector4(6, 6, 6, 6));

            int m = Mathf.Clamp(s.medallionTexels | 1, 11, 25);
            Add("medallion", m, m, (x, y) => MedallionPixel(x, y, m, s), Vector4.zero);
            Add("medallion_glow", m + 8, m + 8, (x, y) => SoftRing(x, y, m + 8, m * 0.5f + 0.5f, 3.4f), Vector4.zero);

            Add("pip_frame", 11, 11, (x, y) => PipFramePixel(x, y, s), Vector4.zero);
            Add("pip_fill", 7, 7, (x, y) => Mathf.Abs(x - 3) + Mathf.Abs(y - 3) <= 3 ? Grey(255, 255) : Clear(), Vector4.zero);
            Add("status_tile", 8, 8, (x, y) => StatusTilePixel(x, y, s), Vector4.one * 2f);

            AddPattern("heart", new[]
            {
                " ## ## ",
                "#hh####",
                "#h#####",
                "#######",
                " #####s",
                "  ###s ",
                "   #   ",
            }, IconShade, outline: true);
            AddPattern("drop", new[]
            {
                "  #  ",
                "  #  ",
                " ### ",
                " h## ",
                "#h###",
                "####s",
                " ##s ",
            }, IconShade, outline: true);
            // 5x5 (7x7 outlined): the panel's third icon, shape distinct from Heart and Drop at a
            // glance — a shaft rising from a sole that reaches right, read as a running boot.
            AddPattern("boot", new[]
            {
                " ##  ",
                " ##  ",
                " ##  ",
                " ##h#",
                "#####",
            }, IconShade, outline: true);
            AddPattern("lock", new[]
            {
                " ### ",
                " # # ",
                "#####",
                "## ##",
                "#####",
                "#####",
            }, IconShade, outline: true);

            // 5x7: small enough to sit in a slot's corner without covering the spell, big enough
            // that which button is lit reads at a glance.
            var mouse = new[]
            {
                " ### ",
                "#LMR#",
                "#LMR#",
                "#####",
                "#bbb#",
                "#bbb#",
                " ### ",
            };
            AddPattern("mouse_l", mouse, c => MousePixel(c, 'L'), outline: true);
            AddPattern("mouse_r", mouse, c => MousePixel(c, 'R'), outline: true);
            AddPattern("mouse_m", mouse, c => MousePixel(c, 'M'), outline: true);

            Add("mote_dot", 1, 1, (x, y) => Grey(255, 255), Vector4.zero);
            Add("mote_plus", 3, 3, (x, y) =>
                (x == 1 && y == 1) ? Grey(255, 255) : (x == 1 || y == 1) ? Grey(255, 170) : Clear(), Vector4.zero);
            Add("mote_star", 5, 5, StarPixel, Vector4.zero);
            Add("mote_glow", 5, 5, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(2f, 2f));
                float a = Mathf.Clamp01(1f - d / 2.7f);
                return Grey(255, (int)(a * a * 255f));
            }, Vector4.zero);
            // An eighth note, for the music panel: the one mote that says what kind of event it
            // answers. No outline — motes are drawn additive, where a dark rim adds nothing.
            AddPattern("mote_note", new[]
            {
                "  # ",
                "  ##",
                "  # ",
                "### ",
                "### ",
            }, c => c == '#' ? (Color32?)Grey(255, 255) : null, outline: false);

            int statusCount = Valkur.Gameplay.Combat.StatusGlyphs.Count;
            for (int i = 0; i < statusCount; i++)
            {
                var rows = Valkur.Gameplay.Combat.StatusGlyphs.Get(i);
                if (rows != null)
                    AddPattern("status_" + i, rows, c => c == '#' ? (Color32?)Grey(255, 255) : null, outline: false);
            }

            BakeFont(_smallPatterns, _small);
            BakeFont(_largePatterns, _large);

            Atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, false)
            {
                name = "HudArt_Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Atlas.SetPixels32(_pixels);
            Atlas.Apply(false, false);
            _pixels = null;

            White = SpriteOf("white");
            BarFrame = SpriteOf("bar_frame");
            BarFrameThin = SpriteOf("bar_frame_thin");
            BarGlow = SpriteOf("bar_glow");
            Shine = SpriteOf("shine");
            Shade = SpriteOf("shade");
            Slot = SpriteOf("slot");
            SlotGlow = SpriteOf("slot_glow");
            PortraitFrame = SpriteOf("portrait_frame");
            Medallion = SpriteOf("medallion");
            MedallionGlow = SpriteOf("medallion_glow");
            PipFrame = SpriteOf("pip_frame");
            PipFill = SpriteOf("pip_fill");
            StatusTile = SpriteOf("status_tile");
            Heart = SpriteOf("heart");
            Drop = SpriteOf("drop");
            Boot = SpriteOf("boot");
            Lock = SpriteOf("lock");
            MouseLeft = SpriteOf("mouse_l");
            MouseRight = SpriteOf("mouse_r");
            MouseMiddle = SpriteOf("mouse_m");
            MoteDot = SpriteOf("mote_dot");
            MotePlus = SpriteOf("mote_plus");
            MoteStar = SpriteOf("mote_star");
            MoteGlow = SpriteOf("mote_glow");
            MoteNote = SpriteOf("mote_note");
            StatusGlyph = new Sprite[statusCount];
            for (int i = 0; i < statusCount; i++) StatusGlyph[i] = SpriteOf("status_" + i);
        }

        // -- Packing ---------------------------------------------------------------

        private Sprite SpriteOf(string name)
        {
            if (!_pieces.TryGetValue(name, out var p)) return null;
            var sprite = Sprite.Create(Atlas, new Rect(p.rect.x, p.rect.y, p.rect.width, p.rect.height),
                                       new Vector2(0.5f, 0.5f), SpritePixelsPerUnit, 0,
                                       SpriteMeshType.FullRect, p.border);
            sprite.name = "Hud_" + name;
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
                throw new System.InvalidOperationException("HudArt atlas is full; raise AtlasSize.");
            var r = new RectInt(_cursorX, _cursorY, w, h);
            _cursorX += w + 1;
            _rowHeight = Mathf.Max(_rowHeight, h);
            return r;
        }

        private void Write(RectInt r, System.Func<int, int, Color32> pixel)
        {
            for (int y = 0; y < r.height; y++)
                for (int x = 0; x < r.width; x++)
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = pixel(x, y);
        }

        /// <summary>Writes a piece (x right, y UP) and records where it went.</summary>
        private void Add(string name, int w, int h, System.Func<int, int, Color32> pixel, Vector4 border)
        {
            var r = Reserve(w, h);
            Write(r, pixel);
            _pieces[name] = (r, border);
        }

        private void AddPattern(string name, string[] rows, System.Func<char, Color32?> map, bool outline)
        {
            var px = Rasterise(rows, map, outline, out int w, out int h);
            var r = Reserve(w, h);
            Write(r, (x, y) => px[y * w + x]);
            _pieces[name] = (r, Vector4.zero);
        }

        /// <summary>Turns a top-down pattern into bottom-up pixels, optionally dilating a dark
        /// one-texel outline around every inked pixel (8-neighbourhood).</summary>
        public static Color32[] Rasterise(string[] rows, System.Func<char, Color32?> map, bool outline,
                                            out int w, out int h)
        {
            int pw = 0;
            for (int i = 0; i < rows.Length; i++) pw = Mathf.Max(pw, rows[i].Length);
            int ph = rows.Length;
            int pad = outline ? 1 : 0;
            w = pw + pad * 2;
            h = ph + pad * 2;
            var px = new Color32[w * h];
            var ink = new bool[w * h];
            for (int ry = 0; ry < ph; ry++)
            {
                string row = rows[ry];
                for (int rx = 0; rx < row.Length; rx++)
                {
                    var c = map(row[rx]);
                    if (!c.HasValue) continue;
                    int x = rx + pad, y = (ph - 1 - ry) + pad;
                    px[y * w + x] = c.Value;
                    ink[y * w + x] = true;
                }
            }
            if (!outline) return px;
            var edge = new Color32(4, 4, 8, 235);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (ink[y * w + x]) continue;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            near = ink[ny * w + nx];
                        }
                    if (near) px[y * w + x] = edge;
                }
            return px;
        }

        private void BakeFont(Dictionary<char, string[]> patterns, Dictionary<char, HudGlyph> into)
        {
            foreach (var kv in patterns)
            {
                var px = Rasterise(kv.Value, ch => ch == ' ' ? (Color32?)null : Grey(255, 255),
                                   outline: true, out int w, out int h);
                var r = Reserve(w, h);
                Write(r, (x, y) => px[y * w + x]);
                var uv = new Rect((float)r.x / AtlasSize, (float)r.y / AtlasSize,
                                  (float)r.width / AtlasSize, (float)r.height / AtlasSize);
                into[kv.Key] = new HudGlyph(uv, w, h);
            }
        }

        // -- Pixel rules ---------------------------------------------------------------

        private static Color32 Clear() => new Color32(0, 0, 0, 0);
        private static Color32 Grey(int v, int a) => new Color32((byte)v, (byte)v, (byte)v, (byte)a);
        private static Color32 C(Color c) => c;

        private static int EdgeDistance(int x, int y, int size) =>
            Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));

        private static bool Chamfered(int x, int y, int size) =>
            (x == 0 || x == size - 1) && (y == 0 || y == size - 1);

        private static Color32 BarFramePixel(int x, int y, int size, PlayerHudStyle s)
        {
            if (Chamfered(x, y, size)) return Clear();
            int d = EdgeDistance(x, y, size);
            if (d == 0) return C(s.outline);
            // The first row under the top edge is the recess's own shadow: the fill covers it,
            // so it only shows in the EMPTY part of the bar, which is exactly where depth reads.
            var recess = s.recess;
            if (y == size - 2) recess = Color.Lerp(recess, Color.black, 0.45f);
            return C(recess);
        }

        private static Color32 RingPixel(int x, int y, int size, float[] alphaByDistance)
        {
            if (Chamfered(x, y, size)) return Clear();
            int d = EdgeDistance(x, y, size);
            if (d >= alphaByDistance.Length) return Clear();
            return Grey(255, (int)(alphaByDistance[d] * 255f));
        }

        private static Color32 SlotPixel(int x, int y, int size, PlayerHudStyle s)
        {
            if (Chamfered(x, y, size)) return Clear();
            int d = EdgeDistance(x, y, size);
            if (d == 0) return C(s.outline);
            if (d == 1)
            {
                // Light from the top-left, which is where every bevel in the kit is lit from.
                bool lit = y == size - 2 || x == 1;
                bool shade = y == 1 || x == size - 2;
                if (lit && !shade) return C(Color.Lerp(s.stoneLight, Color.white, 0.18f));
                if (shade && !lit) return C(Color.Lerp(s.outline, s.stoneDark, 0.4f));
                return C(s.stoneLight);
            }
            return C(s.recess);
        }

        private static Color32 PortraitFramePixel(int x, int y, int size, PlayerHudStyle s)
        {
            if (Chamfered(x, y, size)) return Clear();
            int d = EdgeDistance(x, y, size);
            if (d == 0) return C(s.outline);
            if (d == 1 || d == 2)
            {
                // Lit when the nearest edge is the top or the left one.
                int toTop = size - 1 - y, toRight = size - 1 - x;
                bool topLeft = Mathf.Min(toTop, x) <= Mathf.Min(y, toRight);
                var lit = Color.Lerp(s.gold, Color.white, 0.32f);
                var col = d == 1 ? (topLeft ? lit : s.gold) : (topLeft ? s.gold : s.goldShade);
                // A rivet in each corner, one texel, catching the light.
                bool corner = (x == 2 || x == size - 3) && (y == 2 || y == size - 3);
                if (corner) col = Color.Lerp(s.gold, Color.white, 0.6f);
                return C(col);
            }
            if (d == 3) return C(new Color(s.outline.r, s.outline.g, s.outline.b, 0.85f));
            return Clear();
        }

        private static Color32 MedallionPixel(int x, int y, int size, PlayerHudStyle s)
        {
            float c = (size - 1) * 0.5f;
            float dx = x - c, dy = y - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float r = size * 0.5f;
            if (d > r - 0.25f) return Clear();
            if (d > r - 1.25f) return C(s.outline);
            if (d > r - 3.25f)
            {
                // The gold band, lit from the top-left: the angle of the pixel decides the tone.
                float light = (-dx + dy) / Mathf.Max(0.001f, d);   // +1 top-left, -1 bottom-right
                var lit = Color.Lerp(s.gold, Color.white, 0.35f);
                var col = light > 0.35f ? lit : light < -0.35f ? s.goldShade : s.gold;
                return C(col);
            }
            if (d > r - 4.25f) return C(s.outline);
            float t = Mathf.Clamp01(d / (r - 4.25f));
            var inner = Color.Lerp(s.medallionFace, s.medallionFaceRim, t);
            return C(inner);
        }

        private static Color32 SoftRing(int x, int y, int size, float radius, float width)
        {
            float c = (size - 1) * 0.5f;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - radius) / width);
            return Grey(255, (int)(a * a * 255f));
        }

        private static Color32 PipFramePixel(int x, int y, PlayerHudStyle s)
        {
            int d = Mathf.Abs(x - 5) + Mathf.Abs(y - 5);
            if (d > 5) return Clear();
            if (d == 5) return C(s.outline);
            if (d == 4) return C(Color.Lerp(s.stoneLight, Color.white, (y > 5 || x < 5) ? 0.2f : 0f));
            return C(s.recess);
        }

        private static Color32 StatusTilePixel(int x, int y, PlayerHudStyle s)
        {
            if (Chamfered(x, y, 8)) return Clear();
            int d = EdgeDistance(x, y, 8);
            if (d == 0) return C(s.outline);
            var c = s.recess;
            c.a = 0.92f;
            return C(c);
        }

        private static Color32 StarPixel(int x, int y)
        {
            int dx = Mathf.Abs(x - 2), dy = Mathf.Abs(y - 2);
            if (dx == 0 && dy == 0) return Grey(255, 255);
            if (dx == 0 || dy == 0) return Grey(255, dx + dy == 1 ? 210 : 90);
            if (dx == 1 && dy == 1) return Grey(255, 70);
            return Clear();
        }

        private static Color32? IconShade(char c)
        {
            switch (c)
            {
                case '#': return Grey(205, 255);
                case 'h': return Grey(255, 255);
                case 's': return Grey(140, 255);
                default: return null;
            }
        }

        private static Color32? MousePixel(char c, char lit)
        {
            switch (c)
            {
                case '#': return Grey(210, 255);
                case 'b': return Grey(92, 255);
                case 'L':
                case 'R':
                    return c == lit ? Grey(255, 255) : Grey(92, 255);
                case 'M':
                    return lit == 'M' ? Grey(255, 255) : Grey(210, 255);
                default: return null;
            }
        }

        // -- One-off bakes ----------------------------------------------------------------

        /// <summary>
        /// The panel's stone at its exact texel size: outline, gold filigree line, a dark bevel,
        /// then a lit-from-above gradient broken up by an ordered dither so a 60-texel ramp of
        /// dark blue does not band into stripes.
        /// </summary>
        public static Texture2D BakePanel(int w, int h, PlayerHudStyle s)
        {
            var px = new Color32[w * h];
            var lit = Color.Lerp(s.gold, Color.white, 0.3f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x), dy = Mathf.Min(y, h - 1 - y);
                    int corner = dx + dy;
                    Color c;
                    if (corner < 2) c = Color.clear;                       // chamfer
                    else if (corner == 2 || dx == 0 || dy == 0) c = s.outline;
                    else if (corner == 3 || dx == 1 || dy == 1)
                        c = (dy == 1 && y > h / 2) || (dx == 1 && x < w / 2) ? lit : s.gold;
                    else if (corner == 4 || dx == 2 || dy == 2) c = Color.Lerp(s.outline, s.stoneDark, 0.35f);
                    else
                    {
                        float t = (float)y / Mathf.Max(1, h - 1);
                        c = Color.Lerp(s.stoneDark, s.stoneLight, t * t * 0.9f + 0.1f);
                        float dither = (Bayer4(x, y) - 7.5f) / 16f * 0.022f;
                        float grain = (Hash(x, y) - 0.5f) * 0.018f;
                        c.r += dither + grain;
                        c.g += dither + grain;
                        c.b += dither + grain * 1.3f;
                        // The row just inside the bevel catches the light.
                        if (dy == 3 && y > h / 2) c = Color.Lerp(c, Color.white, 0.07f);
                        // Rivets, one per inner corner.
                        if ((dx == 4 || dx == 5) && (dy == 4 || dy == 5))
                            c = (dx == 4 && dy == 4) ? Color.Lerp(s.gold, Color.white, 0.4f) : s.goldShade;
                    }
                    px[y * w + x] = c;
                }
            }

            // A small gem set into the top edge, centred: the one ornament the frame carries, so
            // the panel reads as a made object rather than a rectangle with a border.
            int gx = w / 2, gy = h - 3;
            var ruby = s.gem;
            var rubyLit = s.gemLit;
            for (int y = gy - 3; y <= gy + 3; y++)
                for (int x = gx - 3; x <= gx + 3; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    int d = Mathf.Abs(x - gx) + Mathf.Abs(y - gy);
                    Color c;
                    if (d == 3) c = s.outline;
                    else if (d == 2) c = (y >= gy && x <= gx) ? lit : s.goldShade;
                    else if (d == 1) c = (y > gy || x < gx) ? rubyLit : ruby;
                    else if (d == 0) c = ruby;
                    else continue;
                    px[y * w + x] = c;
                }
            return Make("HudPanelStone", w, h, px, FilterMode.Point);
        }

        /// <summary>The portrait window's backdrop: a warm glow behind the head fading to the
        /// frame's own dark at the edges, dithered for the same reason as the panel.</summary>
        public static Texture2D BakeBackdrop(int w, int h, Color centre, Color edge)
        {
            var px = new Color32[w * h];
            var focus = new Vector2(w * 0.55f, h * 0.62f);
            float reach = Mathf.Max(w, h) * 0.78f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), focus) / reach);
                    t += (Bayer4(x, y) - 7.5f) / 16f * 0.06f;
                    var c = Color.Lerp(centre, edge, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            return Make("HudPortraitBackdrop", w, h, px, FilterMode.Point);
        }

        /// <summary>A soft white screen edge, transparent over most of the screen. Bilinear on
        /// purpose: it is the one piece of the panel that is not pixel art.</summary>
        public static Texture2D BakeVignette(int w = 128, int h = 64)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    float d = Mathf.Sqrt(u * u * 0.85f + v * v);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1.28f, d));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            return Make("HudEdgeVignette", w, h, px, FilterMode.Bilinear);
        }

        private static Texture2D Make(string name, int w, int h, Color32[] px, FilterMode filter)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        private static int Bayer4(int x, int y)
        {
            switch ((y & 3) * 4 + (x & 3))
            {
                case 0: return 0;  case 1: return 8;  case 2: return 2;  case 3: return 10;
                case 4: return 12; case 5: return 4;  case 6: return 14; case 7: return 6;
                case 8: return 3;  case 9: return 11; case 10: return 1; case 11: return 9;
                case 12: return 15; case 13: return 7; case 14: return 13; default: return 5;
            }
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }
        }
    }
}
