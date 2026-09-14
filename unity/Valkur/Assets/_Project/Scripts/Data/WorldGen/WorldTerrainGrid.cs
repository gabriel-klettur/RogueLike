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
        /// When two touching vertices must be reconciled, the one LATER in this list is the one
        /// rewritten. Water first, so coastlines and rivers keep the shape the generator gave them
        /// and it is the shore that grows a bridge, never the sea that retreats.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Immutable rank table of terrain names; never mutated and holds " +
            "no Unity objects, so it cannot go stale across a Play session.")]
        private static readonly string[] Rank = { WaterDeep, Water, Lava, "rock", Stone, "sand", "grass", "dirt" };

        public readonly int Width;   // in vertices
        public readonly int Height;  // in vertices

        private readonly string[] _terrain;
        private readonly float[] _elevation;
        private readonly Func<string, string, bool> _compatible;

        public int UnresolvedPairs { get; private set; }
        public int RepairedVertices { get; private set; }

        private WorldTerrainGrid(int width, int height, Func<string, string, bool> compatible)
        {
            Width = width;
            Height = height;
            _terrain = new string[width * height];
            _elevation = new float[width * height];
            _compatible = compatible;
        }

        public string TerrainAt(int vx, int vy) => _terrain[vy * Width + vx];
        public float ElevationAt(int vx, int vy) => _elevation[vy * Width + vx];

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
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            if (compatible == null) throw new ArgumentNullException(nameof(compatible));

            var s = climate.Settings;
            var grid = new WorldTerrainGrid(widthTiles + 1, heightTiles + 1, compatible);

            for (int vy = 0; vy < grid.Height; vy++)
                for (int vx = 0; vx < grid.Width; vx++)
                {
                    int i = vy * grid.Width + vx;
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
            var riverTiles = WorldRiverRaster.Tiles(rivers, s.riverWidth, s.widthTiles, s.heightTiles);
            foreach (var t in riverTiles)
                for (int dy = 0; dy <= 1; dy++)
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        int i = (t.y + dy) * grid.Width + (t.x + dx);
                        if (grid._terrain[i] != WaterDeep) grid._terrain[i] = Water;
                    }

            if (towns != null) grid.StampTowns(towns);

            grid.RepairTransitions();
            return grid;
        }

        public const string TownGround = "grass";
        public const string StreetGround = "dirt";

        private void StampTowns(IReadOnlyList<WorldTown> towns)
        {
            foreach (var town in towns)
            {
                int r = town.Radius + 2;
                for (int vy = town.Center.y - r; vy <= town.Center.y + r + 1; vy++)
                    for (int vx = town.Center.x - r; vx <= town.Center.x + r + 1; vx++)
                    {
                        if (vx < 0 || vy < 0 || vx >= Width || vy >= Height) continue;
                        if (!town.Contains(new Vector2Int(vx, vy), 2)) continue;
                        int i = vy * Width + vx;
                        if (_terrain[i] == Water || _terrain[i] == WaterDeep) continue;
                        _terrain[i] = TownGround;
                    }

                // A street TILE makes its four corners dirt, so a street is fully dirt with a soft edge.
                foreach (var t in town.StreetTiles)
                    for (int dy = 0; dy <= 1; dy++)
                        for (int dx = 0; dx <= 1; dx++)
                        {
                            int vx = t.x + dx, vy = t.y + dy;
                            if (vx >= Width || vy >= Height) continue;
                            int i = vy * Width + vx;
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

        private void RepairTransitions()
        {
            int repaired = 0;
            for (int pass = 0; pass < MaxRepairPasses; pass++)
            {
                int changed = 0;
                for (int vy = 0; vy < Height; vy++)
                    for (int vx = 0; vx < Width; vx++)
                    {
                        // Right, up, up-right and up-left cover every touching pair exactly once:
                        // two vertices touch when they are corners of one tile.
                        changed += Reconcile(vx, vy, vx + 1, vy);
                        changed += Reconcile(vx, vy, vx, vy + 1);
                        changed += Reconcile(vx, vy, vx + 1, vy + 1);
                        changed += Reconcile(vx, vy, vx - 1, vy + 1);
                    }
                repaired += changed;
                if (changed == 0) break;
            }
            RepairedVertices = repaired;

            int unresolved = 0;
            for (int vy = 0; vy < Height; vy++)
                for (int vx = 0; vx < Width; vx++)
                {
                    if (!Touching(vx, vy, vx + 1, vy)) unresolved++;
                    if (!Touching(vx, vy, vx, vy + 1)) unresolved++;
                    if (!Touching(vx, vy, vx + 1, vy + 1)) unresolved++;
                    if (!Touching(vx, vy, vx - 1, vy + 1)) unresolved++;
                }
            UnresolvedPairs = unresolved;
        }

        /// <summary>True when the pair is out of bounds or drawable.</summary>
        private bool Touching(int ax, int ay, int bx, int by)
        {
            if (bx < 0 || by < 0 || bx >= Width || by >= Height) return true;
            return Compatible(_terrain[ay * Width + ax], _terrain[by * Width + bx]);
        }

        private int Reconcile(int ax, int ay, int bx, int by)
        {
            if (bx < 0 || by < 0 || bx >= Width || by >= Height) return 0;
            int ia = ay * Width + ax, ib = by * Width + bx;
            string a = _terrain[ia], b = _terrain[ib];
            if (Compatible(a, b)) return 0;

            // Keep the lower-ranked (wetter) terrain; rewrite the other to the next step towards it.
            bool rewriteA = RankOf(a) > RankOf(b);
            string keep = rewriteA ? b : a;
            string change = rewriteA ? a : b;

            string step = NextStep(keep, change);
            if (step == null || step == change) return 0;

            _terrain[rewriteA ? ia : ib] = step;
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
