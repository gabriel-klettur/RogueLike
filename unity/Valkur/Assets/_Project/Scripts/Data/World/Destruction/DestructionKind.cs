namespace Valkur.Data
{
    /// <summary>
    /// What a building DOES when its durability runs out — the family its death
    /// sequence belongs to.
    ///
    /// <para>Dispatching a look off one enum is the shape this project already uses
    /// twice: <c>SlashProfile</c> maps an arc angle to one of four families, and
    /// <c>CastFlourishProfile</c> maps a spell type to one of nine gestures. Each family
    /// then sizes itself off the profile's own data, so two trees of different heights
    /// fall for different lengths of time without either being authored frame by
    /// frame.</para>
    /// </summary>
    public enum DestructionKind
    {
        /// <summary>
        /// Topples about its base and lies down. Trees, poles, statues, masts — anything
        /// tall enough that its own height is the story.
        /// </summary>
        Fell = 0,

        /// <summary>Bursts outward in place. Crates, barrels, pots, windows.</summary>
        Shatter = 1,

        /// <summary>Sinks and crumbles where it stands. Walls, rubble piles.</summary>
        Crumble = 2,

        /// <summary>
        /// Falls in on itself over a longer beat, with debris and dust. Houses and other
        /// structures large enough that a single frame of collapse reads as a pop.
        /// </summary>
        Collapse = 3,
    }
}
