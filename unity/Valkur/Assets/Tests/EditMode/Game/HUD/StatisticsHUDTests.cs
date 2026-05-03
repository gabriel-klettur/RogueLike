using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.HUD;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="StatisticsHUD"/>: ComputeStatsText reflects the
    /// bound <see cref="IProfileDb"/> across all four sections (lifetime
    /// counters, top kills, recent runs, achievement count). Open/Close
    /// toggle is honoured.
    /// </summary>
    [TestFixture]
    public class StatisticsHUDTests
    {
        private GameObject _hudGo;
        private StatisticsHUD _hud;
        private InMemoryProfileDb _db;

        [SetUp]
        public void SetUp()
        {
            _hudGo = new GameObject("StatisticsHUD");
            _hud = _hudGo.AddComponent<StatisticsHUD>();
            _hud.EnsureBuilt();

            _db = new InMemoryProfileDb();
            _hud.BindDb(_db);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void NoBoundDb_ProducesEmptyText()
        {
            var bare = new GameObject("Bare").AddComponent<StatisticsHUD>();
            try
            {
                Assert.AreEqual(string.Empty, bare.ComputeStatsText());
            }
            finally { Object.DestroyImmediate(bare.gameObject); }
        }

        [Test]
        public void EmptyDb_RendersStubSections()
        {
            string text = _hud.ComputeStatsText();
            StringAssert.Contains("Lifetime", text);
            StringAssert.Contains("Total runs: 0", text);
            StringAssert.Contains("(no kills yet)", text);
            StringAssert.Contains("(no runs yet)", text);
        }

        [Test]
        public void LifetimeCounters_ReflectProfileEntries()
        {
            _db.Profile.SetInt("total_runs", 5);
            _db.Profile.SetFloat("total_playtime_sec", 3725f); // 1h 02m 05s
            _db.Achievements.Unlock("first_kill");
            _db.Achievements.Unlock("survivor_10m");

            string text = _hud.ComputeStatsText();
            StringAssert.Contains("Total runs: 5", text);
            StringAssert.Contains("1h 02m 05s", text,
                "Total playtime must format as hours/minutes/seconds.");
            StringAssert.Contains("Achievements: 2", text);
        }

        [Test]
        public void TopKills_ListedDescending()
        {
            for (int i = 0; i < 7; i++) _db.KillStats.RecordKill("wolf");
            for (int i = 0; i < 3; i++) _db.KillStats.RecordKill("bear");
            _db.KillStats.RecordKill("rat");

            string text = _hud.ComputeStatsText();
            int wolfIdx = text.IndexOf("wolf: 7");
            int bearIdx = text.IndexOf("bear: 3");
            int ratIdx  = text.IndexOf("rat: 1");

            Assert.Greater(wolfIdx, 0, "wolf must appear in the top kills section.");
            Assert.Greater(bearIdx, wolfIdx,
                "bear must appear AFTER wolf — descending order.");
            Assert.Greater(ratIdx, bearIdx);
        }

        [Test]
        public void RecentRuns_LimitedToTen_OrderedDescending()
        {
            // Insert 12 runs with monotonically-increasing started_at; the
            // HUD must show only the most-recent 10, newest first.
            for (int i = 1; i <= 12; i++)
            {
                _db.Runs.Insert(new RunRecord
                {
                    runId        = "r" + i,
                    startedAtIso = $"2026-05-{i:00}T00:00:00Z",
                    durationSeconds = 60f * i,
                    totalKills   = i,
                    depthReached = i,
                });
            }

            string text = _hud.ComputeStatsText();
            // Newest (r12) must be present; oldest (r1) must NOT.
            StringAssert.Contains("2026-05-12", text);
            Assert.IsFalse(text.Contains("2026-05-01"),
                "Recent runs section must show only the last 10 — older runs must be excluded.");
            Assert.IsFalse(text.Contains("2026-05-02"),
                "11th-newest must also be excluded (limit = 10).");
        }

        [Test]
        public void Open_TogglesIsOpenFlag()
        {
            Assert.IsFalse(_hud.IsOpen);
            _hud.Open();
            Assert.IsTrue(_hud.IsOpen);
            _hud.Close();
            Assert.IsFalse(_hud.IsOpen);
        }

        [Test]
        public void EnsureBuilt_Idempotent()
        {
            int canvasesBefore = _hudGo.GetComponentsInChildren<Canvas>(true).Length;
            _hud.EnsureBuilt();
            _hud.EnsureBuilt();
            int canvasesAfter = _hudGo.GetComponentsInChildren<Canvas>(true).Length;
            Assert.AreEqual(canvasesBefore, canvasesAfter,
                "Repeat EnsureBuilt must not stack multiple Canvases.");
        }
    }
}
