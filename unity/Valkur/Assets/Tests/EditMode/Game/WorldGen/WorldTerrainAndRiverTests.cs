using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.WorldGen;

namespace Valkur.Tests.EditMode.Game.WorldGen
{
    /// <summary>
    /// Rivers and the vertex terrain grid — the two non-local stages of the generator (phase 2 of
    /// <c>.github/SEED_WORLD_ROADMAP.md</c>). Pure: the pack pairs are injected, no catalogue.
    /// </summary>
    public class WorldTerrainAndRiverTests
    {
        /// <summary>The Corner16 pairs the shipped TerrainCatalog has, as of 2026-09-14.</summary>
        private static readonly HashSet<string> ShippedPairs = new HashSet<string>
        {
            Key("grass", "dirt"), Key("grass", "rock"), Key("rock", "water"), Key("sand", "grass"),
            Key("sand", "rock"), Key("stone", "lava"), Key("water", "water_deep"),
        };

        private static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        private static bool ShippedCompatible(string a, string b) => ShippedPairs.Contains(Key(a, b));

        private static WorldGenSettings Settings(int seed = 1337, int rivers = 10)
            => new WorldGenSettings { seed = seed, widthTiles = 200, heightTiles = 200, riverCount = rivers };

        // ── Rivers ─────────────────────────────────────────────────────────────

        [Test]
        public void Rivers_AreDeterministic()
        {
            var a = WorldRivers.Generate(new WorldClimate(Settings(77)));
            var b = WorldRivers.Generate(new WorldClimate(Settings(77)));
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                CollectionAssert.AreEqual(a[i].Tiles.ToList(), b[i].Tiles.ToList());
        }

        /// <summary>
        /// A tile joined to the rest of its river only at a corner is a gap a player walks through,
        /// and the ground reads it as two pools. Every tile after the source must share an EDGE with
        /// some earlier tile of the same river — the path may double back over its own bed, so
        /// "the previous entry" is the wrong question.
        /// </summary>
        [Test]
        public void EveryRiverTile_SharesAnEdgeWithAnEarlierOne()
        {
            foreach (int seed in new[] { 1, 42, 1337 })
                foreach (var river in WorldRivers.Generate(new WorldClimate(Settings(seed))))
                {
                    var seen = new HashSet<Vector2Int> { river.Tiles[0] };
                    for (int i = 1; i < river.Length; i++)
                    {
                        var t = river.Tiles[i];
                        bool joined = seen.Contains(t + Vector2Int.up) || seen.Contains(t + Vector2Int.down)
                                   || seen.Contains(t + Vector2Int.left) || seen.Contains(t + Vector2Int.right);
                        Assert.IsTrue(joined, $"seed {seed}: tile {i} {t} touches its river only at a corner");
                        seen.Add(t);
                    }
                }
        }

        /// <summary>
        /// Rivers must bend. Choosing among four neighbours drew ruler lines 52 to 70 tiles long on a
        /// 400-tile map; tracing a curve took the longest straight run to about ten.
        /// </summary>
        [Test]
        public void Rivers_Meander_NoLongStraightRuns()
        {
            foreach (int seed in new[] { 1, 42, 1337, 99999 })
                foreach (var river in WorldRivers.Generate(new WorldClimate(Settings(seed))))
                {
                    int run = 1, longest = 1;
                    for (int i = 2; i < river.Length; i++)
                    {
                        if (river.Tiles[i] - river.Tiles[i - 1] == river.Tiles[i - 1] - river.Tiles[i - 2])
                            longest = Mathf.Max(longest, ++run);
                        else run = 1;
                    }
                    Assert.LessOrEqual(longest, 25, $"seed {seed}: a river runs {longest} tiles dead straight");
                }
        }

        /// <summary>A river ends in the sea or in another river; one that just stops is a puddle the generator should have dropped.</summary>
        [Test]
        public void EveryRiver_EndsInWaterOrAnotherRiver()
        {
            var climate = new WorldClimate(Settings(42));
            var rivers = WorldRivers.Generate(climate);
            var earlier = new HashSet<Vector2Int>();
            foreach (var river in rivers)
            {
                var mouth = river.Mouth;
                var biome = climate.BiomeAt(mouth.x + 0.5f, mouth.y + 0.5f);
                bool sea = biome == WorldBiome.Ocean || biome == WorldBiome.DeepOcean;
                Assert.IsTrue(sea || earlier.Contains(mouth), $"river ending at {mouth} ({biome}) reaches nothing");
                Assert.GreaterOrEqual(river.Length, WorldRivers.MinLength);
                foreach (var t in river.Tiles) earlier.Add(t);
            }
        }

        [Test]
        public void ZeroRivers_OrRiverDisabled_ProducesNone()
        {
            Assert.AreEqual(0, WorldRivers.Generate(new WorldClimate(Settings(3, rivers: 0))).Count);

            var s = Settings(3);
            s.WeightOf(WorldBiome.River).enabled = false;
            Assert.AreEqual(0, WorldRivers.Generate(new WorldClimate(s)).Count);
        }

        [Test]
        public void RiverWidth_WidensTheRaster()
        {
            var river = new WorldRiver(new List<Vector2Int> { new Vector2Int(10, 10), new Vector2Int(10, 11) });
            var rivers = new List<WorldRiver> { river };
            Assert.AreEqual(2, WorldRiverRaster.Tiles(rivers, 1, 50, 50).Count);
            // Width 2 covers the line and the tile to its +x/+y side: 2 columns by 3 rows.
            Assert.AreEqual(6, WorldRiverRaster.Tiles(rivers, 2, 50, 50).Count);
            // Width 3 is a 3x3 stamp on each of the two tiles: 3 columns by 4 rows.
            Assert.AreEqual(12, WorldRiverRaster.Tiles(rivers, 3, 50, 50).Count);
        }

        // ── Terrain grid ───────────────────────────────────────────────────────

        [Test]
        public void TheGrid_HasOneMoreVertexThanTilesOnEachAxis()
        {
            var climate = new WorldClimate(Settings());
            var grid = WorldTerrainGrid.Build(climate, new List<WorldRiver>(), 200, 200, ShippedCompatible);
            Assert.AreEqual(201, grid.Width);
            Assert.AreEqual(201, grid.Height);
        }

        /// <summary>
        /// After repair, every touching pair of vertices is drawable except pairs no chain of packs
        /// can connect at all (stone/lava against the rest). That exception must stay a small
        /// minority, or the repair is not working.
        /// </summary>
        [Test]
        public void AfterRepair_AlmostEveryTouchingPairIsDrawable()
        {
            foreach (int seed in new[] { 1, 42, 1337, 99999 })
            {
                var climate = new WorldClimate(Settings(seed));
                var grid = WorldTerrainGrid.Build(climate, WorldRivers.Generate(climate), 200, 200, ShippedCompatible);

                int bad = 0, total = 0, badNotVolcanic = 0;
                for (int y = 0; y < grid.Height - 1; y++)
                    for (int x = 0; x < grid.Width - 1; x++)
                    {
                        foreach (var (bx, by) in new[] { (x + 1, y), (x, y + 1), (x + 1, y + 1) })
                        {
                            total++;
                            string a = grid.TerrainAt(x, y), b = grid.TerrainAt(bx, by);
                            if (grid.Compatible(a, b)) continue;
                            bad++;
                            bool volcanic = a == "stone" || a == "lava" || b == "stone" || b == "lava";
                            if (!volcanic) badNotVolcanic++;
                        }
                    }

                Assert.AreEqual(0, badNotVolcanic, $"seed {seed}: a non-volcanic pair was left undrawable");
                Assert.Less(bad, total / 50, $"seed {seed}: {bad} of {total} pairs undrawable");
            }
        }

        /// <summary>The repair rewrites the LAND side: the sea keeps the shape the climate gave it.</summary>
        [Test]
        public void Repair_NeverTurnsWaterIntoLand()
        {
            var s = Settings(42, rivers: 0);
            var climate = new WorldClimate(s);
            var grid = WorldTerrainGrid.Build(climate, new List<WorldRiver>(), 200, 200, ShippedCompatible);

            for (int y = 0; y < grid.Height; y += 3)
                for (int x = 0; x < grid.Width; x += 3)
                {
                    var biome = climate.BiomeAt(x, y);
                    if (biome != WorldBiome.Ocean && biome != WorldBiome.DeepOcean) continue;
                    string t = grid.TerrainAt(x, y);
                    Assert.That(t, Is.EqualTo("water").Or.EqualTo("water_deep"), $"({x},{y}) was {biome}, painted {t}");
                }
        }

        [Test]
        public void RiverTiles_AreWaterOnAllFourCorners()
        {
            var climate = new WorldClimate(Settings(1337));
            var rivers = WorldRivers.Generate(climate);
            Assume.That(rivers.Count, Is.GreaterThan(0), "this seed should trace at least one river");
            var grid = WorldTerrainGrid.Build(climate, rivers, 200, 200, ShippedCompatible);

            foreach (var t in rivers[0].Tiles)
                for (int dy = 0; dy <= 1; dy++)
                    for (int dx = 0; dx <= 1; dx++)
                        Assert.That(grid.TerrainAt(t.x + dx, t.y + dy), Is.EqualTo("water").Or.EqualTo("water_deep"));
        }

        [Test]
        public void TheOverflowBeyondTheWorld_IsDeepWater()
        {
            var s = Settings();
            s.widthTiles = 180;
            var grid = WorldTerrainGrid.Build(new WorldClimate(s), new List<WorldRiver>(), 200, 200, ShippedCompatible);
            Assert.AreEqual("water_deep", grid.TerrainAt(195, 100));
        }

        [Test]
        public void EveryBiome_NamesAGroundTerrain()
        {
            for (int i = 0; i < WorldBiomeTable.Count; i++)
                Assert.IsFalse(string.IsNullOrEmpty(WorldBiomeTable.GetAt(i).GroundTerrain), WorldBiomeTable.GetAt(i).Biome.ToString());
        }
    }
}
