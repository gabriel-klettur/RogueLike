using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Game.WorldGen
{
    /// <summary>
    /// The bake: a generated world written as a map slot. Every test writes under a temporary
    /// folder through <see cref="SeedWorldBakeRequest.ForTest"/>, never under persistentDataPath.
    /// </summary>
    public class SeedWorldBakerTests
    {
        private string _root;
        private SeedWorldTilePalette _palette;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "valkur_seedworld_" + Guid.NewGuid().ToString("N"));
            _palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static WorldGenSettings Small(int seed = 1337)
            => new WorldGenSettings { seed = seed, widthTiles = 100, heightTiles = 100, riverCount = 3 };

        private static Dictionary<string, object> Parse(string path)
            => MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;

        [Test]
        public void TheShippedCatalog_CanDrawEveryGroundTerrain()
        {
            Assert.IsTrue(_palette.HasCatalog);
            for (int i = 0; i < WorldBiomeTable.Count; i++)
            {
                string terrain = WorldBiomeTable.GetAt(i).GroundTerrain;
                Assert.IsNotNull(_palette.Solid(terrain, 0), $"no solid tile for '{terrain}'");
            }
            Assert.IsNotNull(_palette.Solid(WorldTerrainGrid.Lava, 0));
        }

        [Test]
        public void ABake_WritesOneOverlayPerZone_AndTheSlotFile()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_test", _root);
            var result = SeedWorldBaker.Bake(Small(), request, _palette);

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.AreEqual(2, result.ZonesX);
            Assert.AreEqual(2, result.ZonesY);
            Assert.AreEqual(0, result.MissingTiles, "every cell must resolve to a tile");
            Assert.AreEqual(4, Directory.GetFiles(request.OverridesDirectory, "*.overlay.json").Length);
            Assert.IsTrue(File.Exists(request.SlotFilePath));
            Assert.IsTrue(File.Exists(request.MarkerPath));

            var slot = Parse(request.SlotFilePath);
            var zones = slot["zones"] as List<object>;
            Assert.AreEqual(4, zones.Count);
        }

        /// <summary>The Y-sort budget is symmetric around zero, so the world must be too.</summary>
        [Test]
        public void TheWorld_IsCentredOnTheOrigin()
        {
            var s = Small();
            s.widthTiles = 400;
            s.heightTiles = WorldGenSettings.MaxHeightTiles;
            var result = SeedWorldBaker.Bake(s, SeedWorldBakeRequest.ForTest("seed_tall", _root), _palette);

            Assert.IsTrue(result.Succeeded, result.Error);
            int top = result.Origin.y + result.ZonesY * SeedWorldBakeRequest.DefaultZoneSize;
            Assert.GreaterOrEqual(result.Origin.y, -Valkur.Core.SortingConfig.MAX_SAFE_WORLD_Y);
            Assert.LessOrEqual(top, Valkur.Core.SortingConfig.MAX_SAFE_WORLD_Y);
        }

        [Test]
        public void EveryZoneFile_Has50Rows_Of50Tiles_AndA51x51TerrainMatrix()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_shape", _root);
            Assert.IsTrue(SeedWorldBaker.Bake(Small(), request, _palette).Succeeded);

            foreach (var file in Directory.GetFiles(request.OverridesDirectory, "*.overlay.json"))
            {
                var root = Parse(file);
                Assert.IsNotNull(root, file);
                var layers = root["layers"] as Dictionary<string, object>;
                var ground = layers["Ground"] as List<object>;
                Assert.AreEqual(50, ground.Count);
                foreach (var row in ground)
                {
                    var cells = row as List<object>;
                    Assert.AreEqual(50, cells.Count);
                    foreach (var c in cells) Assert.IsFalse(string.IsNullOrEmpty(c as string), file);
                }

                var terrains = root["terrains"] as List<object>;
                Assert.AreEqual(51, terrains.Count);
                Assert.AreEqual(51, (terrains[0] as List<object>).Count);
            }
        }

        /// <summary>
        /// Every tile name must load straight from Resources — a name the category probe misses
        /// falls through to a synchronous LoadAll of every tile in the project.
        /// </summary>
        [Test]
        public void EveryTileName_LoadsDirectlyFromResources()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_names", _root);
            Assert.IsTrue(SeedWorldBaker.Bake(Small(42), request, _palette).Succeeded);

            var names = new HashSet<string>();
            foreach (var file in Directory.GetFiles(request.OverridesDirectory, "*.overlay.json"))
                foreach (var row in (Parse(file)["layers"] as Dictionary<string, object>)["Ground"] as List<object>)
                    foreach (var c in row as List<object>) names.Add(c as string);

            foreach (var name in names)
                Assert.IsNotNull(Resources.Load<Sprite>("Tiles/" + name), $"'{name}' does not load from Resources/Tiles");
        }

        [Test]
        public void TheSameSettings_BakeTheSameFiles()
        {
            var a = SeedWorldBakeRequest.ForTest("seed_a", _root);
            var b = SeedWorldBakeRequest.ForTest("seed_b", _root);
            Assert.IsTrue(SeedWorldBaker.Bake(Small(9), a, _palette).Succeeded);
            Assert.IsTrue(SeedWorldBaker.Bake(Small(9), b, _palette).Succeeded);

            foreach (var file in Directory.GetFiles(a.OverridesDirectory, "*.overlay.json"))
                Assert.AreEqual(File.ReadAllText(file),
                    File.ReadAllText(Path.Combine(b.OverridesDirectory, Path.GetFileName(file))), file);
        }

        [Test]
        public void ASlotMadeByHand_IsNeverOverwritten()
        {
            var request = SeedWorldBakeRequest.ForTest("hand_made", _root);
            Directory.CreateDirectory(request.MapsDirectory);
            File.WriteAllText(request.SlotFilePath, "{\"zones\":[]}");

            Assert.AreEqual(SeedWorldBaker.SlotState.Foreign, SeedWorldBaker.Inspect(request));
            var result = SeedWorldBaker.Bake(Small(), request, _palette);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("{\"zones\":[]}", File.ReadAllText(request.SlotFilePath));
        }

        [Test]
        public void ASeedWorldSlot_IsRebuilt_AndLosesZonesItNoLongerHas()
        {
            var request = SeedWorldBakeRequest.ForTest("rebuilt", _root);
            var big = Small();
            big.widthTiles = 150;
            Assert.IsTrue(SeedWorldBaker.Bake(big, request, _palette).Succeeded);
            Assert.AreEqual(6, Directory.GetFiles(request.OverridesDirectory, "*.overlay.json").Length);

            Assert.AreEqual(SeedWorldBaker.SlotState.SeedWorld, SeedWorldBaker.Inspect(request));
            Assert.IsTrue(SeedWorldBaker.Bake(Small(), request, _palette).Succeeded);
            Assert.AreEqual(4, Directory.GetFiles(request.OverridesDirectory, "*.overlay.json").Length,
                "a stale zone overlay would be an orphan in the slot's folder");
        }

        private static Valkur.Data.BuildingCatalog LoadBuildingCatalog()
            => UnityEditor.AssetDatabase.LoadAssetAtPath<Valkur.Data.BuildingCatalog>(
                "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset");

        /// <summary>Every curated town piece names art that exists: a renamed PNG would quietly thin every town.</summary>
        [Test]
        public void EveryTownPaletteEntry_ResolvesInTheShippedCatalog()
        {
            var missing = new List<string>();
            var options = SeedWorldTownPalette.Options(LoadBuildingCatalog(), missing);
            Assert.IsEmpty(missing, "unresolved town pieces: " + string.Join(", ", missing));
            Assert.Greater(options.Count, 40);
        }

        [Test]
        public void ABakeWithTowns_WritesTheSlotsBuildings_WithACollisionGridEach()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_towns", _root);
            var s = Small();
            s.widthTiles = 200;
            s.heightTiles = 200;
            s.townCount = 2;
            var result = SeedWorldBaker.Bake(s, request, _palette, LoadBuildingCatalog());

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.Greater(result.Towns, 0);
            Assert.Greater(result.Buildings, 0);
            Assert.IsTrue(File.Exists(request.BuildingsFilePath));

            var list = MiniJsonRuntime.Deserialize(File.ReadAllText(request.BuildingsFilePath)) as List<object>;
            Assert.AreEqual(result.Buildings + result.Trees, list.Count, "the file holds the towns AND the trees");

            var slot = Parse(request.SlotFilePath);
            var zoneNames = new HashSet<string>();
            foreach (var z in slot["zones"] as List<object>) zoneNames.Add((string)((Dictionary<string, object>)z)["zoneName"]);

            foreach (var item in list)
            {
                var b = (Dictionary<string, object>)item;
                Assert.IsTrue(zoneNames.Contains((string)b["zone"]), $"building in unknown zone '{b["zone"]}'");
                var overrides = (Dictionary<string, object>)b["overrides"];
                var grid = (Dictionary<string, object>)overrides["collision_override"];
                var rows = (List<object>)grid["collision"];
                Assert.AreEqual(System.Convert.ToInt32(grid["height"]), rows.Count);
                Assert.IsTrue(rows.Any(r => ((List<object>)r).Contains("#")), "a generated building that collides with nothing is a picture");
            }
        }

        [Test]
        public void ZoneNames_AreUnique_AndNameTheStartingTown()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_names2", _root);
            var s = Small();
            s.widthTiles = 300;
            s.heightTiles = 300;
            s.townCount = 3;
            Assert.IsTrue(SeedWorldBaker.Bake(s, request, _palette, null).Succeeded);

            var names = ((List<object>)Parse(request.SlotFilePath)["zones"])
                .Select(z => (string)((Dictionary<string, object>)z)["zoneName"]).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count(), "zone names must be unique");
            Assert.Contains("Pueblo inicial", names);
        }

        [Test]
        public void TheDefaultSlot_CannotBeATarget()
        {
            Assert.IsNull(SeedWorldBakeRequest.ForTest("default", _root));
            Assert.IsNull(SeedWorldBakeRequest.ForTest("   ", _root));
        }

        /// <summary>The spawn written into the slot must be on a tile the player can walk off.</summary>
        [Test]
        public void TheSpawn_IsNotInsideTheWater()
        {
            var request = SeedWorldBakeRequest.ForTest("seed_spawn", _root);
            var s = Small(1337);
            var result = SeedWorldBaker.Bake(s, request, _palette);
            Assert.IsTrue(result.Succeeded, result.Error);

            var climate = new WorldClimate(s);
            var local = result.SpawnWorld - (Vector2)result.Origin;
            var biome = climate.BiomeAt(local.x, local.y);
            Assert.That(biome, Is.Not.EqualTo(WorldBiome.Ocean).And.Not.EqualTo(WorldBiome.DeepOcean));
        }
    }
}
