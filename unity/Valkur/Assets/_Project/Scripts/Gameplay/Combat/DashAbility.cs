using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Duration-based dash ability with collision detection.
    /// Maps to Python's DashComponent + DashSystem.
    /// Moves the entity at high speed in a direction for a set duration,
    /// with optional collision damage and knockback on impact.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class DashAbility : MonoBehaviour
    {
        [Header("Dash Settings")]
        [SerializeField] private float dashSpeed = 18f;
        [SerializeField] private float dashDuration = 0.2f;
        [SerializeField] private float dashCooldown = 1f;
        [SerializeField] private float collisionDamage = 2f;
        [SerializeField] private float knockbackOnHit = 4f;

        [Header("Layers")]
        [SerializeField] private LayerMask targetLayers;

        private Rigidbody2D _rb;
        private bool _isDashing;
        private float _dashTimer;
        private float _cooldownTimer;
        private Vector2 _dashDirection;

        public bool IsDashing => _isDashing;
        public bool CanDash => !_isDashing && _cooldownTimer <= 0f;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);
        public float CooldownTotal => dashCooldown;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;
        }

        private void FixedUpdate()
        {
            if (!_isDashing) return;

            _dashTimer -= Time.fixedDeltaTime;

            if (_dashTimer <= 0f)
            {
                EndDash();
                return;
            }

            // Move at dash speed
            _rb.velocity = _dashDirection * dashSpeed;

            // Check for collision damage during dash
            if (collisionDamage > 0f)
            {
                var hits = Physics2D.OverlapCircleAll(
                    (Vector2)transform.position,
                    0.5f,
                    targetLayers);

                foreach (var hit in hits)
                {
                    if (hit.gameObject == gameObject) continue;
                    var health = hit.GetComponent<Health>();
                    if (health != null && !health.IsDead)
                    {
                        health.TakeDamage(Mathf.RoundToInt(collisionDamage));

                        // Apply knockback to hit target
                        var feedback = hit.GetComponent<CombatFeedback>();
                        if (feedback != null)
                            feedback.ApplyKnockback(transform.position);
                    }
                }
            }
        }

        /// <summary>
        /// Start a dash in the given direction.
        /// Returns true if dash started successfully.
        /// </summary>
        public bool TryDash(Vector2 direction)
        {
            if (!CanDash) return false;
            if (direction.sqrMagnitude < 0.01f) return false;

            _dashDirection = direction.normalized;
            _isDashing = true;
            _dashTimer = dashDuration;

            Debug.Log($"[DashAbility] {gameObject.name} dashing {_dashDirection}");
            return true;
        }

        public void SetTargetLayers(LayerMask layers)
        {
            targetLayers = layers;
        }

        private void EndDash()
        {
            _isDashing = false;
            _cooldownTimer = dashCooldown;
            _rb.velocity = Vector2.zero;
        }
    }
}
