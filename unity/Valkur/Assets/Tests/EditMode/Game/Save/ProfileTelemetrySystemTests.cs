using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Save;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// Pins <see cref="ProfileTelemetrySystem"/>: StartRun creates an
    /// active row, OnEntityDied increments kill stats AND the active
    /// run's totalKills, OnPlayerDied bumps the lifetime <c>deaths_total</c>
    /// counter WITHOUT ending the active run (the spirit/altar revive
    /// flow keeps the run open), OnRunEnded closes the run + updates
    /// global counters + persists, OnXpGained / OnLevelUp accumulate
    /// run stats.
    /// </summary>
    [TestFixture]
    public class ProfileTelemetrySystemTests
    {
        private GameObject _go;
        private ProfileTelemetrySystem _sys;
        private InMemoryProfileDb _db;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _db = new InMemoryProfileDb();
            _go = new GameObject("ProfileTelemetrySystem");
            _sys = _go.AddComponent<ProfileTelemetrySystem>();
            _sys.BindDb(_db);

            // OnEnable doesn't fire reliably in EditMode AddComponent.
            var onEnable = typeof(ProfileTelemetrySystem).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);
            onEnable.Invoke(_sys, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            GameEvents.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static GameObject MakeMonster(string monsterKey)
        {
            var go = new GameObject("Monster_" + monsterKey);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<Health>();
            var brain = go.AddComponent<FSMMonsterBrain>();
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = monsterKey;
            def.displayName = monsterKey;
            var f = typeof(FSMMonsterBrain).GetField("definition",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(brain, def);
            return go;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void StartRun_CreatesActiveRecord()
        {
            _sys.StartRun(permadeath: false);
            Assert.IsNotNull(_sys.ActiveRun);
            Assert.IsNotEmpty(_sys.ActiveRun.runId);
            Assert.IsFalse(_sys.ActiveRun.wasPermadeath);
            Assert.AreEqual(1, _db.Runs.Count(),
                "StartRun must insert one row immediately so it shows up in mid-run UI.");
        }

        [Test]
        public void OnEntityDied_NPC_IncrementsKillStatsAndActiveRun()
        {
            _sys.StartRun();
            var wolf = MakeMonster("wolf");
            try
            {
                GameEvents.FireEntityDied(wolf, killer: null);

                Assert.AreEqual(1, _db.KillStats.Get("wolf").totalKills,
                    "Lifetime kill stats must increment on NPC death.");
                Assert.AreEqual(1, _sys.ActiveRun.totalKills,
                    "Active run's kill counter must mirror the lifetime increment.");
            }
            finally { Object.DestroyImmediate(wolf); }
        }

        [Test]
        public void OnEntityDied_Player_IsSkipped()
        {
            _sys.StartRun();
            var player = new GameObject("Player") { tag = "Player" };
            try
            {
                GameEvents.FireEntityDied(player, null);
                Assert.AreEqual(0, _db.KillStats.TotalAcrossAllEntities(),
                    "Player death must NOT increment NPC kill stats.");
                Assert.AreEqual(0, _sys.ActiveRun.totalKills);
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void OnPlayerDied_KeepsRunOpenAndIncrementsDeathCounter()
        {
            _sys.StartRun();
            int totalRunsBefore  = _db.Profile.GetInt("total_runs");
            int deathsBefore     = _db.Profile.GetInt("deaths_total");

            GameEvents.FirePlayerDied();

            Assert.IsTrue(string.IsNullOrEmpty(_sys.ActiveRun.endedAtIso),
                "Spirit/altar flow: the active run must stay open across deaths.");
            Assert.AreEqual(totalRunsBefore, _db.Profile.GetInt("total_runs"),
                "Global total_runs must NOT increment on a death (run isn't over).");
            Assert.AreEqual(deathsBefore + 1, _db.Profile.GetInt("deaths_total"),
                "Global deaths_total must bump once per OnPlayerDied.");
        }

        [Test]
        public void OnRunEnded_ClosesRunAndIncrementsGlobalCounters()
        {
            _sys.StartRun();
            int totalRunsBefore = _db.Profile.GetInt("total_runs");

            GameEvents.FireRunEnded();

            Assert.IsNotEmpty(_sys.ActiveRun.endedAtIso,
                "Run must be marked ended after OnRunEnded.");
            Assert.AreEqual(totalRunsBefore + 1, _db.Profile.GetInt("total_runs"),
                "Global total_runs must increment when the run ends.");
            Assert.GreaterOrEqual(_db.Profile.GetFloat("total_playtime_sec"), 0f,
                "total_playtime_sec must be persisted (cumulative).");
        }

        [Test]
        public void OnXpGained_AccumulatesIntoActiveRun()
        {
            _sys.StartRun();
            var entity = new GameObject("Player") { tag = "Player" };
            try
            {
                GameEvents.FireXpGained(entity, 25);
                GameEvents.FireXpGained(entity, 17);
                Assert.AreEqual(42, _sys.ActiveRun.totalXpGained,
                    "totalXpGained must sum across XP-grant events during the run.");
            }
            finally { Object.DestroyImmediate(entity); }
        }

        [Test]
        public void OnLevelUp_TracksMaxDepth()
        {
            _sys.StartRun();
            var entity = new GameObject("Player") { tag = "Player" };
            try
            {
                GameEvents.FireLevelUp(entity, 3);
                GameEvents.FireLevelUp(entity, 5);
                GameEvents.FireLevelUp(entity, 4); // regression guard

                Assert.AreEqual(5, _sys.ActiveRun.depthReached,
                    "depthReached must track the MAXIMUM level reached, not the latest.");
            }
            finally { Object.DestroyImmediate(entity); }
        }

        [Test]
        public void EntityKey_FallsBackToGameObjectName_WhenNoBrain()
        {
            // Generic NPC without FSMMonsterBrain — telemetry must still
            // count the kill, using the GameObject name (cleaned of "(Clone)").
            _sys.StartRun();
            var npc = new GameObject("rat(Clone)");
            try
            {
                GameEvents.FireEntityDied(npc, null);
                Assert.IsNotNull(_db.KillStats.Get("rat"),
                    "(Clone) suffix must be stripped so 'rat(Clone)' aggregates with 'rat'.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        [Test]
        public void StartRun_WithoutBindDb_LogsAndContinues()
        {
            // System without a bound DB must not crash; instead it logs and
            // skips telemetry for the rest of the run.
            var go2 = new GameObject("Unbound");
            var sys2 = go2.AddComponent<ProfileTelemetrySystem>();
            try
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("StartRun called before BindDb"));
                sys2.StartRun();
                Assert.IsNull(sys2.ActiveRun,
                    "Without a DB the system must NOT roll a run; gameplay continues, telemetry skipped.");
            }
            finally { Object.DestroyImmediate(go2); }
        }

        // ── Run ordinal — per-profile monotonic identifier ──────────────────

        [Test]
        public void StartRun_FirstRun_AssignsOrdinalOne()
        {
            _sys.StartRun();
            Assert.AreEqual(1, _sys.ActiveRun.runOrdinal,
                "The first run on a fresh profile must have ordinal #1.");
            Assert.AreEqual(1, _sys.ActiveRunOrdinal,
                "ActiveRunOrdinal property must mirror ActiveRun.runOrdinal.");
        }

        [Test]
        public void StartRun_SequentialRuns_OrdinalIsMonotonic()
        {
            _sys.StartRun(); int o1 = _sys.ActiveRunOrdinal;
            _sys.StartRun(); int o2 = _sys.ActiveRunOrdinal;
            _sys.StartRun(); int o3 = _sys.ActiveRunOrdinal;
            Assert.AreEqual(1, o1);
            Assert.AreEqual(2, o2);
            Assert.AreEqual(3, o3);
            Assert.AreEqual(3, _db.Runs.Count(),
                "Each StartRun must insert one new row, never collide on the previous ordinal.");
        }

        [Test]
        public void StartRun_DistinctOrdinals_NeverCollideAcrossRuns()
        {
            // Mint 50 runs and verify every ordinal is unique. Catches any
            // future regression where the counter accidentally gets reset
            // between runs (e.g. SaveAll wiping it, or the increment not
            // persisting).
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 50; i++)
            {
                _sys.StartRun();
                int ord = _sys.ActiveRunOrdinal;
                Assert.IsTrue(seen.Add(ord),
                    $"Ordinal #{ord} was minted twice — uniqueness invariant broken at iteration {i}.");
            }
        }

        [Test]
        public void StartRun_ReuseOrdinal_PreservesProvidedValue()
        {
            // Loading a save must adopt the saved ordinal verbatim instead of
            // bumping the counter — otherwise resuming "Run #3" would suddenly
            // become "Run #4" on next launch and the human-facing identity
            // would silently break.
            _sys.StartRun(reuseRunId: "abc", reuseOrdinal: 7);
            Assert.AreEqual(7, _sys.ActiveRunOrdinal,
                "When reuseOrdinal is supplied, StartRun must adopt it without consulting the counter.");
            Assert.AreEqual("abc", _sys.ActiveRun.runId,
                "When reuseRunId is supplied, StartRun must adopt it without minting a fresh GUID.");

            // The counter must NOT have been incremented — a subsequent
            // fresh-mint StartRun should produce the next free ordinal,
            // not "8" (which would imply the resume call leaked an increment).
            _sys.StartRun();
            Assert.AreEqual(1, _sys.ActiveRunOrdinal,
                "Fresh StartRun after a reuseOrdinal call must mint #1 (the counter was untouched).");
        }
    }
}
