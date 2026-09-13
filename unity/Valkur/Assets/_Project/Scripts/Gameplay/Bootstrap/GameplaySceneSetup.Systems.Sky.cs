using UnityEngine;
using Valkur.Gameplay.World.Sky;

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        /// <summary>
        /// The sky: cloud shadows over the world and the sun every shadow reads. One object,
        /// created after the weather it listens to and before any building or entity spawns,
        /// so the first frame a caster syncs already has a sun to read.
        /// </summary>
        private void EnsureSkyLayer()
        {
            if (FindObjectOfType<CloudShadowLayer>() != null) return;
            var go = new GameObject("CloudShadowLayer");
            go.AddComponent<CloudShadowLayer>();
            go.transform.SetParent(GetSceneContainer("[VFX]"), false);
        }
    }
}
