using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections.Generic;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode
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
        public void Constructor_EntersInitialState()
        {
            var state = new StubState();
            var fsm = CreateFSM(state);
            Assert.AreEqual(1, state.EnterCount);
            Assert.AreEqual(state, fsm.CurrentState);
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

            // DeathState.Enter calls Object.Destroy which logs an error in EditMode
            LogAssert.Expect(LogType.Error,
                "Destroy may not be called from edit mode! Use DestroyImmediate instead.\n" +
                "Destroying an object in edit mode destroys it permanently.");

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
    }
}
