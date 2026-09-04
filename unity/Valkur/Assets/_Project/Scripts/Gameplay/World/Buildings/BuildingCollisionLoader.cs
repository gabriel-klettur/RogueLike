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
        /// Drop every painted collision cell a building owns, making it walkable.
        ///
        /// <para>Exists for destruction: a felled tree that only hides its sprite leaves the
        /// forest solid and invisible, because the colliders are child objects of the
        /// building and outlive any renderer change. Static and public so
        /// <c>BuildingDurability</c> can reach it without holding a loader reference — the
        /// tiles belong to the building, not to the loader that painted them.</para>
        /// </summary>
        public static void ClearColliders(BuildingObject bObj)
        {
            if (bObj == null) return;
            ClearCollisionTiles(bObj);
            RestoreDefaultColliderState(bObj);
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

            // An authored grid with NO solid cells is treated differently by scope:
            //
            //   CG (per-image): treat as an unintentional placeholder. Keep the root
            //     collider enabled so buildings with empty/unpainted image-level JSON
            //     entries still block movement. This prevented 140/142 buildings from
            //     accidentally becoming walk-throughable.
            //
            //   CU (per-instance): treat as an intentional "reset to walkable" action
            //     (e.g. produced by BuildingsRuntimeEditor "Reset All to Walkable").
            //     Disable the root collider so physics matches the Buildings Editor's
            //     authored state — i.e. both systems agree the building is passable.
            if (!HasSolidCells(grid))
            {
                if (string.Equals(bObj.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase))
                {
                    var mainColl = bObj.GetComponent<BoxCollider2D>();
                    if (mainColl != null) mainColl.enabled = false;
                }
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

    }
}