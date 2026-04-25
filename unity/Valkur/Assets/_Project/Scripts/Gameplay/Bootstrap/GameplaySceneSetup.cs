using System;
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

        [Header("Map")]
        [Tooltip("Overlay JSON filename in StreamingAssets/Maps/ (single-zone fallback)")]
        [SerializeField] private string overlayFile = "lobby.overlay.json";

        [Tooltip("When true, loads the full multi-zone world from zones_database.json " +
                 "instead of a single overlay file.")]
        [SerializeField] private bool loadFullWorld = true;

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

        [Header("Dungeon")]
        [SerializeField, Tooltip("Dungeon generator config. Create via 'Create > Valkur > Dungeon Generator Config'.")]
        private DungeonGeneratorConfig _dungeonConfig;

        [Tooltip("Seed for dungeon generation. -1 for random each run.")]
        [SerializeField] private int _dungeonSeed = -1;

        private WorldGridBuilder _gridBuilder;

        private void Start()
        {
            BuildWorldGrid();
            EnsureZoneManager();
            LoadWorld();
            RebakeTilemapColliders(); // repaint tiles → CompositeCollider2D geometry must be rebuilt
            EnsureGlobalLight2D();
            EnsureVFXManager();
            EnsureParticleInstancesLoader();
            EnsureTileEditor();
            EnsureMapEditor();
            EnsureSaveService();
            EnsureSaveLoadInput();
            EnsureNPCSeparation();
            EnsureVendorShopUI();
            EnsureVendorEconomyService();
            EnsureChatSystem();
            EnsureWorldLightLoader();
            EnsureBuildingCollisionLoader();
            EnsureSpawnerEditor();
            EnsureBuildingsRuntimeEditor();
            EnsureDevConsole();

            // Combat support systems (death drops, respawn, toast)
            EnsureDeathDropSystem();
            EnsureNPCRespawnSystem();
            EnsureToastSystem();

            // Player & camera MUST be created before risky loaders
            // so a loader crash can never leave the scene without a camera target.
            SpawnPlayer();
            SpawnTestMonsters();

            // Buildings / spawners may fail (missing sprites, templates, etc.).
            // Wrap in try-catch so the game remains playable even when data is incomplete.
            try { EnsureMonsterSpawner(); }            catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] MonsterSpawner failed: {ex.Message}"); }
            try { EnsureBuildingLoader(); }             catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] BuildingLoader failed: {ex.Message}"); }
            try { EnsureSpawnerInstanceLoader(); }      catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] SpawnerInstanceLoader failed: {ex.Message}"); }

            // Audio must init after all gameplay systems are ready
            try { EnsureAudioManager(); }               catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] AudioManager failed: {ex.Message}"); }
            try { EnsureCombatAudioSystem(); }           catch (System.Exception ex) { Debug.LogError($"[GameplaySceneSetup] CombatAudioSystem failed: {ex.Message}"); }
            EnterGameAudio();

            // If we're coming from a menu "Continue" or "Load Game", apply saved state
            if (Save.PendingSaveLoad.HasPending)
            {
                string savePath = Save.PendingSaveLoad.Consume();
                if (SaveService.Instance != null)
                {
                    SaveService.Instance.Load(savePath);
                    // After loading the full save, apply the position checkpoint only if
                    // it is strictly newer — protects position lost to a crash between the
                    // last save and quit. Then delete it: the checkpoint belongs to the
                    // previous session. The in-session timer will write a fresh one.
                    ApplyPositionCheckpointIfNewer(SaveService.Instance.LastLoadedTimestamp);
                    Debug.Log($"[GameplaySceneSetup] Loaded pending save: {savePath}");
                }
            }
            else
            {
                // No explicit save selected (e.g., direct scene load in editor).
                // Restore position from the most recent checkpoint if one exists.
                ApplyPositionCheckpointIfNewer(null);
            }

            // Always clear the checkpoint after consuming it so that stale data from
            // a previous session can never interfere with future scene loads.
            Save.SaveFileManager.DeletePositionCheckpoint();
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
    }
}
