using UnityEngine;
using Valkur.Data.Feel;
using Valkur.Gameplay.Enemies;

namespace Valkur.Gameplay.Feel
{
    /// <summary>
    /// What the rest of the game calls.
    ///
    /// A static facade with no state of its own, so a VFX class does not have to know
    /// whether a camera director exists, and every entry point is a silent no-op when one
    /// does not — which is the normal case in EditMode tests, during boot, and in the
    /// spell-preview scene.
    ///
    /// Every method opens with <c>HasInstance</c> rather than <c>Instance?.</c>:
    /// <c>SingletonMonoBehaviour</c> returns the raw backing field with no Unity-null
    /// coercion, so with Domain Reload off a destroyed director survives the C# null check
    /// and throws on use.
    /// </summary>
    public static class CameraFeel
    {
        /// <summary>
        /// Fire one authored beat. <paramref name="direction"/> is where the blow pushes the
        /// frame; leave it zero for something that has no direction.
        /// </summary>
        public static void Cue(CameraFeelCue cue, Vector2 direction = default,
                               float intensity01 = 1f)
        {
            if (!CameraFeelDirector.HasInstance) return;
            CameraFeelDirector.Instance.FireCue(cue, direction, intensity01);
        }

        public static void Dash(Vector2 direction, float distanceWu, float moveDuration)
        {
            if (!CameraFeelDirector.HasInstance) return;
            CameraFeelDirector.Instance.FireDash(direction, distanceWu, moveDuration);
        }

        public static void Freeze(float realSeconds)
        {
            if (!CameraFeelDirector.HasInstance) return;
            CameraFeelDirector.Instance.FireFreeze(realSeconds);
        }

        /// <summary>Bosses self-register, exactly as they already do with the boss health bar.</summary>
        public static void RegisterBoss(BossPhaseController boss)
            => CameraFeelDirector.TrackBoss(boss);

        public static void UnregisterBoss(BossPhaseController boss)
            => CameraFeelDirector.UntrackBoss(boss);
    }
}
