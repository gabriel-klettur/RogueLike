using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Loads fine-grained collision grids from StreamingAssets/Buildings/ and applies
    /// per-cell BoxCollider2D tiles to BuildingObjects already in the scene.
    ///
    /// Resolution order (matches Python):
    ///   1. Per-building-instance override (buildings_collisions_by_building_instance_id.json)
    ///   2. Per-spawn-id override (buildings_collisions_by_spawn_id.json)
    ///   3. Per-image default (buildings_collisions_by_image.json)
    ///
    /// Collision grid format: 2D array of "." (walkable) and "#" (solid).
    /// Each "#" cell produces a child BoxCollider2D tile sized (tileW, tileH) in world units.
    ///
    /// Must run AFTER BuildingLoader has spawned all BuildingObjects.
    /// Call ApplyCollisionGrids() from GameplaySceneSetup or BuildingLoader post-load.
    /// </summary>
    public partial class BuildingCollisionLoader : MonoBehaviour
    {
        private const float PPU = 32f;
        private const string STREAMING_SUBFOLDER = "Buildings";
        private const string BY_IMAGE_FILE = "buildings_collisions_by_image.json";
        private const string BY_INSTANCE_FILE = "buildings_collisions_by_building_instance_id.json";
        private const string BY_SPAWN_FILE = "buildings_collisions_by_spawn_id.json";
        private const string INSTANCES_FILE = "buildings_instances.json";

        [Tooltip("Physics layer for collision tile children. 11 = World.")]
        [SerializeField] private int _collisionLayer = 11;

        public int CollisionLayer => _collisionLayer;

        // Loaded data
        private Dictionary<string, CollisionGrid> _byImage;
        private Dictionary<string, CollisionGrid> _byInstanceId;
        private Dictionary<string, CollisionGrid> _bySpawnId;
        private Dictionary<string, CollisionGrid> _inlineInstanceOverrides;

        private bool _loaded;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Load collision JSON files and apply grids to all BuildingObjects in the scene.
        /// Safe to call multiple times; reloads data each time.
        /// </summary>
        public void ApplyCollisionGrids()
        {
            LoadData();

            var buildings = FindObjectsOfType<BuildingObject>();
            int applied = 0;
            foreach (var bObj in buildings)
            {
                if (TryApplyGrid(bObj))
                    applied++;
            }

            Debug.Log($"[BuildingCollisionLoader] Applied collision grids to {applied}/{buildings.Length} buildings.");
        }

        /// <summary>
        /// Apply a collision grid to a specific building. Call after the building is spawned.
        /// </summary>
        public bool TryApplyGrid(BuildingObject bObj)
        {
            if (bObj == null) return false;
            if (!_loaded) LoadData();

            ClearCollisionTiles(bObj);
            RestoreDefaultColliderState(bObj);

            var grid = ResolveGrid(bObj);
            if (grid == null) return false;

            if (!HasSolidCells(grid))
            {
                var mainCollider = bObj.GetComponent<BoxCollider2D>();
                if (mainCollider != null)
                    mainCollider.enabled = false;
                return true;
            }

            ApplyGridToBuilding(bObj, grid);
            return true;
        }

        // ------------------------------------------------------------------
        // Data Loading
        // ------------------------------------------------------------------

        private void LoadData()
        {
            _byImage = LoadCollisionFile(ResolveCollisionFilePath(BY_IMAGE_FILE, isGlobalData: true));
            _byInstanceId = LoadCollisionFile(ResolveCollisionFilePath(BY_INSTANCE_FILE, isGlobalData: false));
            _bySpawnId = LoadCollisionFile(ResolveCollisionFilePath(BY_SPAWN_FILE, isGlobalData: false));
            _inlineInstanceOverrides = LoadInlineInstanceOverrides(ResolveInstancesFilePath());
            _loaded = true;
        }

        private static Dictionary<string, CollisionGrid> LoadCollisionFile(string path)
        {
            var result = new Dictionary<string, CollisionGrid>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;

            string json = File.ReadAllText(path);
            var root = MiniJsonRuntime.Deserialize(json) as Dictionary<string, object>;
            if (root == null) return result;

            foreach (var kvp in root)
            {
                if (kvp.Value is Dictionary<string, object> entry)
                {
                    var grid = ParseGrid(entry);
                    if (grid != null)
                        result[kvp.Key] = grid;
                }
            }
            return result;
        }

        private static Dictionary<string, CollisionGrid> LoadInlineInstanceOverrides(string path)
        {
            var result = new Dictionary<string, CollisionGrid>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;

            var raw = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as List<object>;
            if (raw == null) return result;

            foreach (var item in raw)
            {
                if (!(item is Dictionary<string, object> dict)) continue;
                if (!dict.TryGetValue("id", out var idObj) || idObj == null) continue;
                if (!dict.TryGetValue("overrides", out var overridesObj) ||
                    !(overridesObj is Dictionary<string, object> overrides))
                    continue;
                if (!overrides.TryGetValue("collision_override", out var collisionOverrideObj) ||
                    !(collisionOverrideObj is Dictionary<string, object> collisionOverride))
                    continue;

                var grid = ParseGrid(collisionOverride);
                if (grid != null)
                    result[Convert.ToInt32(idObj).ToString()] = grid;
            }

            return result;
        }

        private static CollisionGrid ParseGrid(Dictionary<string, object> dict)
        {
            int width = dict.TryGetValue("width", out var w) ? Convert.ToInt32(w) : 0;
            int height = dict.TryGetValue("height", out var h) ? Convert.ToInt32(h) : 0;
            if (width <= 0 || height <= 0) return null;

            if (!dict.TryGetValue("collision", out var collObj) ||
                !(collObj is List<object> rows))
                return null;

            var collision = new string[rows.Count][];
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r] is List<object> cols)
                {
                    collision[r] = new string[cols.Count];
                    for (int c = 0; c < cols.Count; c++)
                        collision[r][c] = cols[c]?.ToString() ?? ".";
                }
                else
                {
                    collision[r] = new string[width];
                    for (int c = 0; c < width; c++)
                        collision[r][c] = ".";
                }
            }

            Vector2Int gridRefSize = Vector2Int.zero;
            if (dict.TryGetValue("grid_ref_size", out var grs) && grs is List<object> grsList && grsList.Count >= 2)
            {
                gridRefSize = new Vector2Int(Convert.ToInt32(grsList[0]), Convert.ToInt32(grsList[1]));
            }

            return new CollisionGrid
            {
                width = width,
                height = height,
                collision = collision,
                gridRefSize = gridRefSize
            };
        }

        // ------------------------------------------------------------------
        // Resolution
        // ------------------------------------------------------------------

        private CollisionGrid ResolveGrid(BuildingObject bObj)
        {
            bool usePerInstanceScope = string.Equals(
                bObj.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase);

            if (usePerInstanceScope)
            {
                string instanceKey = bObj.InstanceId.ToString();
                if (_inlineInstanceOverrides != null &&
                    _inlineInstanceOverrides.TryGetValue(instanceKey, out var inlineOverride))
                    return inlineOverride;

                if (_byInstanceId != null && _byInstanceId.TryGetValue(instanceKey, out var byInst))
                    return byInst;
            }

            // Priority 2: Per-spawn-id (future use; currently empty in base world)
            // Would need spawn_id on BuildingObject — skip for now

            if (bObj.Template != null && _byImage != null)
            {
                string assetKey = bObj.Template.sourceImagePath;
                if (!string.IsNullOrEmpty(assetKey))
                {
                    if (_byImage.TryGetValue(assetKey, out var byImg))
                        return byImg;
                    string normalizedKey = assetKey.Replace("\\", "/");
                    if (_byImage.TryGetValue(normalizedKey, out byImg))
                        return byImg;
                    string windowsKey = assetKey.Replace("/", "\\");
                    if (_byImage.TryGetValue(windowsKey, out byImg))
                        return byImg;
                }
            }

            return null;
        }

        private static bool HasSolidCells(CollisionGrid grid)
        {
            foreach (var row in grid.collision)
            {
                foreach (var cell in row)
                {
                    if (cell == "#") return true;
                }
            }

            return false;
        }

        private static string ResolveCollisionFilePath(string fileName, bool isGlobalData)
        {
            foreach (var candidate in GetCollisionFileCandidates(fileName, isGlobalData))
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string ResolveInstancesFilePath()
        {
            foreach (var candidate in GetInstanceFileCandidates())
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<string> GetCollisionFileCandidates(string fileName, bool isGlobalData)
        {
            yield return Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER, fileName);

            string repoRoot = TryGetRepoRootPath();
            if (string.IsNullOrEmpty(repoRoot)) yield break;

            if (isGlobalData)
                yield return Path.Combine(repoRoot, "python", "data", "buildings", fileName);
            else
                yield return Path.Combine(repoRoot, "python", "data", "worlds", "base", "buildings", fileName);
        }

        private static IEnumerable<string> GetInstanceFileCandidates()
        {
            yield return Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER, INSTANCES_FILE);

            string repoRoot = TryGetRepoRootPath();
            if (string.IsNullOrEmpty(repoRoot)) yield break;

            yield return Path.Combine(repoRoot, "python", "data", "worlds", "base", "buildings", INSTANCES_FILE);
        }

        private static string TryGetRepoRootPath()
        {
            try
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            }
            catch
            {
                return null;
            }
        }

        // Grid Application + ResampleGrid + CollisionGrid are in BuildingCollisionLoader.Grid.cs
    }
}
