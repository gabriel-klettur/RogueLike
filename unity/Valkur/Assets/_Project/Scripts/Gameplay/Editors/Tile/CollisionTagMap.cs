using Valkur.Core;
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

        /// <summary>Number of visual layers tracked by a layer mask. Derived from
        /// <see cref="SortingConfig.VISUAL_LAYER_COUNT"/>, the project's single source for it.</summary>
        public const int LayerCount = SortingConfig.VISUAL_LAYER_COUNT;

        /// <summary>Bitmask with every visual-layer bit set — canonical "all layers"
        /// representation, equivalent to <see cref="Wildcard"/>. Packed into an int, which is
        /// the third Unity-adjacent ceiling on <see cref="SortingConfig.VISUAL_LAYER_COUNT"/>
        /// after the 32 physics layers and the sorting-layer ladder: at most 31 layers fit.</summary>
        public const int FullLayerMask = (1 << LayerCount) - 1; // 0x1FF

        /// <summary>
        /// The valid tag values: <see cref="Wildcard"/> plus one per visual layer. GENERATED
        /// from <see cref="LayerCount"/> rather than written out, because it was a literal
        /// array of ten when the ladder grew to sixteen — and the Colliders panel builds one
        /// button per entry, so a stale array is a layer the author cannot tag, silently.
        /// </summary>
        public static readonly string[] ValidTags = BuildValidTags();

        private static string[] BuildValidTags()
        {
            var tags = new string[LayerCount + 1];
            tags[0] = Wildcard;
            for (int i = 0; i < LayerCount; i++) tags[i + 1] = i.ToString();   // "0".."15"
            return tags;
        }

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
        /// i.e. <see cref="Wildcard"/> or a CSV of layer numbers in [0, LayerCount).
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
        /// "*" stays "*"; a subset gets sorted + deduped; every layer set collapses to "*";
        /// anything unparseable or out of range returns <c>null</c> (the caller falls back to
        /// <see cref="Wildcard"/>).
        /// </summary>
        internal static string Canonicalize(string raw)
        {
            return TryParseMask(raw, out int mask) ? TagFromLayerMask(mask) : null;
        }

        /// <summary>
        /// THE parser. A segment is a RUN OF DIGITS read as one decimal number, so "12" is
        /// layer twelve rather than layers one and two, and segments are separated by commas
        /// or spaces.
        ///
        /// <para>It used to read one CHARACTER per layer (the character '0' plus the index) and reject any
        /// two-digit run outright, with a comment saying that kept the schema "tight to the
        /// 0..8 enum range". That was true and it became the reason layers 9..15 could not be
        /// tagged at all once the ladder grew — the writer emits <c>"0,2,5"</c>, so a
        /// two-digit layer had no spelling. Nothing on disk was at risk: the shipped maps
        /// carry zero collision tags, and every string the writer has ever produced is
        /// comma-separated, so no comma-less run like "358" exists to be re-read as one number.</para>
        ///
        /// <para>There was also a SECOND parser — <c>LayerMaskFromTag</c> walked the canonical
        /// string character by character with its own <c>'0'..'8'</c> bound — which is the
        /// shape that lets two readers disagree about the same string. Both go through here.</para>
        /// </summary>
        internal static bool TryParseMask(string raw, out int mask)
        {
            mask = 0;
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw == Wildcard) { mask = FullLayerMask; return true; }

            int idx = 0;
            bool sawSegment = false;
            while (idx < raw.Length)
            {
                char c = raw[idx];
                if (c == ' ' || c == ',') { idx++; continue; }
                if (c < '0' || c > '9') { mask = 0; return false; }

                int value = 0;
                while (idx < raw.Length && raw[idx] >= '0' && raw[idx] <= '9')
                {
                    value = value * 10 + (raw[idx] - '0');
                    if (value >= LayerCount) { mask = 0; return false; }   // out of range, and cannot shrink
                    idx++;
                }
                mask |= 1 << value;
                sawSegment = true;
            }

            if (!sawSegment) { mask = 0; return false; }
            return true;
        }

        /// <summary>
        /// Convert a canonical (or canonicalisable) tag string to its 9-bit layer mask.
        /// "*" → <see cref="FullLayerMask"/>; empty or invalid → <see cref="FullLayerMask"/>
        /// (matches the legacy "missing tag = wildcard" semantic that pre-M1.10 maps rely on).
        /// </summary>
        public static int LayerMaskFromTag(string tag)
        {
            // Empty or unparseable reads as the wildcard, which is the legacy "a cell with no
            // tag collides on every layer" semantic every pre-M1.10 map relies on.
            if (string.IsNullOrEmpty(tag)) return FullLayerMask;
            return TryParseMask(tag, out int mask) ? mask : FullLayerMask;
        }

        /// <summary>
        /// Convert a layer <paramref name="mask"/> to its canonical string form.
        /// <see cref="FullLayerMask"/> → <see cref="Wildcard"/>; 0 → empty string (no
        /// layers, semantically "no collider"); otherwise comma-separated ascending
        /// layer numbers (e.g. <c>0x025</c> → <c>"0,2,5"</c>). Bits above the last visual
        /// layer are silently ignored so callers can pass an int without pre-masking.
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
                sb.Append(i);   // the NUMBER: layer 12 is "12", not the character after '9'
            }
            return sb.ToString();
        }

        /// <summary>
        /// Enumerate the visual-layer indices covered by <paramref name="tag"/>.
        /// "*" yields every layer in order; a single number yields that one index; CSV yields each
        /// covered index. Empty / null yields every layer (legacy wildcard fallback). Caller
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
