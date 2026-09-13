using UnityEngine;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;

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

        /// <summary>True while this entity's feet are held by a root.</summary>
        public bool IsRooted
        {
            get
            {
                var s = Status;
                return s != null && s.IsRooted;
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
            // A corpse: UnconsciousState makes the body Static so nothing can shove it, and a
            // Static body has no velocity to set — Unity refuses the write with a warning. Every
            // monster death went Unconscious -> Death, and DeathState.Enter stops the body, so
            // every kill in the game logged "Cannot use 'velocity' on a static body".
            if (Rb.bodyType == RigidbodyType2D.Static) return;
            // Root joins stun here and nowhere else: it refuses the feet, so the FSM may
            // go on chasing and swinging while the body does not move. AttackState and
            // NPCAutoCast read IsStunned only, on purpose.
            Rb.velocity = (IsStunned || IsRooted) ? Vector2.zero : velocity;
        }

        /// <summary>Convenience for the many states that stop the body on Enter/Exit.</summary>
        public void StopMovement() => SetVelocity(Vector2.zero);

        // ── Target resolution, cached for the frame ──────────────────────────────
        //
        // Four states asked FactionTargeting.EnemyOf every tick and then paid a
        // GetComponent<Health> and a GetComponent<PlayerSpiritState> on whatever came
        // back — AttackState asked up to four times in ONE frame (telegraph, facing,
        // damage window, range re-check). None of those answers can change between two
        // reads in the same frame, and the components on a target never change at all,
        // so the whole thing collapses to one resolve per entity per frame plus a
        // dictionary-free component cache keyed on the target itself.

        private int _targetFrame = -1;
        private GameObject _target;
        private GameObject _cachedComponentsFor;
        private Health _targetHealth;
        private PlayerSpiritState _targetSpirit;

        /// <summary>
        /// This entity's current enemy, resolved at most once per frame.
        /// Null when there is nothing to hunt.
        /// </summary>
        public GameObject Target(StateMachine fsm)
        {
            int frame = Time.frameCount;
            if (_targetFrame == frame) return _target;

            _targetFrame = frame;
            _target = FactionTargeting.EnemyOf(fsm != null ? fsm.Owner : _owner);
            return _target;
        }

        /// <summary>Health of the current target, or null. Resolved once per target.</summary>
        public Health TargetHealth(StateMachine fsm)
        {
            CacheTargetComponents(Target(fsm));
            return _targetHealth;
        }

        /// <summary>True while the target is a player in spirit form — unperceivable.</summary>
        public bool TargetIsSpirit(StateMachine fsm)
        {
            CacheTargetComponents(Target(fsm));
            return _targetSpirit != null && _targetSpirit.IsSpirit;
        }

        /// <summary>
        /// The one question the hostile states actually ask: is there a target, is it alive,
        /// and is it solid? Answers false for all three so a caller can bail with one test.
        /// </summary>
        public bool HasViableTarget(StateMachine fsm)
        {
            var t = Target(fsm);
            if (t == null || !t.activeInHierarchy) return false;
            CacheTargetComponents(t);
            if (_targetHealth != null && _targetHealth.IsDead) return false;
            return _targetSpirit == null || !_targetSpirit.IsSpirit;
        }

        private void CacheTargetComponents(GameObject target)
        {
            if (ReferenceEquals(_cachedComponentsFor, target)) return;
            _cachedComponentsFor = target;
            _targetHealth = target != null ? target.GetComponent<Health>() : null;
            _targetSpirit = target != null ? target.GetComponent<PlayerSpiritState>() : null;
        }

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
