namespace Valkur.Data.Feel
{
    /// <summary>
    /// Whole-camera starting points.
    ///
    /// Twenty-four sliders is a lot of surface to explore from a standing start, and the
    /// interesting question is usually not "what is followOmega" but "does this game want a
    /// welded camera or a floating one". A preset answers that in one click and leaves every
    /// slider free afterwards.
    /// </summary>
    public enum CameraFeelPreset
    {
        /// <summary>The shipped tuning.</summary>
        Default,

        /// <summary>
        /// How the camera behaved before any of this existed: welded to the player, no lead,
        /// no smoothing. The comparison baseline — if the new one does not beat this in the
        /// hand, it is not worth its complexity.
        /// </summary>
        Rigid,

        /// <summary>Looser and further ahead. More cinematic, less precise for combat.</summary>
        Cinematic,

        /// <summary>Barely-there movement: smoothing only, almost no lead.</summary>
        Subtle,

        /// <summary>
        /// Follow and lead only, every transient off. The fastest way to judge the movement
        /// on its own without shake and kick arguing with it.
        /// </summary>
        MovementOnly,
    }
}
