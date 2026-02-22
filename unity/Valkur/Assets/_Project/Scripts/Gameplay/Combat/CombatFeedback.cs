using System.Collections;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Visual and physical feedback when an entity takes damage.
    /// Handles hit flash (white tint), knockback impulse, and damage logging.
    /// Attach to any entity with Health + SpriteRenderer.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class CombatFeedback : MonoBehaviour
    {
        [Header("Hit Flash")]
        [SerializeField] private float flashDuration = 0.12f;
        [SerializeField] private Color flashColor = Color.white;

        [Header("Knockback")]
        [SerializeField] private float knockbackForce = 4f;
        [SerializeField] private float knockbackDuration = 0.15f;

        [Header("Death")]
        [SerializeField] private float deathFadeTime = 0.5f;
        [SerializeField] private float deathDestroyDelay = 1f;

        private Health _health;
        private SpriteRenderer _spriteRenderer;
        private Rigidbody2D _rb;
        private Color _originalColor;
        private Coroutine _flashCoroutine;
        private bool _isDying;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();

            if (_spriteRenderer != null)
                _originalColor = _spriteRenderer.color;
        }

        private void OnEnable()
        {
            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;
        }

        private void OnDisable()
        {
            _health.OnDamaged -= OnDamaged;
            _health.OnDeath -= OnDeath;
        }

        private void OnDamaged(int amount)
        {
            if (_isDying) return;

            // Hit flash
            if (_spriteRenderer != null)
            {
                if (_flashCoroutine != null)
                    StopCoroutine(_flashCoroutine);
                _flashCoroutine = StartCoroutine(HitFlashRoutine());
            }

            Debug.Log($"[Combat] {gameObject.name} took {amount} damage. HP: {_health.CurrentHp}/{_health.MaxHp}");
        }

        /// <summary>
        /// Apply knockback impulse away from a damage source position.
        /// Call this from the attacker after dealing damage.
        /// </summary>
        public void ApplyKnockback(Vector2 sourcePosition)
        {
            if (_rb == null || _isDying) return;

            Vector2 direction = ((Vector2)transform.position - sourcePosition).normalized;
            _rb.AddForce(direction * knockbackForce, ForceMode2D.Impulse);

            if (gameObject.activeInHierarchy)
                StartCoroutine(KnockbackDecayRoutine());
        }

        private void OnDeath()
        {
            if (_isDying) return;
            _isDying = true;

            Debug.Log($"[Combat] {gameObject.name} died!");

            // Unregister from EntityRegistry immediately to prevent stale references
            EntityRegistry.UnregisterMonster(gameObject);

            // Disable AI/movement
            var controller = GetComponent<PlayerController>();
            if (controller != null) controller.enabled = false;

            var brain = GetComponent<FSM.FSMMonsterBrain>();
            if (brain != null) brain.enabled = false;

            StartCoroutine(DeathRoutine());
        }

        private IEnumerator HitFlashRoutine()
        {
            _spriteRenderer.color = flashColor;
            yield return new WaitForSeconds(flashDuration);
            _spriteRenderer.color = _originalColor;
            _flashCoroutine = null;
        }

        private IEnumerator KnockbackDecayRoutine()
        {
            yield return new WaitForSeconds(knockbackDuration);
            if (_rb != null)
                _rb.velocity = Vector2.zero;
        }

        private IEnumerator DeathRoutine()
        {
            // Fade out
            if (_spriteRenderer != null)
            {
                float elapsed = 0f;
                Color startColor = _spriteRenderer.color;
                while (elapsed < deathFadeTime)
                {
                    elapsed += Time.deltaTime;
                    float alpha = Mathf.Lerp(1f, 0f, elapsed / deathFadeTime);
                    _spriteRenderer.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
                    yield return null;
                }
            }

            yield return new WaitForSeconds(deathDestroyDelay - deathFadeTime);

            // For monsters, destroy. For player, could trigger respawn.
            if (GetComponent<PlayerController>() == null)
                Destroy(gameObject);
        }
    }
}
