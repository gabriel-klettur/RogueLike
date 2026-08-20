using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Editors.Boss;
using Valkur.Gameplay.Editors.General;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.Items;
using Valkur.Gameplay.WorldDrops;
using Valkur.Infrastructure;
using Valkur.Infrastructure.Persistence.Repositories;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
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

        // Top-level launcher panel toggled with ESC. Lists every other editor
        // and session action as a clickable button. Spawned last among the
        // editors so all sibling singletons exist when the registry's lambdas
        // resolve them at click time. The panel keeps itself idle (canvas
        // hidden) until the user activates it via the hotkey.
        private void EnsureGeneralEditor()
        {
            if (GeneralEditorManager.Instance != null) return;

            var go = new GameObject("GeneralEditorManager");
            go.AddComponent<GeneralEditorManager>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            Debug.Log("[GameplaySceneSetup] GeneralEditorManager created. Press ESC to open.");
        }

        // Lighting editor (Ctrl+F3). Pulls the LightPresetCatalog from the live
        // WorldLightLoader (single source of truth for lights at runtime). When the
        // loader is missing the editor still spawns — the picker shows an empty grid
        // with a clear hint to populate the catalog. Mirrors the
        // EnsureItemsRuntimeEditor + ItemDropService bootstrap pattern.
        private void EnsureLightingRuntimeEditor()
        {
            if (Valkur.Gameplay.World.LightingRuntimeEditor.Instance != null) return;

            var go = new GameObject("LightingRuntimeEditor");
            go.AddComponent<Valkur.Gameplay.World.LightingRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            Debug.Log("[GameplaySceneSetup] LightingRuntimeEditor created. Press Ctrl+F3 to toggle.");
        }

        // Time & Weather editor (F2). Hosts every modifying control for the
        // time-of-day + weather subsystems (speed slider, phase shortcuts,
        // weather toggles, phase-tuning sliders) so the gameplay HUD only
        // shows the read-only sundial. Idempotent.
        /// <summary>
        /// The Camera Editor has no hotkey by design — it is reached from the General Editor
        /// (ESC). Twelve of the thirteen function keys are already bound, and a surface used
        /// during a tuning pass does not need one the player can hit by accident.
        /// </summary>
        private void EnsureCameraEditor()
        {
            if (Valkur.Gameplay.Editors.CameraFeelEditor.CameraRuntimeEditor.Instance != null) return;
            var go = new GameObject("CameraEditor");
            go.AddComponent<Valkur.Gameplay.Editors.CameraFeelEditor.CameraRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);
            Debug.Log("[GameplaySceneSetup] CameraEditor created. Open it from the General Editor (ESC).");
        }

        private void EnsureTimeWeatherEditor()
        {
            if (Valkur.Gameplay.TimeWeather.TimeWeatherEditor.Instance != null) return;
            var go = new GameObject("TimeWeatherEditor");
            go.AddComponent<Valkur.Gameplay.TimeWeather.TimeWeatherEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);
            Debug.Log("[GameplaySceneSetup] TimeWeatherEditor created. Press F2 to toggle.");
        }

        private void EnsureItemsRuntimeEditor()
        {
            // Surface the catalog before the editor's first activation so its
            // ServiceLocator-first lookup hits a populated binding instead of
            // falling back to AssetDatabase / Resources. Try the inspector
            // field first, then the canonical Resources/AssetDatabase
            // fallback, and only warn if BOTH paths are empty — otherwise
            // the warning fires on every cold boot of a freshly-migrated
            // project even though the catalog is correctly discovered.
            var catalog = _itemCatalog != null ? _itemCatalog : ResolveItemCatalogFallback();
            if (catalog != null)
            {
                ServiceLocator.Register<ItemCatalog>(catalog);
                if (_itemCatalog == null)
                    Debug.Log("[GameplaySceneSetup] ItemCatalog inspector field empty — resolved via Resources/AssetDatabase fallback.");
            }
            else
            {
                Debug.LogWarning("[GameplaySceneSetup] No ItemCatalog assigned and no fallback found — items editor will be empty.");
            }

            EnsureItemDropService();

            if (ItemsRuntimeEditor.Instance != null) return;

            var go = new GameObject("ItemsRuntimeEditor");
            go.AddComponent<ItemsRuntimeEditor>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            Debug.Log("[GameplaySceneSetup] ItemsRuntimeEditor created. Press F7 to toggle.");
        }

        /// <summary>
        /// Bootstrap the persistent-drop pipeline:
        ///  • Authoring repo → <c>StreamingAssets/Items/item_drops.json</c>
        ///    (lives with the world content; survives across runs).
        ///  • Run repo → <c>{persistentDataPath}/Saves/{runId}/world_drops.json</c>
        ///    (per-run gameplay drops; loot, player throws).
        ///
        /// Both repos are created up-front; the service merges them into a
        /// single in-memory cache, but flushes each subset to its own file.
        /// Idempotent — no-op when the service is already registered.
        /// </summary>
        private void EnsureItemDropService()
        {
            if (ServiceLocator.TryGet<ItemDropService>(out _)) return;

            // Fall back to the canonical Catalogs/Items/ItemCatalog.asset when
            // the inspector field is empty, so persistence works out-of-the-box
            // before someone wires the GameplaySceneSetup field by hand. Without
            // this fallback the service is never created, the F7 editor silently
            // uses the legacy ephemeral DropSystem path, and drops never reach
            // disk — which is exactly the "items don't persist" failure mode.
            var catalog = _itemCatalog != null ? _itemCatalog : ResolveItemCatalogFallback();
            if (catalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No ItemCatalog (inspector + AssetDatabase + Resources fallbacks all empty) — skipping item drop persistence wiring.");
                return;
            }
            // Surface the catalog through ServiceLocator too so the F7 editor
            // (which checks ServiceLocator before AssetDatabase) hits a single
            // shared instance instead of a different copy per system.
            ServiceLocator.Register<ItemCatalog>(catalog);

            var authoringRepo = new JsonFileItemDropRepository();
            var runRepo       = BuildRunDropRepository();
            var service       = new ItemDropService(
                authoringRepo, runRepo, catalog,
                Valkur.Core.Coordinates.WorldId.Base);

            int loaded  = service.LoadFromRepository();
            int spawned = service.Rehydrate();
            Debug.Log($"[GameplaySceneSetup] ItemDropService ready — loaded {loaded} record(s), rehydrated {spawned} pickup(s) (authoring + run). Catalog source: {(_itemCatalog != null ? "inspector" : "fallback")}.");

            ServiceLocator.Register<ItemDropService>(service);
        }

        private static ItemCatalog ResolveItemCatalogFallback()
        {
            // 1) Resources path (works in builds + editor).
            var fromResources = Resources.Load<ItemCatalog>("Catalogs/ItemCatalog");
            if (fromResources != null) return fromResources;

#if UNITY_EDITOR
            // 2) Editor-only direct asset load — covers freshly migrated
            //    projects where Resources/ doesn't host the catalog yet.
            var fromAssets = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemCatalog>(
                "Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset");
            if (fromAssets != null) return fromAssets;
#endif
            return null;
        }

        /// <summary>
        /// Resolve the run-scoped JSON repository. Today the run id is a single
        /// "default" slot; once SaveService surfaces a real run identifier we'll
        /// route it here so drops follow the right save folder.
        /// </summary>
        private static IItemDropRepository BuildRunDropRepository()
        {
            string saveRoot = System.IO.Path.Combine(Application.persistentDataPath, "Saves", "default");
            return new JsonFileItemDropRepository(saveRoot, "WorldDrops", "world_drops.json");
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

        private void EnsureBossEditor()
        {
            if (BossEditorManager.Instance != null) return;

            var go = new GameObject("BossEditorManager");
            go.AddComponent<BossEditorManager>();
            go.transform.SetParent(GetSceneContainer("[Editors]"), false);

            Debug.Log("[GameplaySceneSetup] BossEditorManager created (accessible via General Editor).");
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
    }
}
