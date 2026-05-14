using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// In-memory per-cell tag for the Collision tilemap. Each painted collision cell
    /// carries a string tag that selects which visual layer(s) the collider applies to:
    ///   • <see cref="Wildcard"/> ("*")   — the collider applies to entities on ANY visual layer.
    ///   • "0".."8"                       — the collider only applies to entities on the
    ///                                       matching <see cref="World.TilemapLayerSetup.TilemapLayer"/>.
    ///
    /// Lives parallel to the visual Collision tilemap exactly the way
    /// <see cref="AutoTile.TerrainMap"/> lives next to the auto-tile catalog: the tilemap
    /// owns "is there a collider here?" and this map owns "what does it apply to?".
    ///
    /// In Milestone 1 the runtime physics still uses a single CompositeCollider2D so the
    /// tag is purely an authoring concept (visualised in the editor, persisted to disk,
    /// erased on Move-To-Layer). Milestone 2 will split the composite by tag and feed
    /// the physics layer matrix.
    ///
    /// Cells with no entry resolve to <see cref="Wildcard"/> by default — that preserves
    /// the pre-feature behaviour where every collider applied to everything. Legacy
    /// overlay JSONs that don't carry the `collisionTags` matrix migrate transparently.
    /// </summary>
    public class CollisionTagMap
    {
        /// <summary>Tag value that means "this collider applies to entities on every visual layer".</summary>
        public const string Wildcard = "*";

        /// <summary>The 10 valid tag values: "*" + "0".."8" (one per <see cref="World.TilemapLayerSetup.TilemapLayer"/>).</summary>
        public static readonly string[] ValidTags =
        {
            Wildcard, "0", "1", "2", "3", "4", "5", "6", "7", "8",
        };

        private readonly Dictionary<Vector2Int, string> _tags = new Dictionary<Vector2Int, string>();

        /// <summary>Read-only view used by serialization + visualisation paths.</summary>
        public IReadOnlyDictionary<Vector2Int, string> Cells => _tags;

        public int Count => _tags.Count;

        /// <summary>
        /// Resolve the tag for <paramref name="cell"/>. Returns <see cref="Wildcard"/> when
        /// no explicit tag has been stored — the migration default for legacy overlays
        /// and for cells painted before the feature shipped.
        /// </summary>
        public string Get(Vector2Int cell)
        {
            return _tags.TryGetValue(cell, out var t) && !string.IsNullOrEmpty(t) ? t : Wildcard;
        }

        public string Get(Vector3Int cell) => Get(new Vector2Int(cell.x, cell.y));

        /// <summary>
        /// Store <paramref name="tag"/> for <paramref name="cell"/>. Empty / null clears
        /// the entry so a subsequent <see cref="Get(Vector2Int)"/> returns
        /// <see cref="Wildcard"/> again. Invalid tag values are clamped to
        /// <see cref="Wildcard"/> so authoring bugs can't corrupt the map.
        /// </summary>
        public void Set(Vector2Int cell, string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                _tags.Remove(cell);
                return;
            }
            if (!IsValidTag(tag)) tag = Wildcard;
            _tags[cell] = tag;
        }

        public void Set(Vector3Int cell, string tag) => Set(new Vector2Int(cell.x, cell.y), tag);

        public void Clear(Vector2Int cell) => _tags.Remove(cell);
        public void Clear(Vector3Int cell) => Clear(new Vector2Int(cell.x, cell.y));

        public void ClearAll() => _tags.Clear();

        /// <summary>True when <paramref name="tag"/> is "*" or one of "0".."8".</summary>
        public static bool IsValidTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            if (tag == Wildcard) return true;
            return tag.Length == 1 && tag[0] >= '0' && tag[0] <= '8';
        }

        /// <summary>
        /// Build a row-major <c>string[h, w]</c> matrix for the rectangle at
        /// <c>(originX, originY)</c>. Row 0 corresponds to the TOP of the zone (highest
        /// Unity Y), matching <see cref="TileOverlayPersistence"/>'s layer + terrain
        /// matrices. Cells without an entry are emitted as empty strings (loader
        /// treats those as <see cref="Wildcard"/>).
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
                    m[row, col] = _tags.TryGetValue(key, out var t) ? (t ?? string.Empty) : string.Empty;
                }
            }
            return m;
        }

        /// <summary>
        /// Inverse of <see cref="BuildMatrix"/>. Empty strings leave the cell with no
        /// explicit entry (resolves to <see cref="Wildcard"/> on read). Invalid tag
        /// strings clamp to <see cref="Wildcard"/> via <see cref="Set(Vector2Int,string)"/>.
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
                    if (string.IsNullOrEmpty(t)) _tags.Remove(key);
                    else Set(key, t);
                }
            }
        }

        /// <summary>
        /// True when any cell of the rectangle has an explicit tag entry. Lets
        /// serialization skip the `collisionTags` field for zones that have never been
        /// authored with tags (keeps legacy-shaped JSONs byte-identical).
        /// </summary>
        public bool HasAnyInRect(int originX, int originY, int w, int h)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var key = new Vector2Int(originX + x, originY + y);
                if (_tags.TryGetValue(key, out var t) && !string.IsNullOrEmpty(t))
                    return true;
            }
            return false;
        }
    }
}
