using UnityEngine;

namespace Valkur.Data.Feel
{
    /// <summary>
    /// The tuning record for one camera beat.
    ///
    /// Nine numbers, because a camera reaction has more than one axis and collapsing them
    /// into "amplitude and duration" is what made every effect in the old system feel the
    /// same. Dealing damage and taking damage differ here in frequency, damping, direction
    /// and whether the frame stops anticipating — not in how hard they shake.
    ///
    /// Public fields with tooltips, matching the <c>SpellDefinition</c> precedent for
    /// serializable data records.
    /// </summary>
    [System.Serializable]
    public struct FeelCue
    {
        [Tooltip("Trauma added when this cue fires, before damage and combo scaling. " +
                 "Trauma is additive and clamped to 1; shake amplitude is trauma squared.")]
        public float traumaAdd;

        [Tooltip("Trauma units shed per second while this cue is the slowest one active. " +
                 "Lower means a longer tail — a boss phase rumbles, a sword hit snaps.")]
        public float traumaDecayPerSecond;

        [Tooltip("Shake frequency in Hz. High reads as sharp and metallic, low as heavy and " +
                 "physical. This, not amplitude, is what separates a sword from a meteor.")]
        public float shakeFrequencyHz;

        [Tooltip("Peak displacement of the directional kick, in world units. Zero means the " +
                 "cue is omnidirectional — a swell rather than a punch.")]
        public float kickAmplitudeWu;

        [Tooltip("Kick spring frequency in rad/s. Higher returns to rest faster.")]
        public float kickOmega;

        [Tooltip("Kick damping ratio. 1 is critical (no overshoot, reads as an impact you " +
                 "delivered); below 1 overshoots once (reads as an impact you absorbed).")]
        public float kickZeta;

        [Tooltip("Seconds the camera stops leading after this cue. The frame stopping its " +
                 "anticipation is how a hit reads as interrupting you.")]
        public float leadFreezeSeconds;

        [Tooltip("Global time freeze in real seconds. Only the player's own actions should " +
                 "ever set this above zero.")]
        public float hitStopSeconds;

        [Tooltip("Minimum real seconds between two firings of this cue. Beams and cones " +
                 "report a hit per tick per victim; without this they pin the shake at full.")]
        public float minIntervalSeconds;
    }
}
