namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Trivial biome that paints every cell of layer 0 with the same tile.
    /// Equivalent in output to the Phase-1 "test_world" overlay JSON, but
    /// generated procedurally instead of read from disk — proves the
    /// biome contract works end-to-end without dragging in a real noise
    /// algorithm yet.
    ///
    /// Useful as:
    ///   - Smoke-test biome for fixtures (no surprise variation).
    ///   - Default fallback when a biome router has no specific match.
    ///   - Reference implementation for biomes that ARE deterministic but
    ///     do not use noise (e.g. flat dungeons, void chunks).
    /// </summary>
    public sealed class UniformFillBiome : IBiome
    {
        private readonly string _id;
        private readonly string _tileName;

        public UniformFillBiome(string id, string tileName)
        {
            _id = id ?? "uniform";
            _tileName = tileName ?? string.Empty;
        }

        public string Id            => _id;
        public int    Version       => 1;
        public bool   IsHandcrafted => false;

        public string TileName => _tileName;

        public ChunkData GenerateChunk(Valkur.Core.Coordinates.ChunkCoord coord,
                                       long worldSeed, IBiomeContext ctx)
        {
            int size  = ctx.ChunkSize;
            int count = ctx.LayerCount > 0 ? ctx.LayerCount : 1;
            var data = new ChunkData(coord, size, count);
            ushort id = ctx.Tiles.GetId(_tileName);
            if (id == 0) return data; // empty fallback if the tile isn't registered

            // Layer 0 is canonically the Ground tilemap; matches the
            // Phase-1 overlay convention.
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                data.Set(0, x, y, id);
            return data;
        }
    }
}
