using UnityEngine;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        /// <summary>
        /// The live Seed World streamer, in EVERY build and outside the editors block: a player
        /// who starts a run on a generated world has no Seed World editor, and without the streamer
        /// that world is a map whose every zone is blank. Idempotent, and on a top-level object so
        /// a world wipe cannot take it with the tiles.
        /// </summary>
        private void EnsureSeedWorldLiveStreamer()
        {
            if (FindObjectOfType<SeedWorldLiveStreamer>() != null) return;
            var go = new GameObject("[SeedWorldLiveStreamer]");
            go.AddComponent<SeedWorldLiveStreamer>();
        }
    }
}
