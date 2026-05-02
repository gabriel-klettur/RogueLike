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
        /// </summary>
        public void LoadBuildings()
        {
            ClearSpawned();

            if (_catalog == null)
            {
                Debug.LogError("[BuildingLoader] BuildingCatalog not assigned.", this);
                return;
            }

            if (_zoneManager == null)
            {
                _zoneManager = FindObjectOfType<ZoneManager>();
                if (_zoneManager == null)
                {
                    Debug.LogError("[BuildingLoader] ZoneManager not found in scene.", this);
                    return;
                }
            }

            string json = ResolveRepository().ReadRawJson(WorldId.Base);
            if (json == null)
            {
                Debug.LogWarning($"[BuildingLoader] No instances file in repository for {WorldId.Base}.");
                return;
            }

            var instances = ParseInstances(json);
            if (instances.Count == 0)
            {
                Debug.Log("[BuildingLoader] No building instances found in JSON.");
                return;
            }

            int spawned = 0;
            int errors  = 0;
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
            }

            var collisionLoader = FindObjectOfType<BuildingCollisionLoader>();
            if (collisionLoader != null)
                collisionLoader.ApplyCollisionGrids();

            Debug.Log($"[BuildingLoader] Spawned {spawned}/{instances.Count} building instances ({errors} errors).");
        }

        /// <summary>
        /// Remove all building GameObjects previously spawned by this loader.
        /// </summary>
        public void ClearSpawned()
        {
            foreach (var b in _spawnedBuildings)
            {
                if (b != null)
                    Destroy(b.gameObject);
            }
            _spawnedBuildings.Clear();
        }

    }
}