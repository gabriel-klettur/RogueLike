using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.NPC;
using Valkur.Infrastructure;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {

        private void EnsureSpawnerEditor()
        {
            if (SpawnerEditorManager.Instance != null) return;

            var go = new GameObject("SpawnerEditorManager");
            var mgr = go.AddComponent<SpawnerEditorManager>();

            if (_spawnerTemplateCatalog != null)
            {
                // Set catalog via serialized field
                var so = new UnityEngine.Object[] { mgr };
#if UNITY_EDITOR
                var serialized = new UnityEditor.SerializedObject(mgr);
                var catalogProp = serialized.FindProperty("_catalog");
                if (catalogProp != null)
                {
                    catalogProp.objectReferenceValue = _spawnerTemplateCatalog;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
#endif
            }

            Debug.Log("[GameplaySceneSetup] SpawnerEditorManager created. Press F3 to toggle.");
        }

        private void EnsureMonsterSpawner()
        {
            if (FindObjectOfType<MonsterSpawner>() != null) return;

            var go = new GameObject("MonsterSpawner");
            var spawner = go.AddComponent<MonsterSpawner>();

            if (monsterPrefab != null)
                spawner.Initialize(monsterPrefab, _monsterCatalog);
            else
                Debug.LogWarning("[GameplaySceneSetup] monsterPrefab is NULL — MonsterSpawner not initialized!");

            Debug.Log("[GameplaySceneSetup] MonsterSpawner created.");
        }

        private void EnsureBuildingLoader()
        {
            var existing = FindObjectOfType<World.BuildingLoader>();
            if (existing != null)
            {
                // The scene-placed loader may have autoLoad=false — ensure buildings are
                // loaded now if they haven't been yet (e.g. first play after scene open).
                if (existing.SpawnedBuildings.Count == 0)
                    existing.LoadBuildings();
                return;
            }

            if (_buildingCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No BuildingCatalog assigned — buildings skipped.");
                return;
            }

            var zm = FindObjectOfType<World.ZoneManager>();

            var go = new GameObject("BuildingLoader");
            var loader = go.AddComponent<World.BuildingLoader>();
            loader.Initialize(_buildingCatalog, zm);
            loader.LoadBuildings();

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
            World.TileCollisionDiagnostics.Report();

            // Safety-net re-bake one frame later — catches tiles painted by loaders
            // that run after Start() (BuildingLoader, override appliers, etc.).
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
            loader.Initialize(_spawnerTemplateCatalog, monsterSpawner);
            loader.LoadInstances();

            Debug.Log("[GameplaySceneSetup] SpawnerInstanceLoader created and loaded.");
        }

        private void EnsureAudioManager()
        {
            if (AudioManager.HasInstance) return;

            if (_audioCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No AudioCatalog assigned — audio system skipped.");
                return;
            }

            var go = new GameObject("AudioManager");
            var mgr = go.AddComponent<AudioManager>();
            mgr.SetCatalog(_audioCatalog);
            Debug.Log("[GameplaySceneSetup] AudioManager created.");
        }

        private void EnsureCombatAudioSystem()
        {
            if (FindObjectOfType<Combat.CombatAudioSystem>() != null) return;

            if (_combatSfxConfig == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No CombatSfxConfig assigned — combat audio skipped.");
                return;
            }

            var go = new GameObject("CombatAudioSystem");
            var sys = go.AddComponent<Combat.CombatAudioSystem>();
            sys.Initialize(_combatSfxConfig);
            Debug.Log("[GameplaySceneSetup] CombatAudioSystem created.");
        }

        private void EnterGameAudio()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;
            audio.EnterGameAudio();
        }

        private void EnsureBuildingsRuntimeEditor()
        {
            if (BuildingsRuntimeEditor.Instance != null) return;

            var go = new GameObject("BuildingsRuntimeEditor");
            var editor = go.AddComponent<BuildingsRuntimeEditor>();

            if (_buildingCatalog != null)
            {
#if UNITY_EDITOR
                var serialized = new UnityEditor.SerializedObject(editor);
                var catalogProp = serialized.FindProperty("_catalog");
                if (catalogProp != null)
                {
                    catalogProp.objectReferenceValue = _buildingCatalog;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
#endif
            }

            Debug.Log("[GameplaySceneSetup] BuildingsRuntimeEditor created. Press F10 to toggle.");
        }

        private void EnsureDeathDropSystem()
        {
            if (FindObjectOfType<DeathDropSystem>() != null) return;
            var go = new GameObject("DeathDropSystem");
            go.AddComponent<DeathDropSystem>();
            Debug.Log("[GameplaySceneSetup] DeathDropSystem created.");
        }

        private void EnsureNPCRespawnSystem()
        {
            if (FindObjectOfType<NPCRespawnSystem>() != null) return;
            var go = new GameObject("NPCRespawnSystem");
            go.AddComponent<NPCRespawnSystem>();
            Debug.Log("[GameplaySceneSetup] NPCRespawnSystem created.");
        }

        private void EnsureToastSystem()
        {
            if (FindObjectOfType<Combat.ToastSystem>() != null) return;
            var go = new GameObject("ToastSystem");
            go.AddComponent<Combat.ToastSystem>();
            Debug.Log("[GameplaySceneSetup] ToastSystem created.");
        }
    }
}