using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Data.Chunks
{
    /// <summary>
    /// Phase 2 acceptance: the diff-on-procedural cycle survives a full
    /// save -> reload roundtrip. Conceptually:
    ///
    ///   1. Procedural baseline P generated from (seed, coord, biome).
    ///   2. Player edits a cell -> result M = P with (x,y) changed.
    ///   3. Diff computed -> ChunkDelta D = DiffFrom(P, M).
    ///   4. Save: D is persisted into IChunkDeltaRepository.
    ///   5. RELOAD: regenerate P, read D, apply -> M' must equal M
    ///      bit-for-bit.
    ///
    /// This is the persistence story Phase 4 networking inherits: the
    /// server replicates only the delta, the client regenerates the
    /// baseline, and both reach the same world.
    /// </summary>
    [TestFixture]
    public class Phase2EndToEndCycleTests
    {
        private const long Seed = 42L;
        private const int  Size = 8;
        private const int  Layers = 1;

        private DictionaryTileIdTable _tiles;
        private NoiseSplitBiome       _biome;
        private SingleBiomeRouter     _router;
        private InMemoryChunkDeltaRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _tiles = new DictionaryTileIdTable();
            _tiles.Register("a"); _tiles.Register("b"); _tiles.Register("c");
            _biome  = new NoiseSplitBiome("split", "a", "b");
            _router = new SingleBiomeRouter(_biome);
            _repo   = new InMemoryChunkDeltaRepository();
        }

        private DiffOverlayChunkProvider BuildProvider() => new DiffOverlayChunkProvider(
            _router, new RepositoryChunkDeltaSource(_repo, WorldId.Base),
            worldSeed: Seed, chunkSize: Size, layerCount: Layers, tiles: _tiles);

        private ChunkData GenerateBaseline(ChunkCoord coord)
        {
            var ctx = new BiomeContext(Seed, coord, Size, Layers, _tiles);
            return _biome.GenerateChunk(coord, Seed, ctx);
        }

        // ── Acceptance ──────────────────────────────────────────────────────────

        [Test]
        public void EndToEndCycle_ProceduralBaseline_PlayerEdit_Save_Reload_Identical()
        {
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            ushort cId = _tiles.GetId("c");

            // ── Step 1: virgin chunk has no delta. Provider returns baseline.
            var providerA = BuildProvider();
            var virgin = providerA.Get(coord);
            uint virginCrc = virgin.ComputeCrc32();
            var baseline = GenerateBaseline(coord);
            Assert.AreEqual(baseline.ComputeCrc32(), virginCrc,
                "Sanity: with no edits, the provider returns the pure baseline.");

            // ── Step 2: player edits two cells in their working copy of M.
            var modified = GenerateBaseline(coord);
            modified.Set(0, 1, 1, cId);
            modified.Set(0, 5, 3, cId);
            uint modifiedCrc = modified.ComputeCrc32();
            Assert.AreNotEqual(virginCrc, modifiedCrc, "Sanity: edits actually change the chunk.");

            // ── Step 3: diff against the regenerated baseline (the save flow).
            var delta = ChunkDelta.DiffFrom(baseline, modified, _biome.Id, _biome.Version);
            Assert.AreEqual(2, delta.Tiles.Count, "Two cells were touched -> two TileEdit entries.");

            // ── Step 4: persist the delta.
            _repo.Write(WorldId.Base, coord, delta);
            Assert.IsTrue(_repo.Exists(WorldId.Base, coord),
                "Non-empty delta must be persisted.");

            // ── Step 5: simulate a fresh boot: brand-new provider that
            //   knows nothing about the previous run other than the
            //   repository contents.
            var providerB = BuildProvider();
            var reloaded = providerB.Get(coord);
            uint reloadedCrc = reloaded.ComputeCrc32();

            Assert.AreEqual(modifiedCrc, reloadedCrc,
                "Phase-2 acceptance: the reloaded chunk must equal the in-memory " +
                "modified chunk byte-for-byte. The full procedural+diff persistence " +
                "story now survives a save/reload boundary.");
        }

        [Test]
        public void EmptyEditSet_NoDeltaPersisted_VirginReloadIsPureBaseline()
        {
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            // Player visits the chunk but doesn't edit anything: empty
            // delta is computed from baseline vs baseline.
            var baseline = GenerateBaseline(coord);
            var same     = GenerateBaseline(coord);
            var delta = ChunkDelta.DiffFrom(baseline, same, _biome.Id, _biome.Version);
            Assert.IsTrue(delta.IsEmpty, "Sanity: same vs same -> empty diff.");

            _repo.Write(WorldId.Base, coord, delta);
            Assert.IsFalse(_repo.Exists(WorldId.Base, coord),
                "Empty deltas don't reach disk — save size stays O(edits).");

            var provider = BuildProvider();
            uint reloadCrc = provider.Get(coord).ComputeCrc32();
            Assert.AreEqual(baseline.ComputeCrc32(), reloadCrc,
                "Reload of a never-edited chunk must reproduce the pure baseline.");
        }

        [Test]
        public void MultipleChunks_EachKeepsItsOwnDelta()
        {
            var coordA = new ChunkCoord(WorldId.Base, 0, 0);
            var coordB = new ChunkCoord(WorldId.Base, 1, 0);
            ushort cId = _tiles.GetId("c");

            // Edit different cells in each chunk.
            var modA = GenerateBaseline(coordA); modA.Set(0, 0, 0, cId);
            var modB = GenerateBaseline(coordB); modB.Set(0, 7, 7, cId);

            _repo.Write(WorldId.Base, coordA,
                ChunkDelta.DiffFrom(GenerateBaseline(coordA), modA, _biome.Id, _biome.Version));
            _repo.Write(WorldId.Base, coordB,
                ChunkDelta.DiffFrom(GenerateBaseline(coordB), modB, _biome.Id, _biome.Version));

            var provider = BuildProvider();
            Assert.AreEqual(modA.ComputeCrc32(), provider.Get(coordA).ComputeCrc32(),
                "Chunk A reload identical to its modified copy.");
            Assert.AreEqual(modB.ComputeCrc32(), provider.Get(coordB).ComputeCrc32(),
                "Chunk B reload identical to its modified copy.");
            Assert.AreNotEqual(modA.ComputeCrc32(), modB.ComputeCrc32(),
                "Sanity: the two chunks differ.");
        }
    }
}
