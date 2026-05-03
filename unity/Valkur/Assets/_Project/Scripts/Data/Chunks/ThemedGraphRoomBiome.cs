using System;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Multi-theme variant of <see cref="GraphRoomBiome"/>: the world is
    /// divided into "theme regions" (default 8 supercells across) and each
    /// region uses its own tile palette. Crossing into a different region
    /// hands the player a visually distinct dungeon (forest dungeon → ice
    /// dungeon → hellscape) without changing the underlying generation
    /// rules (rooms still placed at the same coords, corridors carved
    /// identically).
    ///
    /// Theme assignment is deterministic per (worldSeed, region coord)
    /// using the same FNV mix the rest of the chunk pipeline uses, so two
    /// clients regenerating the same world get the same theme distribution.
    ///
    /// Why a wrapper around GraphRoomBiome instead of an extension on the
    /// base class: themes change ONLY the tile names supplied to the
    /// painter — they never change room placement, corridor logic, or
    /// CellKind classification. Composing here keeps the base biome
    /// theme-agnostic and avoids a swelling constructor signature.
    /// </summary>
    public sealed class ThemedGraphRoomBiome : IBiome
    {
        [Serializable]
        public struct ThemePalette
        {
            public string id;
            public string floorTile;
            public string wallTile;
            public string bossFloorTile;
            public string bossWallTile;
            public string doorTile;
        }

        private readonly string _id;
        private readonly ThemePalette[] _themes;
        private readonly int _supercellTiles;
        private readonly int _regionSizeSupercells;
        private readonly int _roomProbPerMille;
        private readonly int _bossRoomProbPerMille;
        private readonly int _wallThickness;

        public ThemedGraphRoomBiome(
            string id,
            ThemePalette[] themes,
            int supercellTiles            = 32,
            int regionSizeSupercells      = 8,
            int roomProbabilityPerMille   = 600,
            int bossRoomProbabilityPerMille = 50,
            int wallThickness             = 1)
        {
            _id = id ?? "themed_graph_room";
            // Reject empty theme arrays — caller must supply at least one
            // palette or the painter has nothing to paint with.
            _themes = (themes != null && themes.Length > 0)
                ? themes
                : throw new ArgumentException("themes must contain at least one palette", nameof(themes));
            _supercellTiles       = supercellTiles > 0 ? supercellTiles : 32;
            _regionSizeSupercells = regionSizeSupercells > 0 ? regionSizeSupercells : 8;
            _roomProbPerMille     = roomProbabilityPerMille < 0 ? 0
                                  : roomProbabilityPerMille > 1000 ? 1000
                                  : roomProbabilityPerMille;
            _bossRoomProbPerMille = bossRoomProbabilityPerMille < 0 ? 0
                                  : bossRoomProbabilityPerMille > 1000 ? 1000
                                  : bossRoomProbabilityPerMille;
            _wallThickness        = wallThickness > 0 ? wallThickness : 1;
        }

        public string Id            => _id;
        public int    Version       => 1;
        public bool   IsHandcrafted => false;

        public ChunkData GenerateChunk(Valkur.Core.Coordinates.ChunkCoord coord,
                                       long worldSeed, IBiomeContext ctx)
        {
            // Determine which region this chunk centre falls into. We use a
            // single theme per chunk — chunks straddling a region boundary
            // pick the theme of the supercell at the chunk's lower-left
            // corner. Sharp visual transitions are intentional: it tells
            // the player "you've entered a new dungeon area".
            int size = ctx.ChunkSize;
            long anchorTx = (long)coord.Cx * size;
            long anchorTy = (long)coord.Cy * size;
            long sx = FloorDiv(anchorTx, _supercellTiles);
            long sy = FloorDiv(anchorTy, _supercellTiles);
            var theme = ResolveTheme(sx, sy, worldSeed);

            // Construct the underlying GraphRoomBiome on demand with this
            // chunk's theme. Cheap allocation — no Tilemap or runtime
            // resources involved, just int / string fields.
            var inner = new GraphRoomBiome(
                id: _id + "." + (theme.id ?? "default"),
                floorTile: theme.floorTile,
                wallTile:  theme.wallTile,
                supercellTiles: _supercellTiles,
                roomProbabilityPerMille: _roomProbPerMille,
                wallThickness: _wallThickness,
                bossFloorTile: theme.bossFloorTile,
                bossWallTile:  theme.bossWallTile,
                bossRoomProbabilityPerMille: _bossRoomProbPerMille,
                doorTile: theme.doorTile);

            return inner.GenerateChunk(coord, worldSeed, ctx);
        }

        // ── Theme assignment ────────────────────────────────────────────────────

        public ThemePalette ResolveTheme(long sx, long sy, long worldSeed)
        {
            // Region coord = supercell coord ÷ regionSize. Same FNV mix as
            // IsRoomSupercell but XOR'd with a magic constant to keep the
            // theme stream independent of room/boss rolls.
            long rx = FloorDiv(sx, _regionSizeSupercells);
            long ry = FloorDiv(sy, _regionSizeSupercells);
            ulong h = MixHash(worldSeed ^ unchecked((long)0x71337133_71337133UL), rx, ry);
            int idx = (int)(h % (ulong)_themes.Length);
            if (idx < 0) idx += _themes.Length;
            return _themes[idx];
        }

        public int RegionSizeSupercells => _regionSizeSupercells;
        public int ThemeCount => _themes.Length;

        // ── Math helpers (mirrors GraphRoomBiome) ──────────────────────────────

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a ^ b) < 0 && q * b != a) q--;
            return q;
        }

        private static ulong MixHash(long worldSeed, long sx, long sy)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                h ^= (ulong)worldSeed;            h *= 1099511628211UL;
                h ^= (ulong)sx;                   h *= 1099511628211UL;
                h ^= (ulong)sy;                   h *= 1099511628211UL;
                return h ^ (h >> 32);
            }
        }
    }
}
