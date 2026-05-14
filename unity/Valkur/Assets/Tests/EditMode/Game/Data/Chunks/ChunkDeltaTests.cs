using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Pins the diff-on-procedural contract: a virgin chunk has an empty
    /// delta, an edited chunk produces a diff that re-applies cleanly,
    /// and the (Add) helper collapses repeated edits to the same cell so
    /// the delta size stays bounded by distinct edited cells.
    /// </summary>
    [TestFixture]
    public class ChunkDeltaTests
    {
        private static ChunkData MakeBaseline(int size, ushort fill)
        {
            var d = new ChunkData(new ChunkCoord(WorldId.Base, 0, 0), size, 1);
            for (int i = 0; i < size * size; i++) d.Layers[0][i] = fill;
            return d;
        }

        [Test]
        public void EmptyDelta_IsEmptyAndApplyIsNoOp()
        {
            var baseline = MakeBaseline(4, fill: 5);
            var delta = new ChunkDelta(baseline.Coord, "biome", 1);

            Assert.IsTrue(delta.IsEmpty);

            var copy = MakeBaseline(4, fill: 5);
            delta.ApplyTo(copy);
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(5, copy.Layers[0][i], "Empty delta must not mutate the baseline.");
        }

        [Test]
        public void DiffFrom_DetectsModifiedCells()
        {
            var baseline = MakeBaseline(4, fill: 5);
            var modified = MakeBaseline(4, fill: 5);
            modified.Set(0, 1, 1, 99);
            modified.Set(0, 2, 3, 42);

            var delta = ChunkDelta.DiffFrom(baseline, modified, "biome", 1);

            Assert.AreEqual(2, delta.Tiles.Count, "One entry per changed cell.");
            // Re-apply onto a fresh copy of the baseline and verify it
            // reproduces the modified content.
            var rebuilt = MakeBaseline(4, fill: 5);
            delta.ApplyTo(rebuilt);
            Assert.AreEqual(99, rebuilt.Get(0, 1, 1));
            Assert.AreEqual(42, rebuilt.Get(0, 2, 3));
        }

        [Test]
        public void Add_SameCellTwice_CollapsesToLatestEdit()
        {
            var delta = new ChunkDelta(new ChunkCoord(WorldId.Base, 0, 0), "b", 1);
            delta.Add(new TileEdit(0, 1, 1, 100));
            delta.Add(new TileEdit(0, 1, 1, 200));
            Assert.AreEqual(1, delta.Tiles.Count,
                "Repeated edits to the same (layer,x,y) must overwrite — " +
                "without this the delta grows linearly with edit ops.");
            Assert.AreEqual(200, delta.Tiles[0].NewTileId);
        }

        [Test]
        public void ApplyTo_OutOfBoundsEdits_AreSkippedNotThrown()
        {
            var baseline = MakeBaseline(4, fill: 5);
            var delta = new ChunkDelta(baseline.Coord, "b", 1);
            delta.Add(new TileEdit(0, 99, 99, 7)); // out of range
            delta.Add(new TileEdit(0, 0, 0, 8));   // valid

            int warns = 0;
            delta.ApplyTo(baseline, _ => warns++);

            Assert.AreEqual(8, baseline.Get(0, 0, 0), "Valid edit must apply.");
            Assert.AreEqual(5, baseline.Get(0, 3, 3), "Untouched cell must not change.");
            Assert.AreEqual(1, warns,
                "Out-of-range edits warn instead of throw so a corrupted save " +
                "file does not kill the game; the rest of the delta still applies.");
        }

        [Test]
        public void DiffThenApply_RoundTripsEdits()
        {
            var baseline = MakeBaseline(4, fill: 1);
            var modified = MakeBaseline(4, fill: 1);
            modified.Set(0, 0, 0, 9);
            modified.Set(0, 3, 3, 9);
            modified.Set(0, 2, 1, 9);

            var delta = ChunkDelta.DiffFrom(baseline, modified, "biome", 1);
            var rebuilt = MakeBaseline(4, fill: 1);
            delta.ApplyTo(rebuilt);

            Assert.AreEqual(modified.ComputeCrc32(), rebuilt.ComputeCrc32(),
                "Round-trip baseline -> diff -> apply must reproduce the " +
                "modified chunk byte-for-byte. This is the persistence story: " +
                "save the diff, regenerate the baseline, replay -> identical world.");
        }
    }
}
