using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Plays the player's hurt animation when they are struck, and turns them to face what
    /// hit them while it plays.
    ///
    /// Every playable character ships an authored <c>damageSheets</c> set, and
    /// <see cref="EntityAnimationBinder"/> loads it into the animator — but nothing has ever
    /// pushed the player into <see cref="DirectionalAnimator.AnimState.Damage"/>. Monsters
    /// get theirs from the FSM's <c>DamageState</c>; the player, who is not FSM-driven, was
    /// simply never given the equivalent. The comment in
    /// <c>PlayerController.Movement.cs</c> claiming Damage is "owned by other systems" was
    /// describing a system that did not exist, so the art has never once been on screen.
    ///
    /// A hit deliberately interrupts a cast or attack animation. Being staggered out of what
    /// you were doing is the readable part of taking damage.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public sealed class PlayerHurtReaction : MonoBehaviour
    {
        [Tooltip("Seconds the hurt animation holds before locomotion takes the animator back. " +
                 "Matches the monsters' DamageState stun window so both read the same.")]
        [SerializeField] private float hurtSeconds = 0.24f;

        [Tooltip("Smallest gap between two hurt reactions. Without it, a damage-over-tick " +
                 "effect restarts the animation every tick and it never advances a frame.")]
        [SerializeField] private float minimumInterval = 0.12f;

        private DirectionalAnimator _animator;
        private Health _health;
        private PlayerController _controller;
        private float _hurtUntil;
        private float _nextAllowed;

        private void Awake()
        {
            _animator = GetComponent<DirectionalAnimator>();
            _health = GetComponent<Health>();
            _controller = GetComponent<PlayerController>();
        }

        private void OnEnable() => GameEvents.OnEntityDamaged += HandleEntityDamaged;
        private void OnDisable() => GameEvents.OnEntityDamaged -= HandleEntityDamaged;

        private void HandleEntityDamaged(GameObject victim, GameObject attacker, int amount)
        {
            if (victim != gameObject || amount <= 0) return;
            if (_animator == null || _health == null || _health.IsDead) return;
            if (Time.time < _nextAllowed) return;

            _nextAllowed = Time.time + minimumInterval;
            _hurtUntil = Time.time + hurtSeconds;

            _animator.SetState(DirectionalAnimator.AnimState.Damage, ResolveFacing(attacker));
        }

        /// <summary>
        /// Face the blow when there is one to face. An unattributed hit — a puddle, a burn
        /// tick — leaves the player looking where they already were rather than snapping to
        /// an arbitrary direction.
        /// </summary>
        private DirectionalAnimator.Direction ResolveFacing(GameObject attacker)
        {
            if (attacker == null || attacker == gameObject) return _animator.CurrentDirection;

            Vector2 toAttacker = (Vector2)attacker.transform.position - (Vector2)transform.position;
            return toAttacker.sqrMagnitude <= 0.0001f
                ? _animator.CurrentDirection
                : _animator.ResolveDirectionFromVector(toAttacker.normalized);
        }

        private void Update()
        {
            if (_hurtUntil <= 0f || Time.time < _hurtUntil) return;
            _hurtUntil = 0f;

            if (_animator == null || _health == null || _health.IsDead) return;
            if (_animator.CurrentState != DirectionalAnimator.AnimState.Damage) return;

            // Hand the animator back to locomotion. PlayerController only overrides the
            // locomotion states, so releasing into Idle is what lets it resume.
            bool moving = _controller != null && _controller.IsMoving;
            _animator.SetState(
                moving ? DirectionalAnimator.AnimState.Walk : DirectionalAnimator.AnimState.Idle,
                _animator.CurrentDirection);
        }
    }
}
