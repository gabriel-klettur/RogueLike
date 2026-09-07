using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins the NPC cast state lifecycle: enter zeroes velocity, execute holds it at zero, the
    /// state pops back to Chase / Attack when the spell's ACTION is over, and the safety timeout
    /// prevents an infinite freeze if the caster is misconfigured.
    ///
    /// <para>THE EXIT CONDITION CHANGED, AND THAT IS THE POINT OF HALF THESE TESTS. The state
    /// used to wait for <c>CurrentPhase == Ready</c>, which <c>SpellCaster</c> only reaches after
    /// <c>prepare + channel + cooldownDuration</c> — so a monster was rooted for the spell's
    /// whole rate limit. Measured live with 31 monsters on the field: 9 in this state at velocity
    /// 0.00, worst 10.3 s, three frozen simultaneously mid-<c>leap_slam</c>. It now leaves as
    /// soon as the caster is out of Prepare/Channel, and the cooldown is what it always should
    /// have been — a limit on the ABILITY, not a duration for the caster.</para>
    ///
    /// <para>What replaced the wait is a POSE FLOOR: half the shipped hostile spells author
    /// <c>prepare: 0, channel: 0</c>, so without one a monster would fire spells out of nowhere
    /// while walking. The tests below therefore tick past the floor rather than expecting an
    /// exit on the first frame, and one of them pins that the floor exists at all.</para>
    /// </summary>
    [TestFixture]
    public class NPCCastStateTests
    {
        private GameObject _npcGo;
        private GameObject _playerGo;
        private SpellCaster _caster;
        private Rigidbody2D _rb;
        private StateMachine _fsm;

        [SetUp]
        public void SetUp()
        {
            _npcGo  = new GameObject("NPC");
            _rb     = _npcGo.AddComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _caster = _npcGo.AddComponent<SpellCaster>();
            // Awake doesn't run in EditMode — prime cooldown array via reflection.
            var f = typeof(SpellCaster).GetField("_cooldownTimers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(_caster, new float[_caster.SlotCount]);

            _playerGo = new GameObject("Player");
            _playerGo.transform.position = new Vector3(10f, 0f, 0f);
            EntityRegistry.RegisterPlayer(_playerGo);

            _fsm = new StateMachine(_npcGo, new IdleState());
            _fsm.SetContext(FSMComponents.KEY, new FSMComponents(_npcGo));
            _fsm.SetContext("melee_range", 1.5f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) EntityRegistry.UnregisterPlayer(_playerGo);
            if (_npcGo    != null) Object.DestroyImmediate(_npcGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static void ForceCasterPhase(SpellCaster caster, SpellCaster.CastPhase phase)
        {
            var f = typeof(SpellCaster).GetField("_phase",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(caster, phase);
        }

        /// <summary>
        /// Ticks past the cast pose floor. Deliberately NOT a single big step: the state
        /// accumulates its own timer from the deltas it is given, and one 10-second tick would
        /// also clear the safety timeout, so a test using it could pass for the wrong reason.
        /// </summary>
        private void TickPastPose()
        {
            // Stops the moment the state pops, rather than ticking a fixed count. Ticking on
            // blindly runs whatever state was entered NEXT, and ChaseState correctly drops to
            // Patrol against this fixture's target — so the assertion would be about Chase's
            // behaviour rather than about the exit under test.
            for (int i = 0; i < 60 && _fsm.CurrentState is NPCCastState; i++)
                _fsm.Update(0.016f);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void Enter_ZeroesVelocity_AndPushesAnimToCast()
        {
            _rb.velocity = new Vector2(5f, 5f);

            _fsm.ChangeState(new NPCCastState());

            Assert.AreEqual(Vector2.zero, _rb.velocity,
                "Entering NPCCastState must stop NPC movement so the cast " +
                "telegraph aligns with the visible NPC position.");
            Assert.IsInstanceOf<NPCCastState>(_fsm.CurrentState);
        }

        [Test]
        public void Execute_HoldsVelocityAtZero_WhilePhaseIsNotReady()
        {
            _fsm.ChangeState(new NPCCastState());
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Channel);

            // Some external system tries to push the NPC.
            _rb.velocity = new Vector2(7f, 0f);
            _fsm.Update(0.016f);

            Assert.AreEqual(Vector2.zero, _rb.velocity,
                "NPCCastState must reassert zero velocity each tick — " +
                "otherwise external systems (knockback, AI) could move the " +
                "NPC mid-cast and break the projectile origin.");
            Assert.IsInstanceOf<NPCCastState>(_fsm.CurrentState,
                "While the caster is in Channel, NPCCastState must stay active.");
        }

        [Test]
        public void Execute_PopsToChase_WhenCasterReadyAndPlayerOutOfMeleeRange()
        {
            _playerGo.transform.position = new Vector3(10f, 0f, 0f);
            _fsm.ChangeState(new NPCCastState());

            // The spell's ACTION is over. Cooldown is irrelevant here now — Cooldown and Ready
            // both mean "not casting any more", and only Prepare/Channel hold the monster.
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Ready);
            TickPastPose();

            Assert.IsInstanceOf<ChaseState>(_fsm.CurrentState,
                "When the caster returns to Ready and the player is far, " +
                "the NPC must resume chase rather than idle/patrol — it was " +
                "aggro'd to begin a cast.");
        }

        [Test]
        public void Execute_PopsToAttack_WhenCasterReadyAndPlayerInMeleeRange()
        {
            _playerGo.transform.position = new Vector3(0.5f, 0f, 0f); // inside default 1.5 melee
            _fsm.ChangeState(new NPCCastState());
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Ready);

            TickPastPose();

            Assert.IsInstanceOf<AttackState>(_fsm.CurrentState,
                "If the player closed the gap during the cast, the NPC must " +
                "swing immediately on cast end rather than re-running chase.");
        }

        [Test]
        public void Cooldown_DoesNotHoldTheCaster()
        {
            // THE REGRESSION THIS FIXTURE EXISTS FOR. war_cry authors a 20 s cooldown; under the
            // old exit test a monster that cast it stood motionless for twenty seconds. Cooldown
            // must read exactly like Ready here: the spell has happened.
            _playerGo.transform.position = new Vector3(10f, 0f, 0f);
            _fsm.ChangeState(new NPCCastState());
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Cooldown);

            TickPastPose();

            Assert.IsInstanceOf<ChaseState>(_fsm.CurrentState,
                "A cooldown is a rate limit on the ABILITY, not a duration for the caster. " +
                "Holding the monster through it is what made every caster in the game a statue.");
        }

        [Test]
        public void ThePoseHoldsForAMoment_EvenWhenTheSpellWasInstant()
        {
            // Half the shipped hostile spells author prepare 0 / channel 0, so the action is over
            // one frame after it starts. Exiting on that frame would mean spells appearing out of
            // nowhere from a monster that never stopped walking.
            _fsm.ChangeState(new NPCCastState());
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Ready);

            _fsm.Update(0.016f);

            Assert.IsInstanceOf<NPCCastState>(_fsm.CurrentState,
                "There must be a readable cast pose even for an instant spell.");
        }

        [Test]
        public void ThePoseIsShort_NotAnotherFreeze()
        {
            // The floor is a tell, not a re-run of the defect it replaced. One second is far
            // above the authored floor and below anything a player would read as a hang.
            _playerGo.transform.position = new Vector3(10f, 0f, 0f);
            _fsm.ChangeState(new NPCCastState());
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Ready);

            for (int i = 0; i < 63; i++) _fsm.Update(0.016f);   // ~1.0 s

            Assert.IsNotInstanceOf<NPCCastState>(_fsm.CurrentState,
                "A cast pose longer than a second is the old freeze in a smaller size.");
        }

        [Test]
        public void Execute_DeathInterrupt_TransitionsToUnconscious()
        {
            var health = _npcGo.AddComponent<Health>();
            health.Initialize(10);
            // Re-build FSMComponents now that Health was added.
            _fsm.SetContext(FSMComponents.KEY, new FSMComponents(_npcGo));

            _fsm.ChangeState(new NPCCastState());
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Channel);

            health.TakeDamage(999);
            _fsm.Update(0.016f);

            Assert.IsInstanceOf<UnconsciousState>(_fsm.CurrentState,
                "Death during a cast must drop straight into Unconscious — " +
                "the safety timeout is a fallback, not the primary interrupt.");
        }

        [Test]
        public void Execute_SafetyTimeout_PopsToHostileEvenIfPhaseStuck()
        {
            _fsm.ChangeState(new NPCCastState());
            // Caster is stuck in Channel forever (misconfigured spell).
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Channel);

            // Past the 8 s deadlock breaker.
            for (int i = 0; i < 12; i++)
                _fsm.Update(1f);

            Assert.IsNotInstanceOf<NPCCastState>(_fsm.CurrentState,
                "After the safety timeout the NPC must escape NPCCastState " +
                "even if SpellCaster never advances — otherwise a buggy spell " +
                "freezes a monster permanently.");
        }
    }
}
