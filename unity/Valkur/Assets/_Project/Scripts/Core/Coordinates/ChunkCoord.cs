using System;
using UnityEngine;

namespace Valkur.Core.Coordinates
{
    /// <summary>
    /// Address of a chunk inside a world. Chunks are the unit of streaming in
    /// Phase 2 — fixed-size square tile regions (see
    /// <c>WorldConfig.chunkSize</c>). Coordinates are signed and can grow up to
    /// the int range, so a 32-tile chunk grid yields a virtual world ~137e9
    /// tiles across before wrapping — effectively infinite for gameplay.
    ///
    /// Today's "zone" maps 1:1 to a chunk because zones are 50×50; in Phase 2
    /// the canonical chunk size becomes 32×32 and the legacy zones get sliced
    /// into multiple chunks by <c>FixedChunkProvider</c>.
    /// </summary>
    [Serializable]
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly WorldId World;
        public readonly int Cx;
        public readonly int Cy;

        public ChunkCoord(WorldId world, int cx, int cy)
        {
            World = world;
            Cx = cx;
            Cy = cy;
        }

        /// <summary>Packed (Cx, Cy) for use as a dictionary key inside a single world.</summary>
        public long PackedXY => ((long)Cx << 32) | (uint)Cy;

        public bool Equals(ChunkCoord other) =>
            World.Equals(other.World) && Cx == other.Cx && Cy == other.Cy;

        public override bool Equals(object obj) => obj is ChunkCoord c && Equals(c);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = World.GetHashCode();
                h = (h * 397) ^ Cx;
                h = (h * 397) ^ Cy;
                return h;
            }
        }

        public override string ToString() => $"{World}:({Cx},{Cy})";

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);

        /// <summary>
        /// Convenience accessor: the SW corner of this chunk in world tile
        /// coordinates, given the chunk side length in tiles.
        /// </summary>
        public Vector2Int OriginTiles(int chunkSize) => new Vector2Int(Cx * chunkSize, Cy * chunkSize);
    }
}
