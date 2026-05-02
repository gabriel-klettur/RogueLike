using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Data.Chunks
{
    /// <summary>
    /// Pins the <see cref="ChunkData"/> tile-buffer contract: the storage
    /// is a flat ushort array per layer, indexing is (layer, x, y) with
    /// y-major rows, every fresh chunk is empty, and Get/Set bounds checks
    /// are honoured.
    /// </summary>
    [TestFixture]
    public class ChunkDataTests
    {
        [Test]
        public void Constructor_AllocatesEmptyLayers()
        {
            var data = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), size: 4, layerCount: 2);
            Assert.AreEqual(4, data.Size);
            Assert.AreEqual(2, data.Layers.Length);
            Assert.AreEqual(16, data.Layers[0].Length);
            Assert.IsTrue(data.IsEmpty(), "Fresh chunk must be empty.");
        }

        [Test]
        public void SetThenGet_RoundTrips()
        {
            var data = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), 4, 2);
            data.Set(0, 1, 2, 42);
            Assert.AreEqual(42, data.Get(0, 1, 2));
            Assert.IsFalse(data.IsEmpty());
        }

        [Test]
        public void IndexOrder_IsRowMajorYMajor()
        {
            var data = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), 4, 1);
            data.Set(0, 0, 0, 1);
            data.Set(0, 3, 0, 2);
            data.Set(0, 0, 3, 3);
            Assert.AreEqual(1, data.Layers[0][0],         "(0,0) index 0");
            Assert.AreEqual(2, data.Layers[0][3],         "(3,0) index 3");
            Assert.AreEqual(3, data.Layers[0][12],        "(0,3) index 12 = y*size+x");
        }

        [Test]
        public void ComputeCrc32_Stable_ForSameContent()
        {
            var a = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), 4, 1);
            var b = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), 4, 1);
            for (int i = 0; i < 16; i++)
            {
                a.Layers[0][i] = (ushort)(i * 7);
                b.Layers[0][i] = (ushort)(i * 7);
            }
            Assert.AreEqual(a.ComputeCrc32(), b.ComputeCrc32(),
                "Two chunks with identical buffers must have identical CRCs " +
                "— deterministic-generation regression tests rely on this.");
        }

        [Test]
        public void ComputeCrc32_DiffersForDifferentContent()
        {
            var a = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), 4, 1);
            var b = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), 4, 1);
            a.Layers[0][0] = 1;
            b.Layers[0][0] = 2;
            Assert.AreNotEqual(a.ComputeCrc32(), b.ComputeCrc32());
        }

        [Test]
        public void Set_OutOfBounds_Throws()
        {
            var data = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), 4, 1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => data.Set(0, -1, 0, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => data.Set(0, 4, 0, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => data.Set(2, 0, 0, 1));
        }
    }
}
