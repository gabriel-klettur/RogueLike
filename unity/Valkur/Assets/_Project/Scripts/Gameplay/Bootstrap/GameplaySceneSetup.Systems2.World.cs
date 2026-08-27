using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Spawners;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        /// <summary>
        /// Build-safe half of the F3 catalog wiring — same pattern and same rationale as
        /// <c>GameplaySceneSetup.RegisterMonsterCatalogFallback</c>
        /// (<c>GameplaySceneSetup.Systems2.Editors.cs</c>). Split into its own method (no
        /// GameObject/editor creation) so it is cheap and safe to call from a test.
        /// TODO(<c>Gameplay/Editors/Spawners</c>, currently under concurrent edit): add
        ///   internal void SetCatalog(SpawnerTemplateCatalog catalog) { if (catalog !=
        ///   null) _catalog = catalog; }
        /// called unconditionally from <see cref="EnsureSpawnerEditor"/>, with an
        /// OnEnable/Awake fallback to ServiceLocator.TryGet&lt;SpawnerTemplateCatalog&gt;
        /// when the field is still null.
        /// </summary>
        private void RegisterSpawnerTemplateCatalogFallback()
        {
            if (_spawnerTemplateCatalog != null)
                ServiceLocator.Register<SpawnerTemplateCatalog>(_spawnerTemplateCatalog);
        }

        private void EnsureSpawnerEditor()
        {
            RegisterSpawnerTemplateCatalogFallback();

            if (SpawnerEditorManager.Instance != null) return;

            var go = new GameObject("SpawnerEditorManager");
            var mgr = go.AddComponent<SpawnerEditorManager>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            // Plain setter, called unconditionally. This was a SerializedObject write
            // inside #if UNITY_EDITOR while the manager itself is created in every build,
            // so a shipped player's F3 picker reported "No catalog assigned."
            mgr.SetCatalog(_spawnerTemplateCatalog);

            Debug.Log("[GameplaySceneSetup] SpawnerEditorManager created. Press F3 to toggle.");
        }

        private void EnsureMonsterSpawner()
        {
            if (FindObjectOfType<MonsterSpawner>() != null) return;

            var go = new GameObject("MonsterSpawner");
            var spawner = go.AddComponent<MonsterSpawner>();
            go.transform.SetParent(GetSceneContainer("[Spawning]"), false);

            if (monsterPrefab != null)
                spawner.Initialize(monsterPrefab, _monsterCatalog);
            else
                Debug.LogWarning("[GameplaySceneSetup] monsterPrefab is NULL — MonsterSpawner not initialized!");

            Debug.Log("[GameplaySceneSetup] MonsterSpawner created.");
        }

        private void EnsureBuildingLoader()
        {
            // Synchronous wrapper — drains the progressive coroutine. Code paths
            // that don't have a coroutine context (tests, scene-bootstrap edge
            // cases) still get the same end state without yielding.
            var iter = EnsureBuildingLoaderProgressively();
            while (iter.MoveNext()) { }
        }

        /// <summary>
        /// Progressive building load — yields between sub-stages so the loading
        /// screen advances ("Parsing building data" → "Spawning building
        /// instances" → "Linking building colliders") instead of freezing on
        /// a single "Loading buildings" stage.
        /// </summary>
        private System.Collections.IEnumerator EnsureBuildingLoaderProgressively()
        {
            var existing = FindObjectOfType<World.BuildingLoader>();
            if (existing != null)
            {
                if (existing.SpawnedBuildings.Count == 0)
                {
                    yield return existing.LoadBuildingsProgressively(stage => Report(stage));
                }
                yield break;
            }

            if (_buildingCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No BuildingCatalog assigned — buildings skipped.");
                yield break;
            }

            var zm = FindObjectOfType<World.ZoneManager>();

            var go = new GameObject("BuildingLoader");
            var loader = go.AddComponent<World.BuildingLoader>();
            go.transform.SetParent(GetSceneContainer("[World]"), false);
            loader.Initialize(_buildingCatalog, zm);
            yield return loader.LoadBuildingsProgressively(stage => Report(stage));

            Debug.Log("[GameplaySceneSetup] BuildingLoader created and loaded.");
        }

        /// <summary>
        /// Bake all CompositeCollider2D geometry after tiles are painted at runtime.
        /// WorldLoader.SetTile() invalidates the composite geometry — without this call
        /// the Collision tilemap's CompositeCollider2D has pathCount=0 and blocks nothing.
        ///
        /// CRITICAL race fix: <see cref="UnityEngine.Tilemaps.TilemapCollider2D"/> processes
        /// queued <c>SetTile</c> changes deferred. Calling <c>GenerateGeometry()</c> on the
        /// same frame as the <c>SetTile</c> calls (which is what <see cref="Start"/> does:
        /// LoadWorld → RebakeTilemapColliders, no yield) yields a composite with
        /// <c>pathCount = 0</c> and the player walks through walls. Calling
        /// <c>RefreshAllTiles()</c> flushes the pending changes synchronously so the
        /// immediate bake sees them. We additionally schedule a deferred re-bake on the
        /// next frame to catch anything (BuildingLoader, override loaders) that paints
        /// after this method returns.
        ///
        /// Regression: <c>PlayerTileCollisionPlayTests.SameFrame_PaintThenBake_*</c>.
        /// </summary>
        private void RebakeTilemapColliders()
        {
            int baked = 0;
            int refreshed = 0;
            foreach (var cc in FindObjectsOfType<UnityEngine.CompositeCollider2D>())
            {
                // Flush pending SetTile changes to the TilemapCollider2D before bake.
                var tilemap = cc.GetComponent<UnityEngine.Tilemaps.Tilemap>();
                if (tilemap != null)
                {
                    tilemap.RefreshAllTiles();
                    refreshed++;
                }
                cc.GenerateGeometry();
                baked++;
            }
            Debug.Log($"[GameplaySceneSetup] Rebaked {baked} CompositeCollider2D(s) (refreshed {refreshed} tilemap(s)).");

            // Safety-net re-bake one frame later — catches tiles painted by loaders
            // that run after Start() (BuildingLoader, override appliers, etc.).
            // Diagnostic report is deferred to that pass so it reflects the final
            // baked state (GenerateGeometry results are only visible after a physics step).
            StartCoroutine(DeferredRebakeNextFrame());
        }

        private System.Collections.IEnumerator DeferredRebakeNextFrame()
        {
            yield return null; // Wait one frame so post-Start loaders complete their SetTile bursts.
            int baked = 0;
            foreach (var cc in FindObjectsOfType<UnityEngine.CompositeCollider2D>())
            {
                var tilemap = cc.GetComponent<UnityEngine.Tilemaps.Tilemap>();
                if (tilemap != null) tilemap.RefreshAllTiles();
                cc.GenerateGeometry();
                baked++;
            }
            Debug.Log($"[GameplaySceneSetup] Deferred re-bake completed for {baked} CompositeCollider2D(s).");
            World.TileCollisionDiagnostics.Report();
        }

        private void EnsureSpawnerInstanceLoader()
        {
            if (FindObjectOfType<Spawners.SpawnerInstanceLoader>() != null) return;

            if (_spawnerTemplateCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No SpawnerTemplateCatalog assigned — spawner instances skipped.");
                return;
            }

            var monsterSpawner = FindObjectOfType<MonsterSpawner>();

            var go = new GameObject("SpawnerInstanceLoader");
            var loader = go.AddComponent<Spawners.SpawnerInstanceLoader>();
            go.transform.SetParent(GetSceneContainer("[Spawning]"), false);
            loader.Initialize(_spawnerTemplateCatalog, monsterSpawner);
            loader.LoadInstances();

            Debug.Log("[GameplaySceneSetup] SpawnerInstanceLoader created and loaded.");
        }
    }
}
