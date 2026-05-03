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
    ///
    /// Boss rooms ARE supported now: a small fraction (default 5%) of
    /// supercells are flagged as boss rooms with a distinct floor / wall
    /// tile pair. <see cref="IsBossSupercell"/> is public so spawners can
    /// place boss entities at the boss-room centres.
    /// </summary>
    public sealed class GraphRoomBiome : IBiome
    {
        private readonly string _id;
        private readonly string _floorTile;
        private readonly string _wallTile;
        private readonly string _bossFloorTile;
        private readonly string _bossWallTile;
        private readonly string _doorTile;
        private readonly int    _supercellTiles;
        private readonly int    _roomProbPerMille;
        private readonly int    _bossRoomProbPerMille;
        private readonly int    _wallThickness;

        public GraphRoomBiome(
            string id,
            string floorTile,
            string wallTile,
            int supercellTiles      = 32,
            int roomProbabilityPerMille = 600,
            int wallThickness       = 1,
            string bossFloorTile    = null,
            string bossWallTile     = null,
            int bossRoomProbabilityPerMille = 50,
            string doorTile         = null)
        {
            _id              = id ?? "graph_room";
            _floorTile       = floorTile ?? string.Empty;
            _wallTile        = wallTile  ?? string.Empty;
            // Boss-tile defaults: when not supplied, fall back to the regular
            // floor / wall tiles so designers who don't care about boss rooms
            // get sensible (if visually identical) output.
            _bossFloorTile   = string.IsNullOrEmpty(bossFloorTile) ? _floorTile : bossFloorTile;
            _bossWallTile    = string.IsNullOrEmpty(bossWallTile)  ? _wallTile  : bossWallTile;
            // Door defaults to the floor tile (visually flush with the
            // corridor) so undecorated worlds don't look broken.
            _doorTile        = string.IsNullOrEmpty(doorTile) ? _floorTile : doorTile;
            // Supercell of 0 would divide-by-zero; clamp to 1 even though
            // single-tile supercells produce nonsense layouts.
            _supercellTiles  = supercellTiles > 0 ? supercellTiles : 32;
            _roomProbPerMille = roomProbabilityPerMille < 0 ? 0
                              : roomProbabilityPerMille > 1000 ? 1000
                              : roomProbabilityPerMille;
            _bossRoomProbPerMille = bossRoomProbabilityPerMille < 0 ? 0
                                  : bossRoomProbabilityPerMille > 1000 ? 1000
                                  : bossRoomProbabilityPerMille;
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

            ushort floor     = ctx.Tiles.GetId(_floorTile);
            ushort wall      = ctx.Tiles.GetId(_wallTile);
            ushort bossFloor = ctx.Tiles.GetId(_bossFloorTile);
            ushort bossWall  = ctx.Tiles.GetId(_bossWallTile);
            ushort door      = ctx.Tiles.GetId(_doorTile);

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
                    case CellKind.RoomFloor:    data.Set(0, x, y, floor);     break;
                    case CellKind.RoomWall:     data.Set(0, x, y, wall);      break;
                    case CellKind.BossFloor:    data.Set(0, x, y, bossFloor); break;
                    case CellKind.BossWall:     data.Set(0, x, y, bossWall);  break;
                    case CellKind.Corridor:     data.Set(0, x, y, floor);     break;
                    case CellKind.TJunction:    data.Set(0, x, y, floor);     break;
                    case CellKind.Door:         data.Set(0, x, y, door);      break;
                    case CellKind.Void:        /* leave 0 */                  break;
                }
            }
            return data;
        }

        // ── Pure classification rules (also exercised by tests) ────────────────

        public enum CellKind { Void, RoomFloor, RoomWall, Corridor, BossFloor, BossWall, Door, TJunction }

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
            bool corridorH = IsHorizontalCorridorCell(sx, sy, ox, oy, worldSeed);
            bool corridorV = IsVerticalCorridorCell(sx, sy, ox, oy, worldSeed);
            if (corridorH && corridorV)
            {
                // Both axes carve through this cell — it's a T-junction or
                // 4-way intersection. Distinct CellKind so spawners / VFX
                // can highlight crossings.
                return CellKind.TJunction;
            }
            if (corridorH || corridorV)
            {
                // A corridor cell that lies ON the supercell border (i.e.
                // it's the wall cell that got carved away to let the player
                // through) is a door. Inside-the-room corridor cells stay
                // Corridor.
                int corridorMax = _supercellTiles - 1;
                int corridorBorder = _wallThickness;
                bool corridorOnBorder = ox < corridorBorder || oy < corridorBorder ||
                                        ox > corridorMax - corridorBorder ||
                                        oy > corridorMax - corridorBorder;
                return corridorOnBorder ? CellKind.Door : CellKind.Corridor;
            }

            if (!IsRoomSupercell(sx, sy, worldSeed))
                return CellKind.Void;

            // Inside a room supercell: thick border = wall, interior = floor.
            int max = _supercellTiles - 1;
            int border = _wallThickness;
            bool onBorder = ox < border || oy < border ||
                            ox > max - border || oy > max - border;

            // Boss-room override: same shape as a regular room, distinct
            // tiles. Spawners read IsBossSupercell to know where to place
            // boss entities; the visual tile swap is just the visible cue.
            if (IsBossSupercell(sx, sy, worldSeed))
                return onBorder ? CellKind.BossWall : CellKind.BossFloor;

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

        /// <summary>
        /// Returns true when the supercell is BOTH a room AND has rolled
        /// the rare "boss" flag. Boss rooms are a strict subset of regular
        /// rooms — a void supercell can never be a boss room. Spawners
        /// enumerate world coords and call this method to find boss-room
        /// centres for entity placement.
        /// </summary>
        public bool IsBossSupercell(long sx, long sy, long worldSeed)
        {
            if (!IsRoomSupercell(sx, sy, worldSeed)) return false;
            // Use a different "channel" of the mix (XOR with a magic
            // constant) so the boss roll is independent from the room
            // roll — a supercell is not more or less likely to be a
            // boss because of its room flag.
            ulong h = MixHash(worldSeed ^ unchecked((long)0xB055_B055_B055_B055UL), sx, sy);
            int roll = (int)(h % 1000UL);
            if (roll < 0) roll += 1000;
            return roll < _bossRoomProbPerMille;
        }

        // Corridor classification split into horizontal and vertical so the
        // ClassifyCell dispatcher can distinguish T-junctions (both axes
        // carve) from straight corridors (only one axis carves).

        private bool IsHorizontalCorridorCell(long sx, long sy, int ox, int oy, long worldSeed)
        {
            int mid = _supercellTiles / 2;
            if (oy != mid) return false;

            bool selfIsRoom = IsRoomSupercell(sx, sy, worldSeed);
            // Eastward corridor: self + east neighbour both rooms.
            if (ox >= mid)
            {
                bool eastIsRoom = IsRoomSupercell(sx + 1, sy, worldSeed);
                if (selfIsRoom && eastIsRoom) return true;
            }
            // Westward corridor: self + west neighbour both rooms.
            if (ox <= mid)
            {
                bool westIsRoom = IsRoomSupercell(sx - 1, sy, worldSeed);
                if (selfIsRoom && westIsRoom) return true;
            }
            return false;
        }

        private bool IsVerticalCorridorCell(long sx, long sy, int ox, int oy, long worldSeed)
        {
            int mid = _supercellTiles / 2;
            if (ox != mid) return false;

            bool selfIsRoom = IsRoomSupercell(sx, sy, worldSeed);
            if (oy >= mid)
            {
                bool aboveIsRoom = IsRoomSupercell(sx, sy + 1, worldSeed);
                if (selfIsRoom && aboveIsRoom) return true;
            }
            if (oy <= mid)
            {
                bool belowIsRoom = IsRoomSupercell(sx, sy - 1, worldSeed);
                if (selfIsRoom && belowIsRoom) return true;
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
