using System.Collections.Generic;

namespace Valkur.UI.HUD
{
    /// <summary>Which of the panel's two bitmap faces a label is drawn in.</summary>
    public enum HudFontFace
    {
        /// <summary>3x5 capitals, digits and a little punctuation. Key caps, mana, timers.</summary>
        Small = 0,

        /// <summary>5x7 digits. The health numbers and the level, the two a player reads mid-fight.</summary>
        Large = 1,
    }

    /// <summary>
    /// The glyphs the player panel draws its numbers with, as pixel patterns.
    ///
    /// <para><b>Why a bitmap face at all.</b> The panel lives on a whole-pixel texel grid and the
    /// only font the project ships is LiberationSans SDF, whose outline-less 13 px bold was the
    /// least legible thing on the old panel — white over a light green fill. A bitmap glyph lands
    /// on the same grid as the frame around it, carries a baked one-texel dark outline (see
    /// <c>HudArt</c>), and reads over any fill colour.</para>
    ///
    /// <para>No accented letters: spell NAMES go through TMP in the tooltip. This face only ever
    /// spells numbers and key caps.</para>
    ///
    /// <para>Returned fresh on every call rather than held in a static: the atlas builds once and
    /// keeps its own copy, and a static table here would be one more field the Domain-Reload
    /// ratchet has to see reset.</para>
    /// </summary>
    public static class HudPixelFont
    {
        /// <summary>Pixels between two glyphs. The outline of each overlaps the gap, on purpose.</summary>
        public const int Tracking = 1;

        /// <summary>Glyph height of a face, without the outline.</summary>
        public static int HeightOf(HudFontFace face) => face == HudFontFace.Large ? 7 : 5;

        /// <summary>
        /// Extra rows a Spanish glyph claims ABOVE the cap line, for its accent or its
        /// inverted mark.
        ///
        /// <para><b>Why a glyph may be taller than its own face.</b> A five-row face has no
        /// room for an accent INSIDE the height of a capital — put it there and it either
        /// eats the crossbar or touches the letter, which at two screen pixels per texel reads
        /// as one blob. The renderer does not require every glyph to be the face's height:
        /// <c>HudPixelText</c> seats every quad on the same baseline (<c>qy = y0 - 1f</c>) and
        /// takes its height from the glyph (<c>g.CellHeight</c>), so a taller glyph grows
        /// UPWARD, which is exactly where an accent goes.</para>
        ///
        /// <para>Declared here rather than written as a 2 in the fixture that checks it: a
        /// literal in the test is a number that goes stale the day the face changes, and the
        /// font is the thing that knows.</para>
        /// </summary>
        public const int AccentRows = 2;

        /// <summary>
        /// The characters allowed to be <see cref="AccentRows"/> taller than their face.
        ///
        /// <para>A CLOSED set, and that is the half that keeps this an invariant rather than a
        /// loophole: without it "a glyph may be taller" lets any glyph be any height, and the
        /// next ragged or oversized one lands with nothing to catch it. Ragged glyphs — rows of
        /// differing width — stay forbidden outright, because those really do break the atlas
        /// packing, while a taller one does not.</para>
        /// </summary>
        public static bool ClaimsAccentRows(char c) =>
            c == 'Á' || c == 'É' || c == 'Í' || c == 'Ó' || c == 'Ú' || c == 'Ü' || c == 'Ñ'
            || c == '¿' || c == '¡';

        /// <summary>Width of a space in a face.</summary>
        public static int SpaceWidthOf(HudFontFace face) => face == HudFontFace.Large ? 3 : 2;

        /// <summary>Every glyph of a face, keyed by character. Rows run top to bottom.</summary>
        public static Dictionary<char, string[]> Glyphs(HudFontFace face)
            => face == HudFontFace.Large ? BuildLarge() : BuildSmall();

        private static Dictionary<char, string[]> BuildLarge()
        {
            return new Dictionary<char, string[]>
            {
                // A plain zero: the slashed one read as "Ø" inside the level medallion, and no
                // letter O shares this face to be confused with it.
                ['0'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
                ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
                ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
                ['3'] = new[] { " ### ", "#   #", "    #", "  ## ", "    #", "#   #", " ### " },
                ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
                ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
                ['6'] = new[] { "  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### " },
                ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
                ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
                ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  " },
                ['/'] = new[] { "   #", "   #", "  # ", "  # ", " #  ", " #  ", "#   " },
                ['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
                ['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
                // Narrow on purpose: the debug HUD prints "10.3" and "01:23" in this face, and a
                // full-width point would put a digit-sized gap in the middle of a number.
                ['.'] = new[] { "  ", "  ", "  ", "  ", "  ", "##", "##" },
                [':'] = new[] { "  ", "##", "##", "  ", "##", "##", "  " },
            };
        }

        private static Dictionary<char, string[]> BuildSmall()
        {
            return new Dictionary<char, string[]>
            {
                ['A'] = new[] { " # ", "# #", "###", "# #", "# #" },
                ['B'] = new[] { "## ", "# #", "## ", "# #", "## " },
                ['C'] = new[] { " ##", "#  ", "#  ", "#  ", " ##" },
                ['D'] = new[] { "## ", "# #", "# #", "# #", "## " },
                ['E'] = new[] { "###", "#  ", "## ", "#  ", "###" },
                ['F'] = new[] { "###", "#  ", "## ", "#  ", "#  " },
                ['G'] = new[] { " ##", "#  ", "# #", "# #", " ##" },
                ['H'] = new[] { "# #", "# #", "###", "# #", "# #" },
                ['I'] = new[] { "###", " # ", " # ", " # ", "###" },
                ['J'] = new[] { "  #", "  #", "  #", "# #", " # " },
                ['K'] = new[] { "# #", "# #", "## ", "# #", "# #" },
                ['L'] = new[] { "#  ", "#  ", "#  ", "#  ", "###" },
                ['M'] = new[] { "# #", "###", "###", "# #", "# #" },
                ['N'] = new[] { "## ", "# #", "# #", "# #", "# #" },
                ['O'] = new[] { " # ", "# #", "# #", "# #", " # " },
                ['P'] = new[] { "## ", "# #", "## ", "#  ", "#  " },
                ['Q'] = new[] { " # ", "# #", "# #", "## ", " ##" },
                ['R'] = new[] { "## ", "# #", "## ", "# #", "# #" },
                ['S'] = new[] { " ##", "#  ", " # ", "  #", "## " },
                ['T'] = new[] { "###", " # ", " # ", " # ", " # " },
                ['U'] = new[] { "# #", "# #", "# #", "# #", "###" },
                ['V'] = new[] { "# #", "# #", "# #", "# #", " # " },
                ['W'] = new[] { "# #", "# #", "###", "###", "# #" },
                ['X'] = new[] { "# #", "# #", " # ", "# #", "# #" },
                ['Y'] = new[] { "# #", "# #", " # ", " # ", " # " },
                ['Z'] = new[] { "###", "  #", " # ", "#  ", "###" },
                ['0'] = new[] { "###", "# #", "# #", "# #", "###" },
                ['1'] = new[] { " # ", "## ", " # ", " # ", "###" },
                ['2'] = new[] { "## ", "  #", " # ", "#  ", "###" },
                ['3'] = new[] { "## ", "  #", " # ", "  #", "## " },
                ['4'] = new[] { "# #", "# #", "###", "  #", "  #" },
                ['5'] = new[] { "###", "#  ", "## ", "  #", "## " },
                ['6'] = new[] { " ##", "#  ", "###", "# #", "###" },
                ['7'] = new[] { "###", "  #", " # ", " # ", " # " },
                ['8'] = new[] { "###", "# #", "###", "# #", "###" },
                ['9'] = new[] { "###", "# #", "###", "  #", "## " },
                ['/'] = new[] { "  #", "  #", " # ", "#  ", "#  " },
                ['+'] = new[] { "   ", " # ", "###", " # ", "   " },
                ['-'] = new[] { "   ", "   ", "###", "   ", "   " },
                ['.'] = new[] { " ", " ", " ", " ", "#" },
                [':'] = new[] { " ", "#", " ", "#", " " },
                ['%'] = new[] { "# #", "  #", " # ", "#  ", "# #" },
                // Punctuation the debug HUD needs to print entity names, counters and states
                // without falling back to TMP. Appended only: no existing glyph changed, and
                // HudAbilitySlot's CanSpell check can only get MORE labels to fit.
                ['('] = new[] { " #", "# ", "# ", "# ", " #" },
                [')'] = new[] { "# ", " #", " #", " #", "# " },
                ['['] = new[] { "##", "# ", "# ", "# ", "##" },
                [']'] = new[] { "##", " #", " #", " #", "##" },
                ['='] = new[] { "   ", "###", "   ", "###", "   " },
                [','] = new[] { " ", " ", " ", "#", "#" },
                ['<'] = new[] { "  #", " # ", "#  ", " # ", "  #" },
                ['>'] = new[] { "#  ", " # ", "  #", " # ", "#  " },
                ['#'] = new[] { "# #", "###", "# #", "###", "# #" },
                ['!'] = new[] { "#", "#", "#", " ", "#" },
                ['?'] = new[] { "## ", "  #", " # ", "   ", " # " },
                ['_'] = new[] { "   ", "   ", "   ", "   ", "###" },
                ['\''] = new[] { "#", "#", " ", " ", " " },

                // Spanish. SEVEN rows in a five-row face, on purpose: the renderer takes each
                // quad's height from the glyph itself (HudGlyph.CellHeight) and seats every
                // glyph on the same baseline, so the two extra rows land ABOVE the cap line —
                // which is exactly where an accent goes. The blank second row is load-bearing:
                // an accent touching the letter under it reads as one blob at 2 screen pixels
                // per texel. Appended only; no existing glyph changed, and CanSpell can only
                // get MORE labels to fit.
                ['Á'] = new[] { "  #", "   ", " # ", "# #", "###", "# #", "# #" },
                ['É'] = new[] { "  #", "   ", "###", "#  ", "## ", "#  ", "###" },
                ['Í'] = new[] { "  #", "   ", "###", " # ", " # ", " # ", "###" },
                ['Ó'] = new[] { "  #", "   ", " # ", "# #", "# #", "# #", " # " },
                ['Ú'] = new[] { "  #", "   ", "# #", "# #", "# #", "# #", "###" },
                ['Ü'] = new[] { "# #", "   ", "# #", "# #", "# #", "# #", "###" },
                ['Ñ'] = new[] { "###", "   ", "## ", "# #", "# #", "# #", "# #" },
                ['¿'] = new[] { " # ", "   ", " # ", "#  ", "#  ", "# #", " # " },
                ['¡'] = new[] { "#", " ", "#", "#", "#", "#", "#" },
            };
        }

        /// <summary>
        /// Width of <paramref name="text"/> in a face, in texels, without the outline — so a
        /// caller can centre a label on a whole texel before any quad exists.
        /// </summary>
        public static int MeasureWidth(string text, HudFontFace face, Dictionary<char, string[]> glyphs)
        {
            if (string.IsNullOrEmpty(text) || glyphs == null) return 0;
            int width = 0;
            bool any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                int w;
                if (c == ' ') w = SpaceWidthOf(face);
                else if (glyphs.TryGetValue(c, out var rows) && rows.Length > 0) w = rows[0].Length;
                else continue;
                if (any) width += Tracking;
                width += w;
                any = true;
            }
            return width;
        }

        /// <summary>True when every character of <paramref name="text"/> has a glyph in the face.</summary>
        public static bool CanSpell(string text, Dictionary<char, string[]> glyphs)
        {
            if (string.IsNullOrEmpty(text) || glyphs == null) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                if (c != ' ' && !glyphs.ContainsKey(c)) return false;
            }
            return true;
        }
    }
}
