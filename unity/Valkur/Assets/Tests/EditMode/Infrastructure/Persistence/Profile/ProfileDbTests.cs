using System.IO;
using NUnit.Framework;
using Valkur.Infrastructure.Persistence.Profile;
namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Profile
{
    /// <summary>
    /// Pins all four <see cref="IProfileDb"/> repositories against both
    /// the InMemory and Json implementations. Same scenarios run twice
    /// (parametric fixture) to ensure the JSON adapter does not drift
    /// from the in-memory contract.
    ///
    /// What we DON'T test here: SQLite implementation. That's documented
    /// as a future drop-in — the contract this fixture pins is what
    /// SqliteProfileDb will need to satisfy when added.
    /// </summary>
    [TestFixture(typeof(InMemoryProfileDb))]
    [TestFixture(typeof(JsonProfileDb))]
    public class ProfileDbTests<TDb> where TDb : IProfileDb
    {
        private string _tempDir;
        private IProfileDb _db;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
                "valkur_profile_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _db = MakeDb();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private IProfileDb MakeDb()
        {
            if (typeof(TDb) == typeof(JsonProfileDb))
                return new JsonProfileDb(Path.Combine(_tempDir, "profile.json"));
            return new InMemoryProfileDb();
        }

        // ── Run history ─────────────────────────────────────────────────────────

        [Test]
        public void RunHistory_InsertAndGet_RoundTrip()
        {
            var run = new RunRecord
            {
                runId = "r1",
                startedAtIso = "2026-05-03T10:00:00Z",
                endedAtIso   = "2026-05-03T10:30:00Z",
                durationSeconds = 1800f,
                totalKills = 42,
                killedBy = "lich",
            };
            _db.Runs.Insert(run);

            var loaded = _db.Runs.GetById("r1");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(42, loaded.totalKills);
            Assert.AreEqual("lich", loaded.killedBy);
        }

        [Test]
        public void RunHistory_GetAll_DescendingByStartedAt()
        {
            _db.Runs.Insert(new RunRecord { runId = "early", startedAtIso = "2026-05-01T00:00:00Z" });
            _db.Runs.Insert(new RunRecord { runId = "late",  startedAtIso = "2026-05-03T00:00:00Z" });
            _db.Runs.Insert(new RunRecord { runId = "mid",   startedAtIso = "2026-05-02T00:00:00Z" });

            var all = _db.Runs.GetAll();
            Assert.AreEqual(3, all.Count);
            Assert.AreEqual("late",  all[0].runId);
            Assert.AreEqual("mid",   all[1].runId);
            Assert.AreEqual("early", all[2].runId);
        }

        [Test]
        public void RunHistory_AverageDuration_IgnoresZeroEntries()
        {
            _db.Runs.Insert(new RunRecord { runId = "a", durationSeconds = 100f });
            _db.Runs.Insert(new RunRecord { runId = "b", durationSeconds = 200f });
            _db.Runs.Insert(new RunRecord { runId = "c", durationSeconds = 0f }); // unfinished

            Assert.AreEqual(150f, _db.Runs.AverageDurationSeconds(), 0.001f,
                "Average must skip zero-duration entries (in-progress runs).");
        }

        [Test]
        public void RunHistory_Update_OverwritesByRunId()
        {
            _db.Runs.Insert(new RunRecord { runId = "r", totalKills = 1 });
            _db.Runs.Update(new RunRecord { runId = "r", totalKills = 7 });

            Assert.AreEqual(7, _db.Runs.GetById("r").totalKills,
                "Update must replace the row with the new payload.");
            Assert.AreEqual(1, _db.Runs.Count(),
                "Update by existing id must NOT insert a duplicate.");
        }

        // ── Kill stats ─────────────────────────────────────────────────────────

        [Test]
        public void KillStats_RecordKill_IncrementsCounter()
        {
            _db.KillStats.RecordKill("wolf");
            _db.KillStats.RecordKill("wolf");
            _db.KillStats.RecordKill("bear");

            Assert.AreEqual(2, _db.KillStats.Get("wolf").totalKills);
            Assert.AreEqual(1, _db.KillStats.Get("bear").totalKills);
            Assert.AreEqual(3, _db.KillStats.TotalAcrossAllEntities());
        }

        [Test]
        public void KillStats_GetTop_OrdersDescending()
        {
            _db.KillStats.RecordKill("a");
            _db.KillStats.RecordKill("a");
            _db.KillStats.RecordKill("a");
            _db.KillStats.RecordKill("b");
            _db.KillStats.RecordKill("b");
            _db.KillStats.RecordKill("c");

            var top2 = _db.KillStats.GetTop(2);
            Assert.AreEqual(2, top2.Count);
            Assert.AreEqual("a", top2[0].entityKey);
            Assert.AreEqual("b", top2[1].entityKey);
        }

        // ── Achievements ───────────────────────────────────────────────────────

        [Test]
        public void Achievement_Unlock_FirstCallReturnsTrue_SecondReturnsFalse()
        {
            Assert.IsTrue(_db.Achievements.Unlock("first_blood"),
                "First Unlock must return true (caller can fire UI/audio).");
            Assert.IsFalse(_db.Achievements.Unlock("first_blood"),
                "Re-unlocking must return false so callers don't double-fire UI.");
            Assert.IsTrue(_db.Achievements.IsUnlocked("first_blood"));
            Assert.AreEqual(1, _db.Achievements.UnlockedCount());
        }

        // ── Profile (key/value) ────────────────────────────────────────────────

        [Test]
        public void Profile_IncrementInt_CreatesAndIncrements()
        {
            int v1 = _db.Profile.IncrementInt("total_runs");
            int v2 = _db.Profile.IncrementInt("total_runs");
            int v3 = _db.Profile.IncrementInt("total_runs", 5);

            Assert.AreEqual(1, v1);
            Assert.AreEqual(2, v2);
            Assert.AreEqual(7, v3);
            Assert.AreEqual(7, _db.Profile.GetInt("total_runs"));
        }

        [Test]
        public void Profile_GetInt_FallbackOnUnsetKey()
        {
            Assert.AreEqual(42, _db.Profile.GetInt("unset", fallback: 42));
            Assert.AreEqual(0,  _db.Profile.GetInt("unset"));
        }

        [Test]
        public void ResetAll_WipesEverything()
        {
            _db.Runs.Insert(new RunRecord { runId = "r" });
            _db.KillStats.RecordKill("wolf");
            _db.Achievements.Unlock("a");
            _db.Profile.SetInt("k", 5);

            _db.ResetAll();

            Assert.AreEqual(0, _db.Runs.Count());
            Assert.AreEqual(0, _db.KillStats.TotalAcrossAllEntities());
            Assert.AreEqual(0, _db.Achievements.UnlockedCount());
            Assert.AreEqual(0, _db.Profile.GetInt("k"));
        }
    }
}
