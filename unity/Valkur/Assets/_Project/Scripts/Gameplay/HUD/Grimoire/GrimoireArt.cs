using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The grimoire window's own pixel pieces, generated into one small point-filtered atlas:
    /// the panel's bevelled frame, the sunken recess its three columns are cut into, the rail
    /// row, the filter chip, the title rule with its diamond, and the soft light the
    /// constellation sits in.
    ///
    /// <para><b>Why this exists, and why its absence was seven defects.</b> Every surface of
    /// this window was an <c>Image</c> with no sprite — a flat rectangle of colour — and a flat
    /// rectangle cannot have a chamfer, a bevel, a recess, a corner or a filet. Measured on the
    /// shipped window, the panel against the hole of its own columns was <b>1.06 : 1</b> and
    /// against its own border <b>1.10 : 1</b>: one percent of a channel, so the three columns
    /// were invisible and the frame did not exist. The inventory, the music plaque and the
    /// player panel each have their own atlas; the grimoire went two iterations without one,
    /// and everything that needed one waited together.</para>
    ///
    /// <para><b>A bevel is what makes an edge visible between two similar tones</b>, which is
    /// why the answer is not simply "make the panel lighter". One texel of light along the top
    /// and left and one of shade along the bottom and right reads as a raised object at any
    /// tone; the same pair inverted reads as a hole. That is the whole vocabulary here.</para>
    ///
    /// <para>Pieces are authored as tone maps — <c>o</c> outline, <c>h</c> light, <c>#</c> body,
    /// <c>s</c> shade, <c>r</c> recess body, <c>.</c> transparent — and coloured at draw time
    /// through <c>Image.color</c>, so one frame serves the panel, the card and the rail row and
    /// each school can tint its own. Sprites are made at
    /// <see cref="HudArt.SpritePixelsPerUnit"/>, which against the window's texel space makes
    /// one atlas pixel exactly one texel.</para>
    /// </summary>
    public sealed class GrimoireArt
    {
        // 128, not 64: nine sigils, two nine-slice frames, three marks and a 32-texel glow
        // do not fit a 64 shelf once the one-texel gutters are paid. Measured area is ~2 100
        // texels, but a shelf packer wastes the whole row height of its tallest piece.
        private const int AtlasSize = 128;

        /// <summary>
        /// The side of a school's mark, in texels, as AUTHORED.
        ///
        /// <para>It is stated here because the number that matters is the COVERAGE - the size
        /// the mark is drawn at against the size it was drawn as - and the two halves live in
        /// two files. The rail drew these at 7 for a while because seven texels were what was
        /// left over beside the name, and a point-filtered 9-into-7 does not shrink a mark: it
        /// DROPS two of its rows and two of its columns, so the martial X lost its arms and
        /// the cryomancy star lost two of its six points. Nothing logged, and at rail scale it
        /// read as art that had been drawn badly.</para>
        /// </summary>
        internal const int SigilTexels = 9;

        /// <summary>The side of a lock mark. Drawn at an integer multiple, for the same
        /// reason: 5 into 6.8 repeats some rows and not others, which tilts a symmetric
        /// glyph.</summary>
        internal const int MarkTexels = 5;

        public Texture2D Atlas { get; private set; }

        /// <summary>The window's own surface: chamfered, outlined, lit from the top-left.</summary>
        public Sprite Frame { get; private set; }

        /// <summary>A hole cut into it — the rail, the board and the card all sit in one.</summary>
        public Sprite Recess { get; private set; }

        /// <summary>One school's row in the rail: a slab that can be lit without a bevel war.</summary>
        public Sprite RowPlate { get; private set; }

        /// <summary>A filter chip. Same grammar as the frame, two texels smaller.</summary>
        public Sprite Chip { get; private set; }

        /// <summary>A one-texel rule for under the title. Stretches horizontally.</summary>
        public Sprite Rule { get; private set; }

        /// <summary>The diamond that sits on the rule. The window's only ornament.</summary>
        public Sprite Diamond { get; private set; }

        /// <summary>
        /// A soft round light. The constellation sits in it so the board has a centre and the
        /// two columns beside it recede — measured before it existed, the whole window was lit
        /// flat and the only thing the eye could find was the one node with a halo.
        /// </summary>
        public Sprite Glow { get; private set; }

        /// <summary>
        /// One mark per school, keyed by <c>SpellTree.schoolKey</c>.
        ///
        /// <para><b>The one thing that said "grimoire" was nothing.</b> Before these, the whole
        /// window was rectangles and circles: the rail was nine identical slabs distinguished
        /// by a word, and the card in its resting state was two words in the middle of a void.
        /// A school is the unit the player navigates by, and it had no face.</para>
        ///
        /// <para>Nine by nine, which is the size at which a mark still reads when the rail
        /// draws it at 9 texels and the card at 24. Authored as luminance like every other
        /// piece, so each takes its own school's accent and nine tints come from one set of
        /// pixels.</para>
        /// </summary>
        public Sprite Sigil(string schoolKey)
        {
            if (!string.IsNullOrEmpty(schoolKey) && _sigils.TryGetValue(schoolKey, out var s))
                return s;
            return _sigilFallback;
        }

        private readonly Dictionary<string, Sprite> _sigils = new Dictionary<string, Sprite>();
        private Sprite _sigilFallback;

        /// <summary>Why a locked node is locked, as a shape. Level, chain, purse.</summary>
        public Sprite MarkLevel { get; private set; }
        public Sprite MarkChain { get; private set; }
        public Sprite MarkPurse { get; private set; }

        private readonly Dictionary<string, RectInt> _pieces = new Dictionary<string, RectInt>();
        private Color32[] _pixels;
        private int _cursorX = 1, _cursorY = 1, _rowHeight;

        // ── Tones ─────────────────────────────────────────────────────────
        // Authored as luminance, never as hue: the window's colour arrives through
        // Image.color, and a piece that carried its own blue could not serve nine schools.

        private static readonly Color32 Clear  = new Color32(0, 0, 0, 0);
        private static readonly Color32 Line   = new Color32(16, 16, 26, 255);     // o
        private static readonly Color32 Light  = new Color32(255, 255, 255, 255);  // h
        // 150, not 190. Every tone here is MULTIPLIED by the panel's own colour, so the step
        // between the light texel and the body is what survives — at 190 against 255 that was
        // 1.34 : 1 after the multiply and the bevel barely spoke. At 150 it is 1.7 : 1, which
        // is what makes a two-pixel edge read as an edge.
        private static readonly Color32 Body   = new Color32(150, 150, 162, 255);  // #
        private static readonly Color32 Shade  = new Color32(120, 120, 134, 255);  // s
        private static readonly Color32 Sunken = new Color32(64, 64, 76, 255);     // r

        // ── Cache ─────────────────────────────────────────────────────────

        private static GrimoireArt s_instance;

        /// <summary>Domain Reload is OFF: the managed handle survives a recompile while the
        /// native texture does not, so a cached atlas would be a destroyed one on the second
        /// Play.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        public static GrimoireArt Get()
        {
            if (s_instance != null && s_instance.Atlas != null) return s_instance;
            s_instance = new GrimoireArt();
            return s_instance;
        }

        /// <summary>The pixel rect a named piece occupies. For the tests.</summary>
        public bool TryGetPieceRect(string name, out RectInt rect) => _pieces.TryGetValue(name, out rect);

        private GrimoireArt()
        {
            _pixels = new Color32[AtlasSize * AtlasSize];

            // RAISED. Light along the top and left, shade along the bottom and right, chamfered
            // corners. Nine by nine with a three-texel border, so the middle column and row are
            // pure body and repeat cleanly at any size.
            AddPattern("frame", new[]
            {
                ".ooooooo.",
                "ohhhhhhho",
                "oh#####so",
                "oh#####so",
                "oh#####so",
                "oh#####so",
                "oh#####so",
                "ossssssso",
                ".ooooooo.",
            });

            // SUNKEN. The same grammar with the light and the shade swapped, which is the whole
            // difference between an object and a hole.
            AddPattern("recess", new[]
            {
                ".ooooooo.",
                "ossssssso",
                "osrrrrrho",
                "osrrrrrho",
                "osrrrrrho",
                "osrrrrrho",
                "osrrrrrho",
                "ohhhhhhho",
                ".ooooooo.",
            });

            // A rail row. Flatter than the frame on purpose: nine of these stacked with a full
            // bevel each would read as nine competing objects rather than one list.
            AddPattern("row", new[]
            {
                "ooooooo",
                "h#####s",
                "#######",
                "#######",
                "#######",
                "s#####s",
                "ooooooo",
            });

            AddPattern("chip", new[]
            {
                ".ooooo.",
                "ohhhhho",
                "oh###so",
                "oh###so",
                "oh###so",
                "ossssso",
                ".ooooo.",
            });

            AddPattern("rule", new[]
            {
                "s#s",
            });

            AddPattern("diamond", new[]
            {
                "..#..",
                ".###.",
                "#####",
                ".###.",
                "..#..",
            });

            // Why a node is shut, as three SHAPES. All three locked states were the same dim
            // socket, so the board said "not yet" three different times in one voice and the
            // player had to read a caption to tell them apart (R6: never colour alone, and
            // never one shape for three meanings either).
            AddPattern("mark_level", new[]
            {
                "..#..",
                ".###.",
                "#####",
                ".....",
                "#####",
            });
            AddPattern("mark_chain", new[]
            {
                ".###.",
                ".#.#.",
                "#####",
                "##.##",
                "#####",
            });
            AddPattern("mark_purse", new[]
            {
                ".###.",
                "##.##",
                "#.#.#",
                "##.##",
                ".###.",
            });

            AddSigils();
            AddGlow("glow", 32);

            Atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false)
            {
                name = "GrimoireArt",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Atlas.SetPixels32(_pixels);
            Atlas.Apply(false, false);

            Frame    = SpriteOf("frame",   new Vector4(3, 3, 3, 3));
            Recess   = SpriteOf("recess",  new Vector4(3, 3, 3, 3));
            RowPlate = SpriteOf("row",     new Vector4(2, 2, 2, 2));
            Chip     = SpriteOf("chip",    new Vector4(3, 3, 3, 3));
            Rule     = SpriteOf("rule",    new Vector4(1, 0, 1, 0));
            Diamond  = SpriteOf("diamond", Vector4.zero);
            Glow     = SpriteOf("glow",    Vector4.zero);

            MarkLevel = SpriteOf("mark_level", Vector4.zero);
            MarkChain = SpriteOf("mark_chain", Vector4.zero);
            MarkPurse = SpriteOf("mark_purse", Vector4.zero);

            // Keyed by the SCHOOL KEY and not by index: the catalogue's order is a list and a
            // reordered one would silently hand every school somebody else's face.
            foreach (var key in new[] { "martial", "pyromancy", "cryomancy", "storm", "arcane",
                                        "radiance", "shadow", "verdant", "ki" })
                _sigils[key] = SpriteOf("sigil_" + key, Vector4.zero);

            // A school with no mark of its own gets the diamond rather than nothing: a rail
            // row with a hole where its neighbours have a face reads as a load that failed.
            _sigilFallback = Diamond;
        }

        /// <summary>
        /// The nine school marks. Shapes chosen to survive nine texels: an X, a flame, a
        /// six-point star, a bolt, an eye, a sun, a crescent, a leaf and a coil. Nothing here
        /// is clever — at this size a clever mark is a smudge, and what a player needs is to
        /// tell nine rows apart at a glance without reading.
        /// </summary>
        private void AddSigils()
        {
            AddPattern("sigil_martial", new[]
            {
                "#.......#",
                ".#.....#.",
                "..#...#..",
                "...#.#...",
                "....#....",
                "...#.#...",
                "..#...#..",
                ".#.....#.",
                "#.......#",
            });
            AddPattern("sigil_pyromancy", new[]
            {
                "....#....",
                "...###...",
                "...###...",
                "..#####..",
                "..#####..",
                ".#######.",
                ".#######.",
                "..#####..",
                "...###...",
            });
            AddPattern("sigil_cryomancy", new[]
            {
                "#...#...#",
                ".#..#..#.",
                "..#.#.#..",
                "...###...",
                "#########",
                "...###...",
                "..#.#.#..",
                ".#..#..#.",
                "#...#...#",
            });
            AddPattern("sigil_storm", new[]
            {
                ".....##..",
                "....##...",
                "...##....",
                "..#####..",
                "....##...",
                "...##....",
                "..##.....",
                ".##......",
                "##.......",
            });
            AddPattern("sigil_arcane", new[]
            {
                ".........",
                "..#####..",
                ".#.....#.",
                "#..###..#",
                "#.#####.#",
                "#..###..#",
                ".#.....#.",
                "..#####..",
                ".........",
            });
            AddPattern("sigil_radiance", new[]
            {
                "#...#...#",
                ".#..#..#.",
                "..#####..",
                ".##...##.",
                "##.....##",
                ".##...##.",
                "..#####..",
                ".#..#..#.",
                "#...#...#",
            });
            AddPattern("sigil_shadow", new[]
            {
                "..#####..",
                ".##....#.",
                "##.......",
                "##.......",
                "##.......",
                "##.......",
                "##.......",
                ".##....#.",
                "..#####..",
            });
            AddPattern("sigil_verdant", new[]
            {
                ".......##",
                ".....####",
                "...#####.",
                "..#####..",
                ".#####...",
                ".####....",
                ".###.....",
                ".#.......",
                "#........",
            });
            AddPattern("sigil_ki", new[]
            {
                "..#####..",
                ".#.....#.",
                "#.#####.#",
                "#.#...#.#",
                "#.#.#.#.#",
                "#.#...#.#",
                "#.#####.#",
                ".#.....#.",
                "..#####..",
            });
        }

        // ── Painting ──────────────────────────────────────────────────────

        private void AddPattern(string name, string[] rows)
        {
            int w = rows[0].Length, h = rows.Length;

            // A mark that is not the size the draw code expects is the COVERAGE defect wearing
            // a different hat: the atlas would be right, the draw would be right, and the two
            // would disagree by two texels with nothing logged. Both families are square, so a
            // ragged row is caught here as well.
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Length == w) continue;
                Debug.LogError("GrimoireArt: '" + name + "' row " + i + " is " +
                               rows[i].Length + " texels against " + w);
                return;
            }

            int expect = name.StartsWith("sigil_") ? SigilTexels
                       : name.StartsWith("mark_")  ? MarkTexels
                       : 0;
            if (expect > 0 && (w != expect || h != expect))
                Debug.LogError("GrimoireArt: '" + name + "' is " + w + "x" + h +
                               " but is drawn at " + expect + "; a point-filtered draw at any " +
                               "other size drops or repeats rows");

            var origin = Claim(name, w, h);

            for (int y = 0; y < h; y++)
            {
                // Rows are authored top to bottom and the texture runs bottom to top.
                string row = rows[h - 1 - y];
                for (int x = 0; x < w; x++)
                    _pixels[(origin.y + y) * AtlasSize + origin.x + x] = ToneOf(row[x]);
            }
        }

        /// <summary>
        /// A round falloff, squared so the centre holds and the rim goes to nothing well before
        /// the sprite's edge — a glow that reaches its own border draws a visible square.
        /// </summary>
        private void AddGlow(string name, int size)
        {
            var origin = Claim(name, size, size);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    _pixels[(origin.y + y) * AtlasSize + origin.x + x] =
                        new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
        }

        private static Color32 ToneOf(char c)
        {
            switch (c)
            {
                case 'o': return Line;
                case 'h': return Light;
                case '#': return Body;
                case 's': return Shade;
                case 'r': return Sunken;
                default:  return Clear;
            }
        }

        /// <summary>
        /// Reserves a rect with a one-texel gutter. The gutter is not tidiness: a stretched
        /// nine-slice samples to its own rect edge and would otherwise pull a neighbour's texel
        /// into its end cap — the same reason the world bars' packer leaves one.
        /// </summary>
        private RectInt Claim(string name, int w, int h)
        {
            if (_cursorX + w + 1 > AtlasSize)
            {
                _cursorX = 1;
                _cursorY += _rowHeight + 1;
                _rowHeight = 0;
            }
            if (_cursorY + h + 1 > AtlasSize)
            {
                // Loud rather than a torn atlas: writing past the row would throw somewhere
                // else entirely, and a piece silently dropped is a window with a hole in it.
                Debug.LogError("GrimoireArt: atlas full at '" + name + "'. Raise AtlasSize.");
                return new RectInt(0, 0, 1, 1);
            }

            var rect = new RectInt(_cursorX, _cursorY, w, h);
            _pieces[name] = rect;
            _cursorX += w + 1;
            if (h > _rowHeight) _rowHeight = h;
            return rect;
        }

        private Sprite SpriteOf(string name, Vector4 border)
        {
            var r = _pieces[name];
            var sprite = Sprite.Create(Atlas, new Rect(r.x, r.y, r.width, r.height),
                                       new Vector2(0.5f, 0.5f), HudArt.SpritePixelsPerUnit, 0,
                                       SpriteMeshType.FullRect, border);
            sprite.name = "grimoire_" + name;
            return sprite;
        }
    }
}
