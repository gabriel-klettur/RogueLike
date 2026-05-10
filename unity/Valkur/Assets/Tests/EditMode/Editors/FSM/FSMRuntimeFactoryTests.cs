using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// Tests <see cref="FSMRuntimeFactory"/> — the bridge that builds a
    /// runtime <see cref="StateMachine"/> from the JSON model that the
    /// FSM Editor (F12) saves to <c>StreamingAssets/FSM/</c>.
    ///
    /// These tests exercise the factory against the real seed JSON committed
    /// in Phase 1. They focus on the observable contract: returning false
    /// (never throwing) for unknown archetypes, returning a wired-up
    /// StateMachine for the known <c>barbol</c> archetype, and recovering
    /// gracefully when the JSON files are absent.
    /// </summary>
    [TestFixture]
    public class FSMRuntimeFactoryTests
    {
        private GameObject _owner;

        [SetUp]
        public void SetUp()
        {
            _owner = new GameObject("FSMFactoryTestOwner");
            FSMRuntimeFactory.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            if (_owner != null) Object.DestroyImmediate(_owner);
            FSMRuntimeFactory.InvalidateCache();
        }

        // ── Happy path: real seed JSON ──────────────────────────────────────────

        [Test]
        public void TryBuildForArchetype_BuildsStateMachine_ForSeededArchetype()
        {
            // 'barbol' was emitted by FSMSeedGenerator (committed in Phase 1).
            // Skip if the seed has been deleted (e.g. clean checkout that
            // hasn't run the generator yet) — the factory still must not
            // throw, only return false.
            if (!FSMRuntimeFactory.HasSetForArchetype("barbol"))
                Assert.Ignore("StreamingAssets/FSM/assignments.json missing 'barbol' — " +
                              "run Valkur > FSM > Generate Seed to regenerate.");

            bool ok = FSMRuntimeFactory.TryBuildForArchetype("barbol", _owner, out var fsm);

            Assert.IsTrue(ok,           "Factory must succeed for a seeded archetype.");
            Assert.IsNotNull(fsm,       "Factory must return a non-null StateMachine.");
            Assert.AreSame(_owner, fsm.Owner, "StateMachine.Owner must be the GameObject we passed in.");
            Assert.IsNotNull(fsm.CurrentState, "Initial state must be entered before TryBuild returns.");
            Assert.AreEqual("IdleState", fsm.CurrentState.GetType().Name,
                "Monster_Default declares IdleState as its initial state.");
        }

        [Test]
        public void TryBuildForArchetype_AppliesAllowedStatesGuard()
        {
            if (!FSMRuntimeFactory.HasSetForArchetype("barbol")) Assert.Ignore("Seed missing.");

            FSMRuntimeFactory.TryBuildForArchetype("barbol", _owner, out var fsm);

            // The guard is private — verify by attempting an illegal transition.
            // A non-special state outside the allowed set must be silently rejected.
            var fakeState = new FakeForbiddenState();
            fsm.ChangeState(fakeState);
            Assert.AreNotSame(fakeState, fsm.CurrentState,
                "SetAllowedStates must reject states absent from the JSON set vocabulary.");

            // The 'special' states (DamageState/DeathState/UnconsciousState) bypass
            // the guard — verify DeathState transitions through cleanly.
            var death = new DeathState();
            fsm.ChangeState(death);
            Assert.AreSame(death, fsm.CurrentState,
                "DeathState must always be allowed regardless of the allowed-set guard.");
        }

        // ── Negative paths: every failure mode returns false, never throws ──────

        [Test]
        public void TryBuildForArchetype_ReturnsFalse_ForNullArchetype()
        {
            Assert.DoesNotThrow(() =>
            {
                bool ok = FSMRuntimeFactory.TryBuildForArchetype(null, _owner, out var fsm);
                Assert.IsFalse(ok);
                Assert.IsNull(fsm);
            });
        }

        [Test]
        public void TryBuildForArchetype_ReturnsFalse_ForEmptyArchetype()
        {
            bool ok = FSMRuntimeFactory.TryBuildForArchetype("", _owner, out var fsm);
            Assert.IsFalse(ok);
            Assert.IsNull(fsm);
        }

        [Test]
        public void TryBuildForArchetype_ReturnsFalse_ForNullOwner()
        {
            bool ok = FSMRuntimeFactory.TryBuildForArchetype("barbol", null, out var fsm);
            Assert.IsFalse(ok);
            Assert.IsNull(fsm);
        }

        [Test]
        public void TryBuildForArchetype_ReturnsFalse_ForUnknownArchetype()
        {
            bool ok = FSMRuntimeFactory.TryBuildForArchetype(
                "definitely_not_a_real_monster_xyz", _owner, out var fsm);
            Assert.IsFalse(ok, "Unknown archetype must fall through to caller's hard-coded fallback.");
            Assert.IsNull(fsm);
        }

        [Test]
        public void HasSetForArchetype_ReturnsFalse_ForUnknownArchetype()
        {
            Assert.IsFalse(FSMRuntimeFactory.HasSetForArchetype("definitely_not_a_real_monster_xyz"));
        }

        [Test]
        public void HasSetForArchetype_ReturnsFalse_ForNullKey()
        {
            Assert.IsFalse(FSMRuntimeFactory.HasSetForArchetype(null));
        }

        // ── IsLoaded contract ───────────────────────────────────────────────────

        [Test]
        public void IsLoaded_BecomesTrueAfterFirstUse()
        {
            // Cache was invalidated in SetUp.
            Assert.IsFalse(FSMRuntimeFactory.IsLoaded,
                "Pre-condition: IsLoaded must be false before any call.");

            FSMRuntimeFactory.HasSetForArchetype("anything"); // triggers EnsureLoaded

            Assert.IsTrue(FSMRuntimeFactory.IsLoaded,
                "After first call, IsLoaded must report true (or false on parse error — " +
                "but the seeded JSON committed in Phase 1 must parse cleanly).");
        }

        [Test]
        public void InvalidateCache_ForcesReload()
        {
            FSMRuntimeFactory.HasSetForArchetype("anything");
            Assert.IsTrue(FSMRuntimeFactory.IsLoaded);

            FSMRuntimeFactory.InvalidateCache();

            Assert.IsFalse(FSMRuntimeFactory.IsLoaded,
                "InvalidateCache must reset the loaded flag so the next call re-reads disk.");
        }

        // ── Test fixtures ───────────────────────────────────────────────────────

        /// <summary>
        /// A test-only IState that is intentionally NOT in the Monster_Default
        /// vocabulary — used to confirm the SetAllowedStates guard rejects it.
        /// </summary>
        private sealed class FakeForbiddenState : IState
        {
            public void Enter(StateMachine fsm)              { }
            public void Execute(StateMachine fsm, float dt)  { }
            public void Exit(StateMachine fsm)               { }
        }
    }
}
