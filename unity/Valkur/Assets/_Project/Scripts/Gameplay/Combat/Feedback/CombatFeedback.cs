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
    public partial class CombatFeedback : MonoBehaviour
    {
        [Header("Knockback")]
        [SerializeField] private float knockbackForce = 4f;
        [SerializeField] private float knockbackDuration = 0.15f;

        [Header("Death")]
        [SerializeField] private float deathFadeTime = 0.5f;
        [SerializeField] private float deathDestroyDelay = 1f;

        private Health _health;
        private SpriteRenderer _spriteRenderer;
        private Rigidbody2D _rb;
        private bool _isDying;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();

            EnsureHitFlashReady();
        }

        private void Update() => TickHitFlash(Time.deltaTime);

        private void OnEnable()
        {
            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;
        }

        private void OnDisable()
        {
            _health.OnDamaged -= OnDamaged;
            _health.OnDeath -= OnDeath;

            // Pooled entities come back with whatever the last hit left behind, so
            // clear the flash rather than reviving a half-white sprite.
            CancelHitFlash();
        }

        private void OnDamaged(int amount)
        {
            if (_isDying) return;

            TriggerHitFlash();

            // Fires once per hit on every entity in a fight — gated so a busy
            // brawl does not bury the console.
            VerboseLog.Log(VerboseLog.Category.Combat,
                () => $"[Combat] {gameObject.name} took {amount} damage. HP: {_health.CurrentHp}/{_health.MaxHp}");
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
            // (player targeting, spell auto-aim, etc.).
            EntityRegistry.UnregisterMonster(gameObject);

            // If the entity has an FSM brain, the FSM owns the death sequence:
            // it transitions to UnconsciousState (corpse pose for deathDisappearTime
            // seconds) and then to DeathState which destroys the GameObject. Stopping
            // the brain here would prevent that transition entirely — the entity
            // would freeze on its idle pose and the sprite fade below would mask it
            // before any death animation could play. So leave the brain alone and
            // skip the alpha-fade routine; GrayscaleDeath handles the corpse tint.
            var brain = GetComponent<FSM.FSMMonsterBrain>();
            if (brain != null)
            {
                CancelHitFlash();
                return;
            }

            // The player has its own death-and-revive flow: DeathSequenceController
            // spawns a corpse marker, fades grayscale, and transitions the player
            // into spirit form so it can walk to the altar. Disabling
            // PlayerController or alpha-fading the sprite here would freeze the
            // spirit in place / hide the ghost the moment the routine tries to
            // make it visible. Yield to that controller exactly like we do for
            // FSM monsters.
            var spiritState = GetComponent<Death.PlayerSpiritState>();
            if (spiritState != null)
            {
                CancelHitFlash();
                return;
            }

            // No FSM brain and no spirit flow (e.g. simple test dummies, prototype
            // entities) — keep the legacy fade-out + destroy fallback so basic
            // Health-only entities still disappear cleanly on death.
            var controller = GetComponent<PlayerController>();
            if (controller != null) controller.enabled = false;

            StartCoroutine(DeathRoutine());
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
