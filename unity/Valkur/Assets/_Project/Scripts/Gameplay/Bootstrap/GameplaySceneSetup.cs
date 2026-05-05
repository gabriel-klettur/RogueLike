using System;
using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.NPC;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    /// <summary>
    /// Sets up the MainGameplay scene at runtime.
    /// Builds the world grid, loads the full multi-zone world, spawns the player, camera, HUD.
    /// </summary>
    public partial class GameplaySceneSetup : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private PlayerDefinition defaultPlayerDef;
        [SerializeField] private GameObject playerPrefab;

        [Header("Monsters (test)")]
        [SerializeField] private MonsterDefinition testMonsterDef;
        [SerializeField] private GameObject monsterPrefab;
        [SerializeField] private int testMonsterCount = 3;
        [SerializeField] private float spawnRadius = 5f;

        // Public accessors so runtime editors (e.g. EntitiesRuntimeEditor) can spawn
        // additional players / monsters without duplicating the prefab references.
        public GameObject PlayerPrefab => playerPrefab;
        public GameObject MonsterPrefab => monsterPrefab;

        [Header("Map")]
        [Tooltip("Overlay JSON filename in StreamingAssets/Maps/ (single-zone fallback)")]
        [SerializeField] private string overlayFile = "lobby.overlay.json";

        [Tooltip("When true, loads the full multi-zone world from zones_database.json " +
                 "instead of a single overlay file.")]
        [SerializeField] private bool loadFullWorld = true;

        [Tooltip("Phase 1 multi-world: optional descriptor wired in the inspector. " +
                 "When set, replaces the legacy in-code base descriptor at boot. " +
                 "Leave null for single-world boot (byte-compatible with pre-Phase 1).")]
        [SerializeField] private WorldDescriptor initialWorld;

        public void SetInitialWorld(WorldDescriptor descriptor) => initialWorld = descriptor;

        [Header("Particles")]
        [SerializeField, Tooltip("Catalog of particle presets. Populate via 'Valkur > Particles > Import Presets from Python JSON'.")]
        private ParticlePresetCatalog _particlePresetCatalog;

        [Header("Lighting")]
        [SerializeField, Tooltip("Catalog of light presets. Populate via 'Valkur > Lighting > Import Presets from Python JSON'.")]
        private LightPresetCatalog _lightPresetCatalog;

        [Header("Spawners")]
        [SerializeField, Tooltip("Catalog of spawner templates. Populate via 'Valkur > Spawners > Import Templates'.")]
        private SpawnerTemplateCatalog _spawnerTemplateCatalog;

        [Header("Buildings")]
        [SerializeField, Tooltip("Catalog of building templates. Populate via 'Valkur > Buildings > Import Buildings'.")]
        private BuildingCatalog _buildingCatalog;

        [Header("Monsters Catalog")]
        [SerializeField, Tooltip("Catalog of monster/vendor definitions. Populate via 'Valkur > Migration > Import Monsters'.")]
        private MonsterCatalog _monsterCatalog;

        [Header("Audio")]
        [SerializeField, Tooltip("Audio catalog (music, SFX, ambient). Populate via 'Valkur > Audio > Import Catalog from Python JSON'.")]
        private AudioCatalogSO _audioCatalog;

        [SerializeField, Tooltip("Combat SFX config. Populate via 'Valkur > Audio > Import Catalog from Python JSON'.")]
        private CombatSfxConfigSO _combatSfxConfig;

        [Header("Spells")]
        [SerializeField, Tooltip("Catalog of all spell definitions. Populate via 'Valkur > Spells > Import Spells from Python JSON'.")]
        private SpellCatalog _spellCatalog;

        [Header("Items")]
        [SerializeField, Tooltip("Catalog of all ItemDefinition assets. Populate via 'Valkur > Migration > Import Items from Python SQLite'.")]
        private ItemCatalog _itemCatalog;

        [Header("Dungeon")]
        [SerializeField, Tooltip("Dungeon generator config. Create via 'Create > Valkur > Dungeon Generator Config'.")]
        private DungeonGeneratorConfig _dungeonConfig;

        [Tooltip("Seed for dungeon generation. -1 for random each run.")]
        [SerializeField] private int _dungeonSeed = -1;

        private WorldGridBuilder _gridBuilder;

        private const int SetupStepTotal = 40;
        private int _setupStep;

        private IEnumerator Start()
        {
            _setupStep = 0;

            BuildWorldGrid();
            Report("Building grid"); yield return null;

            EnsureZoneManager();
            Report("Initializing zones"); yield return null;

            EnsureWorldManager();
            Report("Initializing WorldManager"); yield return null;

            LoadWorld();
            Report("Loading world"); yield return null;

            RebakeTilemapColliders();
            Report("Rebaking tile colliders"); yield return null;

            EnsureGlobalLight2D();
            Report("Initializing global lighting"); yield return null;

            EnsureDayNightCycle();
            Report("Starting day/night cycle"); yield return null;

            EnsureVFXManager();
            Report("Initializing visual effects"); yield return null;

            EnsureParticleInstancesLoader();
            Report("Loading particles"); yield return null;

            EnsureTileEditor();
            Report("Initializing tile editor"); yield return null;

            EnsureMapEditor();
            Report("Initializing map editor"); yield return null;

            EnsureSaveService();
            Report("Initializing save system"); yield return null;

            EnsureSaveLoadInput();
            Report("Initializing save input"); yield return null;

            EnsureNPCSeparation();
            Report("Initializing NPC separation"); yield return null;

            EnsureVendorShopUI();
            Report("Initializing shops"); yield return null;

            EnsureVendorEconomyService();
            Report("Initializing economy"); yield return null;

            EnsureChatSystem();
            Report("Initializing chat"); yield return null;

            EnsureWorldLightLoader();
            Report("Loading world lights"); yield return null;

            EnsureBuildingCollisionLoader();
            Report("Loading building collisions"); yield return null;

            EnsureSpawnerEditor();
            Report("Initializing spawner editor"); yield return null;

            EnsureBuildingsRuntimeEditor();
            Report("Initializing buildings editor"); yield return null;

            EnsureFSMRuntimeEditor();
            Report("Initializing FSM editor"); yield return null;

            EnsureItemsRuntimeEditor();
            Report("Initializing items editor"); yield return null;

            EnsureSpellsRuntimeEditor();
            Report("Initializing spells editor"); yield return null;

            EnsureEntitiesRuntimeEditor();
            Report("Initializing entities editor"); yield return null;

            EnsureInventoryRuntimeEditor();
            Report("Initializing inventory editor"); yield return null;

            EnsureParticlesRuntimeEditor();
            Report("Initializing particles editor"); yield return null;

            EnsureLightingRuntimeEditor();
            Report("Initializing lighting editor"); yield return null;

            EnsureGeneralEditor();
            Report("Initializing general editor"); yield return null;

            EnsureDayNightAtmosphere();
            Report("Initializing day/night atmosphere"); yield return null;

            EnsureWeatherManager();
            Report("Initializing weather system"); yield return null;

            EnsureTimeWeatherEditor();
            Report("Initializing time & weather editor"); yield return null;

            EnsureDevConsole();
            Report("Initializing dev console"); yield return null;

            EnsureDeathDropSystem();
            Report("Initializing death drops"); yield return null;

            EnsureDeathSequenceFlow();
            Report("Initializing death & revival cycle"); yield return null;

            EnsureLevelUpRestoreSystem();
            Report("Initializing level-up restore"); yield return null;

            EnsurePermadeathSaveCleanupSystem();
            Report("Initializing permadeath"); yield return null;

            EnsureLevelUpSkillPointSystem();
            Report("Initializing skill points per level"); yield return null;

            EnsureXpFeedbackSystem();
            Report("Initializing XP feedback"); yield return null;

            EnsureProfileTelemetrySystem();
            Report("Initializing progression telemetry"); yield return null;

            EnsureNPCRespawnSystem();
            Report("Initializing NPC respawn"); yield return null;

            EnsureToastSystem();
            Report("Initializing notifications"); yield return null;

            // Pre-apply player class from pending save so SpawnPlayer() uses the correct
            // class for visuals and stats. The full restore (position, HP, etc.) happens
            // later via SaveService.Load().
            if (Save.PendingSaveLoad.HasPending && !string.IsNullOrWhiteSpace(Save.PendingSaveLoad.PlayerClass))
                PlayerSelectionState.SetSelectedPlayer(Save.PendingSaveLoad.PlayerClass);

            SpawnPlayer();
            Report("Spawning player"); yield return null;

            EnsureProceduralChunkStreamer();
            Report("Initializing procedural streaming"); yield return null;

            SpawnTestMonsters();
            Report("Spawning test monsters"); yield return null;

            try { EnsureMonsterSpawner(); }
            catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] MonsterSpawner failed: {ex.Message}"); }
            Report("Initializing monster spawner"); yield return null;

            try { EnsureBuildingLoader(); }
            catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] BuildingLoader failed: {ex.Message}"); }
            Report("Loading buildings"); yield return null;

            try { EnsureSpawnerInstanceLoader(); }
            catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] SpawnerInstanceLoader failed: {ex.Message}"); }
            Report("Loading spawner instances"); yield return null;

            try { EnsureAudioManager(); }
            catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] AudioManager failed: {ex.Message}"); }
            Report("Initializing audio"); yield return null;

            try { EnsureCombatAudioSystem(); }
            catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] CombatAudioSystem failed: {ex.Message}"); }
            Report("Initializing combat audio"); yield return null;

            EnterGameAudio();
            Report("Starting game music"); yield return null;

            // Apply saved state / checkpoint
            if (Save.PendingSaveLoad.HasPending)
            {
                string savePath = Save.PendingSaveLoad.Consume();
                if (SaveService.Instance != null)
                {
                    SaveService.Instance.Load(savePath);
                    ApplyPositionCheckpointIfNewer(SaveService.Instance.LastLoadedTimestamp);
                    Debug.Log($"[GameplaySceneSetup] Loaded pending save: {savePath}");
                }
            }
            else
            {
                // New game — generate a fresh run ID so all autosaves from this session
                // are grouped together in the Load Game panel.
                SaveService.Instance?.BeginNewRun();
                ApplyPositionCheckpointIfNewer(null);
            }
            Save.SaveFileManager.DeletePositionCheckpoint();
            Report("Restoring session"); yield return null;

            // All systems ready — signal the loading screen to fade out
            LoadingReporter.ReportGameplayReady();
        }

        /// <summary>Reports progress to the loading screen for the current setup step.</summary>
        private void Report(string message)
        {
            _setupStep++;
            LoadingReporter.ReportStage(message, (float)_setupStep / SetupStepTotal);
        }

        /// <summary>
        /// Moves the player to the position stored in the crash-safe position checkpoint
        /// if the checkpoint is strictly newer than <paramref name="loadedSaveTimestamp"/>
        /// and within a 60-minute window (so intentionally loading an old save never
        /// overrides the player's chosen starting position).
        /// When <paramref name="loadedSaveTimestamp"/> is null the checkpoint is applied
        /// unconditionally (direct scene entry with no main-menu save selection).
        /// </summary>
        private void ApplyPositionCheckpointIfNewer(string loadedSaveTimestamp)
        {
            var checkpoint = Save.SaveFileManager.ReadPositionCheckpoint();
            if (checkpoint == null) return;

            if (!string.IsNullOrEmpty(loadedSaveTimestamp))
            {
                if (!DateTime.TryParse(checkpoint.timestamp, out var cpTime))  return;
                if (!DateTime.TryParse(loadedSaveTimestamp,  out var saveTime)) return;

                double diffMinutes = (cpTime - saveTime).TotalMinutes;
                // Only apply if checkpoint is newer than the save AND within 60 minutes.
                // A gap > 60 min means the player loaded an old save intentionally.
                if (diffMinutes <= 0 || diffMinutes > 60) return;
            }

            var player = EntityRegistry.Player;
            if (player == null) return;

            player.transform.position = new Vector3(checkpoint.x, checkpoint.y, 0f);
            Debug.Log($"[GameplaySceneSetup] Position restored from crash-safe checkpoint: " +
                      $"({checkpoint.x:F2}, {checkpoint.y:F2}) [{checkpoint.timestamp}]");
        }

        // ── Scene Hierarchy Containers ──────────────────────────────────────────────

        private readonly System.Collections.Generic.Dictionary<string, Transform> _containerCache =
            new System.Collections.Generic.Dictionary<string, Transform>();

        /// <summary>
        /// Returns the Transform of a scene container GameObject (e.g. "[World]").
        /// Cached on first access. Returns null (root level) if the container is not found.
        /// </summary>
        private Transform GetSceneContainer(string name)
        {
            if (_containerCache.TryGetValue(name, out var cached))
                return cached;

            var go = GameObject.Find(name);
            if (go == null)
                Debug.LogWarning($"[GameplaySceneSetup] Scene container '{name}' not found — object will spawn at root.");

            var t = go != null ? go.transform : null;
            _containerCache[name] = t;
            return t;
        }
    }
}
