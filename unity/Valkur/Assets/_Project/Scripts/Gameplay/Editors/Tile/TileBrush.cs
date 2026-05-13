using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Handles tile painting operations on a Tilemap.
    /// Supports brush, eraser, fill, and eyedropper tools.
    /// Maps to Python's TileEditorController.apply_brush + flood_fill.
    /// </summary>
    public static class TileBrush
    {
        /// <summary>
        /// Paint a tile at the given cell position with the specified brush size.
        /// Returns list of affected cells for undo tracking.
        /// </summary>
        public static List<TileEdit> Paint(Tilemap tilemap, Vector3Int cellPos, TileBase tile, int brushSize, Func<Vector3Int, bool> canEditCell = null)
        {
            var edits = new List<TileEdit>();
            // Cursor is the TOP-LEFT of the N×N footprint: footprint extends right and DOWN.
            for (int dy = 0; dy < brushSize; dy++)
            {
                for (int dx = 0; dx < brushSize; dx++)
                {
                    var pos = new Vector3Int(cellPos.x + dx, cellPos.y - dy, 0);
                    if (canEditCell != null && !canEditCell(pos)) continue;
                    var oldTile = tilemap.GetTile(pos);
                    if (oldTile == tile) continue;
                    edits.Add(new TileEdit(pos, oldTile, tile));
                    tilemap.SetTile(pos, tile);
                }
            }
            return edits;
        }

        /// <summary>
        /// Erase tiles at the given cell position with the specified brush size.
        /// </summary>
        public static List<TileEdit> Erase(Tilemap tilemap, Vector3Int cellPos, int brushSize, Func<Vector3Int, bool> canEditCell = null)
        {
            return Paint(tilemap, cellPos, null, brushSize, canEditCell);
        }

        /// <summary>
        /// Flood fill from the given cell position, replacing matching tiles.
        /// </summary>
        public static List<TileEdit> FloodFill(Tilemap tilemap, Vector3Int startPos, TileBase newTile, int maxCells = 10000, Func<Vector3Int, bool> canEditCell = null)
        {
            var edits = new List<TileEdit>();
            if (tilemap == null) return edits;
            if (canEditCell != null && !canEditCell(startPos)) return edits;

            var targetTile = tilemap.GetTile(startPos);
            if (targetTile == newTile) return edits;

            var cells = ComputeFloodFillCells(tilemap, startPos, maxCells, canEditCell);
            foreach (var pos in cells)
            {
                var current = tilemap.GetTile(pos);
                edits.Add(new TileEdit(pos, current, newTile));
                tilemap.SetTile(pos, newTile);
            }
            return edits;
        }

        /// <summary>
        /// Compute the set of connected cells whose tile matches the tile at startPos,
        /// without mutating the tilemap. 4-connected BFS, capped at maxCells.
        /// Returns empty if tilemap is null or the start cell fails canEditCell.
        /// </summary>
        public static HashSet<Vector3Int> ComputeFloodFillCells(Tilemap tilemap, Vector3Int startPos, int maxCells = 10000, Func<Vector3Int, bool> canEditCell = null)
        {
            var result = new HashSet<Vector3Int>();
            if (tilemap == null) return result;
            if (canEditCell != null && !canEditCell(startPos)) return result;

            var targetTile = tilemap.GetTile(startPos);

            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(startPos);
            visited.Add(startPos);

            var directions = new Vector3Int[]
            {
                Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
            };

            while (queue.Count > 0 && result.Count < maxCells)
            {
                var pos = queue.Dequeue();
                if (canEditCell != null && !canEditCell(pos)) continue;

                var current = tilemap.GetTile(pos);
                if (current != targetTile) continue;

                result.Add(pos);

                foreach (var dir in directions)
                {
                    var neighbor = pos + dir;
                    if (canEditCell != null && !canEditCell(neighbor))
                        continue;

                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Pick the tile at the given cell position (eyedropper).
        /// </summary>
        public static TileBase Pick(Tilemap tilemap, Vector3Int cellPos)
        {
            return tilemap.GetTile(cellPos);
        }

        /// <summary>
        /// Stamp a heterogeneous TileBase pattern onto the tilemap. <paramref name="anchor"/>
        /// is the world cell that maps to <c>pattern[0, 0]</c>; the rest of the pattern
        /// extends right (+X) and down (-Y), matching the row/col convention used by the
        /// tileset migration tool. Null entries in the pattern are skipped (preserve the
        /// existing tile underneath). Returns the edit list so callers can append it to a
        /// <see cref="TileEditBatch"/> for undo/redo grouping.
        /// </summary>
        public static List<TileEdit> Stamp(Tilemap tilemap, Vector3Int anchor,
            TileBase[,] pattern, Func<Vector3Int, bool> canEditCell = null)
        {
            var edits = new List<TileEdit>();
            if (tilemap == null || pattern == null) return edits;
            int rows = pattern.GetLength(0);
            int cols = pattern.GetLength(1);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var tile = pattern[r, c];
                    if (tile == null) continue;
                    var pos = new Vector3Int(anchor.x + c, anchor.y - r, 0);
                    if (canEditCell != null && !canEditCell(pos)) continue;
                    var oldTile = tilemap.GetTile(pos);
                    if (oldTile == tile) continue;
                    edits.Add(new TileEdit(pos, oldTile, tile));
                    tilemap.SetTile(pos, tile);
                }
            }
            return edits;
        }
    }

    /// <summary>
    /// Represents a single tile edit for undo/redo support.
    /// Maps to Python's PaintTilesCommand edit tuple.
    ///
    /// <see cref="TargetTilemap"/> is optional: leave null and the edit is applied to
    /// the enclosing <see cref="TileEditBatch.TargetTilemap"/> on Undo/Redo (the legacy
    /// single-layer behaviour every brush/eraser/fill/paste call relies on). Set it
    /// non-null when a single batch needs to mutate more than one tilemap atomically —
    /// e.g. Move-To-Layer, which clears a cell on the source layer and paints it on a
    /// different destination layer inside the same Ctrl+Z scope.
    /// </summary>
    public struct TileEdit
    {
        public Vector3Int Position;
        public TileBase OldTile;
        public TileBase NewTile;
        public Tilemap TargetTilemap;

        public TileEdit(Vector3Int pos, TileBase oldTile, TileBase newTile)
        {
            Position = pos;
            OldTile = oldTile;
            NewTile = newTile;
            TargetTilemap = null;
        }

        public TileEdit(Vector3Int pos, TileBase oldTile, TileBase newTile, Tilemap target)
        {
            Position = pos;
            OldTile = oldTile;
            NewTile = newTile;
            TargetTilemap = target;
        }
    }

    /// <summary>
    /// A batch of tile edits that can be undone/redone as a single operation.
    /// Maps to Python's PaintTilesCommand.
    /// </summary>
    public class TileEditBatch
    {
        public Tilemap TargetTilemap;
        public List<TileEdit> Edits = new List<TileEdit>();

        public void Undo()
        {
            for (int i = Edits.Count - 1; i >= 0; i--)
            {
                var map = Edits[i].TargetTilemap != null ? Edits[i].TargetTilemap : TargetTilemap;
                if (map == null) continue;
                map.SetTile(Edits[i].Position, Edits[i].OldTile);
            }
        }

        public void Redo()
        {
            foreach (var edit in Edits)
            {
                var map = edit.TargetTilemap != null ? edit.TargetTilemap : TargetTilemap;
                if (map == null) continue;
                map.SetTile(edit.Position, edit.NewTile);
            }
        }
    }
}
