using System;
using System.Collections.Generic;
using System.IO;
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
