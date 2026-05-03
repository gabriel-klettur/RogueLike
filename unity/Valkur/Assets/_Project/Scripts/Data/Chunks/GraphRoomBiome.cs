namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Second-pass procedural dungeon: each "supercell" of the world (a
    /// fixed N×N tile block) deterministically rolls for whether it is a
    /// room, and adjacent room supercells are connected by straight
    /// corridors carved through their shared border. The result is a
    /// world-scale graph of rooms — corridors guarantee reachability
    /// between adjacent rooms, fixing the per-chunk independence
    /// limitation of <see cref="RoomedChunkBiome"/>.
    ///
    /// Determinism: the room flag for supercell (sx, sy) is a pure
    /// function of <c>worldSeed</c> and the supercell coords via the same
    /// FNV mix the rest of the chunk pipeline uses, so two clients
    /// regenerating the same world produce identical layouts. Corridors
    /// are derived (no extra randomness) from the room flags of the four
    /// neighbours.
    ///
    /// Limitations vs a full NodeGraph (DungeonGunnerCourse):
    ///   - No theme rotation, no room templates — every room is the same
    ///     rectangle.
    ///   - Corridors are straight, single-cell wide. No T-intersections,
    ///     no doors, no biome-aware corridor styling.
    ///   - No "boss room" / "spawn room" specialisation.
    ///
    /// These are designer-level features. The biome contract supports
    /// adding them later as additional channels without disturbing the
    /// chunk pipeline.
    /// </summary>
    public sealed class GraphRoomBiome : IBiome
    {
        private readonly string _id;
        private readonly string _floorTile;
        private readonly string _wallTile;
        private readonly int    _supercellTiles;
        private readonly int    _roomProbPerMille;
        private readonly int    _wallThickness;

        public GraphRoomBiome(
            string id,
            string floorTile,
            string wallTile,
            int supercellTiles      = 32,
            int roomProbabilityPerMille = 600,
            int wallThickness       = 1)
        {
            _id              = id ?? "graph_room";
            _floorTile       = floorTile ?? string.Empty;
            _wallTile        = wallTile  ?? string.Empty;
            // Supercell of 0 would divide-by-zero; clamp to 1 even though
            // single-tile supercells produce nonsense layouts.
            _supercellTiles  = supercellTiles > 0 ? supercellTiles : 32;
            _roomProbPerMille = roomProbabilityPerMille < 0 ? 0
                              : roomProbabilityPerMille > 1000 ? 1000
                              : roomProbabilityPerMille;
            _wallThickness   = wallThickness > 0 ? wallThickness : 1;
        }

        public string Id            => _id;
        public int    Version       => 1;
        public bool   IsHandcrafted => false;

        // ── IBiome ──────────────────────────────────────────────────────────────

        public ChunkData GenerateChunk(Valkur.Core.Coordinates.ChunkCoord coord,
                                       long worldSeed, IBiomeContext ctx)
        {
            int size  = ctx.ChunkSize;
            int count = ctx.LayerCount > 0 ? ctx.LayerCount : 1;
            var data = new ChunkData(coord, size, count);

            ushort floor = ctx.Tiles.GetId(_floorTile);
            ushort wall  = ctx.Tiles.GetId(_wallTile);

            long baseX = (long)coord.Cx * size;
            long baseY = (long)coord.Cy * size;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                long tx = baseX + x;
                long ty = baseY + y;
                var cell = ClassifyCell(tx, ty, worldSeed);

                switch (cell)
                {
                    case CellKind.RoomFloor:    data.Set(0, x, y, floor); break;
                    case CellKind.RoomWall:     data.Set(0, x, y, wall);  break;
                    case CellKind.Corridor:     data.Set(0, x, y, floor); break;
                    case CellKind.Void:        /* leave 0 */              break;
                }
            }
            return data;
        }

        // ── Pure classification rules (also exercised by tests) ────────────────

        public enum CellKind { Void, RoomFloor, RoomWall, Corridor }

        public CellKind ClassifyCell(long tx, long ty, long worldSeed)
        {
            // Find the supercell coord and the local offset inside it.
            long sx = FloorDiv(tx, _supercellTiles);
            long sy = FloorDiv(ty, _supercellTiles);
            int  ox = (int)PositiveMod(tx, _supercellTiles);
            int  oy = (int)PositiveMod(ty, _supercellTiles);

            // Corridor first — independent of whether the *current* supercell
            // is a room. A corridor passes through this cell if it would
            // connect two adjacent rooms; carving it removes the wall that
            // would otherwise block the doorway.
            if (IsCorridorCell(sx, sy, ox, oy, worldSeed))
                return CellKind.Corridor;

            if (!IsRoomSupercell(sx, sy, worldSeed))
                return CellKind.Void;

            // Inside a room supercell: thick border = wall, interior = floor.
            int max = _supercellTiles - 1;
            int border = _wallThickness;
            bool onBorder = ox < border || oy < border ||
                            ox > max - border || oy > max - border;
            return onBorder ? CellKind.RoomWall : CellKind.RoomFloor;
        }

        // Per-supercell deterministic room flag. Same FNV mix the rest of
        // the chunk pipeline uses so two clients agree.
        public bool IsRoomSupercell(long sx, long sy, long worldSeed)
        {
            int roll = (int)(MixHash(worldSeed, sx, sy) % 1000UL);
            if (roll < 0) roll += 1000; // unsigned mix shouldn't go negative but defend anyway
            return roll < _roomProbPerMille;
        }

        // Corridor cells: a single-tile-wide horizontal/vertical strip at
        // the centre of each supercell pair where both supercells are rooms.
        // This guarantees adjacent rooms reach each other through their
        // walls without scattering corridors at random.
        private bool IsCorridorCell(long sx, long sy, int ox, int oy, long worldSeed)
        {
            int mid = _supercellTiles / 2;

            // Horizontal corridor between (sx, sy) and (sx+1, sy): cells on
            // row 'mid' that span the eastern edge of the left supercell or
            // the western edge of the right supercell.
            if (oy == mid)
            {
                bool leftIsRoom  = IsRoomSupercell(sx, sy, worldSeed);
                bool rightIsRoom = IsRoomSupercell(sx + 1, sy, worldSeed);
                if (leftIsRoom && rightIsRoom && ox >= mid) return true;

                bool farLeftIsRoom = IsRoomSupercell(sx - 1, sy, worldSeed);
                bool selfIsRoom    = leftIsRoom;
                if (farLeftIsRoom && selfIsRoom && ox <= mid) return true;
            }

            // Vertical corridor between (sx, sy) and (sx, sy+1).
            if (ox == mid)
            {
                bool selfIsRoom = IsRoomSupercell(sx, sy, worldSeed);
                bool aboveIsRoom = IsRoomSupercell(sx, sy + 1, worldSeed);
                if (selfIsRoom && aboveIsRoom && oy >= mid) return true;

                bool belowIsRoom = IsRoomSupercell(sx, sy - 1, worldSeed);
                if (belowIsRoom && selfIsRoom && oy <= mid) return true;
            }
            return false;
        }

        // ── Math helpers ────────────────────────────────────────────────────────

        // FloorDiv / PositiveMod handle negative coords correctly (C#'s
        // built-in / and % round toward zero, which would split supercells
        // unevenly across the origin and break determinism).
        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a ^ b) < 0 && q * b != a) q--;
            return q;
        }

        private static long PositiveMod(long a, long b)
        {
            long r = a % b;
            if (r < 0) r += b;
            return r;
        }

        // FNV-1a 64-bit mix — same construction as BiomeContext.MixSeed but
        // exposed here as a pure function so the test fixture can predict
        // a specific (seed, sx, sy) → roll without standing up a context.
        private static ulong MixHash(long worldSeed, long sx, long sy)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL; // FNV offset basis
                h ^= (ulong)worldSeed;            h *= 1099511628211UL;
                h ^= (ulong)sx;                   h *= 1099511628211UL;
                h ^= (ulong)sy;                   h *= 1099511628211UL;
                return h ^ (h >> 32);
            }
        }
    }
}
