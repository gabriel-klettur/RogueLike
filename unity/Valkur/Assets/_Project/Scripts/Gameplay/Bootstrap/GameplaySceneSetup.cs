using System;
using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Boot;
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

        [Tooltip("When true, GenerateDungeon() runs BspDungeonStrategy to procedurally paint the " +
                 "'dungeon' zone. Disable for hand-authored maps where the dungeon zone is painted " +
                 "manually via the F8 Tile Editor overlay.")]
        [SerializeField] private bool _generateBspDungeon = true;

        private WorldGridBuilder _gridBuilder;

        /// <summary>
        /// How long the runner may spend before it must give the loading screen a
        /// frame. The old code yielded once per step unconditionally, which on a
        /// seventy-step sequence is 1.17 s of pure waiting at 60 Hz even if every
        /// step were free. Steps that need the frame ask for it
        /// (<see cref="BootStep.Barrier"/>); the rest are collapsed by this budget.
        /// </summary>
        private const float FrameBudgetMs = 8f;

        /// <summary>
        /// Runs the boot sequence built by <c>BuildBootSequence</c>.
        ///
        /// Everything this method used to hard-code — the order, the labels, the
        /// step total, which calls were protected — is data now. What is left is the
        /// three things a runner is actually for: measure each step, keep one
        /// exception boundary around all of them, and decide when to give the screen
        /// a frame.
        /// </summary>
        private IEnumerator Start()
        {
            var steps = BuildBootSequence();
            BootTimeline.BeginRun(steps);
            LoadingReporter.ReportStage(LoadingText.BuildingWorld, 0f);

            float lastYield = Time.realtimeSinceStartup;

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                BootTimeline.BeginStep(step);

                // Report BEFORE running: the label names what is happening now and
                // the bar shows the work already banked. The old code reported after,
                // so every label described something that had already finished.
                if (step.IsReported)
                    LoadingReporter.ReportStage(step.Label, BootTimeline.FractionAtStepStart);

                bool failed = false;
                IEnumerator body = null;

                try
                {
                    if (step.Run != null) step.Run();
                    else if (step.Progressive != null) body = step.Progressive();
                }
                catch (System.Exception ex)
                {
                    failed = true;
                    Debug.LogError($"[GameplaySceneSetup] Paso #{i} '{step.Label}' fallo: " +
                                   $"{ex.Message}\n{ex.StackTrace}");
                    BootTimeline.RecordFailure(step.Label, ex);
                }

                if (body != null)
                    yield return RunGuarded(body, step.Label);

                BootTimeline.EndStep(i, failed);

                bool budgetSpent = (Time.realtimeSinceStartup - lastYield) * 1000f >= FrameBudgetMs;
                if (step.Barrier || budgetSpent)
                {
                    yield return null;
                    lastYield = Time.realtimeSinceStartup;
                }
            }

            // The one place the bar is allowed to read 100 %, and it is reached only
            // after every step has run. BootProgress caps itself below 1 until here.
            BootTimeline.CompleteRun();

            if (BootTimeline.AnyFailed)
                Debug.LogError($"[GameplaySceneSetup] El arranque termino con " +
                               $"{BootTimeline.Failures.Count} fallo(s). Escribe 'boot' en la consola.");
            else
                Debug.Log($"[GameplaySceneSetup] Arranque completo: {steps.Count} etapas en " +
                          $"{BootTimeline.TotalMilliseconds / 1000f:F2} s.");

            LoadingReporter.ReportGameplayReady();
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
        /// Cached on first access. Bracketed names ("[X]") follow our scene
        /// organisational convention and are auto-created when missing so a
        /// fresh scene that doesn't pre-author every container still groups
        /// spawned objects under the correct root instead of polluting the
        /// scene root with hundreds of loose nodes. Non-bracketed names
        /// (callers that want a strict lookup) fall back to root with a
        /// single warning.
        /// </summary>
        private Transform GetSceneContainer(string name)
        {
            if (_containerCache.TryGetValue(name, out var cached))
                return cached;

            var go = GameObject.Find(name);
            if (go == null)
            {
                bool isConventionContainer =
                    !string.IsNullOrEmpty(name) && name.StartsWith("[") && name.EndsWith("]");
                if (isConventionContainer)
                    go = new GameObject(name);
                else
                    Debug.LogWarning($"[GameplaySceneSetup] Scene container '{name}' not found — object will spawn at root.");
            }

            var t = go != null ? go.transform : null;
            _containerCache[name] = t;
            return t;
        }
    }
}
