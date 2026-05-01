using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void EnsureCollTile(BuildingObject building, int row, int col, int rows, int cols)
        {
            string childName = $"{CollTilePrefix}{row}_{col}";
            Transform tileTransform = building.transform.Find(childName);
            if (tileTransform == null)
                tileTransform = TryReusePooledCollTile(building.transform, childName);

            if (tileTransform == null)
            {
                var tileGo = new GameObject(childName);
                tileGo.transform.SetParent(building.transform, worldPositionStays: false);
                tileTransform = tileGo.transform;
            }

            // Single source of truth: derive the cell's WORLD rect from the
            // building's own helper so this BoxCollider2D, the visual overlay
            // and the click-to-paint hit test all share one coordinate system.
            // Then convert center+size into the building's local space (taking
            // its lossy scale into account so non-uniform scales are correct).
            if (!building.TryGetWorldCellRect(row, col, rows, cols, out var worldCell))
            {
                Debug.LogWarning(
                    $"[BuildingsRuntimeEditor] Could not compute world cell rect for {building.name} cell ({row},{col}) — collider skipped.",
                    building);
                tileTransform.gameObject.SetActive(false);
                return;
            }

            Vector3 worldCenter = new Vector3(worldCell.center.x, worldCell.center.y, 0f);
            Vector3 localCenter = building.transform.InverseTransformPoint(worldCenter);
            Vector3 lossy = building.transform.lossyScale;
            float invSx = Mathf.Abs(lossy.x) > 0.0001f ? 1f / lossy.x : 1f;
            float invSy = Mathf.Abs(lossy.y) > 0.0001f ? 1f / lossy.y : 1f;
            Vector2 localSize = new Vector2(worldCell.width * invSx, worldCell.height * invSy);

            tileTransform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            tileTransform.localRotation = Quaternion.identity;
            tileTransform.localScale = Vector3.one;
            tileTransform.gameObject.layer = ResolveCollisionLayer();
            tileTransform.gameObject.SetActive(true);

            var box = tileTransform.GetComponent<BoxCollider2D>();
            if (box == null)
                box = tileTransform.gameObject.AddComponent<BoxCollider2D>();
            box.enabled = true;
            box.isTrigger = false; // explicit: must block movement, not just detect
            box.offset = Vector2.zero;
            box.size = localSize;
        }

        private static Transform TryReusePooledCollTile(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.name.StartsWith(PooledCollTilePrefix, StringComparison.Ordinal))
                    continue;

                child.name = childName;
                return child;
            }

            return null;
        }

        private static Vector2 GetBuildingLocalSpriteSize(BuildingObject building)
        {
            float width = 0f;
            float height = 0f;

            var footprint = building.transform.Find("Footprint")?.GetComponent<SpriteRenderer>();
            if (footprint != null && footprint.sprite != null)
            {
                width = Mathf.Max(width, footprint.sprite.rect.width / 32f);
                height += footprint.sprite.rect.height / 32f;
            }

            var canopy = building.transform.Find("Canopy")?.GetComponent<SpriteRenderer>();
            if (canopy != null && canopy.sprite != null)
            {
                width = Mathf.Max(width, canopy.sprite.rect.width / 32f);
                height += canopy.sprite.rect.height / 32f;
            }

            var mainCollider = building.GetComponent<BoxCollider2D>();
            if (mainCollider != null)
            {
                width = Mathf.Max(width, mainCollider.size.x);
                height = Mathf.Max(height, mainCollider.offset.y + mainCollider.size.y * 0.5f);
            }

            return new Vector2(
                Mathf.Max(0.0001f, width),
                Mathf.Max(0.0001f, height));
        }

        private static void ClearCollisionTiles(BuildingObject building)
        {
            if (building == null) return;

            int pooledIndex = 0;
            for (int i = building.transform.childCount - 1; i >= 0; i--)
            {
                var child = building.transform.GetChild(i);
                if (!child.name.StartsWith(CollTilePrefix, StringComparison.Ordinal) &&
                    !child.name.StartsWith(PooledCollTilePrefix, StringComparison.Ordinal))
                    continue;

                child.name = $"{PooledCollTilePrefix}{pooledIndex++}";
                var box = child.GetComponent<BoxCollider2D>();
                if (box != null)
                    box.enabled = false;
                child.gameObject.SetActive(false);
            }
        }

        private static void RestoreDefaultColliderState(BuildingObject building)
        {
            if (building == null || building.Template == null) return;
            var mainCollider = building.GetComponent<BoxCollider2D>();
            if (mainCollider != null)
                mainCollider.enabled = building.Template.solid;
        }

        private static ColliderGridData ResampleGrid(ColliderGridData source, int targetW_px, int targetH_px)
        {
            if (source == null) return null;
            if (source.gridRefSize == Vector2Int.zero ||
                (source.gridRefSize.x == targetW_px && source.gridRefSize.y == targetH_px))
            {
                return CloneGrid(source);
            }

            int newCols = Mathf.Max(1, Mathf.CeilToInt(targetW_px / 32f));
            int newRows = Mathf.Max(1, Mathf.CeilToInt(targetH_px / 32f));
            if (newCols == source.width && newRows == source.height)
            {
                var sameSizeClone = CloneGrid(source);
                sameSizeClone.gridRefSize = new Vector2Int(targetW_px, targetH_px);
                return sameSizeClone;
            }

            var newGrid = CreateEmptyGrid(newCols, newRows, new Vector2Int(targetW_px, targetH_px));
            for (int dr = 0; dr < newRows; dr++)
            {
                for (int dc = 0; dc < newCols; dc++)
                {
                    float srcRowStart = (float)dr / newRows * source.height;
                    float srcRowEnd = (float)(dr + 1) / newRows * source.height;
                    float srcColStart = (float)dc / newCols * source.width;
                    float srcColEnd = (float)(dc + 1) / newCols * source.width;

                    bool solid = false;
                    for (int sr = Mathf.FloorToInt(srcRowStart); sr < Mathf.CeilToInt(srcRowEnd) && sr < source.height; sr++)
                    {
                        for (int sc = Mathf.FloorToInt(srcColStart); sc < Mathf.CeilToInt(srcColEnd) && sc < source.width; sc++)
                        {
                            if (source.collision != null &&
                                sr < source.collision.Length &&
                                source.collision[sr] != null &&
                                sc < source.collision[sr].Length &&
                                source.collision[sr][sc] == "#")
                            {
                                solid = true;
                                break;
                            }
                        }
                        if (solid) break;
                    }
                    newGrid.collision[dr][dc] = solid ? "#" : ".";
                }
            }

            return newGrid;
        }

        private static ColliderGridData ParseColliderGrid(Dictionary<string, object> dict)
        {
            if (dict == null) return null;
            int width = dict.TryGetValue("width", out var w) ? Convert.ToInt32(w) : 0;
            int height = dict.TryGetValue("height", out var h) ? Convert.ToInt32(h) : 0;
            if (width <= 0 || height <= 0) return null;

            var grid = CreateEmptyGrid(width, height, Vector2Int.zero);
            if (dict.TryGetValue("collision", out var collisionRaw) && collisionRaw is List<object> rows)
            {
                for (int row = 0; row < Mathf.Min(height, rows.Count); row++)
                {
                    if (!(rows[row] is List<object> cols)) continue;
                    for (int col = 0; col < Mathf.Min(width, cols.Count); col++)
                        grid.collision[row][col] = cols[col]?.ToString() == "#" ? "#" : ".";
                }
            }

            if (dict.TryGetValue("grid_ref_size", out var refRaw) && refRaw is List<object> refList && refList.Count >= 2)
            {
                grid.gridRefSize = new Vector2Int(Convert.ToInt32(refList[0]), Convert.ToInt32(refList[1]));
            }

            return grid;
        }

        private static void LoadCollisionImageStore(string path, Dictionary<string, ColliderGridData> destination)
        {
            destination.Clear();
            if (!File.Exists(path)) return;

            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) return;

            foreach (var kvp in root)
            {
                if (!(kvp.Value is Dictionary<string, object> dict)) continue;
                var grid = ParseColliderGrid(dict);
                // Skip all-walkable JSON entries: they are unintentional placeholders
                // written by old editor versions into the CG (per-image) store.
                // Per-instance (CU) stores keep all-walkable grids so that an
                // intentional "reset all to walkable" survives across sessions.
                if (grid != null && GridHasSolidCells(grid))
                    destination[NormalizeAssetPath(kvp.Key)] = grid;
            }
        }

        private static void LoadCollisionInstanceStore(string path, Dictionary<int, ColliderGridData> destination)
        {
            if (!File.Exists(path)) return;

            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) return;

            foreach (var kvp in root)
            {
                if (!(kvp.Value is Dictionary<string, object> dict)) continue;
                var grid = ParseColliderGrid(dict);
                // Per-instance (CU) grids: no solid-cell filter. An all-walkable grid
                // here is intentional (e.g. produced by "Reset all to walkable").
                if (grid != null && int.TryParse(kvp.Key, out int id))
                    destination[id] = grid;
            }
        }

        private static void LoadInlineInstanceColliders(string path, Dictionary<int, ColliderGridData> destination)
        {
            if (!File.Exists(path)) return;

            var raw = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as List<object>;
            if (raw == null) return;

            for (int i = 0; i < raw.Count; i++)
            {
                if (!(raw[i] is Dictionary<string, object> entry)) continue;
                if (!entry.TryGetValue("id", out var idRaw) || idRaw == null) continue;
                if (!entry.TryGetValue("overrides", out var overridesRaw) || !(overridesRaw is Dictionary<string, object> overrides)) continue;
                if (!overrides.TryGetValue("collision_override", out var collisionRaw) || !(collisionRaw is Dictionary<string, object> collisionDict)) continue;
                var grid = ParseColliderGrid(collisionDict);
                // Per-instance (CU) inline grids: no solid-cell filter (same as
                // LoadCollisionInstanceStore — all-walkable may be intentional).
                if (grid != null)
                    destination[Convert.ToInt32(idRaw)] = grid;
            }
        }

    }
}