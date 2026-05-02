using System;
using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Pure POCO snapshot of a single chunk's tile data. The Phase-2 unit
    /// of streaming, persistence, and (eventually Phase 4) network
    /// replication. Lives in <c>Valkur.Data</c> so any layer that needs to
    /// describe chunk content — Sim, Net.Common, the editor — can do so
    /// without pulling in Unity scene types.
    ///
    /// Tile storage uses a flat <see cref="ushort"/> array per layer rather
    /// than a string matrix. Reasons:
    ///   - 2 bytes/cell vs ~16 bytes/cell for the legacy string layout
    ///     (50x50x9 layers ≈ 22500 strings -> ushort buffer hits ~50KB
    ///     uncompressed, fits trivially in a network packet after LZ4).
    ///   - Lookup is O(1) array index instead of <see cref="string"/>
    ///     interning + dictionary roundtrip during paint.
    ///   - Determinism: same seed + same biome produce the same buffer
    ///     bit-for-bit, which is what client prediction in Phase 4 needs.
    ///
    /// Tile id <c>0</c> is reserved for "empty" so a freshly allocated
    /// buffer represents an empty chunk. Real tiles start at id <c>1</c>;
    /// the mapping (id ↔ tile name) is the responsibility of a per-world
    /// <see cref="ITileIdTable"/>, not this struct.
    /// </summary>
    [Serializable]
    public sealed class ChunkData
    {
        /// <summary>Side length of every chunk in tiles. Phase 2 default; can
        /// be lowered to 32 once world generation is purely chunk-based.</summary>
        public const int DefaultChunkSize = 50;

        /// <summary>Address of this chunk within its world.</summary>
        public ChunkCoord Coord;

        /// <summary>Side length in tiles. Always equal across all layers.</summary>
        public int Size;

        /// <summary>Number of layers stored. Each entry is a flat row-major
        /// <c>Size * Size</c> ushort buffer (row 0 = bottom by Unity convention,
        /// matching the existing tilemap orientation).</summary>
        public ushort[][] Layers;

        public ChunkData() { }

        public ChunkData(ChunkCoord coord, int size, int layerCount)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (layerCount <= 0) throw new ArgumentOutOfRangeException(nameof(layerCount));
            Coord = coord;
            Size  = size;
            Layers = new ushort[layerCount][];
            for (int i = 0; i < layerCount; i++)
                Layers[i] = new ushort[size * size];
        }

        /// <summary>Read a tile id by (layer, localX, localY).</summary>
        public ushort Get(int layer, int x, int y)
        {
            CheckBounds(layer, x, y);
            return Layers[layer][y * Size + x];
        }

        /// <summary>Write a tile id by (layer, localX, localY).</summary>
        public void Set(int layer, int x, int y, ushort tileId)
        {
            CheckBounds(layer, x, y);
            Layers[layer][y * Size + x] = tileId;
        }

        /// <summary>True iff every cell across every layer is the empty id.
        /// Used by repository implementations to skip persisting blank chunks.</summary>
        public bool IsEmpty()
        {
            if (Layers == null) return true;
            for (int l = 0; l < Layers.Length; l++)
            {
                var buf = Layers[l];
                if (buf == null) continue;
                for (int i = 0; i < buf.Length; i++)
                    if (buf[i] != 0) return false;
            }
            return true;
        }

        /// <summary>Cheap CRC32 over every layer. Phase 2 uses it for
        /// deterministic-generation regression tests; Phase 4 networking
        /// can reuse it for chunk hashing.</summary>
        public uint ComputeCrc32()
        {
            uint crc = 0xFFFFFFFFu;
            if (Layers != null)
            {
                for (int l = 0; l < Layers.Length; l++)
                {
                    var buf = Layers[l];
                    if (buf == null) continue;
                    for (int i = 0; i < buf.Length; i++)
                    {
                        crc ^= (uint)(buf[i] & 0xFF);
                        for (int b = 0; b < 8; b++)
                            crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xEDB88320u : (crc >> 1);
                        crc ^= (uint)((buf[i] >> 8) & 0xFF);
                        for (int b = 0; b < 8; b++)
                            crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xEDB88320u : (crc >> 1);
                    }
                }
            }
            return ~crc;
        }

        private void CheckBounds(int layer, int x, int y)
        {
            if (Layers == null || layer < 0 || layer >= Layers.Length)
                throw new ArgumentOutOfRangeException(nameof(layer));
            if (x < 0 || x >= Size || y < 0 || y >= Size)
                throw new ArgumentOutOfRangeException(
                    $"({x},{y}) is outside chunk size {Size}.");
        }
    }
}
