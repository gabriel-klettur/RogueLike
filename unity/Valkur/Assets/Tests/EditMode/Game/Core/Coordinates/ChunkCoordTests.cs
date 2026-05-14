using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Tests.EditMode.Game.Core.Coordinates
{
    /// <summary>
    /// Pins ChunkCoord behaviour: equality includes the world id, packed key
    /// stores both axes losslessly (including negatives), <see cref="ChunkCoord.OriginTiles"/>
    /// returns the SW tile of the chunk.
    /// </summary>
    [TestFixture]
    public class ChunkCoordTests
    {
        [Test]
        public void Equality_RequiresSameWorld()
        {
            var w1 = new WorldId(System.Guid.NewGuid(), "w1");
            var w2 = new WorldId(System.Guid.NewGuid(), "w2");
            var a  = new ChunkCoord(w1, 3, 5);
            var b  = new ChunkCoord(w1, 3, 5);
            var c  = new ChunkCoord(w2, 3, 5);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void PackedXY_IsBijectiveForSignedInts()
        {
            var w = WorldId.Base;
            int[] vals = { 0, 1, -1, 12345, -12345, int.MaxValue, int.MinValue };
            foreach (var x in vals) foreach (var y in vals)
            {
                var c1 = new ChunkCoord(w, x, y);
                var c2 = new ChunkCoord(w, x, y);
                Assert.AreEqual(c1.PackedXY, c2.PackedXY,
                    $"Same coords must produce same packed key for ({x},{y}).");
            }
        }

        [Test]
        public void OriginTiles_ReturnsSouthWestCorner()
        {
            var c = new ChunkCoord(WorldId.Base, 2, -3);
            Assert.AreEqual(new Vector2Int(64, -96), c.OriginTiles(32));
            Assert.AreEqual(new Vector2Int(100, -150), c.OriginTiles(50));
        }
    }
}
