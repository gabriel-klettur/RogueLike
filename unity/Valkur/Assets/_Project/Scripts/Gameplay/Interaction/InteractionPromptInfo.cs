namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// Whether the interact key would do anything here, and how the prompt should say so.
    /// </summary>
    public enum InteractionAvailability
    {
        /// <summary>
        /// Nothing to offer. The prompt is not drawn and the target is not even considered —
        /// a felled tree's stump, a building whose profile was never harvestable.
        /// </summary>
        Hidden = 0,

        /// <summary>The key works. This is the only state that draws a key cap.</summary>
        Ready = 1,

        /// <summary>
        /// There IS something here and the player cannot have it yet: a seam still refilling,
        /// a material their tools cannot touch at all. Drawn dimmed, with no key cap, and the
        /// detail line carries the reason.
        ///
        /// <para>Showing this rather than hiding the prompt is the whole point. A worked-out
        /// mine that displays nothing is indistinguishable from a decorative rock, so the
        /// player either concludes the feature is broken or keeps walking into it hoping.
        /// Saying "agotada, vuelve en 2:41" answers the question they actually have.</para>
        /// </summary>
        Blocked = 2,

        /// <summary>Being worked right now. The key stops it rather than starting it.</summary>
        Busy = 3,
    }

    /// <summary>
    /// Everything the floating prompt needs to draw itself for one target, resolved fresh
    /// every frame.
    ///
    /// <para>Asked per frame rather than cached because every field of it changes with state:
    /// the same seam reads "Extraer mineral" with charges, "Extrayendo…" while it is being
    /// worked, and "Agotada · 2:41" when it is spent — and that countdown has to keep
    /// counting. A cached string would go on inviting the player to work an empty node.</para>
    /// </summary>
    public readonly struct InteractionPromptInfo
    {
        /// <summary>How the prompt is drawn, and whether it is drawn at all.</summary>
        public readonly InteractionAvailability Availability;

        /// <summary>
        /// What pressing the key does, as a short verb phrase the player reads first —
        /// "Extraer mineral", "Talar", "Hablar".
        /// </summary>
        public readonly string Verb;

        /// <summary>
        /// The second line, smaller and dimmer. Carries whatever the player needs that the
        /// verb cannot say: why they are blocked, how long until they can return, or a warning
        /// that they CAN act but it will be miserable. Empty draws no second line at all
        /// rather than an empty one, so the badge stays the size of its content.
        /// </summary>
        public readonly string Detail;

        public InteractionPromptInfo(InteractionAvailability availability, string verb,
            string detail = null)
        {
            Availability = availability;
            Verb = verb;
            Detail = detail;
        }

        /// <summary>Nothing to draw.</summary>
        public static InteractionPromptInfo None =>
            new InteractionPromptInfo(InteractionAvailability.Hidden, string.Empty);

        /// <summary>Whether this target should appear in the prompt at all.</summary>
        public bool IsVisible => Availability != InteractionAvailability.Hidden;

        /// <summary>Whether the key press would be accepted.</summary>
        public bool IsActionable =>
            Availability == InteractionAvailability.Ready ||
            Availability == InteractionAvailability.Busy;
    }
}
