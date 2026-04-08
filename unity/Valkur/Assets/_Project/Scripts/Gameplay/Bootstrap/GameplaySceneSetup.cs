using UnityEngine;
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
    /// Builds the world grid, spawns the player, camera, HUD, and test monsters.
    /// Temporary bootstrap until full spawner system is ported.
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
        [Tooltip("Overlay JSON filename in StreamingAssets/Maps/")]
        [SerializeField] private string overlayFile = "lobby.overlay.json";

        [Header("Particles")]
        [SerializeField, Tooltip("Catalog of particle presets. Populate via 'Valkur > Particles > Import Presets from Python JSON'.")]
        private ParticlePresetCatalog _particlePresetCatalog;

        private WorldGridBuilder _gridBuilder;

        private void Start()
        {
            BuildWorldGrid();
            LoadOverlay();
            EnsureGlobalLight2D();
            EnsureVFXManager();
            EnsureZoneManager();
            EnsureParticleInstancesLoader();
            EnsureTileEditor();
            EnsureMapEditor();
            EnsureSaveLoadInput();
            EnsureNPCSeparation();
            EnsureVendorShopUI();
            EnsureDevConsole();
            SpawnPlayer();
            SpawnTestMonsters();
        }
    }
}
