using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Data;

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
    public class BuildingLoader : MonoBehaviour
    {
        private const string STREAMING_SUBFOLDER   = "Buildings";
        private const string INSTANCES_FILENAME    = "buildings_instances.json";
        private const float  PPU                   = 32f;

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

            string jsonPath = Path.Combine(
                Application.streamingAssetsPath, STREAMING_SUBFOLDER, INSTANCES_FILENAME);

            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"[BuildingLoader] Instances file not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
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

        // ── Spawning ───────────────────────────────────────────────────────────────

        private bool SpawnInstance(BuildingInstanceDto inst)
        {
            var template = _catalog.GetById(inst.TemplateId);
            if (template == null)
            {
                Debug.LogWarning(
                    $"[BuildingLoader] Template id={inst.TemplateId} not found " +
                    $"(instance id={inst.Id}, zone={inst.Zone}).");
                return false;
            }

            if (!_zoneManager.TryGetZone(inst.Zone, out var zoneDef))
            {
                Debug.LogWarning(
                    $"[BuildingLoader] Zone '{inst.Zone}' not registered in ZoneManager " +
                    $"(instance id={inst.Id}). Add it to the ZoneManager component.");
                return false;
            }

            // Effective pixel dimensions (instance override or template default)
            int effW = (inst.ScaleOverride.x > 0) ? inst.ScaleOverride.x : template.originalScale.x;
            int effH = (inst.ScaleOverride.y > 0) ? inst.ScaleOverride.y : template.originalScale.y;

            // ── Coordinate conversion ────────────────────────────────────────────
            // Python: top-left of building at (zone_gridOffset_tiles + rel_px/32), Y-down.
            // Unity:  bottom-center of building, Y-up.
            //
            // unityX = gridOffset.x + (rel_x + effW/2) / PPU
            // unityY = gridOffset.y + (zoneHeight - 1) - (rel_y + effH) / PPU
            //   (mirrors OverlayLoader.flippedY = zoneHeight-1 - rowIndex)
            int   zoneH  = _zoneManager.ZoneHeightTiles;
            float worldX = zoneDef.gridOffset.x + (inst.RelX + effW * 0.5f) / PPU;
            float worldY = zoneDef.gridOffset.y + (zoneH - 1) - (inst.RelY + effH) / PPU;

            // ── Spawn ────────────────────────────────────────────────────────────
            Transform root = _buildingsRoot != null ? _buildingsRoot : transform;

            var go = new GameObject($"Building_{inst.Id}_{template.name}");
            go.transform.SetParent(root, worldPositionStays: false);
            go.transform.position = new Vector3(worldX, worldY, 0f);
            go.layer = _buildingPhysicsLayer;

            var bObj = go.AddComponent<BuildingObject>();
            bObj.ZoneName           = inst.Zone;
            bObj.InstanceId         = inst.Id;
            bObj.Apply(template, inst.ScaleOverride, inst.SplitRatioOverride);

            _spawnedBuildings.Add(bObj);
            return true;
        }

        // ── JSON parsing ────────────────────────────────────────────────────────────

        private static List<BuildingInstanceDto> ParseInstances(string json)
        {
            var result = new List<BuildingInstanceDto>();

            var raw = MiniJsonRuntime.Deserialize(json) as List<object>;
            if (raw == null)
            {
                Debug.LogError("[BuildingLoader] Failed to parse instances JSON — expected a JSON array.");
                return result;
            }

            foreach (var item in raw)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;

                var inst = new BuildingInstanceDto
                {
                    Id               = GetInt(dict, "id"),
                    TemplateId       = GetInt(dict, "template_id"),
                    Zone             = GetString(dict, "zone", "Lobby"),
                    RelX             = GetInt(dict, "rel_x"),
                    RelY             = GetInt(dict, "rel_y"),
                    SplitRatioOverride = -1f,          // default: no override
                };

                // Optional 'overrides' block
                if (dict.TryGetValue("overrides", out var ovRaw) &&
                    ovRaw is Dictionary<string, object> overrides)
                {
                    if (overrides.TryGetValue("scale", out var scaleRaw) &&
                        scaleRaw is List<object> scaleList && scaleList.Count >= 2)
                    {
                        inst.ScaleOverride = new Vector2Int(
                            Convert.ToInt32(scaleList[0]),
                            Convert.ToInt32(scaleList[1]));
                    }

                    if (overrides.TryGetValue("split_ratio", out var srRaw))
                        inst.SplitRatioOverride = Convert.ToSingle(srRaw);
                }

                result.Add(inst);
            }

            return result;
        }

        // ── JSON helpers ────────────────────────────────────────────────────────────

        private static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToInt32(v);
            return fallback;
        }

        private static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d.TryGetValue(key, out var v) && v is string s)
                return s;
            return fallback;
        }

        // ── DTO ─────────────────────────────────────────────────────────────────────

        /// <summary>Parsed representation of one buildings_instances.json entry.</summary>
        private struct BuildingInstanceDto
        {
            public int        Id;
            public int        TemplateId;
            public string     Zone;
            public int        RelX;
            public int        RelY;
            /// <summary>(0,0) = use template.originalScale.</summary>
            public Vector2Int ScaleOverride;
            /// <summary>Negative = use template.splitRatio.</summary>
            public float      SplitRatioOverride;
        }
    }
}
