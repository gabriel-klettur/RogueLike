using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Editors.General;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.NPC;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.Items;
using Valkur.Gameplay.WorldDrops;
using Valkur.Gameplay.Entities;
using Valkur.Infrastructure;
using Valkur.Infrastructure.Persistence.Repositories;
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

        // Atmospheric particles + ambient audio bed driven by DayNightCycle.
        // Sit under [VFX] / [Systems] respectively so the existing scene
        // hierarchy stays organised. Both are idempotent.
        private void EnsureDayNightAtmosphere()
        {
            if (FindObjectOfType<Valkur.Gameplay.World.DayNightAtmosphericParticles>() == null)
            {
                var go  = new GameObject("DayNightAtmosphericParticles", typeof(ParticleSystem));
                go.AddComponent<Valkur.Gameplay.World.DayNightAtmosphericParticles>();
                go.transform.SetParent(GetSceneContainer("[VFX]"), false);
                Debug.Log("[GameplaySceneSetup] DayNightAtmosphericParticles created.");
            }

            if (FindObjectOfType<Valkur.Gameplay.World.DayNightAmbientAudio>() == null)
            {
                var go = new GameObject("DayNightAmbientAudio");
                go.AddComponent<Valkur.Gameplay.World.DayNightAmbientAudio>();
                go.transform.SetParent(GetSceneContainer("[Systems]"), false);
                Debug.Log("[GameplaySceneSetup] DayNightAmbientAudio created (clips wired via inspector).");
            }
        }

        // Weather orchestrator (Wind / Rain / Snow). The manager creates each
        // effect lazily on first request — at boot we just ensure a single
        // root GameObject exists so the WeatherHUD has somewhere to publish to.
        private void EnsureWeatherManager()
        {
            if (Valkur.Gameplay.World.Weather.WeatherManager.Instance != null) return;
            var go = new GameObject("WeatherManager");
            go.AddComponent<Valkur.Gameplay.World.Weather.WeatherManager>();
            go.transform.SetParent(GetSceneContainer("[VFX]"), false);
            Debug.Log("[GameplaySceneSetup] WeatherManager created (effects spawn lazily on first toggle).");
        }

        // Time & Weather editor (F2). Hosts every modifying control for the
        // time-of-day + weather subsystems (speed slider, phase shortcuts,
        // weather toggles, phase-tuning sliders) so the gameplay HUD only
        // shows the read-only sundial. Idempotent.
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
            // falling back to AssetDatabase / Resources.
            if (_itemCatalog != null)
            {
                ServiceLocator.Register<ItemCatalog>(_itemCatalog);
            }
            else
            {
                Debug.LogWarning("[GameplaySceneSetup] No ItemCatalog assigned — items editor will fall back to AssetDatabase/Resources.");
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
            // after a fresh PythonDataMigrator run even before someone wires
            // the GameplaySceneSetup field by hand. Without this fallback the
            // service is never created, the F7 editor silently uses the legacy
            // ephemeral DropSystem path, and drops never reach disk — which is
            // exactly the "items don't persist" failure mode.
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

        // Death-sequence orchestrator + URP grayscale volume + altar binder.
        // All three must coexist for the spirit/altar revive flow to work, so
        // they're wired in a single Ensure method.
        private void EnsureDeathSequenceFlow()
        {
            if (FindObjectOfType<DeathSequenceController>() == null)
            {
                var grayscaleGo = new GameObject("DeathGrayscaleVolume");
                grayscaleGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                var grayscale = grayscaleGo.AddComponent<GrayscaleVolumeController>();

                var seqGo = new GameObject("DeathSequenceController");
                seqGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                var controller = seqGo.AddComponent<DeathSequenceController>();
                controller.BindGrayscaleController(grayscale);

                Debug.Log("[GameplaySceneSetup] DeathSequenceController + GrayscaleVolumeController created.");
            }

            if (FindObjectOfType<ResurrectionZoneAutoBinder>() == null)
            {
                var binderGo = new GameObject("ResurrectionZoneAutoBinder");
                binderGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                binderGo.AddComponent<ResurrectionZoneAutoBinder>();
                Debug.Log("[GameplaySceneSetup] ResurrectionZoneAutoBinder created.");
            }
        }

        // LevelUpRestoreSystem listens to GameEvents.OnLevelUp and refills
        // HP/MP. Idempotent: bails if a designer already wired one in the
        // scene. Created in [Systems] alongside DeathDropSystem so the
        // gameplay-loop helpers cluster in one place.
        private void EnsureLevelUpRestoreSystem()
        {
            if (FindObjectOfType<LevelUpRestoreSystem>() != null) return;
            var go = new GameObject("LevelUpRestoreSystem");
            go.AddComponent<LevelUpRestoreSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] LevelUpRestoreSystem created.");
        }

        // PermadeathSaveCleanupSystem deletes the active autosave when the
        // player dies AND GameSettings.permadeath is on. The component
        // itself reads the flag each death — adding it here is harmless
        // when permadeath is off (it just listens and skips).
        private void EnsurePermadeathSaveCleanupSystem()
        {
            if (FindObjectOfType<PermadeathSaveCleanupSystem>() != null) return;
            var go = new GameObject("PermadeathSaveCleanupSystem");
            go.AddComponent<PermadeathSaveCleanupSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] PermadeathSaveCleanupSystem created.");
        }

        // LevelUpSkillPointSystem grants skill points to the levelled
        // entity's LearnedSkills on each level-up. Sibling to
        // LevelUpRestoreSystem; both can safely coexist on the same event.
        // Skipped silently for NPCs without a LearnedSkills component.
        private void EnsureLevelUpSkillPointSystem()
        {
            if (FindObjectOfType<LevelUpSkillPointSystem>() != null) return;
            var go = new GameObject("LevelUpSkillPointSystem");
            go.AddComponent<LevelUpSkillPointSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] LevelUpSkillPointSystem created.");
        }

        // XpFeedbackSystem closes the visual juice loop: floating "+N XP"
        // above the player and "LEVEL UP!" toast on level-up. Audio is
        // already covered by CombatAudioSystem.OnLevelUp, so this only
        // adds the visual layer. Idempotent.
        private void EnsureXpFeedbackSystem()
        {
            if (FindObjectOfType<XpFeedbackSystem>() != null) return;
            var go = new GameObject("XpFeedbackSystem");
            go.AddComponent<XpFeedbackSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] XpFeedbackSystem created.");
        }

        // Boots the meta-progression telemetry layer: creates a
        // JsonProfileDb at persistentDataPath/profile.json, registers
        // it in ServiceLocator, hydrates from disk, and starts the
        // first run row. Subsequent OnEntityDied / OnPlayerDied /
        // OnLevelUp / OnXpGained events flow into the DB through
        // ProfileTelemetrySystem.
        private void EnsureProfileTelemetrySystem()
        {
            if (FindObjectOfType<Save.ProfileTelemetrySystem>() != null) return;

            // Resolve or create the IProfileDb singleton.
            if (!ServiceLocator.TryGet<Valkur.Infrastructure.Persistence.Profile.IProfileDb>(out var db))
            {
                var json = new Valkur.Infrastructure.Persistence.Profile.JsonProfileDb();
                json.LoadAll();
                ServiceLocator.Register<Valkur.Infrastructure.Persistence.Profile.IProfileDb>(json);
                db = json;
                Debug.Log($"[GameplaySceneSetup] ProfileDb hydrated from {json.FilePath}.");
            }

            var go = new GameObject("ProfileTelemetrySystem");
            var sys = go.AddComponent<Save.ProfileTelemetrySystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            sys.BindDb(db);
            sys.StartRun(permadeath: GameSettings.Instance.permadeath);
            Debug.Log("[GameplaySceneSetup] ProfileTelemetrySystem created and run started.");
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