using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Loads building instances from StreamingAssets/Buildings/buildings_instances.json
    /// and spawns a BuildingObject into the scene for each entry.
    ///
    /// Coordinate mapping (Python → Unity):
    ///   Python stores buildings as (zone, rel_x, rel_y) where:
    ///     - zone is a string matching a ZoneDefinition.zoneName in ZoneManager
    ///     - rel_x, rel_y are pixel offsets from the zone's top-left corner (Y-down)
    ///     - 1 tile = 32 px (matching TILE_PPU in ValkurAssetPostprocessor)
    ///
    ///   Converted to Unity world position (bottom-center of sprite, Y-up):
    ///     absX_tiles = gridOffset.x + rel_x / 32
    ///     absY_tiles = gridOffset.y + rel_y / 32   (Python Y-down tile coord)
    ///
    ///     Unity Y formula (matches OverlayLoader flippedY inversion):
    ///       worldX = gridOffset.x + (rel_x + effWidth/2)  / PPU
    ///       worldY = gridOffset.y + (zoneHeightTiles - 1) - (rel_y + effHeight) / PPU
    ///
    /// Maps to Python's load_buildings_from_json + the loop in entities that builds
    /// Building[] from templates + instances at game startup.
    /// </summary>
    public partial class BuildingLoader : MonoBehaviour
    {
        // Subdir + filename now owned by JsonFileBuildingInstanceRepository.
        private const float PPU = 32f;

        [Header("References")]
        [Tooltip("Catalog of all BuildingTemplateData assets. Created by BuildingImporter.")]
        [SerializeField] private BuildingCatalog _catalog;

        [Tooltip("ZoneManager used to resolve zone names to world-space offsets.")]
        [SerializeField] private ZoneManager _zoneManager;

        [Tooltip("Parent transform for all spawned building GameObjects. " +
                 "If null, buildings are parented to this transform.")]
        [SerializeField] private Transform _buildingsRoot;

        [Header("Settings")]
        [Tooltip("Physics layer index for spawned buildings. 11 = World (matches project convention).")]
        [SerializeField] private int _buildingPhysicsLayer = 11;

        [Tooltip("Load buildings automatically in Start. " +
                 "Set false to call LoadBuildings() manually from GameBootstrap.")]
        [SerializeField] private bool _autoLoad = true;

        private readonly List<BuildingObject> _spawnedBuildings = new List<BuildingObject>();

        /// <summary>All currently spawned BuildingObjects managed by this loader.</summary>
        public IReadOnlyList<BuildingObject> SpawnedBuildings => _spawnedBuildings;

        /// <summary>
        /// Catalog of all building templates known to this loader. Exposed so
        /// designer-driven systems (e.g. the Map Editor biome generator) can
        /// filter and spawn templates without re-implementing catalog access.
        /// </summary>
        public BuildingCatalog Catalog => _catalog;

        // Repository handle. Tests inject an InMemoryBuildingInstanceRepository
        // through SetRepository(); production paths fall back to the JSON
        // file backend on first use.
        private IBuildingInstanceRepository _repository;

        public void SetRepository(IBuildingInstanceRepository repository) => _repository = repository;

        private IBuildingInstanceRepository ResolveRepository()
            => _repository ?? (_repository = new JsonFileBuildingInstanceRepository());

        // ── Programmatic setup ──────────────────────────────────────────────────────

        /// <summary>
        /// Wire references from code (e.g. GameplaySceneSetup) and disable auto-load
        /// so the caller can invoke <see cref="LoadBuildings"/> at the right time.
        /// </summary>
        public void Initialize(BuildingCatalog catalog, ZoneManager zoneManager = null)
        {
            _catalog   = catalog;
            _autoLoad  = false;
            if (zoneManager != null) _zoneManager = zoneManager;
        }

        // ── Unity lifecycle ────────────────────────────────────────────────────────

        private void Start()
        {
            if (_autoLoad)
                LoadBuildings();
        }

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Parse buildings_instances.json and spawn one BuildingObject per entry.
        /// Clears previously spawned buildings first (safe to call multiple times).
        /// Synchronous wrapper that drains the progressive coroutine in one shot.
        /// </summary>
        public void LoadBuildings()
        {
            // Progressive iterator only yields plain `null` between batches, so
            // MoveNext drains the entire pipeline synchronously without any
            // frame waits — same end state as the previous monolithic loop.
            var iter = LoadBuildingsProgressively(null);
            while (iter.MoveNext()) { }
        }

        /// <summary>
        /// Coroutine variant of <see cref="LoadBuildings"/> that yields between
        /// sub-stages and reports a label via <paramref name="reportStage"/>
        /// (typically wired to <c>LoadingReporter.ReportStage</c>) so the loading
        /// screen can show "Parsing building data" → "Spawning building
        /// instances" → "Linking building colliders" with the bar advancing
        /// between them. Mid-pass yields fire every BUILDINGS_PER_BATCH
        /// instances so the spawn loop never freezes the loading screen even
        /// for very dense maps.
        /// </summary>
        public System.Collections.IEnumerator LoadBuildingsProgressively(System.Action<string> reportStage)
        {
            ClearSpawned();

            if (_catalog == null)
            {
                Debug.LogError("[BuildingLoader] BuildingCatalog not assigned.", this);
                yield break;
            }

            if (_zoneManager == null)
            {
                _zoneManager = FindObjectOfType<ZoneManager>();
                if (_zoneManager == null)
                {
                    Debug.LogError("[BuildingLoader] ZoneManager not found in scene.", this);
                    yield break;
                }
            }

            // ── Pass 1: parse JSON ──────────────────────────────────────────
            reportStage?.Invoke("Parsing building data");
            yield return null;
            string json = ResolveRepository().ReadRawJson(WorldId.Base);
            if (json == null)
            {
                Debug.LogWarning($"[BuildingLoader] No instances file in repository for {WorldId.Base}.");
                yield break;
            }
            var instances = ParseInstances(json);
            if (instances.Count == 0)
            {
                Debug.Log("[BuildingLoader] No building instances found in JSON.");
                yield break;
            }

            // ── Pass 2: spawn instances in batches ──────────────────────────
            // BUILDINGS_PER_BATCH yields once for every batch so the loading
            // screen repaints mid-spawn on dense maps (e.g. 300+ instances).
            // 60 was picked empirically: small enough to keep frame time well
            // below 50 ms on the heaviest single-batch instantiate cost,
            // large enough to keep total yield count under ~6 for typical maps.
            const int BUILDINGS_PER_BATCH = 60;
            reportStage?.Invoke("Spawning building instances");
            yield return null;
            int spawned = 0;
            int errors  = 0;
            int processed = 0;
            foreach (var inst in instances)
            {
                try
                {
                    if (SpawnInstance(inst))
                        spawned++;
                }
                catch (System.Exception ex)
                {
                    errors++;
                    Debug.LogWarning($"[BuildingLoader] Failed to spawn instance id={inst.Id}: {ex.Message}");
                }
                processed++;
                if (processed % BUILDINGS_PER_BATCH == 0) yield return null;
            }

            // ── Pass 3: collision grids ─────────────────────────────────────
            // Wires per-cell BoxCollider2D children onto every painted grid,
            // including any inline / per-image / per-instance overrides. With
            // the no-default-footprint rule, this is the ONLY source of
            // building colliders.
            reportStage?.Invoke("Linking building colliders");
            yield return null;
            var collisionLoader = FindObjectOfType<BuildingCollisionLoader>();
            if (collisionLoader != null)
                collisionLoader.ApplyCollisionGrids();

            Debug.Log($"[BuildingLoader] Spawned {spawned}/{instances.Count} building instances ({errors} errors).");
        }

        /// <summary>
        /// Remove all building GameObjects previously spawned by this loader,
        /// PLUS any orphan <see cref="BuildingObject"/> currently parented under
        /// <c>_buildingsRoot</c>. The orphan sweep is critical: the runtime
        /// Buildings editor (F10) creates BuildingObjects through its own
        /// placement / fill / erase-undo paths and historically did not register
        /// them with this loader's <see cref="_spawnedBuildings"/> list, so a
        /// strict list-only Clear left those instances alive across map-slot
        /// switches. They then leaked into the next slot's save (the editor
        /// serialises via <c>FindObjectsOfType&lt;BuildingObject&gt;()</c>),
        /// causing buildings placed in one map to appear in every other map —
        /// the regression this method now defends against.
        /// </summary>
        public void ClearSpawned()
        {
            Transform root = _buildingsRoot != null ? _buildingsRoot : transform;
            if (root != null)
            {
                var inScene = root.GetComponentsInChildren<BuildingObject>(includeInactive: true);
                for (int i = 0; i < inScene.Length; i++)
                {
                    if (inScene[i] != null)
                        DestroyBuildingGameObject(inScene[i].gameObject);
                }
            }
            // Cover the (rare) case where a tracked building was reparented out
            // of `_buildingsRoot` after spawn — still owned by us, still must die.
            foreach (var b in _spawnedBuildings)
            {
                if (b != null)
                    DestroyBuildingGameObject(b.gameObject);
            }
            _spawnedBuildings.Clear();
        }

        // EditMode-safe destroy: <see cref="Object.Destroy"/> is deferred to the
        // next frame, which never ticks inside EditMode tests, leaving the
        // GameObject technically alive for the rest of the test. Production
        // (Play mode) keeps the deferred semantics — only EditMode falls back
        // to <see cref="Object.DestroyImmediate"/> so test assertions about
        // post-clear state can read the truth synchronously.
        private static void DestroyBuildingGameObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else                       DestroyImmediate(go);
        }

        /// <summary>
        /// Register a <see cref="BuildingObject"/> created outside this loader
        /// (e.g. via the runtime Buildings editor's place / fill / undo paths)
        /// so <see cref="ClearSpawned"/>, <see cref="SpawnedBuildings"/> and
        /// dependent systems (<c>ResurrectionZoneAutoBinder</c>,
        /// <c>GameplaySceneSetup</c>) see the same set of buildings the loader
        /// itself spawned. Idempotent: a second call with the same instance is
        /// a no-op so callers don't have to track which path they came from.
        /// </summary>
        public void RegisterPlacedBuilding(BuildingObject building)
        {
            if (building == null) return;
            if (_spawnedBuildings.Contains(building)) return;
            _spawnedBuildings.Add(building);
        }

        /// <summary>
        /// Remove every previously-spawned building whose <c>InstanceId</c> is
        /// at or above <paramref name="instanceIdFloor"/>. The biome generator
        /// uses this to wipe its own previous run before re-scattering, without
        /// disturbing the data-driven instances loaded from JSON (whose IDs sit
        /// well below the reserved biome range).
        /// </summary>
        public int ClearGeneratedAbove(int instanceIdFloor)
        {
            int removed = 0;
            for (int i = _spawnedBuildings.Count - 1; i >= 0; i--)
            {
                var b = _spawnedBuildings[i];
                if (b != null && b.InstanceId >= instanceIdFloor)
                {
                    Destroy(b.gameObject);
                    _spawnedBuildings.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

    }
}