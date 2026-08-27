using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins the per-monster feel knobs and the "0 means engine default" contract they rest
    /// on.
    ///
    /// These were eleven <c>private const float</c> spread across the state classes, two of
    /// them duplicated verbatim between <c>ChaseState</c> and <c>AlertChaseState</c>. A
    /// designer could not reach any of them, so aggro hysteresis, repath cadence, leash
    /// length, flee timing and re-swing reach were identical for every monster in the game.
    ///
    /// The riskiest part of moving them is silent regression: publish a 0 instead of
    /// publishing nothing and every monster's hysteresis, repath interval and flee duration
    /// become zero. That is what most of this fixture guards.
    /// </summary>
    [TestFixture]
    public class FSMTuningTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("tuning-probe");
            _go.AddComponent<Rigidbody2D>().gravityScale = 0f;
            _go.AddComponent<Health>().Initialize(100);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private StateMachine Machine()
        {
            var fsm = new StateMachine(_go, new IdleState());
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(_go));
            return fsm;
        }

        // ── Defaults ────────────────────────────────────────────────────────────

        [Test]
        public void UnsetKnobs_ResolveToTheConstantsTheStatesUsedToHold()
        {
            var fsm = Machine();

            Assert.AreEqual(1.15f, FSMTuning.AggroExitHysteresis(fsm), 0.0001f);
            Assert.AreEqual(0.5f,  FSMTuning.RepathInterval(fsm),      0.0001f);
            Assert.AreEqual(0.25f, FSMTuning.WaypointReachDistance(fsm), 0.0001f);
            Assert.AreEqual(5f,    FSMTuning.AlertDuration(fsm),       0.0001f);
            Assert.AreEqual(3f,    FSMTuning.FleeDuration(fsm),        0.0001f);
            Assert.AreEqual(1.5f,  FSMTuning.FleeSpeedMultiplier(fsm), 0.0001f);
            Assert.AreEqual(1.5f,  FSMTuning.ReswingRangeFactor(fsm),  0.0001f);
        }

        [Test]
        public void AuthoredKnob_OverridesTheDefault()
        {
            var fsm = Machine();
            fsm.SetContext(FSMTuning.KeyRepathInterval, 0.1f);

            Assert.AreEqual(0.1f, FSMTuning.RepathInterval(fsm), 0.0001f);
            Assert.AreEqual(5f, FSMTuning.AlertDuration(fsm), 0.0001f,
                "overriding one knob must not disturb the others");
        }

        // ── The leash derives from the monster's own reach ───────────────────────

        [Test]
        public void Leash_WithNothingAuthored_ScalesWithAggroRange()
        {
            var fsm = Machine();

            Assert.AreEqual(30f, FSMTuning.LeashRange(fsm, aggroRange: 10f), 0.0001f,
                "A wide-ranging monster must get a correspondingly long tether without " +
                "anyone authoring two numbers that have to agree.");
        }

        [Test]
        public void Leash_AuthoredValueWins_RegardlessOfAggroRange()
        {
            var fsm = Machine();
            fsm.SetContext(FSMTuning.KeyLeashRange, 12f);

            Assert.AreEqual(12f, FSMTuning.LeashRange(fsm, aggroRange: 30f), 0.0001f);
        }

        // ── The zero-means-default contract ─────────────────────────────────────

        [Test]
        public void PublishingZero_WouldBreakEveryMonster_SoTheBrainMustNotDoIt()
        {
            // This is the failure mode the publish helper exists to avoid, written out so
            // the reason is testable rather than only commented: a knob explicitly set to 0
            // resolves to 0, NOT to the default. Hence FSMMonsterBrain publishes nothing
            // for an unset field instead of publishing its zero.
            var fsm = Machine();
            fsm.SetContext(FSMTuning.KeyAggroExitHysteresis, 0f);

            Assert.AreEqual(0f, FSMTuning.AggroExitHysteresis(fsm), 0.0001f,
                "A published 0 must read back as 0 — which is exactly why an UNSET knob " +
                "must never be published.");
        }

        // ── The flinch resumes what it interrupted ──────────────────────────────

        [Test]
        public void DamageState_ReturnsToTheStateItInterrupted()
        {
            var fsm = Machine();
            fsm.SetContext("damage_stop_probability", 1f);   // always flinch
            fsm.SetContext("damage_duration", 0.1f);

            fsm.ChangeState(new PatrolState());
            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit, Damage = 1 });
            fsm.Update(0.016f);

            Assert.IsInstanceOf<DamageState>(fsm.CurrentState, "the hit must land as a flinch");

            fsm.Update(0.2f);   // outlast the flinch

            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState,
                "A patrolling vendor clipped by a stray area spell used to start CHASING, " +
                "because DamageState's only exit was ChaseState.");
        }

        [Test]
        public void DamageState_WithNoRememberedState_StillFallsBackToChase()
        {
            var fsm = Machine();

            // Constructed directly, the way older callers did, with nothing to resume.
            fsm.ChangeState(new DamageState(0.1f, fromLeft: false));
            fsm.Update(0.2f);

            Assert.IsInstanceOf<ChaseState>(fsm.CurrentState,
                "the historical behaviour has to survive as the fallback");
        }
    }
}
