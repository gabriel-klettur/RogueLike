using System.IO;
using NUnit.Framework;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.Tests.EditMode.Game.Infrastructure.Persistence.Profile
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

    /// <summary>
    /// Json-specific contract: persistence across instances. We can't
    /// run this against InMemory because it intentionally has no disk
    /// round-trip.
    /// </summary>
    [TestFixture]
    public class JsonProfileDbPersistenceTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(),
                "valkur_profile_persistence_" + System.Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path)) File.Delete(_path);
            string bak = _path + ".bak";
            if (File.Exists(bak)) File.Delete(bak);
        }

        [Test]
        public void SaveLoad_PersistsAcrossInstances()
        {
            var db1 = new JsonProfileDb(_path);
            db1.KillStats.RecordKill("wolf");
            db1.KillStats.RecordKill("wolf");
            db1.Achievements.Unlock("first_kill");
            db1.Profile.SetInt("total_runs", 3);
            db1.SaveAll();

            var db2 = new JsonProfileDb(_path);
            db2.LoadAll();

            Assert.AreEqual(2, db2.KillStats.Get("wolf").totalKills);
            Assert.IsTrue(db2.Achievements.IsUnlocked("first_kill"));
            Assert.AreEqual(3, db2.Profile.GetInt("total_runs"));
        }

        [Test]
        public void Load_NonExistentFile_StartsEmpty()
        {
            var db = new JsonProfileDb(_path);
            Assert.DoesNotThrow(() => db.LoadAll(),
                "Loading from a non-existent file must be a silent no-op (fresh profile path).");
            Assert.AreEqual(0, db.Runs.Count());
        }

        [Test]
        public void Save_AtomicWrite_LeavesBakSidecar()
        {
            var db = new JsonProfileDb(_path);
            db.Profile.SetInt("k", 1);
            db.SaveAll();
            db.Profile.SetInt("k", 2);
            db.SaveAll();

            Assert.IsTrue(File.Exists(_path), "Primary file must exist.");
            Assert.IsTrue(File.Exists(_path + ".bak"),
                "Second SaveAll must produce the .bak sidecar via File.Replace.");
        }

        [Test]
        public void Load_FromBakSidecar_WhenPrimaryMissing()
        {
            // Simulate a crash that left only the .bak intact.
            var db = new JsonProfileDb(_path);
            db.Profile.SetInt("from_bak", 99);
            db.SaveAll();
            db.SaveAll(); // creates .bak from previous save
            File.Delete(_path); // primary lost

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("loading sidecar"));

            var db2 = new JsonProfileDb(_path);
            db2.LoadAll();
            Assert.AreEqual(99, db2.Profile.GetInt("from_bak"),
                "When the primary file is missing, LoadAll must fall back to .bak.");
        }

        [Test]
        public void ResetAll_DeletesFiles()
        {
            var db = new JsonProfileDb(_path);
            db.Profile.SetInt("k", 1);
            db.SaveAll();
            db.SaveAll();

            Assert.IsTrue(File.Exists(_path));

            db.ResetAll();

            Assert.IsFalse(File.Exists(_path), "ResetAll must delete the primary file.");
            Assert.IsFalse(File.Exists(_path + ".bak"), "ResetAll must delete the .bak too.");
        }
    }
}
