namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Concentric-rings biome. Picks one of two tiles based on the Chebyshev
    /// distance from the world origin (0,0) divided by a configurable ring
    /// width: rings of <c>ringWidthTiles</c> tiles alternate between the
    /// primary and secondary tile.
    ///
    /// Useful for a "central plaza outwards" world layout, or for visualising
    /// distance-from-origin in tests. Like <see cref="CheckerboardBiome"/>
    /// it is purely positional (no noise, no RNG), so two chunks generated
    /// independently with the same coords produce identical output.
    /// </summary>
    public sealed class RingBiome : IBiome
    {
        private readonly string _id;
        private readonly string _primaryTile;
        private readonly string _secondaryTile;
        private readonly int    _ringWidthTiles;

        public RingBiome(string id, string primaryTile, string secondaryTile,
                         int ringWidthTiles = 16)
        {
            _id             = id ?? "ring";
            _primaryTile    = primaryTile ?? string.Empty;
            _secondaryTile  = secondaryTile ?? string.Empty;
            // 0 / negative widths would divide by zero or fold the rings
            // back into a single band — clamp to a sane minimum.
            _ringWidthTiles = ringWidthTiles > 0 ? ringWidthTiles : 16;
        }

        public string Id            => _id;
        public int    Version       => 1;
        public bool   IsHandcrafted => false;

        public ChunkData GenerateChunk(Valkur.Core.Coordinates.ChunkCoord coord,
                                       long worldSeed, IBiomeContext ctx)
        {
            int size  = ctx.ChunkSize;
            int count = ctx.LayerCount > 0 ? ctx.LayerCount : 1;
            var data = new ChunkData(coord, size, count);

            ushort primary   = ctx.Tiles.GetId(_primaryTile);
            ushort secondary = ctx.Tiles.GetId(_secondaryTile);

            long baseX = (long)coord.Cx * size;
            long baseY = (long)coord.Cy * size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                long tx = baseX + x;
                long ty = baseY + y;
                long absX = tx < 0 ? -tx : tx;
                long absY = ty < 0 ? -ty : ty;
                long chebyshev = absX > absY ? absX : absY;
                long ring = chebyshev / _ringWidthTiles;
                bool primaryRing = (ring & 1) == 0;
                data.Set(0, x, y, primaryRing ? primary : secondary);
            }
            return data;
        }
    }
}
