using System.Collections.Generic;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Gameplay.World.Chunks;

namespace Valkur.Tests.EditMode.Game.World.Chunks
{
    /// <summary>
    /// Pins the streaming policy: every SyncTo call updates the active
    /// set to exactly the chunks within the radius, calling Show on
    /// new entrants and Hide on departees, idempotent across repeats.
    /// </summary>
    [TestFixture]
    public class ChunkStreamerTests
    {
        // Recorder painter: just records the order of Show/Hide calls so
        // tests can assert on which chunks the streamer brought into /
        // out of the active set this tick.
        private sealed class RecorderPainter : IChunkPainter
        {
            public List<ChunkCoord> Shown  = new List<ChunkCoord>();
            public List<ChunkCoord> Hidden = new List<ChunkCoord>();

            public void Show(ChunkData chunk) { Shown.Add(chunk.Coord); }
            public void Hide(ChunkCoord coord) { Hidden.Add(coord); }
        }

        private static InMemoryChunkProvider BuildProvider(int radiusChunks)
        {
            var p = new InMemoryChunkProvider();
            // Pre-seed the provider with every chunk inside a generous radius
            // around (0,0) so the streamer always finds something to Show.
            for (int dy = -radiusChunks; dy <= radiusChunks; dy++)
                for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
                {
                    var coord = new ChunkCoord(WorldId.Base, dx, dy);
                    p.Set(new ChunkData(coord, 4, 1));
                }
            return p;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void SyncTo_FirstCall_ShowsRadiusSquaredChunks()
        {
            var provider = BuildProvider(5);
            var painter  = new RecorderPainter();
            var streamer = new ChunkStreamer(provider, painter, activeRadius: 1);

            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));

            // Radius 1 -> 3x3 = 9 chunks visible.
            Assert.AreEqual(9, painter.Shown.Count,
                "First sync at radius 1 must show the 3x3 block centred on focus.");
            Assert.AreEqual(0, painter.Hidden.Count,
                "Nothing was active before — nothing to hide.");
            Assert.AreEqual(9, streamer.ActiveChunks.Count);
        }

        [Test]
        public void SyncTo_SameFocusTwice_IsNoOp()
        {
            var provider = BuildProvider(5);
            var painter  = new RecorderPainter();
            var streamer = new ChunkStreamer(provider, painter, activeRadius: 1);

            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));
            int shownAfterFirst  = painter.Shown.Count;
            int hiddenAfterFirst = painter.Hidden.Count;

            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));

            Assert.AreEqual(shownAfterFirst,  painter.Shown.Count,
                "Repeat sync with unchanged focus must NOT re-Show — that " +
                "would re-paint every active chunk every frame.");
            Assert.AreEqual(hiddenAfterFirst, painter.Hidden.Count,
                "Repeat sync must NOT spuriously Hide active chunks.");
        }

        [Test]
        public void SyncTo_FocusMovesByOne_ShowsAndHidesOneStrip()
        {
            var provider = BuildProvider(10);
            var painter  = new RecorderPainter();
            var streamer = new ChunkStreamer(provider, painter, activeRadius: 1);

            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));
            painter.Shown.Clear();
            painter.Hidden.Clear();

            // Move focus one chunk to the right. Active set shifts one
            // column: 3 chunks leave on the left, 3 chunks enter on the right.
            streamer.SyncTo(new ChunkCoord(WorldId.Base, 1, 0));

            Assert.AreEqual(3, painter.Hidden.Count,
                "Focus moved one chunk -> three columns of one-chunk depth " +
                "leave the active set on the trailing side.");
            Assert.AreEqual(3, painter.Shown.Count,
                "Three chunks enter on the leading side.");

            // Active set still 3x3.
            Assert.AreEqual(9, streamer.ActiveChunks.Count);
        }

        [Test]
        public void HideAll_ClearsActiveSetAndHidesEveryChunk()
        {
            var provider = BuildProvider(5);
            var painter  = new RecorderPainter();
            var streamer = new ChunkStreamer(provider, painter, activeRadius: 1);

            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));
            painter.Hidden.Clear();

            streamer.HideAll();

            Assert.AreEqual(0, streamer.ActiveChunks.Count,
                "After HideAll the active set must be empty so a world swap " +
                "does not leak chunks from the previous dimension.");
            Assert.AreEqual(9, painter.Hidden.Count,
                "Every previously-active chunk must be Hide'd exactly once.");
        }

        [Test]
        public void ProviderWithoutChunk_StreamerDoesNotShow_NoCrash()
        {
            var provider = new InMemoryChunkProvider(); // empty
            var painter  = new RecorderPainter();
            var streamer = new ChunkStreamer(provider, painter, activeRadius: 1);

            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));

            Assert.AreEqual(0, painter.Shown.Count,
                "Streamer must not call Show for a chunk the provider does " +
                "not have — silently skipping is the documented quiet " +
                "fallback for sparse worlds.");
            Assert.AreEqual(0, streamer.ActiveChunks.Count);
        }
    }
}
