using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Regression coverage for the per-CALL unique temp-file name fix in
    /// <see cref="JsonFileTileOverrideRepository.Write"/>, added alongside the
    /// Tile Editor's debounced background autosave (perf wave 2).
    ///
    /// Before the fix, every writer of a given zone shared the fixed name
    /// <c>"&lt;path&gt;.tmp"</c> — two overlapping writes (e.g. a debounced
    /// background autosave racing an explicit <c>SaveZone</c> on the main
    /// thread) opened the same handle and the loser threw
    /// <c>"Access to the path is denied"</c>. This is complementary to
    /// <c>TileOverrideRepositoryContractTests</c> (shared, sequential,
    /// single-threaded contract) — this file is specifically about what
    /// happens when two writes to the SAME zone genuinely overlap in time.
    /// </summary>
    [TestFixture]
    public class JsonFileTileOverrideRepositoryConcurrencyTests
    {
        private string _tempDir;
        private JsonFileTileOverrideRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
                "valkur_tileoverride_concurrency_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _repo = new JsonFileTileOverrideRepository(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Test]
        public void Write_TwoOverlappingWritesToSameZone_BothComplete_WithoutThrowing()
        {
            const string zone = "overlap_zone";

            Exception errorA = null, errorB = null;
            var t1 = Task.Run(() =>
            {
                try { _repo.Write(WorldId.Base, zone, "{\"v\":1}"); }
                catch (Exception ex) { errorA = ex; }
            });
            var t2 = Task.Run(() =>
            {
                try { _repo.Write(WorldId.Base, zone, "{\"v\":2}"); }
                catch (Exception ex) { errorB = ex; }
            });

            bool completed = Task.WaitAll(new[] { t1, t2 }, TimeSpan.FromSeconds(10));

            Assert.IsTrue(completed, "Both overlapping writes must complete within the timeout.");
            Assert.IsNull(errorA, $"First overlapping write must not throw. Got: {errorA}");
            Assert.IsNull(errorB, $"Second overlapping write must not throw. Got: {errorB}");
        }

        [Test]
        public void Write_TwoOverlappingWritesToSameZone_FinalFileContainsOneCompleteValidPayload()
        {
            const string zone = "overlap_zone_integrity";
            const string payloadA = "{\"v\":1}";
            const string payloadB = "{\"v\":2}";

            var t1 = Task.Run(() => _repo.Write(WorldId.Base, zone, payloadA));
            var t2 = Task.Run(() => _repo.Write(WorldId.Base, zone, payloadB));
            Task.WaitAll(new[] { t1, t2 }, TimeSpan.FromSeconds(10));

            string result = _repo.Read(WorldId.Base, zone);
            Assert.IsTrue(result == payloadA || result == payloadB,
                $"The final file must be exactly one writer's complete, uncorrupted payload " +
                $"(a shared temp name could interleave partial writes) — got '{result}'.");
        }

        [Test]
        public void Write_TwoOverlappingWritesToSameZone_LeavesNoOrphanedTempFiles()
        {
            const string zone = "overlap_zone_cleanup";

            var t1 = Task.Run(() => _repo.Write(WorldId.Base, zone, "{\"v\":1}"));
            var t2 = Task.Run(() => _repo.Write(WorldId.Base, zone, "{\"v\":2}"));
            Task.WaitAll(new[] { t1, t2 }, TimeSpan.FromSeconds(10));

            var leftoverTemps = Directory.GetFiles(_tempDir, "*.tmp", SearchOption.AllDirectories);
            Assert.AreEqual(0, leftoverTemps.Length,
                "Every writer's GUID-named temp file must be consumed (moved/replaced) — none " +
                "should survive two successful overlapping writes.");
        }

        [Test]
        public void Write_ManySequentialWrites_NeverLeaveTempFilesBehind()
        {
            // Each call mints its own GUID suffix — not a fixed "<path>.tmp" — even for
            // strictly sequential (non-overlapping) writes.
            const string zone = "sequential_zone";
            for (int i = 0; i < 5; i++)
                _repo.Write(WorldId.Base, zone, "{\"v\":" + i + "}");

            Assert.AreEqual("{\"v\":4}", _repo.Read(WorldId.Base, zone));
            var leftoverTemps = Directory.GetFiles(_tempDir, "*.tmp", SearchOption.AllDirectories);
            Assert.AreEqual(0, leftoverTemps.Length,
                "No temp files should survive repeated sequential writes to the same zone.");
        }
    }
}
