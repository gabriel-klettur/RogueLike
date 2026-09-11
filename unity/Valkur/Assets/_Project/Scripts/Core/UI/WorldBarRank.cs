namespace Valkur.Core.UI
{
    /// <summary>
    /// What the frame around an entity's bar is saying about the entity.
    ///
    /// <para>Lives in Core because both the tuning asset (<c>Valkur.Data</c>) and the rig that
    /// draws it (<c>Valkur.Gameplay</c>) need it, and Data may not reference Gameplay — the same
    /// constraint <c>LoadoutStateSheets.state</c> answers by being a string.</para>
    ///
    /// <para>APPENDED, never renumbered: the value is serialised as an integer inside
    /// <c>WorldBarStyle</c>'s per-rank colour array, so inserting in the middle repoints every
    /// authored colour at the wrong rank without touching a file.</para>
    /// </summary>
    public enum WorldBarRank
    {
        /// <summary>An ordinary creature. Dark frame, nothing said.</summary>
        Normal = 0,

        /// <summary>Fights for the player. Read from <c>AlliedUnit</c>, never from a faction string.</summary>
        Ally = 1,

        /// <summary>Tougher than its kind. Brighter frame.</summary>
        Elite = 2,

        /// <summary>Carries a <c>BossDefinition</c>. Gold frame — the only rank that also earns a screen bar.</summary>
        Boss = 3,

        /// <summary>The player's own bar.</summary>
        Player = 4,
    }
}
