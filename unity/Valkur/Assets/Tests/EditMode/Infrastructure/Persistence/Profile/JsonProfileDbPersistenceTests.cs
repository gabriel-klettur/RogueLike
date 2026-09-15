using System.IO;
using NUnit.Framework;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Profile
{
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
