namespace Valkur.Data.Feel
{
    /// <summary>
    /// Every global number on <see cref="CameraFeelProfile"/> that a designer may move,
    /// addressable by id.
    ///
    /// The Camera Editor drives its whole UI off this enum rather than off twenty-five
    /// hand-written slider rows, so adding a tunable is one enum member plus one line in
    /// each of three switches — and <c>CameraFeelTunableTests</c> sweeps the enum, so a
    /// member added without its switch cases fails immediately instead of shipping as a
    /// slider that silently does nothing.
    ///
    /// Per-cue numbers are NOT here: they live in <see cref="FeelCue"/> and are edited as a
    /// group once a cue is selected.
    /// </summary>
    public enum CameraFeelTunable
    {
        FollowOmega,
        MaxFollowLagWu,
        FollowSettlePixels,

        MoveLeadWu,
        AimLeadIdleWu,
        AimLeadMovingWu,
        MaxLeadWu,
        AimDeadzoneWu,
        LeadOmega,
        LeadOmegaHeavy,
        LeadDeadzonePixels,
        SpiritLeadScale,

        MaxShakeWu,
        DefaultTraumaDecay,
        MaxTraumaPerSecond,

        MasterIntensity01,
        TeleportThresholdWu,
        MaxStepSeconds,

        HeavyPrepareSeconds,
        HeavyCooldownSeconds,
        WhiffWindowSeconds,
        DamageReference,
        ComboGain,
        SevereDamageFraction,
    }

    /// <summary>Which panel a tunable belongs to in the Camera Editor.</summary>
    public enum CameraFeelGroup
    {
        Follow,
        Lead,
        Shake,
        Global,
        Classification,
    }

    /// <summary>Label, range and grouping for one tunable.</summary>
    public readonly struct CameraFeelTunableInfo
    {
        public readonly CameraFeelTunable Id;
        public readonly CameraFeelGroup Group;
        public readonly string Label;
        public readonly float Min;
        public readonly float Max;
        public readonly string Suffix;
        public readonly string Help;

        public CameraFeelTunableInfo(CameraFeelTunable id, CameraFeelGroup group, string label,
                                     float min, float max, string suffix, string help)
        {
            Id = id; Group = group; Label = label;
            Min = min; Max = max; Suffix = suffix; Help = help;
        }
    }
}
