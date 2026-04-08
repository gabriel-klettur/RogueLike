using System;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    public partial class BuildingCollisionLoader
    {
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
