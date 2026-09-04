using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        /// <summary>
        /// Stand up the run-scoped record of what the player has broken and worked.
        ///
        /// <para>MUST RUN BEFORE THE BUILDING LOADER. <c>BuildingLoader.SpawnAtCore</c> asks
        /// the <see cref="ServiceLocator"/> for this service as it spawns each building, and a
        /// service registered afterwards would be found by nothing — every felled tree would
        /// come back whole on load, silently, with no error anywhere to say why.</para>
        /// </summary>
        private void EnsureWorldDamageService()
        {
            if (ServiceLocator.TryGet<WorldDamageService>(out var existing) && existing != null)
                return;

            var repository = new JsonFileWorldDamageRepository(BuildRunSaveRoot());
            var service = new WorldDamageService(repository, WorldId.Base);

            int loaded = service.Load();
            ServiceLocator.Register<WorldDamageService>(service);

            var go = new GameObject("WorldDamageFlusher");
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            go.AddComponent<WorldDamageFlusher>().Bind(service);

            Debug.Log($"[GameplaySceneSetup] WorldDamageService ready — {loaded} damaged building(s) restored from this run.");
        }

        /// <summary>
        /// Where this run's own files live. Mirrors <c>BuildRunDropRepository</c> deliberately:
        /// both are run-scoped stores under the same folder, and the day <c>SaveService</c>
        /// surfaces a real run identifier they must both start using it in the same edit.
        /// </summary>
        private static string BuildRunSaveRoot()
        {
            return System.IO.Path.Combine(Application.persistentDataPath, "Saves", "default");
        }
    }
}
