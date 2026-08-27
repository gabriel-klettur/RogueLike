using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Pre-resolved component references for FSM states.
    /// Cached once in StateMachine initialization to avoid per-frame GetComponent calls.
    /// States retrieve this via fsm.GetContext&lt;FSMComponents&gt;("components").
    /// </summary>
    public class FSMComponents
    {
        public readonly Rigidbody2D Rb;
        public readonly Health Health;
        public readonly MeleeCombat Combat;
        public readonly SpriteRenderer Sprite;
        public readonly DirectionalAnimator Animator;

        private readonly GameObject _owner;

        // StatusEffectManager and CombatFeedback are added by
        // EntitySetup.ConfigureMonster AFTER it calls brain.Initialize(def), which is
        // what builds this object — so resolving them in the constructor would cache
        // null for every monster in the game. Resolve once, lazily, on the first FSM
        // tick (by which point ConfigureMonster has finished) and never look again.
        private bool _extrasResolved;
        private StatusEffectManager _status;
        private CombatFeedback _feedback;

        public FSMComponents(GameObject owner)
        {
            _owner = owner;
            Rb = owner.GetComponent<Rigidbody2D>();
            Health = owner.GetComponent<Health>();
            Combat = owner.GetComponent<MeleeCombat>();
            Sprite = owner.GetComponentInChildren<SpriteRenderer>();
            Animator = owner.GetComponent<DirectionalAnimator>();
        }

        public const string KEY = "components";

        /// <summary>Status effect hub, resolved lazily. Null on entities that have none.</summary>
        public StatusEffectManager Status { get { ResolveExtras(); return _status; } }

        /// <summary>Hit feedback (flash + knockback), resolved lazily.</summary>
        public CombatFeedback Feedback { get { ResolveExtras(); return _feedback; } }

        /// <summary>True while a stun is active on this entity.</summary>
        public bool IsStunned
        {
            get
            {
                var s = Status;
                return s != null && s.IsStunned;
            }
        }

        /// <summary>True while a knockback impulse should still be carrying the body.</summary>
        public bool KnockbackActive
        {
            get
            {
                var f = Feedback;
                return f != null && f.KnockbackActive;
            }
        }

        /// <summary>
        /// THE single seam every FSM state writes movement through.
        ///
        /// Two things used to fight the states for ownership of <c>velocity</c> and
        /// lose, because the states wrote it unconditionally every tick:
        ///
        ///   • <b>Knockback</b> — an impulse the next tick overwrote, so no hit in the
        ///     game had any physical push-back.
        ///   • <b>Stun</b> — <c>StatusEffectManager.IsStunned</c> was honoured by the
        ///     player controller and by NPCAutoCast, and by nothing in the FSM, so a
        ///     stunned monster chased and swung normally. StunEffect's own
        ///     velocity-zeroing raced the chase state in the same frame with no script
        ///     execution order defined between them.
        ///
        /// Stun forces zero (a stunned entity must stop); knockback yields entirely
        /// (the impulse is the intended motion). Anything that wants to move an
        /// FSM-driven entity goes through here or it will be silently overwritten.
        /// </summary>
        public void SetVelocity(Vector2 velocity)
        {
            if (Rb == null) return;
            if (KnockbackActive) return;
            Rb.velocity = IsStunned ? Vector2.zero : velocity;
        }

        /// <summary>Convenience for the many states that stop the body on Enter/Exit.</summary>
        public void StopMovement() => SetVelocity(Vector2.zero);

        private void ResolveExtras()
        {
            if (_extrasResolved) return;
            _extrasResolved = true;
            if (_owner == null) return;
            _status = _owner.GetComponent<StatusEffectManager>();
            _feedback = _owner.GetComponent<CombatFeedback>();
        }
    }
}
