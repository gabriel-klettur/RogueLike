using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins the authored-transition path: the half of the F12 editor that used to be a
    /// drawing.
    ///
    /// Before this, <c>FSMRuntimeFactory</c> read exactly two things out of
    /// <c>sets.json</c> — the initial state name and the list of state ids — and the word
    /// <c>transitions</c> appeared nowhere in the runtime. A designer could wire Chase to
    /// Flee with a condition and a cooldown, save it, and get byte-identical gameplay.
    ///
    /// Transitions are ADDITIVE: evaluated before the current state's Execute, first
    /// passing guard wins, and a machine with none behaves exactly as it always did.
    /// That last property is the one most worth protecting, so it gets its own test.
    /// </summary>
    [TestFixture]
    public class FSMAuthoredTransitionTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("fsm-transition-probe");
            _go.AddComponent<Rigidbody2D>().gravityScale = 0f;
            // MUST be initialised: an uninitialised Health has MaxHp 0, reads as dead, and
            // every real state routes a dead entity straight to UnconsciousState — so the
            // probe would leave the state under test before the assertion ran.
            _go.AddComponent<Health>().Initialize(100);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        private StateMachine MakeMachine(IState initial)
        {
            var fsm = new StateMachine(_go, initial);
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(_go));
            return fsm;
        }

        private static FSMTransition Edge(string from, string to, string guard,
                                          int priority = 0, float cooldown = 0f)
        {
            var cond = FSMCondition.Parse(guard, out string error);
            Assert.IsNull(error, "fixture guard must parse: " + error);
            return new FSMTransition(from, to, cond, priority, cooldown, guard);
        }

        // ── Grammar ─────────────────────────────────────────────────────────────

        [Test]
        public void Parse_EmptyGuard_IsUnconditional_NotAnError()
        {
            Assert.IsNull(FSMCondition.Parse("", out string error));
            Assert.IsNull(error, "an empty guard is a legal unconditional edge");
        }

        [Test]
        public void Parse_Garbage_ReportsAnError_RatherThanBecomingAlwaysTrue()
        {
            var cond = FSMCondition.Parse("hp_pct is small", out string error);

            Assert.IsNull(cond);
            Assert.IsNotNull(error,
                "A mistyped guard treated as 'always true' would fire every frame, which " +
                "reads as a broken FSM rather than as a typo.");
        }

        [Test]
        public void Parse_TwoCharacterOperator_IsNotSplitByItsFirstCharacter()
        {
            var cond = FSMCondition.Parse("state_time >= 0", out string error);
            Assert.IsNull(error);
            Assert.IsNotNull(cond);
        }

        [Test]
        public void Evaluate_HpPct_ReadsTheLiveHealth()
        {
            var health = _go.GetComponent<Health>();
            health.Initialize(100);
            var fsm = MakeMachine(new IdleState());

            Assert.IsTrue(FSMCondition.Parse("hp_pct > 0.9", out _).Evaluate(fsm));

            health.TakeDamage(80, null);

            Assert.IsTrue(FSMCondition.Parse("hp_pct < 0.25", out _).Evaluate(fsm));
            Assert.IsFalse(FSMCondition.Parse("hp_pct > 0.9", out _).Evaluate(fsm));
        }

        [Test]
        public void Evaluate_UnknownTerm_FallsThroughToTheFsmContext()
        {
            var fsm = MakeMachine(new IdleState());
            fsm.SetContext("aggro_range", 10f);

            // Both sides resolve from context/literals, which is what makes
            // "distance_to_player > aggro_range" expressible at all.
            Assert.IsTrue(FSMCondition.Parse("aggro_range == 10", out _).Evaluate(fsm));
            Assert.IsFalse(FSMCondition.Parse("aggro_range < 5", out _).Evaluate(fsm));
        }

        [Test]
        public void Evaluate_ConjunctionRequiresEveryClause()
        {
            _go.GetComponent<Health>().Initialize(100);
            var fsm = MakeMachine(new IdleState());
            fsm.SetContext("aggro_range", 10f);

            Assert.IsTrue(FSMCondition.Parse("hp_pct > 0.5 && aggro_range == 10", out _).Evaluate(fsm));
            Assert.IsFalse(FSMCondition.Parse("hp_pct > 0.5 && aggro_range == 99", out _).Evaluate(fsm));
        }

        // ── StateMachine integration ────────────────────────────────────────────

        [Test]
        public void NoAuthoredTransitions_LeavesBehaviourUnchanged()
        {
            var fsm = MakeMachine(new IdleState());

            Assert.IsFalse(fsm.HasAuthoredTransitions);
            fsm.Update(0.1f);

            Assert.IsInstanceOf<IdleState>(fsm.CurrentState,
                "With nothing authored the machine must behave exactly as before.");
        }

        [Test]
        public void PassingGuard_TakesTheAuthoredEdge()
        {
            _go.GetComponent<Health>().Initialize(100);
            _go.GetComponent<Health>().TakeDamage(90, null);

            var fsm = MakeMachine(new IdleState());
            fsm.SetTransitions(new[] { Edge("IdleState", "FleeState", "hp_pct < 0.25") });

            fsm.Update(0.1f);

            Assert.IsInstanceOf<FleeState>(fsm.CurrentState);
        }

        [Test]
        public void FailingGuard_LeavesTheStateAlone()
        {
            _go.GetComponent<Health>().Initialize(100);

            var fsm = MakeMachine(new IdleState());
            fsm.SetTransitions(new[] { Edge("IdleState", "FleeState", "hp_pct < 0.25") });

            fsm.Update(0.1f);

            Assert.IsInstanceOf<IdleState>(fsm.CurrentState);
        }

        [Test]
        public void GlobalEdge_AppliesFromAnyState()
        {
            _go.GetComponent<Health>().Initialize(100);
            _go.GetComponent<Health>().TakeDamage(90, null);

            var fsm = MakeMachine(new IdleState());
            fsm.SetTransitions(new[] { Edge("*", "FleeState", "hp_pct < 0.25") });

            fsm.Update(0.1f);

            Assert.IsInstanceOf<FleeState>(fsm.CurrentState);
        }

        [Test]
        public void FirstMatchingEdgeWins_InTheOrderItWasGiven()
        {
            var fsm = MakeMachine(new IdleState());
            // The factory sorts by descending priority before handing them over, so the
            // machine's contract is "first match in the given order".
            fsm.SetTransitions(new[]
            {
                Edge("IdleState", "PatrolState", "", priority: 100),
                Edge("IdleState", "FleeState",   "", priority: 0),
            });

            fsm.Update(0.1f);

            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState);
        }

        [Test]
        public void Cooldown_BlocksTheEdgeUntilItElapses()
        {
            var fsm = MakeMachine(new IdleState());
            fsm.SetTransitions(new[]
            {
                Edge("IdleState",   "PatrolState", "", cooldown: 1f),
                Edge("PatrolState", "IdleState",   ""),
            });

            fsm.Update(0.1f);
            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState, "first hop");

            fsm.Update(0.1f);
            Assert.IsInstanceOf<IdleState>(fsm.CurrentState, "second hop back");

            // Idle -> Patrol is now resting, so the machine must stay put.
            fsm.Update(0.1f);
            Assert.IsInstanceOf<IdleState>(fsm.CurrentState,
                "the cooled-down edge must not re-fire immediately");

            fsm.Update(1.5f);
            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState,
                "and must fire again once the cooldown has elapsed");
        }

        [Test]
        public void CorpseStates_AreNotSteerable()
        {
            var fsm = MakeMachine(new UnconsciousState());
            fsm.SetTransitions(new[] { Edge("*", "IdleState", "") });

            fsm.Update(0.1f);

            Assert.IsInstanceOf<UnconsciousState>(fsm.CurrentState,
                "An authored edge out of a corpse would resurrect it mid-despawn.");
        }

        // ── Signals added for AlertChase and the leash ──────────────────────────

        [Test]
        public void TimeSinceHit_StartsAtTheCap_AndResetsOnDamage()
        {
            var fsm = MakeMachine(new IdleState());
            Assert.AreEqual(StateMachine.HitMemorySeconds, fsm.TimeSinceLastHit, 0.001f,
                "an entity that has never been hit must not look like it was just hit");

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit, Damage = 1 });
            fsm.Update(0.016f);

            Assert.Less(fsm.TimeSinceLastHit, 0.1f);
        }

        [Test]
        public void TimeSinceHit_IsStamped_EvenWhenTheFlinchRollFails()
        {
            // The roll decides whether the entity staggers. Whether it noticed being shot
            // is a different question, and the retaliation guard needs the second one —
            // barbol ships damageStopProbability 0.1, so nine hits in ten never flinch.
            var fsm = MakeMachine(new IdleState());
            fsm.SetContext("damage_stop_probability", 0f);   // never flinch

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit, Damage = 1 });
            fsm.Update(0.016f);

            Assert.IsInstanceOf<IdleState>(fsm.CurrentState, "fixture must not have flinched");
            Assert.Less(fsm.TimeSinceLastHit, 0.1f, "but the hit must still be remembered");
        }

        [Test]
        public void TimeSinceHit_IsUsableAsAGuard()
        {
            var fsm = MakeMachine(new IdleState());
            fsm.SetContext("damage_stop_probability", 0f);
            fsm.SetTransitions(new[] { Edge("*", "FleeState", "time_since_hit < 0.5") });

            fsm.Update(0.016f);
            Assert.IsInstanceOf<IdleState>(fsm.CurrentState, "never hit — must not fire");

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit, Damage = 1 });
            fsm.Update(0.016f);

            Assert.IsInstanceOf<FleeState>(fsm.CurrentState);
        }

        [Test]
        public void DistanceFromHome_IsZero_WhenNoAnchorWasPublished()
        {
            // A guard on an absent signal must never fire, rather than firing constantly.
            var fsm = MakeMachine(new IdleState());
            _go.transform.position = new Vector3(50f, 50f, 0f);

            Assert.IsFalse(FSMCondition.Parse("distance_from_home > 1", out _).Evaluate(fsm));
        }

        [Test]
        public void DistanceFromHome_MeasuresFromTheSpawnAnchor()
        {
            var fsm = MakeMachine(new IdleState());
            fsm.SetContext(FSMHomeAnchor.KeyX, 0f);
            fsm.SetContext(FSMHomeAnchor.KeyY, 0f);
            _go.transform.position = new Vector3(3f, 4f, 0f);   // 5 units out

            Assert.IsTrue(FSMCondition.Parse("distance_from_home > 4.9", out _).Evaluate(fsm));
            Assert.IsFalse(FSMCondition.Parse("distance_from_home > 5.1", out _).Evaluate(fsm));
        }

        [Test]
        public void TimeInCurrentState_ResetsOnEveryChange()
        {
            var fsm = MakeMachine(new IdleState());
            fsm.Update(0.5f);
            Assert.AreEqual(0.5f, fsm.TimeInCurrentState, 0.001f);

            fsm.ChangeState(new PatrolState());
            Assert.AreEqual(0f, fsm.TimeInCurrentState, 0.001f);
        }
    }
}
