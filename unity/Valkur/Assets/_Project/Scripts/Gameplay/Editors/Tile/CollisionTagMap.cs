using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// In-memory per-cell tag for the Collision tilemap. Each painted collision cell
    /// carries a string tag that selects which visual layer(s) the collider applies to:
    ///   • <see cref="Wildcard"/> ("*")   — the collider applies to entities on every visual layer.
    ///   • "0".."8"                       — single visual layer (matches
    ///                                       <see cref="World.TilemapLayerSetup.TilemapLayer"/>).
    ///   • CSV like "0,2,5"               — multi-layer subset (M1.10). Stored in canonical
    ///                                       form (sorted, deduped). The string "0,1,2,3,4,5,6,7,8"
    ///                                       auto-collapses to <see cref="Wildcard"/> on Set.
    ///
    /// Lives parallel to the visual Collision tilemap exactly the way
    /// <see cref="AutoTile.TerrainMap"/> lives next to the auto-tile catalog: the tilemap
    /// owns "is there a collider here?" and this map owns "what does it apply to?".
    ///
    /// Cells with no entry resolve to <see cref="Wildcard"/> by default — that preserves
    /// the pre-feature behaviour where every collider applied to everything. Legacy
    /// overlay JSONs that don't carry the `collisionTags` matrix migrate transparently.
    /// </summary>
    public class CollisionTagMap : ITileMetadataMap
    {
        /// <summary>Tag value that means "this collider applies to entities on every visual layer".</summary>
        public const string Wildcard = "*";

        /// <summary>Number of visual layers tracked by a layer mask (matches
        /// <see cref="World.TilemapLayerSetup.TilemapLayer"/> enum size).</summary>
        public const int LayerCount = 9;

        /// <summary>Bitmask with all 9 visual-layer bits set — canonical "all layers"
        /// representation, equivalent to <see cref="Wildcard"/>.</summary>
        public const int FullLayerMask = (1 << LayerCount) - 1; // 0x1FF

        /// <summary>The 10 valid tag values: "*" + "0".."8" (one per <see cref="World.TilemapLayerSetup.TilemapLayer"/>).
        /// Kept for back-compat with M1 callers that pre-validate against the legacy list; the
        /// new <see cref="IsValidTag"/> accepts any canonical CSV subset.</summary>
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
        /// Raw accessor for undo/redo. Unlike <see cref="Get(Vector2Int)"/> (which resolves
        /// an absent cell to <see cref="Wildcard"/>), this returns null when there is no
        /// explicit entry — so the capture-old/Set(old) round trip in Undo does NOT
        /// materialize a spurious "*" row where nothing existed before (which would also
        /// dirty <see cref="HasAnyInRect"/> and the overlay JSON of zones never touched).
        /// </summary>
        public string GetRaw(Vector2Int cell) => _tags.TryGetValue(cell, out var t) ? t : null;

        public string GetRaw(Vector3Int cell) => GetRaw(new Vector2Int(cell.x, cell.y));

        /// <summary>
        /// Store <paramref name="tag"/> for <paramref name="cell"/>. The tag is
        /// CANONICALIZED before storage — input "5,2,0" becomes "0,2,5", "0,1,...,8"
        /// collapses to <see cref="Wildcard"/>, and garbage input clamps to
        /// <see cref="Wildcard"/>. Empty / null clears the entry so a subsequent
        /// <see cref="Get(Vector2Int)"/> returns <see cref="Wildcard"/> by default.
        /// </summary>
        public void Set(Vector2Int cell, string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                _tags.Remove(cell);
                return;
            }
            string canonical = Canonicalize(tag);
            if (canonical == null) canonical = Wildcard;   // garbage → wildcard
            _tags[cell] = canonical;
        }

        public void Set(Vector3Int cell, string tag) => Set(new Vector2Int(cell.x, cell.y), tag);

        public void Clear(Vector2Int cell) => _tags.Remove(cell);
        public void Clear(Vector3Int cell) => Clear(new Vector2Int(cell.x, cell.y));

        public void ClearAll() => _tags.Clear();

        /// <summary>
        /// True when <paramref name="tag"/> is parseable as a canonical layer subset —
        /// i.e. <see cref="Wildcard"/>, "0".."8", or a CSV of digits 0..8.
        /// Multi-segment CSV strings are valid even when out-of-order or with duplicates
        /// (<see cref="Set"/> will canonicalize them).
        /// </summary>
        public static bool IsValidTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            return Canonicalize(tag) != null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  M1.10 — Multi-tag helpers (Canonicalize, mask↔tag, enumerate bits)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reduce <paramref name="raw"/> to a canonical layer-subset representation:
        /// "*" stays "*"; single-digit stays as-is; CSV gets sorted + deduped; all-9
        /// digits collapse to "*"; any segment outside "0".."8" → returns <c>null</c>
        /// (caller is responsible for falling back to <see cref="Wildcard"/>).
        /// </summary>
        internal static string Canonicalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (raw == Wildcard) return Wildcard;

            int mask = 0;
            int idx = 0;
            while (idx < raw.Length)
            {
                // Skip whitespace + commas between segments.
                while (idx < raw.Length && (raw[idx] == ' ' || raw[idx] == ','))
                    idx++;
                if (idx >= raw.Length) break;

                char c = raw[idx];
                if (c < '0' || c > '8') return null;          // non-digit or out-of-range
                if (idx + 1 < raw.Length)
                {
                    char next = raw[idx + 1];
                    // Reject multi-char numeric segments like "10" — keeps the schema
                    // tight to the 0..8 enum range.
                    if (next >= '0' && next <= '9') return null;
                }
                mask |= 1 << (c - '0');
                idx++;
            }

            return TagFromLayerMask(mask);
        }

        /// <summary>
        /// Convert a canonical (or canonicalisable) tag string to its 9-bit layer mask.
        /// "*" → <see cref="FullLayerMask"/>; empty or invalid → <see cref="FullLayerMask"/>
        /// (matches the legacy "missing tag = wildcard" semantic that pre-M1.10 maps rely on).
        /// </summary>
        public static int LayerMaskFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return FullLayerMask;
            if (tag == Wildcard) return FullLayerMask;
            string canon = Canonicalize(tag);
            if (canon == null) return FullLayerMask;          // garbage → "*"
            if (canon == Wildcard) return FullLayerMask;

            int mask = 0;
            for (int i = 0; i < canon.Length; i++)
            {
                char c = canon[i];
                if (c >= '0' && c <= '8') mask |= 1 << (c - '0');
            }
            return mask;
        }

        /// <summary>
        /// Convert a 9-bit layer <paramref name="mask"/> to its canonical string form.
        /// <see cref="FullLayerMask"/> → <see cref="Wildcard"/>; 0 → empty string (no
        /// layers, semantically "no collider"); otherwise comma-separated ascending
        /// digits (e.g. <c>0x025</c> → <c>"0,2,5"</c>). Bits above index 8 are silently
        /// ignored so callers can pass an int without pre-masking.
        /// </summary>
        public static string TagFromLayerMask(int mask)
        {
            int trimmed = mask & FullLayerMask;
            if (trimmed == FullLayerMask) return Wildcard;
            if (trimmed == 0) return string.Empty;

            var sb = new StringBuilder(LayerCount * 2);
            for (int i = 0; i < LayerCount; i++)
            {
                if ((trimmed & (1 << i)) == 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append((char)('0' + i));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Enumerate the visual-layer indices (0..8) covered by <paramref name="tag"/>.
        /// "*" yields 0..8 in order; single-digit yields that one index; CSV yields each
        /// covered index. Empty / null yields 0..8 (legacy wildcard fallback). Caller
        /// can rely on ascending order.
        /// </summary>
        public static IEnumerable<int> EnumerateLayers(string tag)
        {
            int mask = LayerMaskFromTag(tag);
            for (int i = 0; i < LayerCount; i++)
                if ((mask & (1 << i)) != 0) yield return i;
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
