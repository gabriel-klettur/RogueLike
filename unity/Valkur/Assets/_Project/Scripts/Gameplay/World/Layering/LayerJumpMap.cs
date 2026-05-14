using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// In-memory per-cell map of "stepping onto this cell sets the player's visual
    /// layer to N". The cornerstone of M1.8's tile-painted "LAYER JUMPS" feature.
    ///
    /// Lives parallel to the visual tilemaps the same way <see cref="TileEditor.CollisionTagMap"/>
    /// does — the Tile Editor's Layer Jumps panel paints into this map, the runtime
    /// <see cref="LayerJumpTriggerSystem"/> reads it every frame, and the on-disk
    /// overlay JSON persists it as a parallel <c>layerJumps</c> matrix.
    ///
    /// Invariants:
    ///   • Missing entries resolve to <see cref="string.Empty"/> (no jump on that cell).
    ///   • Valid target values are <c>"0"</c>..<c>"8"</c>, matching the indices of
    ///     <see cref="TilemapLayerSetup.TilemapLayer"/>. Invalid strings clamp to
    ///     empty (which clears the cell) rather than corrupting the map.
    ///   • No wildcard concept — unlike <see cref="TileEditor.CollisionTagMap"/>,
    ///     a "jump to all layers" doesn't have a meaningful gameplay semantic.
    /// </summary>
    public class LayerJumpMap
    {
        public const int MinTarget = 0;
        public const int MaxTarget = 8;

        private readonly Dictionary<Vector2Int, string> _jumps = new Dictionary<Vector2Int, string>();

        /// <summary>Read-only view used by serialization + the runtime trigger sweep.</summary>
        public IReadOnlyDictionary<Vector2Int, string> Cells => _jumps;

        public int Count => _jumps.Count;

        /// <summary>
        /// Return the target layer string ("0".."8") stored at <paramref name="cell"/>,
        /// or <see cref="string.Empty"/> when the cell has no jump. Always non-null.
        /// </summary>
        public string Get(Vector2Int cell)
        {
            return _jumps.TryGetValue(cell, out var t) && !string.IsNullOrEmpty(t)
                ? t
                : string.Empty;
        }

        public string Get(Vector3Int cell) => Get(new Vector2Int(cell.x, cell.y));

        /// <summary>
        /// Store <paramref name="targetLayer"/> for <paramref name="cell"/>. Null /
        /// empty / invalid strings clear the entry — preserving the "no jump" default
        /// and protecting the runtime trigger from parsing garbage.
        /// </summary>
        public void Set(Vector2Int cell, string targetLayer)
        {
            if (!IsValidTarget(targetLayer))
            {
                _jumps.Remove(cell);
                return;
            }
            _jumps[cell] = targetLayer;
        }

        public void Set(Vector3Int cell, string targetLayer) => Set(new Vector2Int(cell.x, cell.y), targetLayer);

        public void Clear(Vector2Int cell) => _jumps.Remove(cell);
        public void Clear(Vector3Int cell) => Clear(new Vector2Int(cell.x, cell.y));

        public void ClearAll() => _jumps.Clear();

        /// <summary>True when <paramref name="targetLayer"/> is "0".."8".</summary>
        public static bool IsValidTarget(string targetLayer)
        {
            if (string.IsNullOrEmpty(targetLayer)) return false;
            if (targetLayer.Length != 1) return false;
            char c = targetLayer[0];
            return c >= '0' && c <= '8';
        }

        /// <summary>
        /// Row-major <c>string[h, w]</c> matrix at <c>(originX, originY)</c>. Row 0 =
        /// top of the zone (highest Unity Y) matching the layer + collisionTags
        /// orientation. Cells without an entry → empty string (loader skips them).
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
                    m[row, col] = _jumps.TryGetValue(key, out var t) ? (t ?? string.Empty) : string.Empty;
                }
            }
            return m;
        }

        /// <summary>
        /// Inverse of <see cref="BuildMatrix"/>. Empty / invalid cells leave the map
        /// alone (no spurious entries); valid cells store the target.
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
                    Set(key, matrix[row, col]);
                }
            }
        }

        /// <summary>
        /// True when any cell of the rectangle has an explicit entry. Lets serialization
        /// skip the <c>layerJumps</c> field on zones the user has never authored —
        /// keeps legacy-shaped JSONs byte-identical when nothing has changed.
        /// </summary>
        public bool HasAnyInRect(int originX, int originY, int w, int h)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var key = new Vector2Int(originX + x, originY + y);
                if (_jumps.TryGetValue(key, out var t) && !string.IsNullOrEmpty(t))
                    return true;
            }
            return false;
        }
    }
}
