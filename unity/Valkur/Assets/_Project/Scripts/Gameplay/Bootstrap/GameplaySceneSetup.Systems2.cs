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
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.Items;
using Valkur.Gameplay.Entities;
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
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

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
            go.transform.SetParent(GetSceneContainer("[Spawning]"), false);

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
            go.transform.SetParent(GetSceneContainer("[World]"), false);
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

        private void EnsureAudioManager()
        {
            // AudioManager is persistent (Persist => true): reuse existing instance
            if (AudioManager.HasInstance)
            {
                var audio = ServiceLocator.Get<IAudioService>();
                if (audio != null)
                {
                    Debug.Log("[GameplaySceneSetup] AudioManager already running (singleton persists).");
                    return;
                }
                // Instance exists but not registered; register it
                ServiceLocator.Register<IAudioService>(AudioManager.Instance);
                Debug.Log("[GameplaySceneSetup] AudioManager found, registered with ServiceLocator.");
                return;
            }

            if (_audioCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No AudioCatalog assigned — audio system skipped.");
                return;
            }

            var go = new GameObject("AudioManager");
            var mgr = go.AddComponent<AudioManager>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            mgr.SetCatalog(_audioCatalog);
            Debug.Log("[GameplaySceneSetup] AudioManager created (first instantiation).");
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
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
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
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

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

        private void EnsureFSMRuntimeEditor()
        {
            if (FSMRuntimeEditor.Instance != null) return;

            var go = new GameObject("FSMRuntimeEditor");
            go.AddComponent<FSMRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            Debug.Log("[GameplaySceneSetup] FSMRuntimeEditor created. Press F12 to toggle.");
        }

        private void EnsureItemsRuntimeEditor()
        {
            if (ItemsRuntimeEditor.Instance != null) return;

            var go = new GameObject("ItemsRuntimeEditor");
            go.AddComponent<ItemsRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            Debug.Log("[GameplaySceneSetup] ItemsRuntimeEditor created. Press F7 to toggle.");
        }

        private void EnsureSpellsRuntimeEditor()
        {
            if (Valkur.Gameplay.Spells.SpellsRuntimeEditor.Instance != null) return;

            var go = new GameObject("SpellsRuntimeEditor");
            var editor = go.AddComponent<Valkur.Gameplay.Spells.SpellsRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

#if UNITY_EDITOR
            var serialized = new UnityEditor.SerializedObject(editor);
            if (_spellCatalog != null)
            {
                var catalogProp = serialized.FindProperty("_catalog");
                if (catalogProp != null) catalogProp.objectReferenceValue = _spellCatalog;
            }
            if (_particlePresetCatalog != null)
            {
                var particleProp = serialized.FindProperty("_particleCatalog");
                if (particleProp != null) particleProp.objectReferenceValue = _particlePresetCatalog;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
#endif

            Debug.Log("[GameplaySceneSetup] SpellsRuntimeEditor created. Press F4 to toggle.");
        }

        private void EnsureEntitiesRuntimeEditor()
        {
            if (EntitiesRuntimeEditor.Instance != null) return;

            var go = new GameObject("EntitiesRuntimeEditor");
            var editor = go.AddComponent<EntitiesRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            if (_monsterCatalog != null)
            {
#if UNITY_EDITOR
                var serialized = new UnityEditor.SerializedObject(editor);
                var catalogProp = serialized.FindProperty("_monsterCatalog");
                if (catalogProp != null)
                {
                    catalogProp.objectReferenceValue = _monsterCatalog;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
#endif
            }

            Debug.Log("[GameplaySceneSetup] EntitiesRuntimeEditor created. Press F5 to toggle.");
        }

        private void EnsureInventoryRuntimeEditor()
        {
            if (Valkur.Gameplay.Inventory.InventoryRuntimeEditor.Instance != null) return;

            var go = new GameObject("InventoryRuntimeEditor");
            go.AddComponent<Valkur.Gameplay.Inventory.InventoryRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            Debug.Log("[GameplaySceneSetup] InventoryRuntimeEditor created. Press F6 to toggle.");
        }

        private void EnsureParticlesRuntimeEditor()
        {
            if (ParticlesRuntimeEditor.Instance != null) return;

            var go = new GameObject("ParticlesRuntimeEditor");
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            if (_particlePresetCatalog != null)
            {
#if UNITY_EDITOR
                var serialized = new UnityEditor.SerializedObject(editor);
                var catalogProp = serialized.FindProperty("_catalog");
                if (catalogProp != null)
                {
                    catalogProp.objectReferenceValue = _particlePresetCatalog;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
#endif
            }
            else
            {
                Debug.LogWarning("[GameplaySceneSetup] ParticlesRuntimeEditor created without ParticlePresetCatalog — picker will be empty.");
            }

            Debug.Log("[GameplaySceneSetup] ParticlesRuntimeEditor created. Press F1 to toggle.");
        }

        private void EnsureDeathDropSystem()
        {
            if (FindObjectOfType<DeathDropSystem>() != null) return;
            var go = new GameObject("DeathDropSystem");
            go.AddComponent<DeathDropSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] DeathDropSystem created.");
        }

        private void EnsureNPCRespawnSystem()
        {
            if (FindObjectOfType<NPCRespawnSystem>() != null) return;
            var go = new GameObject("NPCRespawnSystem");
            go.AddComponent<NPCRespawnSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] NPCRespawnSystem created.");
        }

        private void EnsureToastSystem()
        {
            if (FindObjectOfType<Combat.ToastSystem>() != null) return;
            var go = new GameObject("ToastSystem");
            go.AddComponent<Combat.ToastSystem>();
            go.transform.SetParent(GetSceneContainer("[UI]"), false);
            Debug.Log("[GameplaySceneSetup] ToastSystem created.");
        }
    }
}