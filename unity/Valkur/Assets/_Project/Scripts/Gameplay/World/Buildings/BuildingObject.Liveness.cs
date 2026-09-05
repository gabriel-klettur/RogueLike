using UnityEngine;

namespace Valkur.Gameplay.World
{
    public partial class BuildingObject
    {
        /// <summary>
        /// Bumps whenever a building appears or disappears — spawned by the loader, placed
        /// or deleted in the editor, torn down by a world swap, disabled by culling.
        ///
        /// It exists so a cache of "every building in the scene" can tell whether it is
        /// stale without anyone having to remember to invalidate it. The Buildings editor
        /// used to answer that by calling <c>FindObjectsOfType</c> on every frame the cursor
        /// was over the world: measured at 7.8 ms a frame with 302 buildings, a quarter of
        /// the frame budget, for a set that changes a few times a session. The alternative —
        /// an <c>InvalidateBuildingCache()</c> call at every site that can change the set —
        /// was already shipped and already incomplete (paint and erase had it, place and
        /// the loader did not).
        /// </summary>
        public static int LiveGeneration => s_liveGeneration;

        private static int s_liveGeneration;

        private void OnEnable()  => s_liveGeneration++;
        private void OnDisable() => s_liveGeneration++;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLiveGeneration()
        {
            s_liveGeneration = 0;
        }
    }
}
