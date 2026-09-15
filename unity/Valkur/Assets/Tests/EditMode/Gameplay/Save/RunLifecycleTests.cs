using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Save;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.Tests.EditMode.Gameplay.Save
{
    /// <summary>
    /// Pins that a run is CLOSED, and that the kill board only counts hostiles.
    ///
    /// <para><b>The defect these exist for.</b> <c>GameEvents.FireRunEnded()</c> had zero
    /// production callers for the life of the project — only a test raised it — while
    /// <c>StartRun</c> was called from the boot sequence. So the game opened a run on every
    /// launch and closed none. Measured on this machine's shipped profile before the fix:
    /// <b>251 run rows, 0 of them with a duration</b>, no <c>total_runs</c> key at all, and 249
    /// of 251 reading <c>kills=0</c> while the lifetime kill table held 39 kills.</para>
    ///
    /// <para>None of it failed loudly. The panel drew "Total runs: 0" above a list of 251 runs
    /// and both halves were internally consistent — the shape this project keeps finding.</para>
    /// </summary>
    [TestFixture]
    public class RunLifecycleTests
    {
        private GameObject _go;
        private ProfileTelemetrySystem _sys;
        private InMemoryProfileDb _db;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _db = new InMemoryProfileDb();
            _go = new GameObject("Telemetry");
            _sys = _go.AddComponent<ProfileTelemetrySystem>();
            _sys.BindDb(_db);

            // Unity does not call OnEnable on a component added in Edit Mode, so the event
            // subscriptions never happen and every Fire* below would reach nobody. The fixture
            // that predates this one already says so in as many words; without the reflection
            // call these tests fail on a correct implementation.
            Invoke("OnEnable");
        }

        /// <summary>
        /// Calls a Unity message Edit Mode will not deliver. OnEnable and OnDestroy are BOTH in
        /// that set — a component whose Awake never ran never receives its teardown either — so
        /// the seam this suite exists to pin cannot be reached any other way here. It was proved
        /// in Play Mode separately: a real run came out with duration 6.85 s where 251 shipped
        /// rows before it had 0.
        /// </summary>
        private void Invoke(string message)
        {
            var m = typeof(ProfileTelemetrySystem).GetMethod(message,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "ProfileTelemetrySystem has no " + message);
            m.Invoke(_sys, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            GameEvents.Clear();
        }

        /// <summary>
        /// Destroying the system closes the run. It is the seam the fix rests on: the scene that
        /// holds the run going away IS the run ending, and a condition cannot be forgotten the way
        /// a call can.
        /// </summary>
        [Test]
        public void DestroyingTheSystem_ClosesTheOpenRun()
        {
            _sys.StartRun();
            var run = _sys.ActiveRun;
            Assert.IsNotNull(run);
            Assert.AreEqual(string.Empty, run.endedAtIso, "A fresh run is open.");

            Invoke("OnDestroy");

            Assert.IsNotEmpty(run.endedAtIso, "Leaving the scene must close the run.");
            Assert.AreEqual(1, _db.Profile.GetInt("total_runs"));

            // The DURATION is deliberately not asserted to be positive here. Edit Mode does not
            // advance Time.time between StartRun and the teardown, so both reads return the same
            // value and a correct implementation measures 0 — the same class of trap as reading
            // Time.deltaTime under execute_code, where nothing is rendering. What is provable
            // here is that the run was CLOSED and STAMPED; that it is stamped with a real length
            // was measured in Play Mode instead: run 252 came out at 6.85 s against 251 shipped
            // rows that all read 0.
            Assert.GreaterOrEqual(run.durationSeconds, 0f);
        }

        /// <summary>
        /// Closing twice must not count twice. Destroy, quit and the explicit event can all reach
        /// the same run, and a double-count is exactly how a lifetime total becomes wrong in a
        /// direction nobody can audit.
        /// </summary>
        [Test]
        public void ARun_CanOnlyBeClosedOnce()
        {
            _sys.StartRun();
            GameEvents.FireRunEnded();
            GameEvents.FireRunEnded();
            Invoke("OnDestroy");
            Invoke("OnApplicationQuit");

            Assert.AreEqual(1, _db.Profile.GetInt("total_runs"),
                "Destroy, quit and the explicit event all reach the same run.");
        }

        /// <summary>
        /// A kill is persisted as it happens. It used to be written only when the player died, so
        /// a run in which nobody died recorded nothing it had done.
        /// </summary>
        [Test]
        public void AKill_IsPersistedImmediately()
        {
            _sys.StartRun();
            var victim = NewHostile("barbol");
            try
            {
                GameEvents.FireEntityDied(victim, null);
                Assert.AreEqual(1, _sys.ActiveRun.totalKills);

                var stored = _db.Runs.GetAll();
                Assert.AreEqual(1, stored.Count);
                Assert.AreEqual(1, stored[0].totalKills,
                    "The row on disk must agree with the run in memory before anyone dies.");
            }
            finally { Object.DestroyImmediate(victim); }
        }

        /// <summary>
        /// Vendors are not trophies. Three of the seven rows of this machine's shipped kill board
        /// were shopkeepers, listed by their database key.
        /// </summary>
        [Test]
        public void ANeutral_IsNotCountedAsAKill()
        {
            _sys.StartRun();
            var vendor = NewEntity("vendor_blacksmith_smith", "NEUTRAL");
            try
            {
                GameEvents.FireEntityDied(vendor, null);
                Assert.AreEqual(0, _sys.ActiveRun.totalKills);
                Assert.AreEqual(0, _db.KillStats.GetTop(5).Count);
            }
            finally { Object.DestroyImmediate(vendor); }
        }

        /// <summary>An ally dying is a loss, not a kill.</summary>
        [Test]
        public void AnAlly_IsNotCountedAsAKill()
        {
            _sys.StartRun();
            var ally = NewEntity("summon_barbol", "GOOD");
            try
            {
                GameEvents.FireEntityDied(ally, null);
                Assert.AreEqual(0, _sys.ActiveRun.totalKills);
            }
            finally { Object.DestroyImmediate(ally); }
        }

        private static GameObject NewHostile(string name) => NewEntity(name, "EVIL");

        private static GameObject NewEntity(string name, string faction)
        {
            var go = new GameObject(name);
            var f = go.AddComponent<Valkur.Gameplay.EntityFaction>();
            f.SetAuthoredFaction(faction);
            return go;
        }
    }
}
