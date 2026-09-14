using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Data.WorldGen;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// One zone of a generated world, turned from vertex terrain into what an overlay carries:
    /// <c>Ground</c> (every cell), <c>Collision</c> (only when the zone has a blocked cell — water,
    /// lava and peaks, with the ground's own tile so it draws nothing new) and <c>terrains</c>,
    /// the vertex terrain the Tile editor's auto-brush continues from.
    ///
    /// <para><b>The single owner of that translation, for both routes.</b> The bake writes it as
    /// JSON and the live world hands it to <c>OverlayLoader</c> as the already-parsed tree; if the
    /// two did the translation separately a zone streamed in and the same zone baked to disk could
    /// disagree about a tile, which is exactly the class of drift this project keeps recording.</para>
    /// </summary>
    public static class SeedWorldZoneBuilder
    {
        public sealed class ZoneContent
        {
            public int BaseX;
            public int BaseY;
            public int Size;

            /// <summary>Tile names, <c>ly * Size + lx</c>, bottom row first.</summary>
            public string[] Ground;
            public bool[] Blocked;
            public bool AnyBlocked;

            /// <summary>Vertex terrain, <c>vy * (Size + 1) + vx</c>, bottom row first.</summary>
            public string[] Terrains;
        }

        /// <summary>Tallies a caller wants back from building zones.</summary>
        public sealed class Stats
        {
            public int HardCuts;
            public int MissingTiles;
            public int BlockedTiles;
        }

        /// <summary>
        /// The zone whose bottom-left cell is plan tile (<paramref name="baseX"/>,
        /// <paramref name="baseY"/>). <paramref name="grid"/> must cover its vertices; tile hashes use
        /// WORLD coordinates so a streamed zone and a baked one pick the same variant.
        /// </summary>
        public static ZoneContent Build(WorldTerrainGrid grid, SeedWorldTilePalette palette, WorldGenSettings s,
                                        int baseX, int baseY, int z, Vector2Int origin, Stats stats)
        {
            var c = new ZoneContent
            {
                BaseX = baseX,
                BaseY = baseY,
                Size = z,
                Ground = new string[z * z],
                Blocked = new bool[z * z],
                Terrains = new string[(z + 1) * (z + 1)],
            };

            for (int ly = 0; ly < z; ly++)
                for (int lx = 0; lx < z; lx++)
                {
                    int x = baseX + lx, y = baseY + ly;
                    string swT = grid.TerrainAt(x, y);
                    string seT = grid.TerrainAt(x + 1, y);
                    string neT = grid.TerrainAt(x + 1, y + 1);
                    string nwT = grid.TerrainAt(x, y + 1);

                    int worldX = origin.x + x, worldY = origin.y + y;
                    int hash = unchecked(worldX * 73856093 ^ worldY * 19349663);
                    var sprite = palette.Resolve(swT, seT, neT, nwT, hash, out bool hardCut);
                    if (stats != null)
                    {
                        if (hardCut) stats.HardCuts++;
                        if (sprite == null) stats.MissingTiles++;
                    }

                    int i = ly * z + lx;
                    c.Ground[i] = palette.NameOf(sprite);

                    if (IsBlocked(grid, s, x, y, swT, seT, neT, nwT))
                    {
                        c.Blocked[i] = true;
                        c.AnyBlocked = true;
                        if (stats != null) stats.BlockedTiles++;
                    }
                }

            for (int vy = 0; vy <= z; vy++)
                for (int vx = 0; vx <= z; vx++)
                    c.Terrains[vy * (z + 1) + vx] = grid.TerrainAt(baseX + vx, baseY + vy);

            return c;
        }

        // ── JSON (the bake) ─────────────────────────────────────────────────────

        public static void AppendJson(StringBuilder sb, ZoneContent c)
        {
            int z = c.Size;
            sb.Append("{\"layers\":{\"Ground\":");
            AppendRows(sb, c.Ground, null, z);
            if (c.AnyBlocked)
            {
                sb.Append(",\"Collision\":");
                AppendRows(sb, c.Ground, c.Blocked, z);
            }
            sb.Append("},\"terrains\":[");
            for (int row = 0; row <= z; row++)
            {
                if (row > 0) sb.Append(',');
                sb.Append('[');
                int vy = z - row;
                for (int col = 0; col <= z; col++)
                {
                    if (col > 0) sb.Append(',');
                    AppendString(sb, c.Terrains[vy * (z + 1) + col]);
                }
                sb.Append(']');
            }
            sb.Append("]}");
        }

        /// <summary>Rows top-first, the overlay convention (row 0 is the zone's highest Y).</summary>
        private static void AppendRows(StringBuilder sb, string[] names, bool[] mask, int z)
        {
            sb.Append('[');
            for (int row = 0; row < z; row++)
            {
                if (row > 0) sb.Append(',');
                sb.Append('[');
                int ly = z - 1 - row;
                for (int lx = 0; lx < z; lx++)
                {
                    if (lx > 0) sb.Append(',');
                    int i = ly * z + lx;
                    AppendString(sb, mask == null || mask[i] ? names[i] : string.Empty);
                }
                sb.Append(']');
            }
            sb.Append(']');
        }

        private static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char ch = value[i];
                    if (ch == '"' || ch == '\\') sb.Append('\\');
                    sb.Append(ch);
                }
            }
            sb.Append('"');
        }

        // ── Parsed tree (the live world) ────────────────────────────────────────

        /// <summary>
        /// The tree <c>MiniJsonRuntime</c> would produce from <see cref="AppendJson"/>, built
        /// directly: lists of lists of strings, row 0 on top. Skipping the text saves a megabyte of
        /// string building and parsing per zone for nothing but the same objects.
        /// </summary>
        public static Dictionary<string, object> ToOverlayRoot(ZoneContent c)
        {
            int z = c.Size;
            var layers = new Dictionary<string, object> { ["Ground"] = Rows(c.Ground, null, z) };
            if (c.AnyBlocked) layers["Collision"] = Rows(c.Ground, c.Blocked, z);

            var terrains = new List<object>(z + 1);
            for (int row = 0; row <= z; row++)
            {
                var line = new List<object>(z + 1);
                int vy = z - row;
                for (int col = 0; col <= z; col++) line.Add(c.Terrains[vy * (z + 1) + col] ?? string.Empty);
                terrains.Add(line);
            }

            return new Dictionary<string, object> { ["layers"] = layers, ["terrains"] = terrains };
        }

        private static List<object> Rows(string[] names, bool[] mask, int z)
        {
            var rows = new List<object>(z);
            for (int row = 0; row < z; row++)
            {
                var line = new List<object>(z);
                int ly = z - 1 - row;
                for (int lx = 0; lx < z; lx++)
                {
                    int i = ly * z + lx;
                    line.Add(mask == null || mask[i] ? (names[i] ?? string.Empty) : string.Empty);
                }
                rows.Add(line);
            }
            return rows;
        }

        // ── Walkability ─────────────────────────────────────────────────────────

        /// <summary>
        /// The nearest tile to <paramref name="start"/> that is not blocked and whose neighbours are
        /// not either, searched in growing rings inside the tiles [x0, x1) x [y0, y1) the grid
        /// covers. The preview picks the spawn at its own coarse resolution, where one cell can hold
        /// a river or a lake shore; landing the player in water they cannot leave is the one failure
        /// a spawn must never have.
        /// </summary>
        public static Vector2Int FindWalkableTile(WorldTerrainGrid grid, WorldGenSettings s, Vector2Int start,
                                                  int x0, int y0, int x1, int y1, int maxRadius)
        {
            for (int r = 0; r <= maxRadius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = start.x + dx, y = start.y + dy;
                        // OpenAround reads cells x-1..x+1, i.e. vertices x-1..x+2.
                        if (x < x0 + 1 || y < y0 + 1 || x > x1 - 2 || y > y1 - 2) continue;
                        if (OpenAround(grid, s, x, y)) return new Vector2Int(x, y);
                    }
            return start;
        }

        private static bool OpenAround(WorldTerrainGrid grid, WorldGenSettings s, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (IsBlocked(grid, s, cx, cy, grid.TerrainAt(cx, cy), grid.TerrainAt(cx + 1, cy),
                                  grid.TerrainAt(cx + 1, cy + 1), grid.TerrainAt(cx, cy + 1)))
                        return false;
                }
            return true;
        }

        public static bool IsBlocked(WorldTerrainGrid grid, WorldGenSettings s, int x, int y,
                                     string sw, string se, string ne, string nw)
        {
            int wet = Wet(sw) + Wet(se) + Wet(ne) + Wet(nw);
            if (wet >= SeedWorldBaker.BlockingCorners) return true;

            float mean = (grid.ElevationAt(x, y) + grid.ElevationAt(x + 1, y)
                          + grid.ElevationAt(x + 1, y + 1) + grid.ElevationAt(x, y + 1)) * 0.25f;
            return mean >= s.mountainLevel + SeedWorldBaker.PeakMargin && (sw == "rock" || sw == WorldTerrainGrid.Stone);
        }

        private static int Wet(string terrain)
            => terrain == WorldTerrainGrid.Water || terrain == WorldTerrainGrid.WaterDeep
               || terrain == WorldTerrainGrid.Lava ? 1 : 0;
    }
}
