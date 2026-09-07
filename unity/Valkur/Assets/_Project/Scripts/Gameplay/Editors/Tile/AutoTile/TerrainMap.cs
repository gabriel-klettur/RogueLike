using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// In-memory terrain layer for the tile editor, keyed by VERTEX.
    ///
    /// <para>Corner16 art is authored per grid POINT: a tile's four corners are four vertices,
    /// so this layer sits half a cell off the render layer — the dual grid — and the tile drawn
    /// at cell <c>(x, y)</c> reads <c>(x, y+1)</c>, <c>(x+1, y+1)</c>, <c>(x+1, y)</c> and
    /// <c>(x, y)</c>. That is what lets a boundary tile be half one terrain and half the other;
    /// one entry per CELL can only ever describe a hard cut.</para>
    ///
    /// <para>Vertices without an entry are returned as <c>null</c> — "no known terrain", which
    /// the bitmask calculator treats as NOT the secondary terrain, so an unpainted world reads
    /// as solid primary and painting the secondary carves into it.</para>
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
        /// Build a row-major <c>string[h+1, w+1]</c> matrix of terrain IDs for the zone at
        /// <c>(originX, originY)</c>. Row 0 corresponds to the TOP (highest Unity Y), matching
        /// the convention <see cref="TileOverlayPersistence"/>'s layer matrices use.
        ///
        /// <para><b>One MORE row and column than the zone has cells, and that is not an
        /// off-by-one.</b> This map is keyed by VERTEX, and a w x h block of cells is bounded by
        /// (w+1) x (h+1) of them — the corners on the zone's far edges belong to it as much as
        /// those on its near edges. Writing only w x h dropped 101 vertices of every 50x50 zone
        /// on every save, measured, so a boundary painted against the zone's top or right edge
        /// came back with two of its corners unknown and could no longer be resolved or
        /// cured.</para>
        ///
        /// <para>Backward compatible in both directions: the loader takes the matrix's own
        /// dimensions rather than the zone's, so a file written at w x h still loads as w x h
        /// vertices, exactly as before.</para>
        ///
        /// <para>Vertices without a stored terrain are emitted as empty strings so the matrix
        /// stays dense.</para>
        /// </summary>
        public string[,] BuildMatrix(int originX, int originY, int w, int h)
        {
            int rows = h + 1, cols = w + 1;
            var m = new string[rows, cols];
            for (int row = 0; row < rows; row++)
            {
                int unityY = originY + (rows - 1 - row);
                for (int col = 0; col < cols; col++)
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
            // The same (w+1) x (h+1) vertex span BuildMatrix writes, so a zone whose ONLY
            // terrain sits on its far edge is not skipped as empty.
            for (int y = 0; y <= h; y++)
            for (int x = 0; x <= w; x++)
            {
                var key = new Vector2Int(originX + x, originY + y);
                if (_terrains.ContainsKey(key) && !string.IsNullOrEmpty(_terrains[key]))
                    return true;
            }
            return false;
        }
    }
}
