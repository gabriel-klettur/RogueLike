namespace Valkur.Data.Feel
{
    /// <summary>
    /// The named camera beats. A call site names the moment; the amplitudes live in
    /// <see cref="CameraFeelProfile"/>.
    ///
    /// This is what replaces nine hardcoded <c>CameraShake.Trigger(0.40f, 0.26f)</c> literals
    /// scattered across the VFX layer. Tuning the feel of the game should not require finding
    /// and editing nine unrelated files, and two effects that are meant to hit equally hard
    /// should be unable to drift apart.
    /// </summary>
    public enum CameraFeelCue
    {
        /// <summary>A hit the player landed.</summary>
        AttackConnect,

        /// <summary>A player melee swing that connected with nothing.</summary>
        AttackWhiff,

        /// <summary>The player took damage.</summary>
        Hurt,

        /// <summary>A heavy spell executed.</summary>
        CastHeavy,

        /// <summary>A heavy spell started winding up. Lead freeze only — the frame goes still.</summary>
        CastPrepare,

        /// <summary>Dash departure.</summary>
        DashLaunch,

        /// <summary>Dash arrival.</summary>
        DashLand,

        /// <summary>World or VFX impact, small.</summary>
        ImpactLight,

        /// <summary>World or VFX impact, medium.</summary>
        ImpactMedium,

        /// <summary>World or VFX impact, heavy.</summary>
        ImpactHeavy,

        /// <summary>World or VFX impact, meteor and mine scale.</summary>
        ImpactMassive,

        /// <summary>The player died.</summary>
        Death,

        /// <summary>A boss crossed into a new phase.</summary>
        BossPhase,

        /// <summary>The player levelled up.</summary>
        LevelUp,

        /// <summary>A long combo ended.</summary>
        ComboPayoff,
    }
}
