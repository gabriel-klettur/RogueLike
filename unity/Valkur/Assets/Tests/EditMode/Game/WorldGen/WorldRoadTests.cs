using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Game.WorldGen
{
    /// <summary>
    /// Roads between towns (roadmap: "caminos entre pueblos"). Pure except the last test, which opens
    /// a live world written before roads existed.
    /// </summary>
    public class WorldRoadTests
    {
        private static WorldGenSettings Settings(int seed, bool roads = true)
            => new WorldGenSettings
            {
                seed = seed, widthTiles = 300, heightTiles = 300, townCount = 4, townRadius = 18,
                roadsBetweenTowns = roads,
            };

        private static readonly int[] Seeds = { 1, 42, 1337, 99999 };

        private static readonly WorldTownBuildingOption[] Options =
        {
            new WorldTownBuildingOption(1, 3, 4, WorldTownPieceKind.House),
            new WorldTownBuildingOption(2, 5, 5, WorldTownPieceKind.House),
            new WorldTownBuildingOption(3, 5, 6, WorldTownPieceKind.Shop),
            new WorldTownBuildingOption(4, 3, 4, WorldTownPieceKind.Centerpiece),
            new WorldTownBuildingOption(5, 1, 3, WorldTownPieceKind.Lamp),
            new WorldTownBuildingOption(6, 3, 3, WorldTownPieceKind.Stall),
        };

        [Test]
        public void Roads_FormATree_AndRunFromArmToArm()
        {
            int roadsSeen = 0;
            foreach (int seed in Seeds)
            {
                var map = WorldGenMap.Generate(Settings(seed), 64);
                Assert.LessOrEqual(map.Roads.Count, Mathf.Max(0, map.Towns.Count - 1),
                    $"seed {seed}: a tree over {map.Towns.Count} towns has at most {map.Towns.Count - 1} roads");

                foreach (var road in map.Roads)
                {
                    roadsSeen++;
                    var from = map.Towns[road.FromTown];
                    var to = map.Towns[road.ToTown];
                    Assert.IsTrue(from.StreetTiles.Contains(road.Path[0]) || OnArm(from, road.Path[0]),
                        $"seed {seed}: road {road.FromTown}->{road.ToTown} must leave from a main street, left from {road.Path[0]}");
                    Assert.IsTrue(to.StreetTiles.Contains(road.Path[road.Path.Count - 1]) || OnArm(to, road.Path[road.Path.Count - 1]),
                        $"seed {seed}: road {road.FromTown}->{road.ToTown} must arrive on a main street");
                    for (int i = 1; i < road.Path.Count; i++)
                        Assert.LessOrEqual(Chebyshev(road.Path[i], road.Path[i - 1]), 1,
                            $"seed {seed}: the centre line jumps at {road.Path[i]}");
                }
            }
            Assert.Greater(roadsSeen, 0, "Sanity: across four seeds at least one pair of towns was joined.");
        }

        [Test]
        public void NoRoadTile_IsWaterOrHighland_OrAHouseLot()
        {
            foreach (int seed in Seeds)
            {
                var climate = new WorldClimate(Settings(seed));
                var map = WorldGenMap.Generate(climate, 64);
                foreach (var t in map.RoadTiles)
                    Assert.IsTrue(WorldTowns.IsBuildable(climate, map.RiverTiles, t), $"seed {seed}: road on unbuildable {t}");

                foreach (var town in map.Towns)
                    foreach (var p in WorldTownLots.Place(town, climate, map.RiverTiles, Options))
                        for (int y = p.Rect.yMin; y < p.Rect.yMax; y++)
                            for (int x = p.Rect.xMin; x < p.Rect.xMax; x++)
                            {
                                var t = new Vector2Int(x, y);
                                if (town.Plaza.Contains(t)) continue;
                                Assert.IsFalse(map.RoadTiles.Contains(t), $"seed {seed}: town {town.Index} built on road tile {t}");
                            }
            }
        }

        [Test]
        public void Roads_AreDeterministic_AndIndependentOfPreviewResolution()
        {
            var low = WorldGenMap.Generate(Settings(21), 32);
            var high = WorldGenMap.Generate(Settings(21), 300);
            CollectionAssert.AreEquivalent(low.RoadTiles, high.RoadTiles,
                "The build plans at 64 cells and the preview at up to 320: roads must not move with that.");
        }

        [Test]
        public void RoadGround_IsDirt()
        {
            foreach (int seed in Seeds)
            {
                var map = WorldGenMap.Generate(Settings(seed), 64);
                if (map.RoadTiles.Count == 0) continue;
                var grid = WorldTerrainGrid.Build(map.Climate, map.Rivers, map.Towns, map.RoadTiles, 300, 300, (a, b) => true);
                int dirt = 0;
                foreach (var t in map.RoadTiles)
                    if (grid.TerrainAt(t.x, t.y) == WorldTerrainGrid.StreetGround) dirt++;
                Assert.Greater(dirt, map.RoadTiles.Count * 9 / 10, $"seed {seed}");
            }
        }

        [Test]
        public void NoTree_StandsOnOrBesideARoad()
        {
            foreach (int seed in Seeds)
            {
                var map = WorldGenMap.Generate(Settings(seed), 64);
                foreach (var site in WorldTrees.Plan(map.Climate, map.RiverTiles, map.Towns, map.RoadTiles))
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            Assert.IsFalse(map.RoadTiles.Contains(site.Tile + new Vector2Int(dx, dy)),
                                $"seed {seed}: a trunk at {site.Tile} blocks a road");
            }
        }

        [Test]
        public void TurningRoadsOff_PlansNone()
        {
            var map = WorldGenMap.Generate(Settings(1337, roads: false), 64);
            Assert.AreEqual(0, map.Roads.Count);
            Assert.AreEqual(0, map.RoadTiles.Count);
        }

        [Test]
        public void PlanningRoads_StaysCheapEnoughForThePreview()
        {
            var settings = Settings(1337);
            settings.widthTiles = 400;
            settings.heightTiles = 400;
            WorldGenMap.Generate(settings, 64); // warm the noise and the JIT
            var withRoads = Stopwatch.StartNew();
            WorldGenMap.Generate(settings, 64);
            withRoads.Stop();
            settings.roadsBetweenTowns = false;
            var without = Stopwatch.StartNew();
            WorldGenMap.Generate(settings, 64);
            without.Stop();

            Assert.Less(withRoads.ElapsedMilliseconds - without.ElapsedMilliseconds, 250,
                $"roads cost {withRoads.ElapsedMilliseconds - without.ElapsedMilliseconds} ms on a 400x400 world; the preview replans on every setting change");
        }

        /// <summary>
        /// A live world regenerates its ground from its settings every time a zone is painted. One
        /// written before roads existed placed its houses and trees on a plan without them, so it must
        /// keep planning without them — or a road would appear under its buildings.
        /// </summary>
        [Test]
        public void ALiveWorldWrittenBeforeRoads_KeepsPlanningWithoutThem()
        {
            int seed = -1;
            foreach (int s in Seeds)
                if (WorldGenMap.Generate(Settings(s), 64).Roads.Count > 0) { seed = s; break; }
            Assume.That(seed, Is.GreaterThanOrEqualTo(0), "no seed of the four plans a road");

            string root = Path.Combine(Path.GetTempPath(), "valkur_roads_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
                var request = SeedWorldBakeRequest.ForTest("seed_roads", root);
                Assert.IsTrue(SeedWorldBaker.BakeLive(Settings(seed), request, palette, null, null).Succeeded);

                var current = SeedWorldLiveWorld.TryOpen(request, palette, out string error);
                Assert.IsNotNull(current, error);
                int roadsNow = current.Plan.Map.Roads.Count;

                var marker = JsonUtility.FromJson<SeedWorldMarker>(File.ReadAllText(request.MarkerPath));
                marker.format = 1;
                File.WriteAllText(request.MarkerPath, JsonUtility.ToJson(marker));

                var old = SeedWorldLiveWorld.TryOpen(request, palette, out error);
                Assert.IsNotNull(old, error);
                Assert.AreEqual(0, old.Plan.Map.Roads.Count, "A format-1 world must plan exactly as it was planned when written.");
                Assert.Greater(roadsNow, 0, "Sanity: the same settings written today do get roads.");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static bool OnArm(WorldTown town, Vector2Int t)
        {
            var d = t - town.Center;
            int mw = WorldTowns.MainStreetWidth / 2;
            return (Mathf.Abs(d.y) <= mw && Mathf.Abs(d.x) <= town.Radius + 1)
                || (Mathf.Abs(d.x) <= mw && Mathf.Abs(d.y) <= town.Radius + 1);
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
}
