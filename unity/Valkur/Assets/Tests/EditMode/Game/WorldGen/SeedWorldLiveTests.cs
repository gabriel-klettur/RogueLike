using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Game.WorldGen
{
    /// <summary>
    /// Phase 5 of <c>.github/SEED_WORLD_ROADMAP.md</c>: a live world generates each zone on demand,
    /// and a zone generated on demand must be the SAME zone a bake writes — tile for tile, seam
    /// for seam — or walking across a live world shows cuts the baked one does not have.
    /// </summary>
    public class SeedWorldLiveTests
    {
        private static readonly HashSet<string> ShippedPairs = new HashSet<string>
        {
            Key("grass", "dirt"), Key("grass", "rock"), Key("rock", "water"), Key("sand", "grass"),
            Key("sand", "rock"), Key("stone", "lava"), Key("water", "water_deep"),
        };

        private static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        private static bool ShippedCompatible(string a, string b) => ShippedPairs.Contains(Key(a, b));

        private string _root;

        [SetUp]
        public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "valkur_seedworld_live_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static WorldGenSettings Settings(int seed) => new WorldGenSettings
        {
            seed = seed, widthTiles = 150, heightTiles = 100, riverCount = 6, townCount = 2, encounterCount = 4,
        };

        /// <summary>
        /// The repair is the one stage that is not local per vertex, so it is the one that could make
        /// a region disagree with the world. Every vertex of every zone, across seeds with rivers,
        /// coasts and towns.
        /// </summary>
        [Test]
        public void ARegion_AgreesWithTheWholeWorld_OnEveryVertexItWasAskedFor()
        {
            foreach (int seed in new[] { 1, 42, 1337, 9001 })
            {
                var plan = SeedWorldPlan.Create(Settings(seed), 50, ShippedCompatible);
                var full = plan.BuildAll(ShippedCompatible);
                int compared = 0;
                for (int zy = 0; zy < plan.ZonesY; zy++)
                    for (int zx = 0; zx < plan.ZonesX; zx++)
                    {
                        var region = plan.BuildRegion(zx * 50, zy * 50, 50, 50, ShippedCompatible);
                        for (int vy = zy * 50; vy <= zy * 50 + 50; vy++)
                            for (int vx = zx * 50; vx <= zx * 50 + 50; vx++)
                            {
                                Assert.AreEqual(full.TerrainAt(vx, vy), region.TerrainAt(vx, vy),
                                    $"seed {seed}, zone ({zx},{zy}), vertex ({vx},{vy})");
                                compared++;
                            }
                    }
                Assert.Greater(compared, 6 * 51 * 51 - 1);
                Assert.Greater(full.RepairedVertices, 0, "a world with no repair proves nothing about the repair");
            }
        }

        [Test]
        public void TheRepair_StillLeavesNoDrawablePairUnresolvedBesideTheVolcanicEdge()
        {
            var plan = SeedWorldPlan.Create(Settings(1337), 50, ShippedCompatible);
            var grid = plan.BuildAll(ShippedCompatible);
            for (int vy = 0; vy < grid.Height - 1; vy++)
                for (int vx = 0; vx < grid.Width - 1; vx++)
                {
                    string a = grid.TerrainAt(vx, vy), b = grid.TerrainAt(vx + 1, vy);
                    if (grid.Compatible(a, b)) continue;
                    bool volcanic = a == WorldTerrainGrid.Stone || b == WorldTerrainGrid.Stone
                                    || a == WorldTerrainGrid.Lava || b == WorldTerrainGrid.Lava;
                    Assert.IsTrue(volcanic, $"({vx},{vy}) {a}|{b} has no pack and no bridge");
                }
        }

        [Test]
        public void ALiveBake_WritesNoGround_AndAMarkerThatOpens()
        {
            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var request = SeedWorldBakeRequest.ForTest("seed_live", _root);
            var result = SeedWorldBaker.BakeLive(Settings(7), request, palette, null, null);

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.IsTrue(result.Live);
            Assert.IsTrue(File.Exists(request.SlotFilePath));
            Assert.IsTrue(File.Exists(request.MarkerPath));
            Assert.AreEqual(0, Directory.GetFiles(request.OverridesDirectory, "*.overlay.json").Length,
                "a live world keeps its ground in the seed, not on disk");
            Assert.AreEqual(SeedWorldBaker.SlotState.SeedWorld, SeedWorldBaker.Inspect(request),
                "a live slot must still be recognised as Seed World's, or it could never be rebuilt");

            var world = SeedWorldLiveWorld.TryOpen(request, palette, out string error);
            Assert.IsNotNull(world, error);
            Assert.AreEqual(result.ZonesX, world.Plan.ZonesX);
            Assert.AreEqual(result.SpawnWorld, world.Plan.SpawnWorld);
        }

        [Test]
        public void ABakedWorld_DoesNotOpenAsLive()
        {
            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var request = SeedWorldBakeRequest.ForTest("seed_baked", _root);
            Assert.IsTrue(SeedWorldBaker.Bake(Settings(7), request, palette).Succeeded);
            Assert.IsNull(SeedWorldLiveWorld.TryOpen(request, palette, out _));
        }

        /// <summary>
        /// The composition: a zone streamed in carries the same tiles, the same collision and the same
        /// vertex terrain as the file a bake writes for it. Each half alone could be consistent and the
        /// pair still disagree — the spawner-drift shape.
        /// </summary>
        [Test]
        public void EveryLiveZone_IsTheBakedZone()
        {
            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var settings = Settings(42);
            var baked = SeedWorldBakeRequest.ForTest("seed_cmp_baked", _root);
            var live = SeedWorldBakeRequest.ForTest("seed_cmp_live", _root);
            Assert.IsTrue(SeedWorldBaker.Bake(settings, baked, palette).Succeeded);
            Assert.IsTrue(SeedWorldBaker.BakeLive(settings, live, palette, null, null).Succeeded);

            var world = SeedWorldLiveWorld.TryOpen(live, palette, out string error);
            Assert.IsNotNull(world, error);

            for (int zy = 0; zy < world.Plan.ZonesY; zy++)
                for (int zx = 0; zx < world.Plan.ZonesX; zx++)
                {
                    string name = world.Plan.ZoneNames[zx, zy];
                    var fromFile = OverlayLoader.ParseOverlay(Path.Combine(baked.OverridesDirectory, name + ".overlay.json"));
                    var generated = world.ZoneOverlay(zx, zy, out bool fromDisk);
                    Assert.IsFalse(fromDisk);
                    Assert.AreEqual(Flatten(fromFile), Flatten(generated), $"zone {name}");
                }
        }

        [Test]
        public void AnEditedZone_IsReadFromItsFile_NotGenerated()
        {
            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var request = SeedWorldBakeRequest.ForTest("seed_edit", _root);
            Assert.IsTrue(SeedWorldBaker.BakeLive(Settings(3), request, palette, null, null).Succeeded);
            var world = SeedWorldLiveWorld.TryOpen(request, palette, out _);

            string path = world.OverlayPathFor(0, 0);
            File.WriteAllText(path, "{\"layers\":{\"Ground\":[[\"edited_tile\"]]}}");

            var root = world.ZoneOverlay(0, 0, out bool fromDisk);
            Assert.IsTrue(fromDisk);
            Assert.AreEqual("edited_tile", Flatten(root).First(v => v.StartsWith("Ground:")).Substring("Ground:".Length));
        }

        [Test]
        public void RebuildingALiveSlot_DropsTheOldWorldsEditedZones()
        {
            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var request = SeedWorldBakeRequest.ForTest("seed_rebuild", _root);
            Assert.IsTrue(SeedWorldBaker.BakeLive(Settings(3), request, palette, null, null).Succeeded);
            var world = SeedWorldLiveWorld.TryOpen(request, palette, out _);
            File.WriteAllText(world.OverlayPathFor(0, 0), "{\"layers\":{}}");

            Assert.IsTrue(SeedWorldBaker.BakeLive(Settings(4), request, palette, null, null).Succeeded);
            Assert.AreEqual(0, Directory.GetFiles(request.OverridesDirectory, "*.overlay.json").Length,
                "an edit of the previous world would override the new world's ground");
        }

        [Test]
        public void TheUnloadSignal_NestsAndCloses()
        {
            Assert.IsFalse(WorldStreamingSignals.IsUnloadingTiles);
            using (WorldStreamingSignals.UnloadingTiles())
            {
                using (WorldStreamingSignals.UnloadingTiles()) Assert.IsTrue(WorldStreamingSignals.IsUnloadingTiles);
                Assert.IsTrue(WorldStreamingSignals.IsUnloadingTiles);
            }
            Assert.IsFalse(WorldStreamingSignals.IsUnloadingTiles);
        }

        /// <summary>Every string leaf of an overlay tree with its path, so two trees compare as lists.</summary>
        private static List<string> Flatten(Dictionary<string, object> root)
        {
            var output = new List<string>();
            Walk(root, string.Empty, output);
            return output;
        }

        private static void Walk(object node, string path, List<string> output)
        {
            switch (node)
            {
                case Dictionary<string, object> d:
                    foreach (var key in d.Keys.OrderBy(k => k, StringComparer.Ordinal))
                        Walk(d[key], path.Length == 0 ? key : path + "/" + key, output);
                    break;
                case List<object> list:
                    for (int i = 0; i < list.Count; i++) Walk(list[i], path, output);
                    break;
                default:
                    string leafPath = path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
                    output.Add(leafPath + ":" + (node as string ?? string.Empty));
                    break;
            }
        }
    }
}
