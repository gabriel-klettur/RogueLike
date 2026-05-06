using System;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    public partial class BuildingCollisionLoader
    {
        private const string CollTilePrefix = "CollTile_";
        private const string PooledCollTilePrefix = "_PooledCollTile_";

        // ------------------------------------------------------------------
        // Grid Application
        // ------------------------------------------------------------------

        private void ApplyGridToBuilding(BuildingObject bObj, CollisionGrid grid)
        {
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

            int count = 0;
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols; col++)
                {
                    if (effectiveGrid.collision[row] == null || col >= effectiveGrid.collision[row].Length)
                        continue;
                    if (effectiveGrid.collision[row][col] != "#")
                        continue;
                    EnsureCollisionTile(bObj, row, col, gridRows, gridCols);
                    count++;
                }
            }

            // Disable the original BoxCollider2D if collision grid is active
            var mainCollider = bObj.GetComponent<BoxCollider2D>();
            if (mainCollider != null && count > 0)
                mainCollider.enabled = false;
        }

        private void EnsureCollisionTile(BuildingObject bObj, int row, int col, int rows, int cols)
        {
            string childName = $"{CollTilePrefix}{row}_{col}";
            Transform tileTransform = bObj.transform.Find(childName);
            if (tileTransform == null)
                tileTransform = TryReusePooledTile(bObj.transform, childName);

            if (tileTransform == null)
            {
                var tileGo = new GameObject(childName);
                tileGo.transform.SetParent(bObj.transform, worldPositionStays: false);
                tileTransform = tileGo.transform;
            }

            // Single source of truth: derive the cell's WORLD rect from the
            // building helper, then convert to local space. Matches the editor
            // (BuildingsRuntimeEditor.EnsureCollTile) and the visual overlay.
            if (!bObj.TryGetWorldCellRect(row, col, rows, cols, out var worldCell))
            {
                Debug.LogWarning(
                    $"[BuildingCollisionLoader] Could not compute world cell rect for {bObj.name} cell ({row},{col}) — collider skipped.",
                    bObj);
                tileTransform.gameObject.SetActive(false);
                return;
            }

            Vector3 worldCenter = new Vector3(worldCell.center.x, worldCell.center.y, 0f);
            Vector3 localCenter = bObj.transform.InverseTransformPoint(worldCenter);
            Vector3 lossy = bObj.transform.lossyScale;
            float invSx = Mathf.Abs(lossy.x) > 0.0001f ? 1f / lossy.x : 1f;
            float invSy = Mathf.Abs(lossy.y) > 0.0001f ? 1f / lossy.y : 1f;
            Vector2 localSize = new Vector2(worldCell.width * invSx, worldCell.height * invSy);

            tileTransform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            tileTransform.localRotation = Quaternion.identity;
            tileTransform.localScale = Vector3.one;
            tileTransform.gameObject.layer = _collisionLayer;
            tileTransform.gameObject.SetActive(true);

            var box = tileTransform.GetComponent<BoxCollider2D>();
            if (box == null)
                box = tileTransform.gameObject.AddComponent<BoxCollider2D>();
            box.enabled = true;
            box.isTrigger = false; // explicit: must block movement, not just detect
            box.offset = Vector2.zero;
            box.size = localSize;
        }

        private static Transform TryReusePooledTile(Transform parent, string childName)
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

        private static void ClearCollisionTiles(BuildingObject bObj)
        {
            if (bObj == null) return;

            int pooledIndex = 0;
            for (int i = bObj.transform.childCount - 1; i >= 0; i--)
            {
                var child = bObj.transform.GetChild(i);
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

        // Buildings have no default footprint collider — only painted per-cell
        // grids produce colliders. This helper guarantees the root BoxCollider2D
        // stays disabled when the user clears a grid (so we never silently
        // resurrect the legacy footprint), regardless of template.solid.
        private static void RestoreDefaultColliderState(BuildingObject bObj)
        {
            if (bObj == null) return;
            var mainCollider = bObj.GetComponent<BoxCollider2D>();
            if (mainCollider != null)
                mainCollider.enabled = false;
        }

        private static Vector2 GetBuildingLocalSpriteSize(BuildingObject bObj)
        {
            float width = 0f;
            float height = 0f;

            var footprint = bObj.transform.Find("Footprint")?.GetComponent<SpriteRenderer>();
            if (footprint != null && footprint.sprite != null)
            {
                width = Mathf.Max(width, footprint.sprite.rect.width / PPU);
                height += footprint.sprite.rect.height / PPU;
            }

            var canopy = bObj.transform.Find("Canopy")?.GetComponent<SpriteRenderer>();
            if (canopy != null && canopy.sprite != null)
            {
                width = Mathf.Max(width, canopy.sprite.rect.width / PPU);
                height += canopy.sprite.rect.height / PPU;
            }

            var mainCollider = bObj.GetComponent<BoxCollider2D>();
            if (mainCollider != null)
            {
                width = Mathf.Max(width, mainCollider.size.x);
                height = Mathf.Max(height, mainCollider.offset.y + mainCollider.size.y * 0.5f);
            }

            return new Vector2(
                Mathf.Max(0.0001f, width),
                Mathf.Max(0.0001f, height));
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
