using UnityEngine;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// The live switches of the world's LOOK layers — cloud shadows, sun shadows, wind sway,
    /// the player's lantern, the night's fireflies — in one place, so the <c>look</c> console
    /// command and the Time &amp; Weather editor flip the same bools the effects read.
    ///
    /// Statics in Core for the same reason <see cref="ScreenGradeSettings"/> is: the readers
    /// are Gameplay components and the renderer half is Core, and Core may not reference
    /// Gameplay. Every switch defaults ON — a layer that ships off is a layer nobody sees —
    /// and every one is reset on Play, because Domain Reload is off and a switch flipped to
    /// measure something in one session would otherwise stay flipped in the next.
    ///
    /// These are session switches, not authored data: the tuning each layer reads lives in its
    /// own style asset under <c>Resources/</c>, where the Inspector can reach it.
    /// </summary>
    public static class WorldLookSettings
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CloudShadows = true;
            SunShadows   = true;
            WindSway     = true;
            Fireflies    = true;
            Footsteps    = true;
        }

        /// <summary>Drifting cloud shadows over the world, by day, outdoors.</summary>
        public static bool CloudShadows { get; set; } = true;

        /// <summary>Projected sun shadows and the ground blob under every entity and building.</summary>
        public static bool SunShadows { get; set; } = true;

        /// <summary>Tree canopies and bushes swaying with the weather wind.</summary>
        public static bool WindSway { get; set; } = true;

        /// <summary>Fireflies over the night's grass and water.</summary>
        public static bool Fireflies { get; set; } = true;

        /// <summary>The dust a step kicks up.</summary>
        public static bool Footsteps { get; set; } = true;
    }
}
