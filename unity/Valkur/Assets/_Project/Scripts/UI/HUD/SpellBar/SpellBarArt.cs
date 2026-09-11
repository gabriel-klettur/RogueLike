using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>The glyphs of the Peace face, by meaning.</summary>
    public enum SpellBarGlyph
    {
        Interact = 0,
        Inventory = 1,
        Map = 2,
        Crafting = 3,
        Quests = 4,
        Talents = 5,
        Grimoire = 6,
        ToPeace = 7,
        ToWar = 8,
    }

    /// <summary>
    /// The pieces the action bar draws that the player panel's kit (<see cref="HudArt"/>) does not
    /// have: nine verb glyphs, a soft glow for the frame's gem, and the frame itself baked at the
    /// bar's exact texel size with the gem in the colour of the posture.
    ///
    /// <para><b>A separate atlas on purpose.</b> <c>HudArt</c> is the shared kit and is due to move
    /// assembly (<c>.github/HUD_VISUAL_LANGUAGE.md</c>, section 5); growing it with one surface's
    /// pieces is what that plan asks nobody to do. Everything here reads its colours from
    /// <see cref="PlayerHudStyle"/>, so the bar and the panel are one material.</para>
    ///
    /// <para>Glyphs are GREYSCALE (light / mid / shade / dark detail) with a baked dark outline,
    /// tinted by the slot through <c>Image.color</c> — so one pattern serves every tint, and the
    /// outline stays dark whatever the tint is, since multiplying near-black gives near-black.</para>
    /// </summary>
    public sealed class SpellBarArt
    {
        private const int AtlasSize = 128;
        private const float PixelsPerUnit = HudArt.SpritePixelsPerUnit;

        public Texture2D Atlas { get; private set; }
        public Sprite GemGlow { get; private set; }
        private readonly Sprite[] _glyphs = new Sprite[9];

        private Color32[] _pixels;
        private int _cursorX = 1, _cursorY = 1, _rowHeight;
        private readonly Dictionary<string, RectInt> _rects = new Dictionary<string, RectInt>();

        private static SpellBarArt s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpellBarArtStatics() => s_instance = null;

        /// <summary>The atlas, built on first use.</summary>
        public static SpellBarArt Get()
        {
            if (s_instance != null && s_instance.Atlas != null) return s_instance;
            s_instance = new SpellBarArt();
            return s_instance;
        }

        /// <summary>The sprite for one glyph.</summary>
        public Sprite Glyph(SpellBarGlyph glyph)
        {
            int i = (int)glyph;
            return i >= 0 && i < _glyphs.Length ? _glyphs[i] : null;
        }

        private SpellBarArt()
        {
            _pixels = new Color32[AtlasSize * AtlasSize];
            for (int i = 0; i < _glyphs.Length; i++)
                AddPattern(((SpellBarGlyph)i).ToString(), Patterns((SpellBarGlyph)i));
            AddGemGlow("gem_glow", 13);

            Atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, false)
            {
                name = "SpellBarArt_Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Atlas.SetPixels32(_pixels);
            Atlas.Apply(false, false);
            _pixels = null;

            for (int i = 0; i < _glyphs.Length; i++) _glyphs[i] = SpriteOf(((SpellBarGlyph)i).ToString());
            GemGlow = SpriteOf("gem_glow");
        }

        // -- The frame -------------------------------------------------------------------

        /// <summary>
        /// The bar's stone at its exact size: the player panel's own bake, with the gem re-cut in
        /// the posture's accent and a groove between groups of slots.
        /// </summary>
        /// <param name="grooves">X of each groove, in texels.</param>
        public static Texture2D BakeFrame(int w, int h, PlayerHudStyle s, Color gem, IReadOnlyList<int> grooves)
        {
            var tex = HudArt.BakePanel(w, h, s);
            tex.name = "SpellBarStone";
            var px = tex.GetPixels32();

            // The gem sits where BakePanel puts it — top edge, centred — and only its two inner
            // rings are re-cut, so the gold setting around it is the panel's.
            int gx = w / 2, gy = h - 3;
            var gemLit = Color.Lerp(gem, Color.white, 0.45f);
            var gemDeep = Color.Lerp(gem, Color.black, 0.25f);
            for (int y = gy - 1; y <= gy + 1; y++)
                for (int x = gx - 1; x <= gx + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    int d = Mathf.Abs(x - gx) + Mathf.Abs(y - gy);
                    if (d > 1) continue;
                    Color c = d == 0 ? gem : (y > gy || x < gx) ? gemLit : gemDeep;
                    px[y * w + x] = c;
                }

            // A groove between groups: a dark cut and its lit lip, inside the bevel only.
            if (grooves != null)
            {
                var cut = Color.Lerp(s.outline, s.stoneDark, 0.35f);
                var lip = Color.Lerp(s.stoneLight, Color.white, 0.12f);
                for (int g = 0; g < grooves.Count; g++)
                {
                    int x = grooves[g];
                    if (x < 4 || x >= w - 5) continue;
                    for (int y = 4; y < h - 4; y++)
                    {
                        px[y * w + x] = cut;
                        px[y * w + x + 1] = lip;
                    }
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // -- Packing -----------------------------------------------------------------------

        private Sprite SpriteOf(string name)
        {
            if (!_rects.TryGetValue(name, out var r)) return null;
            var sprite = Sprite.Create(Atlas, new Rect(r.x, r.y, r.width, r.height), new Vector2(0.5f, 0.5f),
                                       PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "SpellBar_" + name;
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
                throw new System.InvalidOperationException("SpellBarArt atlas is full; raise AtlasSize.");
            var r = new RectInt(_cursorX, _cursorY, w, h);
            _cursorX += w + 1;
            _rowHeight = Mathf.Max(_rowHeight, h);
            return r;
        }

        private void AddGemGlow(string name, int size)
        {
            var r = Reserve(size, size);
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // A diamond, like the gem it lights, fading from the middle.
                    float d = (Mathf.Abs(x - c) + Mathf.Abs(y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            _rects[name] = r;
        }

        private void AddPattern(string name, string[] rows)
        {
            var px = Rasterise(rows, out int w, out int h);
            var r = Reserve(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = px[y * w + x];
            _rects[name] = r;
        }

        /// <summary>
        /// A top-down pattern to bottom-up pixels, with a dark one-texel outline dilated round the
        /// ink (8-neighbourhood). Its own copy rather than <c>HudArt.Rasterise</c>, which is
        /// internal to an assembly the kit is due to leave.
        /// </summary>
        internal static Color32[] Rasterise(string[] rows, out int w, out int h)
        {
            int pw = 0;
            for (int i = 0; i < rows.Length; i++) pw = Mathf.Max(pw, rows[i].Length);
            int ph = rows.Length;
            w = pw + 2;
            h = ph + 2;
            var px = new Color32[w * h];
            var ink = new bool[w * h];
            for (int ry = 0; ry < ph; ry++)
            {
                string row = rows[ry];
                for (int rx = 0; rx < row.Length; rx++)
                {
                    int v = Tone(row[rx]);
                    if (v < 0) continue;
                    int x = rx + 1, y = (ph - 1 - ry) + 1;
                    px[y * w + x] = new Color32((byte)v, (byte)v, (byte)v, 255);
                    ink[y * w + x] = true;
                }
            }
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

        private static int Tone(char c)
        {
            switch (c)
            {
                case 'h': return 255;
                case 'm': return 205;
                case 's': return 140;
                case 'd': return 60;
                default: return -1;
            }
        }

        /// <summary>The nine glyphs, 11 texels at most, designed at 6x in a preview first.</summary>
        internal static string[] Patterns(SpellBarGlyph glyph)
        {
            switch (glyph)
            {
                case SpellBarGlyph.Interact: return new[]
                {
                    "   m       ",
                    "  mhm m    ",
                    "  mhmmhm   ",
                    "  mhmmhm m ",
                    "  mhmmhmmhm",
                    "m mhhhhmmhm",
                    "mhmhhhhhhhm",
                    " mhhhhhhhhm",
                    " mhhhhhhhs ",
                    "  mhhhhhs  ",
                    "   mhhss   ",
                };
                case SpellBarGlyph.Inventory: return new[]
                {
                    "   msssm   ",
                    "  m     m  ",
                    " mmmmmmmmm ",
                    "mhhhhhhhhhm",
                    "mhmmmmmmmhm",
                    "mhm dsd mhm",
                    "mhhhhhhhhhm",
                    "mhhhhhhhhhm",
                    "mhhhhhhhhhs",
                    "mhhhhhhhhss",
                    " sssssssss ",
                };
                case SpellBarGlyph.Map: return new[]
                {
                    "     h     ",
                    "    mhm    ",
                    "    mhm    ",
                    "   mmhmm   ",
                    " hhhhdssss ",
                    "hhhhhdsssss",
                    " hhhhdssss ",
                    "   mmsmm   ",
                    "    msm    ",
                    "    msm    ",
                    "     s     ",
                };
                case SpellBarGlyph.Crafting: return new[]
                {
                    "  hhhhh    ",
                    " hhhhhhm   ",
                    " hhhhhhm   ",
                    "  mmhmmm   ",
                    "   mmmmss  ",
                    "    s sss  ",
                    "       sss ",
                    "        sss",
                    "         ss",
                };
                case SpellBarGlyph.Quests: return new[]
                {
                    " hhhhhhhh  ",
                    "hmmmmmmmmh ",
                    " mhhhhhhhm ",
                    " mhhh dhhm ",
                    " mhhh dhhm ",
                    " mhhh dhhm ",
                    " mhhhhhhhm ",
                    " mhhh dhhm ",
                    " mhhhhhhhm ",
                    "smmmmmmmms ",
                    " ssssssss  ",
                };
                case SpellBarGlyph.Talents: return new[]
                {
                    "     h     ",
                    "    hhh    ",
                    "    hhh    ",
                    "hhhhhhhhhhm",
                    " mhhhhhhhm ",
                    "  mhhhhhm  ",
                    "  mhhmhhm  ",
                    " mhms smhm ",
                    " ms     sm ",
                };
                case SpellBarGlyph.Grimoire: return new[]
                {
                    " mmm   mmm ",
                    "mhhhm mhhhm",
                    "mhsshmhsshm",
                    "mhhhhmhhhhm",
                    "mhsshmhsshm",
                    "mhhhhmhhhhm",
                    "mhsshmhsshm",
                    "mhhhhmhhhhm",
                    " mmmmsmmmm ",
                    "     s     ",
                };
                case SpellBarGlyph.ToPeace: return new[]
                {
                    "       hhhm",
                    "     hhhhhm",
                    "    hhhhhhm",
                    "   hhhhhmm ",
                    "  hhhhdmm  ",
                    "  hhhdmm   ",
                    " hhhdmms   ",
                    " hmdmms    ",
                    " dmss      ",
                    "ds         ",
                };
                default: return new[]
                {
                    "hm       mh",
                    "mhm     mhm",
                    " mhm   mhm ",
                    "  mhm mhm  ",
                    "   mhmhm   ",
                    "    mhm    ",
                    "   mhmhm   ",
                    " ssmm mmss ",
                    " smm   mms ",
                    "ss       ss",
                };
            }
        }
    }
}
