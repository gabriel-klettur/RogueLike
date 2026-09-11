using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The music panel's own pieces, generated in code into ONE point-filtered atlas: stone keys
    /// in three states, the transport glyphs, the volume notch, the gold bead of the playhead,
    /// the medallion and its zone sigils — plus the plaque's stone, baked to its exact size.
    ///
    /// <para><b>Why generated, and why beside <see cref="HudArt"/> rather than inside it.</b>
    /// Generated for the reason every HUD atlas is: every colour comes from the theme and every
    /// size from the style, so a retune regenerates art that fits instead of resampling art that
    /// does not. A separate atlas because these pieces belong to one panel; text and motes still
    /// come from <see cref="HudArt"/>, so the words on the plaque are the same glyphs as the
    /// numbers on the player panel.</para>
    ///
    /// <para><b>The old panel's glyphs were rasterised at 32 px and drawn at 15.6 x 24.5</b>, and
    /// three of them were wrong: the pause was two dots and the skip bars were ticks, because
    /// <c>FillRect</c> was called with its width, height and origin in the wrong order. Glyphs
    /// here are pixel PATTERNS, drawn at exactly one texel per pattern cell, and the pause test
    /// reads its two bars back out of the atlas.</para>
    /// </summary>
    public sealed class MusicHudArt
    {
        private const int AtlasSize = 128;

        public Texture2D Atlas { get; private set; }
        public PlayerHudStyle Theme { get; }

        public Sprite White { get; private set; }
        public Sprite Key { get; private set; }
        public Sprite KeyHot { get; private set; }
        public Sprite KeyDown { get; private set; }
        public Sprite Well { get; private set; }

        /// <summary>
        /// The groove's well, three texels tall. A 9-slice whose borders add up to more than its
        /// rect is SQUASHED onto half texels — measured on the first live capture, the groove was
        /// the one row of the plaque off the pixel grid, drawn with <see cref="Well"/>'s 2+2 border.
        /// </summary>
        public Sprite WellThin { get; private set; }
        public Sprite Prev { get; private set; }
        public Sprite Next { get; private set; }
        public Sprite Play { get; private set; }
        public Sprite Pause { get; private set; }
        public Sprite SpeakerOn { get; private set; }
        public Sprite SpeakerOff { get; private set; }
        public Sprite Close { get; private set; }
        public Sprite Resonance { get; private set; }
        public Sprite Bead { get; private set; }
        public Sprite BeadHot { get; private set; }
        public Sprite MedallionFace { get; private set; }
        public Sprite MedallionRim { get; private set; }
        public Sprite MedallionGlow { get; private set; }
        public Sprite Shine { get; private set; }
        public Sprite[] Sigils { get; private set; }

        private readonly Dictionary<string, (RectInt rect, Vector4 border)> _pieces =
            new Dictionary<string, (RectInt, Vector4)>();
        private Color32[] _pixels;
        private int _cursorX = 1, _cursorY = 1, _rowHeight;

        // -- Cache ---------------------------------------------------------------

        private static MusicHudArt s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMusicHudArtStatics() => s_instance = null;

        /// <summary>The atlas for a theme. Rebuilt only when the theme changes.</summary>
        public static MusicHudArt Get(PlayerHudStyle theme)
        {
            if (theme == null) theme = PlayerHudStyle.Active;
            if (s_instance != null && s_instance.Atlas != null && s_instance.Theme == theme) return s_instance;
            s_instance = new MusicHudArt(theme);
            return s_instance;
        }

        private MusicHudArt(PlayerHudStyle theme)
        {
            Theme = theme;
            Build();
        }

        /// <summary>The pixel rect a named piece occupies. For the tests.</summary>
        public bool TryGetPieceRect(string name, out RectInt rect)
        {
            if (_pieces.TryGetValue(name, out var p)) { rect = p.rect; return true; }
            rect = default;
            return false;
        }

        // -- Glyph patterns (rows top-down) -------------------------------------------

        // Glyph patterns, rows top-down separated by '|'. Constants rather than static arrays:
        // a static array is mutable state the Domain-Reload ratchet (rightly) refuses.

        public const string PrevRows = "#   #|#  ##|# ###|#  ##|#   #";
        public const string NextRows = "#   #|##  #|### #|##  #|#   #";
        public const string PlayRows = "#  |## |###|## |#  ";
        public const string PauseRows = "# #|# #|# #|# #|# #";
        public const string SpeakerOnRows = "   # #  |####  # |####  # |####  # |   # #  ";
        public const string SpeakerOffRows = "   #    |#### # #|####  # |#### # #|   #    ";
        public const string CloseRows = "# #| # |# #";
        public const string ResonanceRows = "  #  |  #  |# #  |# # #|# # #";

        /// <summary>The medallion sigil for a zone picture, rows top-down.</summary>
        public static string SigilRows(MusicSigil sigil)
        {
            switch (sigil)
            {
                // Note: the menu, and anything with no zone.
                default: return "     ##  |     # # |     #  #|     #   |     #   |  ####   | #####   | #####   |  ###    ";
                // Town: Pepitoria. The walls are as wide as the roof minus its eaves; narrower,
                // the house reads as an arrow pointing up (it did, on the first live capture).
                case MusicSigil.Town: return "    #    |   ###   |  #####  | ####### |#########| ####### | ####### | ### ### | ### ### ";
                // Forest.
                case MusicSigil.Forest: return "    #    |   ###   |  #####  |   ###   |  #####  | ####### |#########|    #    |    #    ";
                // Desert: a sun over a dune.
                case MusicSigil.Desert: return "    #    |  #   #  |   ###   | # ### # |   ###   |  #   #  |         |   ####  |#########";
                // Crypt: Covetus, an arched doorway into the dark.
                case MusicSigil.Crypt: return "   ###   | ####### | ##   ## |##     ##|##     ##|##     ##|##     ##|##     ##|##     ##";
            }
        }

        /// <summary>Splits a pattern constant into its rows.</summary>
        public static string[] Rows(string rows) => rows.Split('|');

        private const int SigilCount = 5;

        // -- Build -----------------------------------------------------------------

        private void Build()
        {
            _pixels = new Color32[AtlasSize * AtlasSize];
            var s = Theme;

            Add("white", 3, 3, (x, y) => Grey(255, 255), Vector4.one);
            Add("key", 7, 7, (x, y) => KeyPixel(x, y, 7, s, KeyState.Up), Vector4.one * 2f);
            Add("key_hot", 7, 7, (x, y) => KeyPixel(x, y, 7, s, KeyState.Hot), Vector4.one * 2f);
            Add("key_down", 7, 7, (x, y) => KeyPixel(x, y, 7, s, KeyState.Down), Vector4.one * 2f);
            Add("well", 5, 5, (x, y) => WellPixel(x, y, 5, s), Vector4.one * 2f);
            Add("well_thin", 3, 3, (x, y) => Chamfered(x, y, 3) ? Clear()
                : EdgeDistance(x, y, 3) == 0 ? C(s.outline) : C(s.recess), Vector4.one);

            AddGlyph("prev", Rows(PrevRows));
            AddGlyph("next", Rows(NextRows));
            AddGlyph("play", Rows(PlayRows));
            AddGlyph("pause", Rows(PauseRows));
            AddGlyph("speaker_on", Rows(SpeakerOnRows));
            AddGlyph("speaker_off", Rows(SpeakerOffRows));
            AddGlyph("close", Rows(CloseRows));
            AddGlyph("resonance", Rows(ResonanceRows));

            AddPattern("bead", Rows("#h#|###|#s#"), BeadShade, outline: true);
            AddPattern("bead_hot", Rows(" ### |#hh##|#h###|####s| #ss "), BeadShade, outline: true);

            Add("medallion_face", 15, 15, (x, y) => MedallionFacePixel(x, y, 15, s), Vector4.zero);
            Add("medallion_rim", 15, 15, (x, y) => MedallionRimPixel(x, y, 15), Vector4.zero);
            Add("medallion_glow", 23, 23, (x, y) => SoftRing(x, y, 23, 8.2f, 3.6f), Vector4.zero);
            Add("shine", 3, 7, (x, y) => Grey(255, x == 1 ? 210 : 70), Vector4.zero);

            for (int i = 0; i < SigilCount; i++) AddGlyph("sigil_" + i, Rows(SigilRows((MusicSigil)i)));

            Atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, false)
            {
                name = "MusicHudArt_Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Atlas.SetPixels32(_pixels);
            Atlas.Apply(false, false);
            _pixels = null;

            White = SpriteOf("white");
            Key = SpriteOf("key");
            KeyHot = SpriteOf("key_hot");
            KeyDown = SpriteOf("key_down");
            Well = SpriteOf("well");
            WellThin = SpriteOf("well_thin");
            Prev = SpriteOf("prev");
            Next = SpriteOf("next");
            Play = SpriteOf("play");
            Pause = SpriteOf("pause");
            SpeakerOn = SpriteOf("speaker_on");
            SpeakerOff = SpriteOf("speaker_off");
            Close = SpriteOf("close");
            Resonance = SpriteOf("resonance");
            Bead = SpriteOf("bead");
            BeadHot = SpriteOf("bead_hot");
            MedallionFace = SpriteOf("medallion_face");
            MedallionRim = SpriteOf("medallion_rim");
            MedallionGlow = SpriteOf("medallion_glow");
            Shine = SpriteOf("shine");
            Sigils = new Sprite[SigilCount];
            for (int i = 0; i < SigilCount; i++) Sigils[i] = SpriteOf("sigil_" + i);
        }

        /// <summary>The sigil sprite for a zone's picture.</summary>
        public Sprite SigilFor(MusicSigil sigil)
        {
            int i = (int)sigil;
            return Sigils != null && i >= 0 && i < Sigils.Length ? Sigils[i] : null;
        }

        // -- Packing ---------------------------------------------------------------

        private Sprite SpriteOf(string name)
        {
            if (!_pieces.TryGetValue(name, out var p)) return null;
            var sprite = Sprite.Create(Atlas, new Rect(p.rect.x, p.rect.y, p.rect.width, p.rect.height),
                                       new Vector2(0.5f, 0.5f), HudArt.SpritePixelsPerUnit, 0,
                                       SpriteMeshType.FullRect, p.border);
            sprite.name = "MusicHud_" + name;
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
                throw new System.InvalidOperationException("MusicHudArt atlas is full; raise AtlasSize.");
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

        private void AddPattern(string name, string[] rows, System.Func<char, Color32?> map, bool outline)
        {
            var px = HudArt.Rasterise(rows, map, outline, out int w, out int h);
            var r = Reserve(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = px[y * w + x];
            _pieces[name] = (r, Vector4.zero);
        }

        /// <summary>A white glyph, tinted by its Image. No outline: it sits on a key face.</summary>
        private void AddGlyph(string name, string[] rows)
            => AddPattern(name, rows, c => c == ' ' ? (Color32?)null : Grey(255, 255), outline: false);

        // -- Pixel rules -------------------------------------------------------------

        private enum KeyState { Up, Hot, Down }

        private static Color32 Clear() => new Color32(0, 0, 0, 0);
        private static Color32 Grey(int v, int a) => new Color32((byte)v, (byte)v, (byte)v, (byte)a);
        private static Color32 C(Color c) => c;

        private static int EdgeDistance(int x, int y, int size) =>
            Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));

        private static bool Chamfered(int x, int y, int size) =>
            (x == 0 || x == size - 1) && (y == 0 || y == size - 1);

        /// <summary>
        /// A stone key lit from the top-left, the direction every bevel in the HUD is lit from.
        /// Pressed, the bevel inverts and the face darkens: the key goes IN.
        /// </summary>
        private static Color32 KeyPixel(int x, int y, int size, PlayerHudStyle s, KeyState state)
        {
            if (Chamfered(x, y, size)) return Clear();
            int d = EdgeDistance(x, y, size);
            if (d == 0) return C(s.outline);
            var face = state == KeyState.Down ? Color.Lerp(s.stoneDark, s.recess, 0.4f)
                     : state == KeyState.Hot ? Color.Lerp(s.stoneLight, Color.white, 0.10f)
                     : s.stoneLight;
            if (d == 1)
            {
                bool top = y == size - 2, left = x == 1, bottom = y == 1, right = x == size - 2;
                var lit = Color.Lerp(face, Color.white, state == KeyState.Hot ? 0.26f : 0.18f);
                var shade = Color.Lerp(s.outline, s.stoneDark, 0.45f);
                bool litEdge = (top || left) && !(bottom || right);
                bool shadeEdge = (bottom || right) && !(top || left);
                if (state == KeyState.Down) (litEdge, shadeEdge) = (shadeEdge, litEdge);
                if (litEdge) return C(lit);
                if (shadeEdge) return C(shade);
                return C(face);
            }
            return C(face);
        }

        /// <summary>A sunken well: outline, a shadow under its top edge, then the recess.</summary>
        private static Color32 WellPixel(int x, int y, int size, PlayerHudStyle s)
        {
            if (Chamfered(x, y, size)) return Clear();
            int d = EdgeDistance(x, y, size);
            if (d == 0) return C(s.outline);
            if (y == size - 2) return C(Color.Lerp(s.recess, Color.black, 0.45f));
            return C(s.recess);
        }

        private static Color32? BeadShade(char c)
        {
            switch (c)
            {
                case '#': return new Color32(230, 194, 97, 255);
                case 'h': return new Color32(255, 240, 196, 255);
                case 's': return new Color32(133, 102, 43, 255);
                default: return null;
            }
        }

        private static Color32 MedallionFacePixel(int x, int y, int size, PlayerHudStyle s)
        {
            float c = (size - 1) * 0.5f;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
            float r = size * 0.5f;
            if (d > r - 2.25f) return Clear();
            float t = Mathf.Clamp01(d / (r - 2.25f));
            var face = Color.Lerp(Color.Lerp(s.recess, s.stoneDark, 0.6f), s.recess, t);
            return C(face);
        }

        /// <summary>
        /// The medallion's rim in GREYS, so its Image colour decides what it is made of: stone
        /// at rest, gold for the moment a new track starts.
        /// </summary>
        private static Color32 MedallionRimPixel(int x, int y, int size)
        {
            float c = (size - 1) * 0.5f;
            float dx = x - c, dy = y - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float r = size * 0.5f;
            if (d > r - 0.25f) return Clear();
            if (d > r - 1.25f) return new Color32(8, 8, 13, 255);
            if (d > r - 2.25f)
            {
                float light = (-dx + dy) / Mathf.Max(0.001f, d);
                int v = light > 0.35f ? 255 : light < -0.35f ? 120 : 190;
                return Grey(v, 255);
            }
            if (d > r - 3.25f) return new Color32(8, 8, 13, 220);
            return Clear();
        }

        private static Color32 SoftRing(int x, int y, int size, float radius, float width)
        {
            float c = (size - 1) * 0.5f;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - radius) / width);
            return Grey(255, (int)(a * a * 255f));
        }

        // -- The plaque's stone ------------------------------------------------------

        /// <summary>
        /// The plaque at its exact texel size: a chamfered outline, a bevel lit from the
        /// top-left, a stone ramp broken by an ordered dither, a stone rivet in each inner
        /// corner — and NO gold, which the theme reserves for importance. With a resonance
        /// (<paramref name="plaqueHeight"/> below <paramref name="h"/>) the two sections are
        /// divided by a seam: a dark row under a lit one, so the upper part reads as a second
        /// slab set on the first rather than a taller box.
        /// </summary>
        public static Texture2D BakePlaque(int w, int h, int plaqueHeight, PlayerHudStyle s)
        {
            var px = new Color32[w * h];
            var lit = Color.Lerp(s.stoneLight, Color.white, 0.16f);
            var shade = Color.Lerp(s.outline, s.stoneDark, 0.5f);
            bool split = plaqueHeight > 0 && plaqueHeight < h;
            for (int y = 0; y < h; y++)
            {
                bool upper = split && y >= plaqueHeight;
                int y0 = upper ? plaqueHeight : 0;
                int y1 = upper ? h : (split ? plaqueHeight : h);
                int sectionH = y1 - y0;
                int ly = y - y0;
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x), dyOuter = Mathf.Min(y, h - 1 - y);
                    Color c;
                    if (dx + dyOuter < 2) c = Color.clear;
                    else if (dx + dyOuter == 2 || dx == 0 || dyOuter == 0) c = s.outline;
                    else if (split && (y == plaqueHeight - 1)) c = shade;          // seam, dark
                    else if (split && (y == plaqueHeight)) c = s.outline;         // seam, edge
                    else if (split && (y == plaqueHeight + 1)) c = lit;           // upper slab's bevel
                    else if (dx == 1 && x < w / 2) c = lit;
                    else if (dx == 1) c = shade;
                    else if (dyOuter == 1 && y > h / 2) c = lit;
                    else if (dyOuter == 1) c = shade;
                    else
                    {
                        float t = (float)ly / Mathf.Max(1, sectionH - 1);
                        c = Color.Lerp(s.stoneDark, s.stoneLight, t * t * 0.8f + 0.12f);
                        float dither = (Bayer4(x, y) - 7.5f) / 16f * 0.022f;
                        float grain = (Hash(x, y) - 0.5f) * 0.016f;
                        c.r += dither + grain;
                        c.g += dither + grain;
                        c.b += dither + grain * 1.3f;
                        int dyIn = Mathf.Min(ly, sectionH - 1 - ly);
                        // A stone rivet in each inner corner of each slab.
                        if ((dx == 3 || dx == 4) && (dyIn == 3 || dyIn == 4))
                            c = (dx == 3 && ((ly > sectionH / 2) ? dyIn == 3 : dyIn == 4)) ? lit : shade;
                    }
                    c.a = c == Color.clear ? 0f : 1f;
                    px[y * w + x] = c;
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = "MusicHudPlaque",
                filterMode = FilterMode.Point,
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
