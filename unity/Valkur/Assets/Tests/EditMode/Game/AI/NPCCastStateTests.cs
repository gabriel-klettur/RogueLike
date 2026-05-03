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
    /// Pins the NPC cast state lifecycle: enter zeroes velocity, execute
    /// holds it at zero, the state pops back to Chase / Attack when the
    /// SpellCaster returns to Ready, and the safety timeout prevents
    /// infinite freezes if the caster is misconfigured. Without these
    /// invariants, NPCs that start a cast can either keep walking
    /// (visual bug) or freeze forever (logic bug).
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

            // Cooldown finished — caster signals ready.
            ForceCasterPhase(_caster, SpellCaster.CastPhase.Ready);
            _fsm.Update(0.016f);

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

            _fsm.Update(0.016f);

            Assert.IsInstanceOf<AttackState>(_fsm.CurrentState,
                "If the player closed the gap during the cast, the NPC must " +
                "swing immediately on cast end rather than re-running chase.");
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

            // Drive enough simulated time to exceed the 30s cap.
            for (int i = 0; i < 35; i++)
                _fsm.Update(1f);

            Assert.IsNotInstanceOf<NPCCastState>(_fsm.CurrentState,
                "After the safety timeout the NPC must escape NPCCastState " +
                "even if SpellCaster never advances — otherwise a buggy spell " +
                "freezes a monster permanently.");
        }
    }
}
