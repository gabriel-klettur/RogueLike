using UnityEngine;
using Valkur.Gameplay.Entities;

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        /// <summary>
        /// Stand up the owner of hand-placed entities and spawn the active map slot's placements.
        /// Runs in every build — see the comment on its step in the sequence.
        /// </summary>
        private void EnsurePlacedEntityService()
        {
            var service = PlacedEntityService.GetOrCreate();
            if (service.transform.parent == null)
                service.transform.SetParent(GetSceneContainer("[Spawning]"), false);

            service.SetMonsterCatalog(_monsterCatalog);
            service.Load();
        }
    }
}
