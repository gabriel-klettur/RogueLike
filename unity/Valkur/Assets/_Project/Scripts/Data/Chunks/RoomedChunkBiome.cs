namespace Valkur.Data.Chunks
{
    /// <summary>
    /// First-pass room-based dungeon biome: each chunk independently
    /// decides whether it is a "room" (rectangular floor surrounded by
    /// walls), a "corridor" (open floor with no walls), or "void" (empty).
    /// The decision is a deterministic function of <c>worldSeed</c> and
    /// the chunk coordinate, so two clients regenerating the same coord
    /// get identical layouts byte-for-byte (Phase-4 networking parity).
    ///
    /// This is a stepping stone, not a full NodeGraph dungeon. It
    /// demonstrates the chunk-as-room idiom and unlocks designer-driven
    /// room density tuning without committing to the full graph-based
    /// generator yet. A future <c>GraphRoomBiome</c> will lay out rooms
    /// at world scale and pre-cut corridors between them; this biome's
    /// per-chunk independence is its limitation.
    ///
    /// Decision thresholds are expressed as integer probabilities out of
    /// 1000 to avoid float drift across machines:
    ///   roll &lt; <c>roomProbabilityPerMille</c>      -> Room
    ///   roll &lt; <c>roomProbabilityPerMille</c> + <c>corridorProbabilityPerMille</c> -> Corridor
    ///   otherwise                                     -> Void
    ///
    /// The roll is derived from <see cref="IBiomeContext.Random"/> so it
    /// flows through the same deterministic stream all biomes use.
    /// </summary>
    public sealed class RoomedChunkBiome : IBiome
    {
        private readonly string _id;
        private readonly string _floorTile;
        private readonly string _wallTile;
        private readonly int    _roomProbPerMille;
        private readonly int    _corridorProbPerMille;
        private readonly int    _wallThickness;

        public RoomedChunkBiome(
            string id,
            string floorTile,
            string wallTile,
            int roomProbabilityPerMille     = 600,
            int corridorProbabilityPerMille = 250,
            int wallThickness               = 1)
        {
            _id              = id ?? "roomed";
            _floorTile       = floorTile ?? string.Empty;
            _wallTile        = wallTile  ?? string.Empty;
            _roomProbPerMille     = Clamp01000(roomProbabilityPerMille);
            _corridorProbPerMille = Clamp01000(corridorProbabilityPerMille);
            _wallThickness   = wallThickness > 0 ? wallThickness : 1;
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

            ushort floor = ctx.Tiles.GetId(_floorTile);
            ushort wall  = ctx.Tiles.GetId(_wallTile);

            // One roll per chunk, taken from a dedicated channel so other
            // biome subsystems (e.g. monster placement) can reuse the same
            // ctx without affecting the room layout.
            var rng = ctx.Random("roomed_kind");
            int roll = rng.Next(1000);

            ChunkKind kind;
            if (roll < _roomProbPerMille) kind = ChunkKind.Room;
            else if (roll < _roomProbPerMille + _corridorProbPerMille) kind = ChunkKind.Corridor;
            else kind = ChunkKind.Void;

            switch (kind)
            {
                case ChunkKind.Room:     PaintRoom(data, size, floor, wall);     break;
                case ChunkKind.Corridor: PaintCorridor(data, size, floor);       break;
                case ChunkKind.Void: /* leave the chunk empty (all zero) */     break;
            }
            return data;
        }

        // ── Painters ──────────────────────────────────────────────────────────

        private void PaintRoom(ChunkData data, int size, ushort floor, ushort wall)
        {
            int border = _wallThickness;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool onBorder = x < border || y < border ||
                                x >= size - border || y >= size - border;
                data.Set(0, x, y, onBorder ? wall : floor);
            }
        }

        private static void PaintCorridor(ChunkData data, int size, ushort floor)
        {
            // Open floor with no walls — neighbouring rooms supply their own.
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                data.Set(0, x, y, floor);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private enum ChunkKind { Room, Corridor, Void }

        private static int Clamp01000(int v) => v < 0 ? 0 : (v > 1000 ? 1000 : v);
    }
}
