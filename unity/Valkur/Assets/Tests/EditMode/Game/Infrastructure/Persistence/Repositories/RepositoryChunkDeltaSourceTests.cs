using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Pins the Infrastructure -> Data adapter that lets a
    /// DiffOverlayChunkProvider read deltas from an
    /// IChunkDeltaRepository. Two invariants:
    ///   - The adapter scopes every Read to the WorldId it was
    ///     constructed with — chunks from a different world are NOT
    ///     reachable, no matter their coord.
    ///   - Pass-through round-trip: what the repo holds is what the
    ///     adapter returns.
    /// </summary>
    [TestFixture]
    public class RepositoryChunkDeltaSourceTests
    {
        [Test]
        public void Read_RoundTripsThroughRepository()
        {
            var repo = new InMemoryChunkDeltaRepository();
            var coord = new ChunkCoord(WorldId.Base, 1, 2);
            var delta = new ChunkDelta(coord, "biome", 1);
            delta.Add(new TileEdit(0, 0, 0, 99));
            repo.Write(WorldId.Base, coord, delta);

            var source = new RepositoryChunkDeltaSource(repo, WorldId.Base);
            var loaded = source.Read(coord);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Tiles.Count);
            Assert.AreEqual(99, loaded.Tiles[0].NewTileId);
        }

        [Test]
        public void Read_OnlyReturnsTheConstructedWorldsDeltas()
        {
            var repo = new InMemoryChunkDeltaRepository();
            var alt = new WorldId(System.Guid.NewGuid(), "alt");

            // Same coord (Cx,Cy) in two different worlds. The adapter must
            // resolve to its own world, not whatever is stored at the
            // coord across all worlds.
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var altCoord = new ChunkCoord(alt, 0, 0);

            var baseDelta = new ChunkDelta(coord, "base_biome", 1);
            baseDelta.Add(new TileEdit(0, 0, 0, 1));
            repo.Write(WorldId.Base, coord, baseDelta);

            var altDelta = new ChunkDelta(altCoord, "alt_biome", 1);
            altDelta.Add(new TileEdit(0, 0, 0, 2));
            repo.Write(alt, altCoord, altDelta);

            var baseSource = new RepositoryChunkDeltaSource(repo, WorldId.Base);
            var altSource  = new RepositoryChunkDeltaSource(repo, alt);

            Assert.AreEqual("base_biome", baseSource.Read(coord).BiomeId);
            Assert.AreEqual("alt_biome",  altSource.Read(altCoord).BiomeId);
        }

        [Test]
        public void Read_MissingChunk_ReturnsNull()
        {
            var repo = new InMemoryChunkDeltaRepository();
            var source = new RepositoryChunkDeltaSource(repo, WorldId.Base);
            Assert.IsNull(source.Read(new ChunkCoord(WorldId.Base, 0, 0)),
                "Missing chunk must propagate as null — DiffOverlayChunkProvider " +
                "treats null as 'no edits' and returns the pure baseline.");
        }
    }
}
