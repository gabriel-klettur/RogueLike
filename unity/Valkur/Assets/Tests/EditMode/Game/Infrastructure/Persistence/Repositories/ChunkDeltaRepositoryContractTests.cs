using System.IO;
using System.Linq;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Contract every <see cref="IChunkDeltaRepository"/> implementation
    /// must satisfy. The headline invariants:
    ///
    ///   - Empty deltas are NOT persisted (save size proportional to
    ///     edits, not visited area). Writing an empty delta over an
    ///     existing file deletes it.
    ///   - Round-trip preserves every TileEdit and the biome version tag
    ///     so a future biome-version bump can detect stale deltas.
    ///   - Worlds are isolated: ListEdited / Read / Write of a chunk in
    ///     world A does not see / mutate the same coord in world B.
    ///
    /// Two derived fixtures plug in the in-memory and JSON-file backends.
    /// </summary>
    public abstract class ChunkDeltaRepositoryContractTests
    {
        protected IChunkDeltaRepository Repo { get; private set; }

        protected abstract IChunkDeltaRepository CreateRepo();
        protected virtual  void OnTearDown() { }

        [SetUp]    public void SetUp()    => Repo = CreateRepo();
        [TearDown] public void TearDown() => OnTearDown();

        private static ChunkDelta MakeDelta(ChunkCoord coord, int editCount = 3, string biomeId = "test")
        {
            var d = new ChunkDelta(coord, biomeId, biomeVersion: 1);
            for (int i = 0; i < editCount; i++)
                d.Add(new TileEdit(layer: 0, localX: i, localY: 0, newTileId: (ushort)(100 + i)));
            return d;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test] public void Read_MissingChunk_ReturnsNull()
        {
            Assert.IsNull(Repo.Read(WorldId.Base, new ChunkCoord(WorldId.Base, 0, 0)));
        }

        [Test] public void Exists_MissingChunk_ReturnsFalse()
        {
            Assert.IsFalse(Repo.Exists(WorldId.Base, new ChunkCoord(WorldId.Base, 0, 0)));
        }

        [Test] public void Write_Then_Read_PreservesEditsAndVersion()
        {
            var coord = new ChunkCoord(WorldId.Base, 1, 2);
            var src   = MakeDelta(coord, editCount: 5, biomeId: "forest");

            Repo.Write(WorldId.Base, coord, src);
            var loaded = Repo.Read(WorldId.Base, coord);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(src.BiomeId,      loaded.BiomeId);
            Assert.AreEqual(src.BiomeVersion, loaded.BiomeVersion);
            Assert.AreEqual(src.Tiles.Count,  loaded.Tiles.Count);
            for (int i = 0; i < src.Tiles.Count; i++)
                Assert.AreEqual(src.Tiles[i].NewTileId, loaded.Tiles[i].NewTileId,
                    $"Edit #{i} tile id must round-trip.");
        }

        [Test] public void Write_EmptyDelta_DoesNotPersist()
        {
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var empty = new ChunkDelta(coord, "test", 1); // no edits added
            Repo.Write(WorldId.Base, coord, empty);
            Assert.IsFalse(Repo.Exists(WorldId.Base, coord),
                "Empty deltas must not occupy disk — the persistence story " +
                "is 'cost == edits', not 'cost == visited chunks'.");
        }

        [Test] public void Write_EmptyDelta_OverExistingFile_DeletesIt()
        {
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            Repo.Write(WorldId.Base, coord, MakeDelta(coord));
            Assert.IsTrue(Repo.Exists(WorldId.Base, coord));

            // Player undoes every edit — their delta becomes empty again.
            var empty = new ChunkDelta(coord, "test", 1);
            Repo.Write(WorldId.Base, coord, empty);
            Assert.IsFalse(Repo.Exists(WorldId.Base, coord),
                "Writing an empty delta over an existing file must delete it " +
                "so 'no file' continues to mean 'no edits'.");
        }

        [Test] public void Delete_RemovesPersistedDelta()
        {
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            Repo.Write(WorldId.Base, coord, MakeDelta(coord));
            Assert.IsTrue(Repo.Delete(WorldId.Base, coord));
            Assert.IsFalse(Repo.Exists(WorldId.Base, coord));
        }

        [Test] public void Delete_NoFile_ReturnsFalseWithoutThrowing()
        {
            Assert.IsFalse(Repo.Delete(WorldId.Base, new ChunkCoord(WorldId.Base, 7, 7)));
        }

        [Test] public void ListEdited_OnlyReturnsChunksInRequestedWorld()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "alt");
            Repo.Write(WorldId.Base, new ChunkCoord(WorldId.Base, 1, 1), MakeDelta(new ChunkCoord(WorldId.Base, 1, 1)));
            Repo.Write(alt,          new ChunkCoord(alt, 2, 2),          MakeDelta(new ChunkCoord(alt, 2, 2)));

            var inBase = Repo.ListEdited(WorldId.Base).ToList();
            var inAlt  = Repo.ListEdited(alt).ToList();

            Assert.That(inBase, Has.Some.Matches<ChunkCoord>(c => c.Cx == 1 && c.Cy == 1));
            Assert.That(inBase, Has.None.Matches<ChunkCoord>(c => c.Cx == 2 && c.Cy == 2));
            Assert.That(inAlt,  Has.Some.Matches<ChunkCoord>(c => c.Cx == 2 && c.Cy == 2));
            Assert.That(inAlt,  Has.None.Matches<ChunkCoord>(c => c.Cx == 1 && c.Cy == 1));
        }

        [Test] public void Worlds_AreIsolated_NoCrossReadWrite()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "alt");
            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var altCoord = new ChunkCoord(alt, 0, 0);

            Repo.Write(WorldId.Base, coord,    MakeDelta(coord, biomeId: "base_biome"));
            Repo.Write(alt,          altCoord, MakeDelta(altCoord, biomeId: "alt_biome"));

            Assert.AreEqual("base_biome", Repo.Read(WorldId.Base, coord).BiomeId);
            Assert.AreEqual("alt_biome",  Repo.Read(alt,          altCoord).BiomeId);
        }
    }

    // ── Concrete fixtures ─────────────────────────────────────────────────────────

    [TestFixture]
    public class InMemoryChunkDeltaRepositoryTests : ChunkDeltaRepositoryContractTests
    {
        protected override IChunkDeltaRepository CreateRepo() => new InMemoryChunkDeltaRepository();
    }

    [TestFixture]
    public class JsonFileChunkDeltaRepositoryTests : ChunkDeltaRepositoryContractTests
    {
        private string _tempRoot;

        protected override IChunkDeltaRepository CreateRepo()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "valkur_chunk_delta_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            return new JsonFileChunkDeltaRepository(_tempRoot);
        }

        protected override void OnTearDown()
        {
            if (!string.IsNullOrEmpty(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
