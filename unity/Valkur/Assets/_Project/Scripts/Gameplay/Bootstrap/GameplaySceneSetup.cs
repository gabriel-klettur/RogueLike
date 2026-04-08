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

        private WorldGridBuilder _gridBuilder;

        private void Start()
        {
            BuildWorldGrid();
            EnsureZoneManager();
            LoadWorld();
            EnsureGlobalLight2D();
            EnsureVFXManager();
            EnsureParticleInstancesLoader();
            EnsureTileEditor();
            EnsureMapEditor();
            EnsureSaveLoadInput();
            EnsureNPCSeparation();
            EnsureVendorShopUI();
            EnsureVendorEconomyService();
            EnsureChatSystem();
            EnsureWorldLightLoader();
            EnsureBuildingCollisionLoader();
            EnsureSpawnerEditor();
            EnsureDevConsole();

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
                    Debug.Log($"[GameplaySceneSetup] Loaded pending save: {savePath}");
                }
            }
        }
    }
}
