using UnityEngine;

namespace Valkur.Gameplay.Feel
{
    /// <summary>
    /// The camera solver's entire mutable state, as plain values.
    ///
    /// Kept in one struct rather than as fields on the director so a test can snapshot it,
    /// step it and assert on it without a scene — and so "reset everything" is one
    /// assignment rather than a list somebody will forget to extend.
    /// </summary>
    internal struct CameraFeelState
    {
        /// <summary>Where the camera is actually looking, before lead. Springs after the player.</summary>
        public Vector2 Follow;
        public Vector2 FollowVelocity;
        public bool FollowInitialised;

        public float Trauma;

        /// <summary>Decay rate of the cue that owns the current trauma. Back to the profile default at zero.</summary>
        public float TraumaDecay;
        public float ShakeFrequencyHz;

        /// <summary>Advances on unscaled time so the shake keeps its cadence through hit-stop.</summary>
        public float NoiseTime;

        public Vector2 Kick;
        public Vector2 KickVelocity;
        public float KickOmega;
        public float KickZeta;

        public Vector2 Lead;
        public Vector2 LeadVelocity;
        public float LeadFreezeRemaining;
        public Vector2 LeadOverride;
        public float LeadOverrideRemaining;

        /// <summary>1 normally, lower while the player is a spirit.</summary>
        public float LeadScale;

        public float SeedX;
        public float SeedY;

        /// <summary>Rolling trauma budget, so a beam cannot pin the shake at full.</summary>
        public float TraumaSpentThisSecond;
        public float TraumaBudgetResetAt;

        public static CameraFeelState Create(float seedX, float seedY, float defaultDecay)
            => new CameraFeelState
            {
                LeadScale = 1f,
                TraumaDecay = defaultDecay,
                ShakeFrequencyHz = 20f,
                KickOmega = 24f,
                KickZeta = 1f,
                SeedX = seedX,
                SeedY = seedY,
            };

        /// <summary>
        /// Drops every transient without touching the seeds or the follow anchor. Used on a
        /// teleport, on entering an editor, and on death — anywhere continuing to animate the
        /// previous frame's motion would be wrong rather than merely ugly.
        /// </summary>
        public void ClearTransients(float defaultDecay)
        {
            Trauma = 0f;
            TraumaDecay = defaultDecay;
            Kick = Vector2.zero;
            KickVelocity = Vector2.zero;
            Lead = Vector2.zero;
            LeadVelocity = Vector2.zero;
            LeadFreezeRemaining = 0f;
            LeadOverride = Vector2.zero;
            LeadOverrideRemaining = 0f;
        }
    }
}
