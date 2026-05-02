using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Tests.EditMode.Core.Coordinates
{
    /// <summary>
    /// Pins WorldPos behaviour. The non-trivial bits are <see cref="WorldPos.ToChunk"/>
    /// (must floor toward minus infinity, not toward zero — a tile at x=-1 lives
    /// in chunk -1, not chunk 0) and <see cref="WorldPos.LocalInChunk"/> (always
    /// returns 0..size-1 even for negative tile coords).
    /// </summary>
    [TestFixture]
    public class WorldPosTests
    {
        private const int CHUNK = 32;

        [Test]
        public void ToChunk_PositiveCoords_FloorsCorrectly()
        {
            Assert.AreEqual(new ChunkCoord(WorldId.Base, 0, 0),
                            new WorldPos(WorldId.Base, 0, 0).ToChunk(CHUNK));
            Assert.AreEqual(new ChunkCoord(WorldId.Base, 0, 0),
                            new WorldPos(WorldId.Base, 31, 31).ToChunk(CHUNK));
            Assert.AreEqual(new ChunkCoord(WorldId.Base, 1, 1),
                            new WorldPos(WorldId.Base, 32, 32).ToChunk(CHUNK));
            Assert.AreEqual(new ChunkCoord(WorldId.Base, 5, 3),
                            new WorldPos(WorldId.Base, 5 * CHUNK + 7, 3 * CHUNK + 11).ToChunk(CHUNK));
        }

        [Test]
        public void ToChunk_NegativeCoords_FloorsTowardMinusInfinity()
        {
            // A tile at -1,-1 lives in chunk -1,-1 (NOT chunk 0,0).
            Assert.AreEqual(new ChunkCoord(WorldId.Base, -1, -1),
                            new WorldPos(WorldId.Base, -1, -1).ToChunk(CHUNK));
            Assert.AreEqual(new ChunkCoord(WorldId.Base, -1, -1),
                            new WorldPos(WorldId.Base, -CHUNK, -CHUNK).ToChunk(CHUNK));
            Assert.AreEqual(new ChunkCoord(WorldId.Base, -2, -2),
                            new WorldPos(WorldId.Base, -CHUNK - 1, -CHUNK - 1).ToChunk(CHUNK));
        }

        [Test]
        public void LocalInChunk_AlwaysWithinRange()
        {
            for (long t = -100; t <= 100; t++)
            {
                var local = new WorldPos(WorldId.Base, t, 0).LocalInChunk(CHUNK);
                Assert.GreaterOrEqual(local.x, 0);
                Assert.Less(local.x, CHUNK,
                    $"local.x out of range for tile {t}: got {local.x}");
            }
        }

        [Test]
        public void ToUnity_RebasesByActiveOrigin()
        {
            var p = new WorldPos(WorldId.Base, 1000, 500);
            var v = p.ToUnity(new Vector2Int(900, 400), tileSize: 1f);
            Assert.AreEqual(new Vector3(100, 100, 0), v);
        }

        [Test]
        public void ToUnity_ScalesByTileSize()
        {
            var p = new WorldPos(WorldId.Base, 10, 5);
            var v = p.ToUnity(Vector2Int.zero, tileSize: 2.5f);
            Assert.AreEqual(new Vector3(25f, 12.5f, 0), v);
        }

        [Test]
        public void Equality_RequiresAllFields()
        {
            var p1 = new WorldPos(WorldId.Base, 10, 20);
            var p2 = new WorldPos(WorldId.Base, 10, 20);
            var p3 = new WorldPos(WorldId.Base, 10, 21);
            Assert.AreEqual(p1, p2);
            Assert.AreNotEqual(p1, p3);
        }
    }
}
