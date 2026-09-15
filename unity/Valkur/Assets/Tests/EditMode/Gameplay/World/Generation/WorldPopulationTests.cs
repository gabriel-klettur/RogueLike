using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Gameplay.World.Generation
{
    /// <summary>
    /// Phase 4 of <c>.github/SEED_WORLD_ROADMAP.md</c>: encounters, trees, the starting town's
    /// vendors, and how they reach disk.
    /// </summary>
    public class WorldPopulationTests
    {
        private string _root;

        [SetUp]
        public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "valkur_seedworld_pop_" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static WorldGenSettings Settings(int seed = 1337)
            => new WorldGenSettings { seed = seed, widthTiles = 300, heightTiles = 300, townCount = 3, encounterCount = 15 };

        private static SpawnerTemplateCatalog SpawnerCatalog()
            => UnityEditor.AssetDatabase.LoadAssetAtPath<SpawnerTemplateCatalog>(
                "Assets/_Project/Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset");

        private static BuildingCatalog BuildingCatalog()
            => UnityEditor.AssetDatabase.LoadAssetAtPath<BuildingCatalog>(
                "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset");

        // ── Encounters ─────────────────────────────────────────────────────────

        [Test]
        public void Encounters_StayOutOfTowns_AndAwayFromEachOther()
        {
            foreach (int seed in new[] { 1, 42, 1337 })
            {
                var map = WorldGenMap.Generate(Settings(seed), 64);
                foreach (var site in map.Encounters)
                {
                    foreach (var town in map.Towns)
                        Assert.IsFalse(town.Contains(site.Tile, WorldEncounters.TownClearance - 1), $"seed {seed}: camp {site.Tile} inside a town's clearance");
                    foreach (var other in map.Encounters)
                        if (other.Tile != site.Tile)
                            Assert.GreaterOrEqual((other.Tile - site.Tile).magnitude, WorldEncounters.Spacing - 0.01f);
                }
            }
        }

        [Test]
        public void Difficulty_GrowsWithDistanceFromTheSpawn()
        {
            var map = WorldGenMap.Generate(Settings(42), 64);
            Assume.That(map.Encounters.Count, Is.GreaterThan(3));
            var spawn = map.SpawnTile;
            var ordered = map.Encounters.OrderBy(e => (e.Tile - spawn).sqrMagnitude).ToList();
            for (int i = 1; i < ordered.Count; i++)
                Assert.GreaterOrEqual(ordered[i].Difficulty, ordered[i - 1].Difficulty - 1e-4f);
        }

        [Test]
        public void TheDragon_OnlyAppearsAtTheFarEdge()
        {
            var rng = new System.Random(1);
            for (int i = 0; i < 500; i++)
            {
                float d = (float)rng.NextDouble() * (SeedWorldPopulation.BossDifficulty - 0.001f);
                Assert.AreNotEqual("red_dragon_lair", SeedWorldPopulation.HostileFor(d, rng), $"difficulty {d}");
            }
        }

        [Test]
        public void EveryPresetThePopulationUses_ExistsAndSpawnsSomething()
        {
            var catalog = SpawnerCatalog();
            foreach (var id in SeedWorldPopulation.VendorPresets.Concat(SeedWorldPopulation.HostilePresets))
            {
                var preset = catalog.GetById(id);
                Assert.IsNotNull(preset, $"preset '{id}' is missing");
                Assert.That(preset.waves, Is.Not.Null.And.Not.Empty, $"preset '{id}' spawns nothing");
            }
        }

        // ── Vendors ────────────────────────────────────────────────────────────

        [Test]
        public void Vendors_StandOnTheStartingTownsStreets_OnDifferentTiles()
        {
            var map = WorldGenMap.Generate(Settings(1337), 64);
            var start = map.Towns.First(t => t.IsStart);
            var tiles = WorldTowns.ResidentTiles(start, SeedWorldPopulation.VendorPresets.Length);
            Assert.AreEqual(SeedWorldPopulation.VendorPresets.Length, tiles.Count);
            Assert.AreEqual(tiles.Count, tiles.Distinct().Count());
            foreach (var t in tiles) Assert.IsTrue(start.StreetTiles.Contains(t));
        }

        // ── Trees ──────────────────────────────────────────────────────────────

        [Test]
        public void Trees_NeverGrowInTownsOrRivers_AndFollowTheirBiome()
        {
            var map = WorldGenMap.Generate(Settings(7), 64);
            var trees = WorldTrees.Plan(map.Climate, map.RiverTiles, map.Towns);
            Assert.Greater(trees.Count, 50);
            foreach (var tree in trees)
            {
                Assert.IsFalse(map.RiverTiles.Contains(tree.Tile));
                foreach (var town in map.Towns) Assert.IsFalse(town.Contains(tree.Tile, WorldTrees.TownClearance - 1));
                var biome = map.Climate.BiomeAt(tree.Tile.x + 0.5f, tree.Tile.y + 0.5f);
                Assert.AreEqual(WorldTrees.FamilyFor(biome), tree.Family);
            }
        }

        [Test]
        public void ZeroDensity_GrowsNoTrees()
        {
            var s = Settings();
            s.treeDensity = 0f;
            var map = WorldGenMap.Generate(s, 64);
            Assert.AreEqual(0, WorldTrees.Plan(map.Climate, map.RiverTiles, map.Towns).Count);
        }

        [Test]
        public void EveryFamilyAGeneratedWorldGrows_HasTreeArt()
        {
            var byFamily = SeedWorldPopulation.TreeTemplates(BuildingCatalog());
            foreach (var family in new[] { TreeFamily.Common, TreeFamily.Autumn, TreeFamily.Winter, TreeFamily.Tropical,
                                           TreeFamily.Swamp, TreeFamily.Volcanic, TreeFamily.Enchanted, TreeFamily.Corrupted })
                Assert.IsTrue(byFamily.ContainsKey(family) && byFamily[family].Count > 0, $"no tree art for {family}");
        }

        // ── On disk ────────────────────────────────────────────────────────────

        [Test]
        public void ABake_WritesSpawnersTheLoaderCanRead_InsideTheirZones()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_pop", _root);
            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var result = SeedWorldBaker.Bake(Settings(1337), request, palette, BuildingCatalog(), SpawnerCatalog());

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.Greater(result.Spawners, SeedWorldPopulation.VendorPresets.Length);
            Assert.Greater(result.Trees, 0);

            var records = SpawnerInstanceSerializer.ParseAll(File.ReadAllText(request.SpawnersFilePath));
            Assert.AreEqual(result.Spawners, records.Count);
            foreach (var r in records)
            {
                Assert.IsNotNull(r.Config, $"{r.InstanceId} has no config: it would be frozen against its preset and lose its level bonus");
                Assert.That(r.Tile.x, Is.InRange(0, SeedWorldBakeRequest.DefaultZoneSize - 1), r.InstanceId);
                Assert.That(r.Tile.y, Is.InRange(0, SeedWorldBakeRequest.DefaultZoneSize - 1), r.InstanceId);
            }
            Assert.IsTrue(records.Any(r => r.TemplateId == "vendor_gatita_respawn_5m"));
        }

        /// <summary>
        /// Zone-relative tile (row 0 at the top) back to world, and it must land on the site the plan
        /// chose. The spawner coordinate-drift incident was a round trip where each half was right alone.
        /// </summary>
        [Test]
        public void SpawnerTiles_RoundTripToTheirPlannedSite()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_pop_rt", _root);
            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var settings = Settings(42);
            var result = SeedWorldBaker.Bake(settings, request, palette, null, SpawnerCatalog());
            Assert.IsTrue(result.Succeeded, result.Error);

            var map = WorldGenMap.Generate(settings, 64);
            var slot = MiniJsonRuntimeAccess.Zones(File.ReadAllText(request.SlotFilePath));
            var records = SpawnerInstanceSerializer.ParseAll(File.ReadAllText(request.SpawnersFilePath))
                .Where(r => r.InstanceId.Contains("vendor") == false).ToList();

            Assert.AreEqual(map.Encounters.Count, records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                var offset = slot[r.Zone];
                Vector2 world = SpawnerTileMapping.TileToWorld(r.Tile.x, r.Tile.y, offset, SeedWorldBakeRequest.DefaultZoneSize);
                var expected = (Vector2)(map.Encounters[i].Tile + result.Origin);
                Assert.AreEqual(expected, world, r.InstanceId);
            }
        }
    }

    internal static class MiniJsonRuntimeAccess
    {
        public static Dictionary<string, Vector2> Zones(string slotJson)
        {
            var root = (Dictionary<string, object>)Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(slotJson);
            var map = new Dictionary<string, Vector2>();
            foreach (var z in (List<object>)root["zones"])
            {
                var d = (Dictionary<string, object>)z;
                map[(string)d["zoneName"]] = new Vector2(System.Convert.ToInt32(d["gridOffsetX"]), System.Convert.ToInt32(d["gridOffsetY"]));
            }
            return map;
        }
    }
}
