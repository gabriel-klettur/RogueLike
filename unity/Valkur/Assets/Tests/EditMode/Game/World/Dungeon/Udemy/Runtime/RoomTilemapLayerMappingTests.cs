using NUnit.Framework;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Dungeon.Udemy.Runtime;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Runtime
{
    public class RoomTilemapLayerMappingTests
    {
        [TestCase("Ground", TilemapLayerSetup.TilemapLayer.Ground)]
        [TestCase("groundTilemap", TilemapLayerSetup.TilemapLayer.Ground)]
        [TestCase("Tilemap1_Ground", TilemapLayerSetup.TilemapLayer.Ground)]
        [TestCase("FloorDecals", TilemapLayerSetup.TilemapLayer.FloorDecals)]
        [TestCase("Decoration1", TilemapLayerSetup.TilemapLayer.FloorDecals)]
        [TestCase("decoration1Tilemap", TilemapLayerSetup.TilemapLayer.FloorDecals)]
        [TestCase("Tilemap2_Decoration1", TilemapLayerSetup.TilemapLayer.FloorDecals)]
        [TestCase("Decorations", TilemapLayerSetup.TilemapLayer.Decorations)]
        [TestCase("Decoration2", TilemapLayerSetup.TilemapLayer.Decorations)]
        [TestCase("decoration2Tilemap", TilemapLayerSetup.TilemapLayer.Decorations)]
        [TestCase("Tilemap3_Decoration2", TilemapLayerSetup.TilemapLayer.Decorations)]
        [TestCase("WallsTop", TilemapLayerSetup.TilemapLayer.WallsTop)]
        [TestCase("Front", TilemapLayerSetup.TilemapLayer.WallsTop)]
        [TestCase("frontTilemap", TilemapLayerSetup.TilemapLayer.WallsTop)]
        [TestCase("Tilemap4_Front", TilemapLayerSetup.TilemapLayer.WallsTop)]
        [TestCase("Collision", TilemapLayerSetup.TilemapLayer.Collision)]
        [TestCase("collisionTilemap", TilemapLayerSetup.TilemapLayer.Collision)]
        [TestCase("Tilemap5_Collision", TilemapLayerSetup.TilemapLayer.Collision)]
        public void TryResolve_KnownNamesMapToExpectedLayer(string name, TilemapLayerSetup.TilemapLayer expected)
        {
            Assert.IsTrue(RoomTilemapLayerMapping.TryResolve(name, out var layer));
            Assert.AreEqual(expected, layer);
        }

        [Test]
        public void TryResolve_IsCaseInsensitive()
        {
            Assert.IsTrue(RoomTilemapLayerMapping.TryResolve("GROUND", out var layer));
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, layer);
        }

        [Test]
        public void TryResolve_UnknownName_ReturnsFalse()
        {
            Assert.IsFalse(RoomTilemapLayerMapping.TryResolve("UnknownLayer", out _));
        }
    }
}
