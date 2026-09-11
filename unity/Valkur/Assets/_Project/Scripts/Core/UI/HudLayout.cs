namespace Valkur.Core.UI
{
    /// <summary>
    /// The screen real estate the always-on HUD widgets claim, in reference pixels
    /// against the 1600x800 canvas every HUD surface scales to.
    ///
    /// <para><b>Why this exists in Core.</b> The minimap lives in <c>Valkur.UI</c> and
    /// the quest log in <c>Valkur.Gameplay</c>, and <c>Valkur.Gameplay -> Valkur.UI</c>
    /// is a forbidden reference. So the two could not agree on where the top-right
    /// corner ends even in principle — and they did not: measured live at 1600x800,
    /// the minimap occupied x[1384..1576] y[540..776] at sortingOrder 105 while the
    /// quest log's panel sat at x[1152..1584] y[440..736] at sortingOrder 40. The
    /// minimap covered <b>192x196 px — 29.4 % of the log</b>, over its top-right
    /// corner, which is exactly where the quest names and their counters run.</para>
    ///
    /// <para><b>Why constants and not a runtime query.</b> Asking the live minimap for
    /// its rect would make the quest log's layout depend on the minimap having been
    /// built first, and both are created by different bootstraps in an order nothing
    /// pins. A declared band is the same answer on the first frame as on the
    /// thousandth, and it is readable from an EditMode test.</para>
    ///
    /// <para><b>The reference resolution is part of the contract.</b> These numbers
    /// only compose if every HUD canvas scales against the SAME reference — the quest
    /// log shipped on Unity's default 800x600 with <c>matchWidthOrHeight = 0</c>, which
    /// is a 2.0 scale factor at 1600 wide against the minimap's 1.0, so its text
    /// rendered at double size and the two corners drifted apart on every resize.
    /// <see cref="ReferenceWidth"/> / <see cref="ReferenceHeight"/> / <see cref="Match"/>
    /// are what a HUD canvas must use.</para>
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Reference width every HUD <c>CanvasScaler</c> uses.</summary>
        public const float ReferenceWidth = 1600f;

        /// <summary>Reference height every HUD <c>CanvasScaler</c> uses.</summary>
        public const float ReferenceHeight = 800f;

        /// <summary>
        /// <c>matchWidthOrHeight</c> every HUD canvas uses. 0.5 splits the difference,
        /// so a widget keeps its proportions on both a wider and a taller window —
        /// 0 (pure width) is what made the quest log's font explode.
        /// </summary>
        public const float Match = 0.5f;

        /// <summary>Gap between the screen edge and the top-right widget column.</summary>
        public const float ScreenMargin = 24f;

        /// <summary>Width of the top-right column: the minimap disc's diameter.</summary>
        public const float TopRightColumnWidth = 192f;

        /// <summary>
        /// Height the minimap claims: the disc, the gap under it and its info band.
        /// Kept here rather than in <c>MinimapHUD</c> so the widget BELOW it can be
        /// placed without reaching across an assembly boundary it may not cross.
        /// </summary>
        public const float MinimapBlockHeight = 192f + 4f + 40f;

        /// <summary>Breathing room between two stacked top-right widgets.</summary>
        public const float StackGap = 12f;

        /// <summary>
        /// Distance from the TOP of the screen to the first pixel a widget stacked
        /// under the minimap may use. Derived, so moving the minimap moves whatever
        /// is beneath it instead of quietly sliding underneath it.
        /// </summary>
        public const float BelowMinimapTop = ScreenMargin + MinimapBlockHeight + StackGap;

        /// <summary>
        /// Distance from the screen bottom that the top-right column must clear.
        ///
        /// <para>Set when the old music widget sat at <c>y[208..344]</c> at sortingOrder 150.
        /// The rebuilt music panel docks at <c>y = 104</c> and, with its resonance open, reaches
        /// 104 + 82 texels x 2 = 268 at the reference resolution — under this line, which
        /// <c>MusicPlayerHUDTests</c> pins. The reserve is a DEFAULT and not a guarantee — the
        /// panel can be dragged and its dock is persisted per machine — which is why the
        /// tracker below hugs its own content instead of claiming the whole band.</para>
        /// </summary>
        public const float BottomReserved = 356f;

        /// <summary>
        /// Sorting order of the music panel's canvas: over the player panel's HUD canvas (100)
        /// and the minimap (105), under the spell bar (150) and the tray (250) — so a panel the
        /// player dragged next to the spell bar slides behind it rather than over its keys.
        /// </summary>
        public const int MusicSortingOrder = 140;

        /// <summary>Width of the music panel at the reference resolution: MusicHudStyle.widthTexels (126) x the reference texel scale (2). Equal to the HUD tray's width (3 x 80 + 2 x 6) on purpose — the two read as one column. MusicPlayerHUDTests pins the style against it.</summary>
        public const float MusicPanelWidth = 252f;

        /// <summary>
        /// Distance from the RIGHT screen edge that a game window (the inventory, and the next
        /// one) opens to the left of. It clears the widest instrument docked on the right — the
        /// music panel (<see cref="MusicPanelWidth"/>; the minimap column is 192) — plus the gap. A
        /// window may be DRAGGED over an instrument; it must not OPEN over one
        /// (HUD_VISUAL_LANGUAGE.md, R13): the old inventory opened at (-16, -16) and hid the whole
        /// minimap, and its first rebuild still covered the music panel's medallion.
        /// </summary>
        public const float GameWindowRightInset = ScreenMargin + MusicPanelWidth + StackGap;
    }
}
