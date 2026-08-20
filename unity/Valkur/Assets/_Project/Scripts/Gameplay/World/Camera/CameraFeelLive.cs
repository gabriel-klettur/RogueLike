using UnityEngine;

namespace Valkur.Gameplay.Feel
{
    /// <summary>
    /// A snapshot of the camera solver, taken for display.
    ///
    /// Deliberately a value type with no references back into the director: the Camera Editor
    /// reads it every frame and must not be able to write anything through it.
    /// </summary>
    internal struct CameraFeelLive
    {
        public float Trauma;
        public float TraumaDecay;
        public float ShakeFrequencyHz;
        public Vector2 Lead;
        public Vector2 Kick;
        public Vector2 Applied;
        public Vector2 FollowLag;
        public float LeadFreezeRemaining;
        public float TraumaSpentThisSecond;
        public float WorldUnitsPerPixel;
        public bool ProxyIsFollowTarget;
        public bool Suppressed;

        /// <summary>Applied offset in screen pixels — the unit the pixel snap works in.</summary>
        public float AppliedPixels => WorldUnitsPerPixel > 0f
            ? Applied.magnitude / WorldUnitsPerPixel
            : 0f;
    }
}
