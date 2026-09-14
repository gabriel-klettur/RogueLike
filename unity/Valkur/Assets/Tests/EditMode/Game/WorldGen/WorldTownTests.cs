using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.WorldGen;

namespace Valkur.Tests.EditMode.Game.WorldGen
{
    /// <summary>
    /// Towns: where they are, their streets and how their lots fill (phase 3 of
    /// <c>.github/SEED_WORLD_ROADMAP.md</c>). Pure — building sizes are fake options, no catalogue.
    /// </summary>
    public class WorldTownTests
    {
        private static WorldGenSettings Settings(int seed = 1337)
            => new WorldGenSettings { seed = seed, widthTiles = 300, heightTiles = 300, townCount = 3, townRadius = 18 };

        private static readonly WorldTownBuildingOption[] Options =
        {
            new WorldTownBuildingOption(1, 3, 4, WorldTownPieceKind.House),
            new WorldTownBuildingOption(2, 5, 5, WorldTownPieceKind.House),
            new WorldTownBuildingOption(3, 5, 6, WorldTownPieceKind.Shop),
            new WorldTownBuildingOption(4, 3, 4, WorldTownPieceKind.Centerpiece),
            new WorldTownBuildingOption(5, 1, 3, WorldTownPieceKind.Lamp),
            new WorldTownBuildingOption(6, 3, 3, WorldTownPieceKind.Stall),
        };

        private static (WorldClimate climate, WorldGenMap map) Plan(int seed = 1337)
        {
            var climate = new WorldClimate(Settings(seed));
            return (climate, WorldGenMap.Generate(climate, 64));
        }

        [Test]
        public void TownPlans_AreDeterministic_AndIndependentOfPreviewResolution()
        {
            var climate = new WorldClimate(Settings(21));
            var low = WorldGenMap.Generate(climate, 32).Towns;
            var high = WorldGenMap.Generate(new WorldClimate(Settings(21)), 300).Towns;

            Assert.AreEqual(low.Count, high.Count);
            for (int i = 0; i < low.Count; i++)
            {
                Assert.AreEqual(low[i].Center, high[i].Center, "a town moved with the preview resolution");
                CollectionAssert.AreEquivalent(low[i].StreetTiles, high[i].StreetTiles);
            }
        }

        [Test]
        public void TheFirstTown_IsTheStartingTown_AndTheSpawnIsOnItsStreet()
        {
            var (_, map) = Plan();
            Assume.That(map.Towns.Count, Is.GreaterThan(0));
            var start = map.Towns[0];
            Assert.IsTrue(start.IsStart);

            var spawn = Vector2Int.FloorToInt(map.SpawnTile);
            Assert.AreEqual(WorldTowns.SpawnTileOf(start), spawn);
            Assert.IsTrue(start.StreetTiles.Contains(spawn), "the run must begin on a street, where no building can stand");
        }

        [Test]
        public void Towns_KeepTheirSpacing()
        {
            var (climate, map) = Plan(7);
            int spacing = climate.Settings.townRadius * 3;
            for (int i = 0; i < map.Towns.Count; i++)
                for (int j = i + 1; j < map.Towns.Count; j++)
                    Assert.GreaterOrEqual((map.Towns[i].Center - map.Towns[j].Center).magnitude, spacing - 0.01f);
        }

        [Test]
        public void NoStreet_CrossesWaterOrHighland()
        {
            var (climate, map) = Plan(42);
            foreach (var town in map.Towns)
                foreach (var t in town.StreetTiles)
                    Assert.IsTrue(WorldTowns.IsBuildable(climate, map.RiverTiles, t), $"street tile {t} is not buildable ground");
        }

        /// <summary>
        /// A town that fits in a zone must sit inside ONE zone. The border is where the zone banner
        /// fires and the zone name changes; the first bake ran one through the starting plaza.
        /// </summary>
        [Test]
        public void ATownThatFitsAZone_NeverStraddlesAZoneBorder()
        {
            foreach (int seed in new[] { 1, 42, 1337, 99999 })
            {
                var (_, map) = Plan(seed);
                foreach (var town in map.Towns)
                {
                    int zx0 = Mathf.FloorToInt((town.Center.x - town.Radius) / (float)WorldTowns.ZoneSize);
                    int zx1 = Mathf.FloorToInt((town.Center.x + town.Radius) / (float)WorldTowns.ZoneSize);
                    int zy0 = Mathf.FloorToInt((town.Center.y - town.Radius) / (float)WorldTowns.ZoneSize);
                    int zy1 = Mathf.FloorToInt((town.Center.y + town.Radius) / (float)WorldTowns.ZoneSize);
                    Assert.AreEqual(zx0, zx1, $"seed {seed}: town {town.Index} at {town.Center} crosses a vertical zone border");
                    Assert.AreEqual(zy0, zy1, $"seed {seed}: town {town.Index} at {town.Center} crosses a horizontal zone border");
                }
            }
        }

        [Test]
        public void ZeroTowns_PlansNone_AndKeepsTheClimateSpawn()
        {
            var s = Settings();
            s.townCount = 0;
            var map = WorldGenMap.Generate(s, 64);
            Assert.AreEqual(0, map.Towns.Count);
            Assert.IsTrue(map.HasSpawn);
        }

        // ── Lots ───────────────────────────────────────────────────────────────

        [Test]
        public void Placements_NeverOverlapEachOtherOrAStreet()
        {
            foreach (int seed in new[] { 1, 1337, 99999 })
            {
                var (climate, map) = Plan(seed);
                foreach (var town in map.Towns)
                {
                    var placed = WorldTownLots.Place(town, climate, map.RiverTiles, Options);
                    Assert.Greater(placed.Count, 4, $"seed {seed}: town {town.Index} is nearly empty");

                    var cover = new HashSet<Vector2Int>();
                    foreach (var p in placed)
                        for (int y = p.Rect.yMin; y < p.Rect.yMax; y++)
                            for (int x = p.Rect.xMin; x < p.Rect.xMax; x++)
                            {
                                var t = new Vector2Int(x, y);
                                Assert.IsTrue(cover.Add(t), $"seed {seed}: two buildings share tile {t}");
                                bool onPlaza = town.Plaza.Contains(t);
                                if (!onPlaza)
                                    Assert.IsFalse(town.StreetTiles.Contains(t), $"seed {seed}: a building stands on street tile {t}");
                                Assert.IsTrue(WorldTowns.IsBuildable(climate, map.RiverTiles, t), $"seed {seed}: building on unbuildable {t}");
                            }
                }
            }
        }

        /// <summary>Two street-side buildings must never touch: the ring between them is the passage the collision grid would otherwise seal.</summary>
        [Test]
        public void StreetBuildings_LeaveAGapBetweenNeighbours()
        {
            var (climate, map) = Plan(1337);
            var town = map.Towns[0];
            var placed = WorldTownLots.Place(town, climate, map.RiverTiles, Options)
                .Where(p => p.Option.Kind == WorldTownPieceKind.House || p.Option.Kind == WorldTownPieceKind.Shop).ToList();

            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                {
                    var a = placed[i].Rect;
                    var grown = new RectInt(a.xMin - 1, a.yMin - 1, a.width + 2, a.height + 2);
                    Assert.IsFalse(grown.Overlaps(placed[j].Rect), $"{a} touches {placed[j].Rect}");
                }
        }

        [Test]
        public void ThePlaza_HasItsCentrepiece()
        {
            var (climate, map) = Plan(1337);
            var town = map.Towns[0];
            var placed = WorldTownLots.Place(town, climate, map.RiverTiles, Options);
            Assert.IsTrue(placed.Any(p => p.Option.Kind == WorldTownPieceKind.Centerpiece && town.Plaza.Overlaps(p.Rect)));
        }

        [Test]
        public void Lots_AreDeterministic()
        {
            var (climate, map) = Plan(5);
            var a = WorldTownLots.Place(map.Towns[0], climate, map.RiverTiles, Options);
            var b = WorldTownLots.Place(map.Towns[0], climate, map.RiverTiles, Options);
            CollectionAssert.AreEqual(a.Select(p => (p.Option.TemplateId, p.Rect)).ToList(),
                                      b.Select(p => (p.Option.TemplateId, p.Rect)).ToList());
        }

        [Test]
        public void TownGround_IsLevelled_AndStreetsAreDirt()
        {
            var (climate, map) = Plan(1337);
            var town = map.Towns[0];
            var grid = WorldTerrainGrid.Build(climate, map.Rivers, map.Towns, 300, 300, (x, y) => true);
            // A street tile beside a river shares a corner with the river's own tile, and water is
            // never levelled — so that corner stays water and the street gets a bank. Everything
            // else must be dirt.
            int dirt = 0;
            foreach (var t in town.StreetTiles)
            {
                string terrain = grid.TerrainAt(t.x, t.y);
                Assert.That(terrain, Is.EqualTo(WorldTerrainGrid.StreetGround)
                    .Or.EqualTo(WorldTerrainGrid.Water).Or.EqualTo(WorldTerrainGrid.WaterDeep), $"street {t}");
                if (terrain == WorldTerrainGrid.StreetGround) dirt++;
            }
            Assert.Greater(dirt, town.StreetTiles.Count * 9 / 10);
        }
    }
}
