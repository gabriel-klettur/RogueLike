namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The eight status silhouettes, authored as 6x6 text.
    ///
    /// <para><b>Why they exist at all.</b> Burn, Poison, Stun, Freeze, Slow, Root, Vulnerable and
    /// Marked are all applied by shipped spells and monsters, and until this row was built not one
    /// of them was visible anywhere in the game. The only trace was the body tint, which
    /// <c>SpriteTintStack</c> multiplies together with the hit flash, the death fade and the
    /// transporter effect — so "that thing is poisoned" and "that thing was just hit" arrived on
    /// the same channel and cancelled each other out.</para>
    ///
    /// <para><b>Why silhouettes rather than colours.</b> Six of the eight tints sit in the same
    /// half of the wheel, and a 6x6 patch of colour is the least legible thing a screen can show.
    /// Shape carries the meaning and the tint only confirms it, which is also what keeps the row
    /// readable for a colour-blind player.</para>
    ///
    /// <para>Ordered by <c>StatusEffectKind</c>'s integer value. A kind with no drawing gets no
    /// icon rather than a placeholder: an absent icon is honest, a wrong one is not.</para>
    /// </summary>
    internal static class StatusGlyphs
    {
        // NOT `static readonly`. Domain Reload is off and the ratchet in
        // DomainReloadStaticResetTests reads the reset hook's raw IL for a `stsfld` on the field
        // itself — which a readonly field cannot legally receive outside its static constructor.
        // A readonly array would therefore be an unresettable mutable static, which is exactly
        // the shape CLAUDE.md records three of this project's scratch buffers falling into.
        private static string[][] s_glyphs = BuildTable();

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatusGlyphStatics() => s_glyphs = BuildTable();

        private static string[][] BuildTable() => new[]
        {
            // 0 Burn - a flame: narrow at the top, heavy at the base.
            new[]
            {
                "  #   ",
                " ##   ",
                " ###  ",
                "##### ",
                "##### ",
                " ###  ",
            },
            // 1 Poison - a falling drop with a second drop above it. The gap is what stops it
            // reading as the flame at this size.
            new[]
            {
                "  #   ",
                "      ",
                " ###  ",
                "##### ",
                " ###  ",
                "  #   ",
            },
            // 2 Stun - a swirl. Open on one side, which no other glyph here is.
            new[]
            {
                " ###  ",
                "#   # ",
                "# ##  ",
                "# #   ",
                "#  #  ",
                " ##   ",
            },
            // 3 Freeze - a snowflake: the only radially symmetric glyph.
            new[]
            {
                "# # # ",
                " ###  ",
                "######",
                " ###  ",
                "# # # ",
                "      ",
            },
            // 4 Slow - two chevrons pointing down.
            new[]
            {
                "#    #",
                " #  # ",
                "  ##  ",
                "#    #",
                " #  # ",
                "  ##  ",
            },
            // 5 Root - a stake with roots spreading under it.
            new[]
            {
                "  ##  ",
                "  ##  ",
                "######",
                " #  # ",
                "#    #",
                "#    #",
            },
            // 6 Vulnerable - a crack. The only diagonal.
            new[]
            {
                "   ## ",
                "  ##  ",
                " ###  ",
                "  ##  ",
                " ##   ",
                " #    ",
            },
            // 7 Marked - a skull. Two eye holes are the whole silhouette.
            new[]
            {
                " #### ",
                "######",
                "# ## #",
                "######",
                " #### ",
                " #  # ",
            },
        };

        /// <summary>How many kinds have a drawing.</summary>
        public static int Count => s_glyphs.Length;

        /// <summary>The glyph for a kind index, or null when that kind has none.</summary>
        public static string[] Get(int kindIndex)
        {
            if (kindIndex < 0 || kindIndex >= s_glyphs.Length) return null;
            return s_glyphs[kindIndex];
        }
    }
}
