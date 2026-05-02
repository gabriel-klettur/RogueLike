namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Two-tile noise biome: queries a single noise channel and writes one
    /// of two tiles per cell based on a threshold. Demonstrates the full
    /// noise pipeline (BiomeContext.Noise -> ValueNoise2D -> sample) and
    /// is the simplest biome that produces a non-trivial pattern.
    ///
    /// Used in Phase-2 tests to prove three things:
    ///   1. Same seed + same coord produces identical chunks (CRC32 stable).
    ///   2. Different chunk coords produce different patterns (no global
    ///      noise alias).
    ///   3. Different seeds for the same coord produce different patterns
    ///      (world-seed isolation).
    ///
    /// Real biomes will compose multiple noise channels (height, moisture,
    /// trees, …); this one is the floor.
    /// </summary>
    public sealed class NoiseSplitBiome : IBiome
    {
        private readonly string _id;
        private readonly string _highTile;
        private readonly string _lowTile;
        private readonly float  _threshold;
        private readonly float  _frequency;
        private readonly string _channel;

        public NoiseSplitBiome(string id,
                               string highTile,
                               string lowTile,
                               float threshold = 0.5f,
                               float frequency = 0.1f,
                               string channel = "split")
        {
            _id = id ?? "noise_split";
            _highTile = highTile ?? string.Empty;
            _lowTile  = lowTile ?? string.Empty;
            _threshold = threshold;
            _frequency = frequency;
            _channel = string.IsNullOrEmpty(channel) ? "split" : channel;
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
            var noise = ctx.Noise(_channel);
            ushort high = ctx.Tiles.GetId(_highTile);
            ushort low  = ctx.Tiles.GetId(_lowTile);

            // Sample in absolute tile coordinates so adjacent chunks line
            // up across their shared boundary (the noise is continuous
            // across the chunk grid).
            long baseX = (long)coord.Cx * size;
            long baseY = (long)coord.Cy * size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = noise.Sample((baseX + x) * _frequency, (baseY + y) * _frequency);
                data.Set(0, x, y, n >= _threshold ? high : low);
            }
            return data;
        }
    }
}
