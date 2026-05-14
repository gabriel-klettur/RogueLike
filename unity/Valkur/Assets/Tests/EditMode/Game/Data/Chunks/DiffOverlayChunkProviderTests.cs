using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Phase 2 acceptance: the full diff-on-procedural cycle works.
    ///
    ///   1. With no delta, the provider returns a pure procedural baseline
    ///      bit-for-bit identical to the biome's direct output (no
    ///      overhead from going through the overlay).
    ///   2. With a delta, every TileEdit applies on top of the baseline
    ///      and only those cells differ (the rest stay procedural).
    ///   3. A stale-version delta still applies but emits a warning so the
    ///      developer notices and a migration tool can rebake offline.
    /// </summary>
    [TestFixture]
    public class DiffOverlayChunkProviderTests
    {
        private const int Size = 8;
        private const int LayerCount = 1;

        private DictionaryTileIdTable _tiles;
        private NoiseSplitBiome _biome;
        private SingleBiomeRouter _router;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("a"); _tiles.Register("b"); _tiles.Register("c");
            _biome = new NoiseSplitBiome("split", "a", "b");
            _router = new SingleBiomeRouter(_biome);
        }

        // Helper source: returns a fixed delta for one coord, null for the rest.
        private sealed class StubSource : IChunkDeltaSource
        {
            public ChunkCoord TargetCoord;
            public ChunkDelta Delta;
            public ChunkDelta Read(ChunkCoord coord) => coord.Equals(TargetCoord) ? Delta : null;
        }

        // Direct biome output for the same (seed, coord) — the "ground
        // truth" baseline the provider should reproduce when there is
        // no delta.
        private ChunkData DirectGenerate(ChunkCoord coord, long seed)
        {
            var ctx = new BiomeContext(seed, coord, Size, LayerCount, _tiles);
            return _biome.GenerateChunk(coord, seed, ctx);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void NoDelta_OutputMatchesBaselineByteForByte()
        {
            var provider = new DiffOverlayChunkProvider(
                _router, new EmptyDeltaSource(),
                worldSeed: 42L, chunkSize: Size, layerCount: LayerCount, tiles: _tiles);

            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            uint expectedCrc = DirectGenerate(coord, 42L).ComputeCrc32();
            uint actualCrc   = provider.Get(coord).ComputeCrc32();

            Assert.AreEqual(expectedCrc, actualCrc,
                "With no edits, the provider must return the pure procedural " +
                "baseline. Any drift would mean the overlay is corrupting " +
                "virgin chunks.");
        }

        [Test]
        public void Delta_ChangesOnlyEditedCells()
        {
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var delta = new ChunkDelta(coord, _biome.Id, _biome.Version);
            // Two edits at known cells — the rest of the chunk must stay
            // procedural.
            ushort cId = _tiles.GetId("c");
            delta.Add(new TileEdit(0, 1, 1, cId));
            delta.Add(new TileEdit(0, 5, 3, cId));

            var src = new StubSource { TargetCoord = coord, Delta = delta };
            var provider = new DiffOverlayChunkProvider(
                _router, src,
                worldSeed: 42L, chunkSize: Size, layerCount: LayerCount, tiles: _tiles);

            var baseline = DirectGenerate(coord, 42L);
            var output   = provider.Get(coord);

            int diffs = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (baseline.Get(0, x, y) != output.Get(0, x, y)) diffs++;

            Assert.AreEqual(2, diffs,
                "Exactly two cells must differ from the baseline — the same " +
                "two cells the delta touched.");
            Assert.AreEqual(cId, output.Get(0, 1, 1));
            Assert.AreEqual(cId, output.Get(0, 5, 3));
        }

        [Test]
        public void Delta_OutsideCoordIsIgnored_BaselineAtOtherCoordsIsPure()
        {
            // The stub source only returns a delta for (0,0). Pulling
            // chunk (1,0) must return a pure baseline.
            var editedCoord = new ChunkCoord(WorldId.Base, 0, 0);
            var virginCoord = new ChunkCoord(WorldId.Base, 1, 0);
            var delta = new ChunkDelta(editedCoord, _biome.Id, _biome.Version);
            delta.Add(new TileEdit(0, 0, 0, _tiles.GetId("c")));

            var src = new StubSource { TargetCoord = editedCoord, Delta = delta };
            var provider = new DiffOverlayChunkProvider(
                _router, src,
                worldSeed: 42L, chunkSize: Size, layerCount: LayerCount, tiles: _tiles);

            uint baselineCrc = DirectGenerate(virginCoord, 42L).ComputeCrc32();
            uint outputCrc   = provider.Get(virginCoord).ComputeCrc32();
            Assert.AreEqual(baselineCrc, outputCrc,
                "Coords without a delta must produce pure baselines — no " +
                "spillover from a neighbour's edit history.");
        }

        [Test]
        public void StaleVersionDelta_AppliesAndWarns()
        {
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            // Same biome id, OLDER version: the conservative path applies
            // anyway and warns rather than silently erasing the edits.
            var delta = new ChunkDelta(coord, _biome.Id, biomeVersion: _biome.Version - 1);
            delta.Add(new TileEdit(0, 0, 0, _tiles.GetId("c")));

            var src = new StubSource { TargetCoord = coord, Delta = delta };
            var provider = new DiffOverlayChunkProvider(
                _router, src,
                worldSeed: 42L, chunkSize: Size, layerCount: LayerCount, tiles: _tiles);

            LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("Stale delta"));

            var output = provider.Get(coord);
            Assert.AreEqual(_tiles.GetId("c"), output.Get(0, 0, 0),
                "Stale-version edits must still apply — the alternative is " +
                "silently erasing player work, which is worse than a warning.");
        }

        [Test]
        public void Determinism_TwoGetsForSameCoord_IdenticalCrc()
        {
            var provider = new DiffOverlayChunkProvider(
                _router, new EmptyDeltaSource(),
                worldSeed: 42L, chunkSize: Size, layerCount: LayerCount, tiles: _tiles);

            var coord = new ChunkCoord(WorldId.Base, 3, 5);
            uint c1 = provider.Get(coord).ComputeCrc32();
            uint c2 = provider.Get(coord).ComputeCrc32();
            Assert.AreEqual(c1, c2,
                "Phase-4 client prediction depends on the provider being " +
                "deterministic. Two consecutive Gets must reproduce the chunk " +
                "byte-for-byte even with the overlay step in the middle.");
        }
    }
}
