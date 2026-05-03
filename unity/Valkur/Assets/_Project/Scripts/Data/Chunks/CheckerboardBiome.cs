namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Two-tile checkerboard biome. Pure positional pattern — no noise,
    /// no random calls — so the output is identical across runs, machines,
    /// and replays without any state. Mostly a contract / fixture biome:
    /// proves the biome interface accepts deterministic non-noise rules,
    /// and produces visually-obvious chunks for QA screenshots.
    ///
    /// Cells where (Tx + Ty) is even get the primary tile; otherwise the
    /// secondary. Tx/Ty are absolute tile coordinates so two adjacent
    /// chunks line up across their shared boundary (no seam).
    /// </summary>
    public sealed class CheckerboardBiome : IBiome
    {
        private readonly string _id;
        private readonly string _primaryTile;
        private readonly string _secondaryTile;

        public CheckerboardBiome(string id, string primaryTile, string secondaryTile)
        {
            _id            = id ?? "checkerboard";
            _primaryTile   = primaryTile ?? string.Empty;
            _secondaryTile = secondaryTile ?? string.Empty;
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
                // Bitwise parity is faster than modulo and handles negative
                // coords correctly (the low bit of a two's-complement int
                // is 1 for odd, 0 for even regardless of sign).
                bool even = ((tx ^ ty) & 1) == 0;
                data.Set(0, x, y, even ? primary : secondary);
            }
            return data;
        }
    }
}
