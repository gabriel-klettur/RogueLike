using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    public class FSMTests
    {
        // --- Stub states for testing ---
        private class StubState : IState
        {
            public int EnterCount;
            public int ExitCount;
            public int ExecuteCount;
            public float TotalDt;

            public void Enter(StateMachine fsm) => EnterCount++;
            public void Execute(StateMachine fsm, float dt) { ExecuteCount++; TotalDt += dt; }
            public void Exit(StateMachine fsm) => ExitCount++;
        }

        private class AutoTransitionState : IState
        {
            private readonly IState _next;
            public AutoTransitionState(IState next) => _next = next;
            public void Enter(StateMachine fsm) { }
            public void Execute(StateMachine fsm, float dt) => fsm.ChangeState(_next);
            public void Exit(StateMachine fsm) { }
        }

        private StateMachine CreateFSM(IState initial)
        {
            var go = new GameObject("FSMOwner");
            var fsm = new StateMachine(go, initial);
            return fsm;
        }

        private void Cleanup(StateMachine fsm)
        {
            Object.DestroyImmediate(fsm.Owner);
        }

        // --- Initialization ---

        [Test]
        public void Constructor_InstallsInitialState_ButDefersEnteringIt()
        {
            // Enter() is where a state reads the context, and every caller publishes
            // the context AFTER construction — so entering in the constructor ran the
            // initial state against an empty dictionary. See StateMachine.Begin().
            var state = new StubState();
            var fsm = CreateFSM(state);
            Assert.AreEqual(state, fsm.CurrentState, "the state is installed immediately");
            Assert.AreEqual(0, state.EnterCount, "but not entered until the context exists");
            Assert.IsTrue(fsm.IsInitialEnterPending);
            Cleanup(fsm);
        }

        [Test]
        public void Begin_EntersInitialState_AndIsIdempotent()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);

            fsm.Begin();
            Assert.AreEqual(1, state.EnterCount);
            Assert.IsFalse(fsm.IsInitialEnterPending);

            fsm.Begin();
            fsm.Update(0.016f);
            Assert.AreEqual(1, state.EnterCount,
                "a second Begin, and the Update safety net, must not re-enter");
            Cleanup(fsm);
        }

        [Test]
        public void Update_EntersInitialState_WhenBeginWasNeverCalled()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);

            fsm.Update(0.016f);

            Assert.AreEqual(1, state.EnterCount,
                "a caller that forgets Begin() still gets the entry, one tick late " +
                "rather than never");
            Cleanup(fsm);
        }

        [Test]
        public void ChangeState_BeforeBegin_DoesNotExitAStateThatNeverEntered()
        {
            var initial = new StubState();
            var next    = new StubState();
            var fsm     = CreateFSM(initial);

            fsm.ChangeState(next);

            Assert.AreEqual(0, initial.ExitCount,
                "Exit would tear down setup Enter never performed");
            Assert.AreEqual(0, initial.EnterCount);
            Assert.AreEqual(1, next.EnterCount);
            Assert.IsFalse(fsm.IsInitialEnterPending);
            Cleanup(fsm);
        }

        [Test]
        public void Constructor_SetsOwner()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);
            Assert.IsNotNull(fsm.Owner);
            Cleanup(fsm);
        }

        // --- State Transitions ---

        [Test]
        public void ChangeState_ExitsOldAndEntersNew()
        {
            var stateA = new StubState();
            var stateB = new StubState();
            var fsm = CreateFSM(stateA);
            fsm.Begin();   // stateA has to be live before leaving it means anything

            fsm.ChangeState(stateB);

            Assert.AreEqual(1, stateA.ExitCount);
            Assert.AreEqual(1, stateB.EnterCount);
            Assert.AreEqual(stateB, fsm.CurrentState);
            Cleanup(fsm);
        }

        [Test]
        public void ChangeState_Null_DoesNothing()
        {
            var stateA = new StubState();
            var fsm = CreateFSM(stateA);
            fsm.ChangeState(null);
            Assert.AreEqual(stateA, fsm.CurrentState);
            Assert.AreEqual(0, stateA.ExitCount);
            Cleanup(fsm);
        }

        [Test]
        public void ChangeState_FiresOnStateChangedEvent()
        {
            var stateA = new StubState();
            var stateB = new StubState();
            var fsm = CreateFSM(stateA);

            IState firedOld = null, firedNew = null;
            fsm.OnStateChanged += (old, @new) => { firedOld = old; firedNew = @new; };
            fsm.ChangeState(stateB);

            Assert.AreEqual(stateA, firedOld);
            Assert.AreEqual(stateB, firedNew);
            Cleanup(fsm);
        }

        // --- Update ---

        [Test]
        public void Update_ExecutesCurrentState()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);
            fsm.Update(0.016f);
            fsm.Update(0.016f);
            Assert.AreEqual(2, state.ExecuteCount);
            Assert.AreEqual(0.032f, state.TotalDt, 0.001f);
            Cleanup(fsm);
        }

        [Test]
        public void Update_TransitionDuringExecute_WorksCorrectly()
        {
            var stateB = new StubState();
            var stateA = new AutoTransitionState(stateB);
            var fsm = CreateFSM(stateA);

            fsm.Update(0.016f);
            Assert.AreEqual(stateB, fsm.CurrentState);
            Cleanup(fsm);
        }

        // --- Guards (Allowed States) ---

        [Test]
        public void AllowedStates_BlocksDisallowedTransition()
        {
            var stateA = new StubState();
            var stateB = new StubState();
            var fsm = CreateFSM(stateA);

            fsm.SetAllowedStates(new HashSet<string> { "StubState" });
            // stateB is also StubState, so it should be allowed
            fsm.ChangeState(stateB);
            Assert.AreEqual(stateB, fsm.CurrentState);
            Cleanup(fsm);
        }

        [Test]
        public void AllowedStates_DeathState_AlwaysAllowed()
        {
            var stateA = new StubState();
            var fsm = CreateFSM(stateA);

            // Only allow StubState — but DeathState should bypass
            fsm.SetAllowedStates(new HashSet<string> { "StubState" });
            var deathState = new DeathState();

            fsm.ChangeState(deathState);
            Assert.AreEqual(deathState, fsm.CurrentState);
            Cleanup(fsm);
        }

        [Test]
        public void AllowedStates_DamageState_AlwaysAllowed()
        {
            var stateA = new StubState();
            var fsm = CreateFSM(stateA);

            fsm.SetAllowedStates(new HashSet<string> { "StubState" });
            var damageState = new DamageState(0.5f, false);
            fsm.ChangeState(damageState);
            Assert.AreEqual(damageState, fsm.CurrentState);
            Cleanup(fsm);
        }

        [Test]
        public void AllowedStates_NullAllowed_AllTransitionsPass()
        {
            var stateA = new StubState();
            var stateB = new StubState();
            var fsm = CreateFSM(stateA);

            fsm.SetAllowedStates(null);
            fsm.ChangeState(stateB);
            Assert.AreEqual(stateB, fsm.CurrentState);
            Cleanup(fsm);
        }

        // --- Context ---

        [Test]
        public void Context_SetAndGet_WorksCorrectly()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);

            fsm.SetContext("speed", 5.5f);
            fsm.SetContext("name", "goblin");
            fsm.SetContext("aggressive", true);

            Assert.AreEqual(5.5f, fsm.GetContextFloat("speed"), 0.001f);
            Assert.AreEqual("goblin", fsm.GetContext<string>("name"));
            Assert.IsTrue(fsm.GetContextBool("aggressive"));
            Cleanup(fsm);
        }

        [Test]
        public void Context_MissingKey_ReturnsDefault()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);

            Assert.AreEqual(0f, fsm.GetContextFloat("missing"));
            Assert.AreEqual(42f, fsm.GetContextFloat("missing", 42f));
            Assert.IsFalse(fsm.GetContextBool("missing"));
            Assert.IsNull(fsm.GetContext<string>("missing"));
            Cleanup(fsm);
        }

        [Test]
        public void Context_IntToFloat_Conversion()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);

            fsm.SetContext("intVal", 10);
            Assert.AreEqual(10f, fsm.GetContextFloat("intVal"), 0.001f);
            Cleanup(fsm);
        }

        // --- Event Queue ---

        [Test]
        public void QueueEvent_OnDeath_TransitionsToUnconsciousState()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnDeath });
            fsm.Update(0.016f);

            Assert.IsInstanceOf<UnconsciousState>(fsm.CurrentState);
            Cleanup(fsm);
        }

        // --- Double flinch ---

        [Test]
        public void SecondFlinch_PreservesTheOriginalInterruptedState()
        {
            // A hit landing DURING a flinch used to capture "DamageState" as the state to
            // resume, which the factory cannot construct (three-parameter constructor), so
            // the resume silently degraded into ChaseState: a patrolling monster hit twice
            // in half a second came out of the stagger hunting. The second flinch must
            // restart the stagger but carry the ORIGINAL interrupted state forward.
            var fsm = CreateFSM(new PatrolState());
            fsm.SetContext("damage_stop_probability", 1f); // every hit wins the roll
            fsm.SetContext("damage_duration", 10f);        // stagger outlives the test
            fsm.Begin();

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit });
            fsm.Update(0.016f);
            var first = fsm.CurrentState as DamageState;
            Assert.IsNotNull(first, "the first hit must flinch");
            Assert.AreEqual(nameof(PatrolState), first.ReturnStateClass);

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit });
            fsm.Update(0.016f);
            var second = fsm.CurrentState as DamageState;
            Assert.IsNotNull(second, "the second hit re-flinches");
            Assert.AreNotSame(first, second, "a fresh stagger, not the old instance");
            Assert.AreEqual(nameof(PatrolState), second.ReturnStateClass,
                "the resume target is what the FIRST flinch interrupted, never DamageState");
            Cleanup(fsm);
        }

        [Test]
        public void FlinchResume_ReturnsToTheInterruptedState()
        {
            var fsm = CreateFSM(new PatrolState());
            fsm.SetContext("damage_stop_probability", 1f);
            fsm.SetContext("damage_duration", 0.05f);
            fsm.Begin();

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit });
            fsm.Update(0.016f);
            Assert.IsInstanceOf<DamageState>(fsm.CurrentState);

            // One tick past the stagger. PatrolState's own Execute then runs and may move
            // on, so assert the RESUME, not the state after a full extra frame: the
            // DamageState instance is gone and what replaced it is a fresh PatrolState.
            fsm.Update(0.1f);
            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState,
                "the flinch must resume what it interrupted, not fall back to ChaseState");
            Cleanup(fsm);
        }

    }
}
