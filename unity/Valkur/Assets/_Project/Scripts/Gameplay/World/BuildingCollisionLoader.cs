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
    public class BuildingCollisionLoader : MonoBehaviour
    {
        private const float PPU = 32f;
        private const string STREAMING_SUBFOLDER = "Buildings";
        private const string BY_IMAGE_FILE = "buildings_collisions_by_image.json";
        private const string BY_INSTANCE_FILE = "buildings_collisions_by_building_instance_id.json";
        private const string BY_SPAWN_FILE = "buildings_collisions_by_spawn_id.json";

        [Tooltip("Physics layer for collision tile children. 11 = World.")]
        [SerializeField] private int _collisionLayer = 11;

        // Loaded data
        private Dictionary<string, CollisionGrid> _byImage;
        private Dictionary<string, CollisionGrid> _byInstanceId;
        private Dictionary<string, CollisionGrid> _bySpawnId;

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
            if (!_loaded) LoadData();

            var grid = ResolveGrid(bObj);
            if (grid == null) return false;

            // Only apply if the grid contains at least one '#' cell
            bool hasSolid = false;
            foreach (var row in grid.collision)
            {
                foreach (var cell in row)
                {
                    if (cell == "#") { hasSolid = true; break; }
                }
                if (hasSolid) break;
            }
            if (!hasSolid) return false;

            ApplyGridToBuilding(bObj, grid);
            return true;
        }

        // ------------------------------------------------------------------
        // Data Loading
        // ------------------------------------------------------------------

        private void LoadData()
        {
            string basePath = Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER);
            _byImage = LoadCollisionFile(Path.Combine(basePath, BY_IMAGE_FILE));
            _byInstanceId = LoadCollisionFile(Path.Combine(basePath, BY_INSTANCE_FILE));
            _bySpawnId = LoadCollisionFile(Path.Combine(basePath, BY_SPAWN_FILE));
            _loaded = true;
        }

        private static Dictionary<string, CollisionGrid> LoadCollisionFile(string path)
        {
            var result = new Dictionary<string, CollisionGrid>();
            if (!File.Exists(path)) return result;

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
            // Priority 1: Per-instance ID
            string instanceKey = bObj.InstanceId.ToString();
            if (_byInstanceId != null && _byInstanceId.TryGetValue(instanceKey, out var byInst))
                return byInst;

            // Priority 2: Per-spawn-id (future use; currently empty in base world)
            // Would need spawn_id on BuildingObject — skip for now

            // Priority 3: Per-image (template asset path)
            if (bObj.Template != null && _byImage != null)
            {
                // Python keys use "assets/buildings/..." relative paths
                string assetKey = bObj.Template.sourceImagePath;
                if (!string.IsNullOrEmpty(assetKey) && _byImage.TryGetValue(assetKey, out var byImg))
                    return byImg;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Grid Application
        // ------------------------------------------------------------------

        private void ApplyGridToBuilding(BuildingObject bObj, CollisionGrid grid)
        {
            // Remove existing grid collider children (tag-based cleanup)
            for (int i = bObj.transform.childCount - 1; i >= 0; i--)
            {
                var child = bObj.transform.GetChild(i);
                if (child.name.StartsWith("CollTile_"))
                    Destroy(child.gameObject);
            }

            if (bObj.Template == null) return;

            // Effective pixel dimensions
            int origW = bObj.Template.originalScale.x;
            int origH = bObj.Template.originalScale.y;
            int effW = (bObj.ScaleOverride.x > 0) ? bObj.ScaleOverride.x : origW;
            int effH = (bObj.ScaleOverride.y > 0) ? bObj.ScaleOverride.y : origH;

            // Resample grid if dimensions changed
            var effectiveGrid = ResampleGrid(grid, effW, effH);

            int gridCols = effectiveGrid.width;
            int gridRows = effectiveGrid.height;

            // Tile size in pixels
            float tileW_px = (float)effW / gridCols;
            float tileH_px = (float)effH / gridRows;

            // Tile size in local units (before transform.localScale)
            float tileW_local = tileW_px / PPU / bObj.transform.localScale.x;
            float tileH_local = tileH_px / PPU / bObj.transform.localScale.y;

            // Total building size in local units
            float totalW_local = (float)origW / PPU;
            float totalH_local = (float)origH / PPU;

            int count = 0;
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols; col++)
                {
                    if (effectiveGrid.collision[row] == null || col >= effectiveGrid.collision[row].Length)
                        continue;
                    if (effectiveGrid.collision[row][col] != "#")
                        continue;

                    // Python grid: row 0 = top of image (Y-down)
                    // Unity local: Y=0 = bottom of building, Y increases upward
                    // localX: col * tileW_local, centered at building center (pivot is 0.5 horizontal)
                    float localX = (col + 0.5f) * tileW_local - totalW_local * 0.5f;
                    // localY: (gridRows - 1 - row) flips Y, * tileH_local, + half tile for center
                    float localY = (gridRows - 1 - row + 0.5f) * tileH_local;

                    var tileGo = new GameObject($"CollTile_{row}_{col}");
                    tileGo.transform.SetParent(bObj.transform, worldPositionStays: false);
                    tileGo.transform.localPosition = new Vector3(localX, localY, 0f);
                    tileGo.transform.localScale = Vector3.one;
                    tileGo.layer = _collisionLayer;

                    var box = tileGo.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(tileW_local, tileH_local);
                    count++;
                }
            }

            // Disable the original BoxCollider2D if collision grid is active
            var mainCollider = bObj.GetComponent<BoxCollider2D>();
            if (mainCollider != null && count > 0)
                mainCollider.enabled = false;
        }

        /// <summary>
        /// Resample collision grid when building is rescaled.
        /// Uses area-pooling: destination cell is "#" if ANY source cell in the block is "#".
        /// Matches Python's resample_collision_map().
        /// </summary>
        private static CollisionGrid ResampleGrid(CollisionGrid source, int targetW_px, int targetH_px)
        {
            // If grid_ref_size matches target (or no ref size), no resampling needed
            if (source.gridRefSize == Vector2Int.zero ||
                (source.gridRefSize.x == targetW_px && source.gridRefSize.y == targetH_px))
            {
                return source;
            }

            // New grid dimensions based on target pixel size / 32
            int newCols = Mathf.Max(1, Mathf.CeilToInt(targetW_px / 32f));
            int newRows = Mathf.Max(1, Mathf.CeilToInt(targetH_px / 32f));

            if (newCols == source.width && newRows == source.height)
                return source;

            var newCollision = new string[newRows][];
            for (int dr = 0; dr < newRows; dr++)
            {
                newCollision[dr] = new string[newCols];
                for (int dc = 0; dc < newCols; dc++)
                {
                    // Map destination cell back to source range
                    float srcRowStart = (float)dr / newRows * source.height;
                    float srcRowEnd = (float)(dr + 1) / newRows * source.height;
                    float srcColStart = (float)dc / newCols * source.width;
                    float srcColEnd = (float)(dc + 1) / newCols * source.width;

                    bool solid = false;
                    for (int sr = Mathf.FloorToInt(srcRowStart); sr < Mathf.CeilToInt(srcRowEnd) && sr < source.height; sr++)
                    {
                        for (int sc = Mathf.FloorToInt(srcColStart); sc < Mathf.CeilToInt(srcColEnd) && sc < source.width; sc++)
                        {
                            if (sr < source.collision.Length && source.collision[sr] != null &&
                                sc < source.collision[sr].Length && source.collision[sr][sc] == "#")
                            {
                                solid = true;
                                break;
                            }
                        }
                        if (solid) break;
                    }
                    newCollision[dr][dc] = solid ? "#" : ".";
                }
            }

            return new CollisionGrid
            {
                width = newCols,
                height = newRows,
                collision = newCollision,
                gridRefSize = new Vector2Int(targetW_px, targetH_px)
            };
        }

        // ------------------------------------------------------------------
        // Data types
        // ------------------------------------------------------------------

        private class CollisionGrid
        {
            public int width;
            public int height;
            public string[][] collision;
            public Vector2Int gridRefSize;
        }
    }
}
