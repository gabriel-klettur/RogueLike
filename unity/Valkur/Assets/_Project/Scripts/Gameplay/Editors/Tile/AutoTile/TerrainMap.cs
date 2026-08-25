using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// In-memory terrain layer for the tile editor. Maps each cell to its terrain ID
    /// (e.g. "grass", "dirt"). Lives parallel to the visual <c>Tilemap</c>: when the
    /// auto-tile tool paints a cell, it stamps the terrain here AND drives the
    /// <c>RulesetSolver</c> to compute which sprite variant to place on the tilemap.
    ///
    /// Cells without a terrain entry are returned as <c>null</c> — that means "no
    /// known terrain", which the bitmask calculator treats as a non-connection.
    /// </summary>
    public class TerrainMap : ITileMetadataMap
    {
        private readonly Dictionary<Vector2Int, string> _terrains = new Dictionary<Vector2Int, string>();

        /// <summary>Read-only view used by <c>BitmaskCalculator</c>.</summary>
        public IReadOnlyDictionary<Vector2Int, string> Cells => _terrains;

        public int Count => _terrains.Count;

        /// <summary>Adapter for <see cref="ITileMetadataMap"/> — delegates to <see cref="SetTerrain(Vector3Int,string)"/>.</summary>
        public void Set(Vector3Int cell, string terrain) => SetTerrain(cell, terrain);

        public string GetTerrain(Vector2Int cell)
        {
            return _terrains.TryGetValue(cell, out var t) ? t : null;
        }

        public string GetTerrain(Vector3Int cell)
        {
            return GetTerrain(new Vector2Int(cell.x, cell.y));
        }

        public void SetTerrain(Vector2Int cell, string terrain)
        {
            if (string.IsNullOrEmpty(terrain))
                _terrains.Remove(cell);
            else
                _terrains[cell] = terrain;
        }

        public void SetTerrain(Vector3Int cell, string terrain)
        {
            SetTerrain(new Vector2Int(cell.x, cell.y), terrain);
        }

        public void Clear() => _terrains.Clear();

        // ── Per-zone matrix serialization ───────────────────────────────────

        /// <summary>
        /// Build a row-major <c>string[h, w]</c> matrix of terrain IDs for the
        /// rectangle at <c>(originX, originY)</c>. Row 0 corresponds to the TOP
        /// of the zone (highest Unity Y), matching the convention used by
        /// <see cref="TileOverlayPersistence"/>'s layer matrices. Cells without
        /// a stored terrain are emitted as empty strings so the matrix is dense.
        /// </summary>
        public string[,] BuildMatrix(int originX, int originY, int w, int h)
        {
            var m = new string[h, w];
            for (int row = 0; row < h; row++)
            {
                int unityY = originY + (h - 1 - row);
                for (int col = 0; col < w; col++)
                {
                    var key = new Vector2Int(originX + col, unityY);
                    m[row, col] = _terrains.TryGetValue(key, out var t) ? (t ?? "") : "";
                }
            }
            return m;
        }

        /// <summary>
        /// Inverse of <see cref="BuildMatrix"/>. Empty strings clear the cell;
        /// non-empty values overwrite. Cells outside the matrix are untouched.
        /// </summary>
        public void LoadMatrix(int originX, int originY, string[,] matrix)
        {
            if (matrix == null) return;
            int h = matrix.GetLength(0);
            int w = matrix.GetLength(1);
            for (int row = 0; row < h; row++)
            {
                int unityY = originY + (h - 1 - row);
                for (int col = 0; col < w; col++)
                {
                    var key = new Vector2Int(originX + col, unityY);
                    string t = matrix[row, col];
                    if (string.IsNullOrEmpty(t)) _terrains.Remove(key);
                    else _terrains[key] = t;
                }
            }
        }

        /// <summary>
        /// Returns true if any cell of the rectangle at <c>(originX, originY)</c>
        /// has a terrain entry. Used by serialization to skip writing the
        /// "terrains" field when the zone has no auto-tile data.
        /// </summary>
        public bool HasAnyInRect(int originX, int originY, int w, int h)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var key = new Vector2Int(originX + x, originY + y);
                if (_terrains.ContainsKey(key) && !string.IsNullOrEmpty(_terrains[key]))
                    return true;
            }
            return false;
        }
    }
}
