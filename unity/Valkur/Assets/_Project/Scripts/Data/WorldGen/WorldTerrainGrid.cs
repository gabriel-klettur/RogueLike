using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// The ground of a built world, one terrain name per VERTEX.
    ///
    /// <para><b>Vertices, not cells, because that is how the art is authored.</b> Every shipped
    /// auto-tile pack is Corner16: a tile's four corners are four vertices of a dual grid, and a
    /// tile half grass and half sand exists only because its corners disagree. A W x H world has
    /// (W+1) x (H+1) of them, and the tile at cell (x, y) reads (x, y), (x+1, y), (x, y+1) and
    /// (x+1, y+1) — the same convention <c>TerrainMap</c> and <c>BitmaskCalculator</c> use.</para>
    ///
    /// <para><b>Transitions are REPAIRED, not assumed.</b> A pack draws exactly two terrains, and
    /// the catalogue only has some pairs: there is grass/sand and sand/rock and rock/water, but no
    /// sand/water, so a beach meeting the sea directly has no tile that can draw it and would be a
    /// hard straight cut. <see cref="RepairTransitions"/> walks every pair of touching vertices
    /// and, where no pack draws the pair, replaces the land side with the next terrain on the
    /// shortest chain of packs that DOES connect them — so a beach grows a rocky edge where it
    /// meets the water. Which pairs exist is injected (<see cref="Compatible"/>), because the
    /// catalogue is an asset and this class must stay testable without one.</para>
    ///
    /// <para><b>A grid may cover only a REGION of the world, and a region answers exactly what
    /// the whole world would.</b> The live world (phase 5) builds one zone at a time, so the
    /// repair is LOCAL by construction: every pass reads the previous pass and writes a fresh
    /// one (never in place), so after <see cref="MaxRepairPasses"/> passes a vertex depends only
    /// on the vertices within that many steps of it. A region padded by <see cref="RegionMargin"/>
    /// therefore agrees with the full world on every vertex it was asked for, and two zones built
    /// separately agree on the seam they share. An in-place sweep would not: its result depends on
    /// where the scan STARTED, which is exactly what differs between a region and the world.</para>
    /// </summary>
    public sealed class WorldTerrainGrid
    {
        public const string WaterDeep = "water_deep";
        public const string Water = "water";
        public const string Lava = "lava";
        public const string Stone = "stone";

        /// <summary>Above the mountain line by more than this, volcanic stone opens into lava.</summary>
        public const float LavaAboveMountain = 0.1f;

        /// <summary>Passes of the repair sweep. Each pass moves a bridge one vertex further inland.</summary>
        public const int MaxRepairPasses = 6;

        /// <summary>
        /// Padding around a region, in vertices. The repair reaches <see cref="MaxRepairPasses"/>
        /// vertices, so a margin one wider than that isolates the requested area from the edge of
        /// the padded one, where the region lacks the neighbours the whole world has.
        /// </summary>
        public const int RegionMargin = MaxRepairPasses + 1;

        /// <summary>
        /// When two touching vertices must be reconciled, the one LATER in this list is the one
        /// rewritten. Water first, so coastlines and rivers keep the shape the generator gave them
        /// and it is the shore that grows a bridge, never the sea that retreats.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Immutable rank table of terrain names; never mutated and holds " +
            "no Unity objects, so it cannot go stale across a Play session.")]
        private static readonly string[] Rank = { WaterDeep, Water, Lava, "rock", Stone, "sand", "grass", "dirt" };

        public readonly int Width;   // in vertices, of THIS grid
        public readonly int Height;  // in vertices, of THIS grid

        /// <summary>World vertex coordinates of this grid's (0, 0).</summary>
        public readonly int OriginX;
        public readonly int OriginY;

        private string[] _terrain;
        private readonly float[] _elevation;
        private readonly Func<string, string, bool> _compatible;

        public int UnresolvedPairs { get; private set; }
        public int RepairedVertices { get; private set; }

        private WorldTerrainGrid(int originX, int originY, int width, int height, Func<string, string, bool> compatible)
        {
            OriginX = originX;
            OriginY = originY;
            Width = width;
            Height = height;
            _terrain = new string[width * height];
            _elevation = new float[width * height];
            _compatible = compatible;
        }

        /// <summary>The terrain at WORLD vertex (vx, vy). Null outside the world.</summary>
        public string TerrainAt(int vx, int vy) => _terrain[(vy - OriginY) * Width + (vx - OriginX)];
        public float ElevationAt(int vx, int vy) => _elevation[(vy - OriginY) * Width + (vx - OriginX)];

        public bool Compatible(string a, string b) => a == b || _compatible(a, b);

        /// <summary>
        /// Samples the climate at every vertex of a <paramref name="widthTiles"/> x
        /// <paramref name="heightTiles"/> area (which may be larger than the settings' world,
        /// to fill whole zones — the overflow is deep water), carves the rivers, then repairs.
        /// </summary>
        public static WorldTerrainGrid Build(WorldClimate climate, IReadOnlyList<WorldRiver> rivers,
                                             int widthTiles, int heightTiles,
                                             Func<string, string, bool> compatible)
            => Build(climate, rivers, null, widthTiles, heightTiles, compatible);

        /// <summary>
        /// As above, with towns: inside a town the ground is levelled to grass (a street across a
        /// patch of rock reads as a mistake) and every street and plaza tile is dirt. Water is
        /// never levelled: the planner keeps streets off it, and a pond inside a town is fine.
        /// </summary>
        public static WorldTerrainGrid Build(WorldClimate climate, IReadOnlyList<WorldRiver> rivers,
                                             IReadOnlyList<WorldTown> towns,
                                             int widthTiles, int heightTiles,
                                             Func<string, string, bool> compatible)
            => Build(climate, rivers, towns, null, widthTiles, heightTiles, compatible);

        /// <summary>As above, with the plan's road raster painted as dirt between the towns.</summary>
        public static WorldTerrainGrid Build(WorldClimate climate, IReadOnlyList<WorldRiver> rivers,
                                             IReadOnlyList<WorldTown> towns, HashSet<Vector2Int> roadTiles,
                                             int widthTiles, int heightTiles,
                                             Func<string, string, bool> compatible)
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            var s = climate.Settings;
            var riverTiles = WorldRiverRaster.Tiles(rivers, s.riverWidth, s.widthTiles, s.heightTiles);
            return BuildRegion(climate, riverTiles, towns, widthTiles, heightTiles,
                               0, 0, widthTiles, heightTiles, 0, compatible, roadTiles);
        }

        /// <summary>
        /// The vertices of the tiles [<paramref name="x0"/>, x0 + <paramref name="tilesW"/>) x
        /// [<paramref name="y0"/>, y0 + <paramref name="tilesH"/>) of a world whose built area is
        /// <paramref name="worldTilesW"/> x <paramref name="worldTilesH"/>, padded by
        /// <paramref name="margin"/> vertices on every side so the requested vertices come out
        /// identical to a full <see cref="Build(WorldClimate, IReadOnlyList{WorldRiver}, IReadOnlyList{WorldTown}, int, int, Func{string, string, bool})"/>.
        /// Pass <see cref="RegionMargin"/> for that guarantee. <paramref name="riverTiles"/> is the
        /// world's whole river raster, computed once by the caller.
        /// </summary>
        public static WorldTerrainGrid BuildRegion(WorldClimate climate, HashSet<Vector2Int> riverTiles,
                                                   IReadOnlyList<WorldTown> towns,
                                                   int worldTilesW, int worldTilesH,
                                                   int x0, int y0, int tilesW, int tilesH, int margin,
                                                   Func<string, string, bool> compatible,
                                                   HashSet<Vector2Int> roadTiles = null)
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            if (compatible == null) throw new ArgumentNullException(nameof(compatible));

            var s = climate.Settings;
            margin = Mathf.Max(0, margin);
            var grid = new WorldTerrainGrid(x0 - margin, y0 - margin,
                                            tilesW + 1 + 2 * margin, tilesH + 1 + 2 * margin, compatible);
            int worldVertW = worldTilesW + 1, worldVertH = worldTilesH + 1;

            for (int gy = 0; gy < grid.Height; gy++)
                for (int gx = 0; gx < grid.Width; gx++)
                {
                    int vx = grid.OriginX + gx, vy = grid.OriginY + gy;
                    int i = gy * grid.Width + gx;
                    if (vx < 0 || vy < 0 || vx >= worldVertW || vy >= worldVertH)
                        continue; // outside the world: no vertex at all
                    if (vx > s.widthTiles || vy > s.heightTiles)
                    {
                        grid._terrain[i] = WaterDeep;
                        continue;
                    }

                    var sample = climate.Sample(vx, vy);
                    var biome = climate.Classify(sample);
                    grid._elevation[i] = sample.Elevation;
                    grid._terrain[i] = GroundFor(biome, sample, s);
                }

            // A river tile makes all four of its corners water, so a one-tile river is one fully
            // water tile wide with a transition tile on each bank.
            if (riverTiles != null)
                foreach (var t in riverTiles)
                {
                    if (t.x + 1 < grid.OriginX || t.y + 1 < grid.OriginY ||
                        t.x >= grid.OriginX + grid.Width || t.y >= grid.OriginY + grid.Height) continue;
                    for (int dy = 0; dy <= 1; dy++)
                        for (int dx = 0; dx <= 1; dx++)
                        {
                            int i = grid.LocalIndex(t.x + dx, t.y + dy);
                            if (i < 0 || grid._terrain[i] == null) continue;
                            if (grid._terrain[i] != WaterDeep) grid._terrain[i] = Water;
                        }
                }

            if (towns != null) grid.StampTowns(towns);
            if (roadTiles != null) grid.StampRoads(roadTiles);

            grid.RepairTransitions();
            return grid;
        }

        /// <summary>Index into this grid of WORLD vertex (vx, vy), or -1 when it is not covered.</summary>
        private int LocalIndex(int vx, int vy)
        {
            int gx = vx - OriginX, gy = vy - OriginY;
            if (gx < 0 || gy < 0 || gx >= Width || gy >= Height) return -1;
            return gy * Width + gx;
        }

        public const string TownGround = "grass";
        public const string StreetGround = "dirt";

        private void StampTowns(IReadOnlyList<WorldTown> towns)
        {
            foreach (var town in towns)
            {
                int r = town.Radius + 2;
                if (town.Center.x + r + 1 < OriginX || town.Center.y + r + 1 < OriginY ||
                    town.Center.x - r >= OriginX + Width || town.Center.y - r >= OriginY + Height) continue;

                for (int vy = town.Center.y - r; vy <= town.Center.y + r + 1; vy++)
                    for (int vx = town.Center.x - r; vx <= town.Center.x + r + 1; vx++)
                    {
                        int i = LocalIndex(vx, vy);
                        if (i < 0 || _terrain[i] == null) continue;
                        if (!town.Contains(new Vector2Int(vx, vy), 2)) continue;
                        if (_terrain[i] == Water || _terrain[i] == WaterDeep) continue;
                        _terrain[i] = TownGround;
                    }

                // A street TILE makes its four corners dirt, so a street is fully dirt with a soft edge.
                foreach (var t in town.StreetTiles)
                    for (int dy = 0; dy <= 1; dy++)
                        for (int dx = 0; dx <= 1; dx++)
                        {
                            int i = LocalIndex(t.x + dx, t.y + dy);
                            if (i < 0 || _terrain[i] == null) continue;
                            if (_terrain[i] == Water || _terrain[i] == WaterDeep) continue;
                            _terrain[i] = StreetGround;
                        }
            }
        }

        /// <summary>
        /// A road TILE makes its four corners dirt, exactly as a street tile does, so a road meeting a
        /// street is one surface. The plan keeps roads off water; the check here is for the corner a
        /// road tile shares with a river bank, which stays water and gives the road its bank.
        /// </summary>
        private void StampRoads(HashSet<Vector2Int> roadTiles)
        {
            int x0 = OriginX - 1, y0 = OriginY - 1, x1 = OriginX + Width, y1 = OriginY + Height;
            foreach (var t in roadTiles)
            {
                if (t.x < x0 || t.y < y0 || t.x >= x1 || t.y >= y1) continue;
                for (int dy = 0; dy <= 1; dy++)
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        int i = LocalIndex(t.x + dx, t.y + dy);
                        if (i < 0 || _terrain[i] == null) continue;
                        if (_terrain[i] == Water || _terrain[i] == WaterDeep) continue;
                        _terrain[i] = StreetGround;
                    }
            }
        }

        private static string GroundFor(WorldBiome biome, in WorldClimateSample sample, WorldGenSettings s)
        {
            if (biome == WorldBiome.Volcanic && sample.Elevation >= s.mountainLevel + LavaAboveMountain)
                return Lava;
            return WorldBiomeTable.Get(biome).GroundTerrain;
        }

        /// <summary>
        /// Jacobi passes: each reads <c>prev</c> and writes <c>next</c>, and when several pairs want
        /// to rewrite one vertex in the same pass the first in scan order wins. Scan order among the
        /// pairs touching one vertex does not depend on where the grid starts, so the rule is as
        /// local as the reads are.
        /// </summary>
        private void RepairTransitions()
        {
            int repaired = 0;
            var prev = _terrain;
            var next = new string[prev.Length];
            var written = new bool[prev.Length];

            for (int pass = 0; pass < MaxRepairPasses; pass++)
            {
                Array.Copy(prev, next, prev.Length);
                Array.Clear(written, 0, written.Length);
                int changed = 0;
                for (int gy = 0; gy < Height; gy++)
                    for (int gx = 0; gx < Width; gx++)
                    {
                        // Right, up, up-right and up-left cover every touching pair exactly once:
                        // two vertices touch when they are corners of one tile.
                        changed += Reconcile(prev, next, written, gx, gy, gx + 1, gy);
                        changed += Reconcile(prev, next, written, gx, gy, gx, gy + 1);
                        changed += Reconcile(prev, next, written, gx, gy, gx + 1, gy + 1);
                        changed += Reconcile(prev, next, written, gx, gy, gx - 1, gy + 1);
                    }
                repaired += changed;
                var swap = prev; prev = next; next = swap;
                if (changed == 0) break;
            }
            _terrain = prev;
            RepairedVertices = repaired;

            int unresolved = 0;
            for (int gy = 0; gy < Height; gy++)
                for (int gx = 0; gx < Width; gx++)
                {
                    if (!Touching(gx, gy, gx + 1, gy)) unresolved++;
                    if (!Touching(gx, gy, gx, gy + 1)) unresolved++;
                    if (!Touching(gx, gy, gx + 1, gy + 1)) unresolved++;
                    if (!Touching(gx, gy, gx - 1, gy + 1)) unresolved++;
                }
            UnresolvedPairs = unresolved;
        }

        /// <summary>True when the pair is out of bounds, outside the world, or drawable.</summary>
        private bool Touching(int ax, int ay, int bx, int by)
        {
            if (bx < 0 || by < 0 || bx >= Width || by >= Height) return true;
            string a = _terrain[ay * Width + ax], b = _terrain[by * Width + bx];
            if (a == null || b == null) return true;
            return Compatible(a, b);
        }

        private int Reconcile(string[] prev, string[] next, bool[] written, int ax, int ay, int bx, int by)
        {
            if (bx < 0 || by < 0 || bx >= Width || by >= Height) return 0;
            int ia = ay * Width + ax, ib = by * Width + bx;
            string a = prev[ia], b = prev[ib];
            if (a == null || b == null || Compatible(a, b)) return 0;

            // Keep the lower-ranked (wetter) terrain; rewrite the other to the next step towards it.
            bool rewriteA = RankOf(a) > RankOf(b);
            string keep = rewriteA ? b : a;
            string change = rewriteA ? a : b;
            int target = rewriteA ? ia : ib;
            if (written[target]) return 0;

            string step = NextStep(keep, change);
            if (step == null || step == change) return 0;

            next[target] = step;
            written[target] = true;
            return 1;
        }

        /// <summary>
        /// The terrain adjacent to <paramref name="from"/> on the shortest chain of drawable pairs
        /// from <paramref name="from"/> to <paramref name="to"/>, or null when none exists (stone and
        /// lava share a pack with each other and nothing else — volcanic ground keeps a hard edge).
        /// </summary>
        private string NextStep(string from, string to)
        {
            var queue = new Queue<string>();
            var parent = new Dictionary<string, string>();
            queue.Enqueue(to);
            parent[to] = null;

            while (queue.Count > 0)
            {
                string node = queue.Dequeue();
                for (int i = 0; i < Rank.Length; i++)
                {
                    string next = Rank[i];
                    if (parent.ContainsKey(next) || !Compatible(node, next)) continue;
                    parent[next] = node;
                    if (next == from) return node; // node is from's neighbour on the chain towards `to`
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        private static int RankOf(string terrain)
        {
            int i = Array.IndexOf(Rank, terrain);
            return i < 0 ? Rank.Length : i;
        }
    }
}
