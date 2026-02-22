using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Generic projectile that moves in a direction, deals damage on hit, and expires.
    /// Maps to Python's projectile spell type (fireball, iceball, darkball, etc.).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Projectile : MonoBehaviour
    {
        [Header("Runtime (set by SpellCaster)")]
        [SerializeField] private float speed = 10f;
        [SerializeField] private float damage = 20f;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private float range = 20f;
        [SerializeField] private LayerMask targetLayers;

        private Vector2 _direction;
        private Vector2 _origin;
        private float _timer;
        private Rigidbody2D _rb;
        private bool _expired;
        private Color _vfxColor = new Color(1f, 0.6f, 0.2f, 0.8f);
        private string _poolKey;

        /// <summary>
        /// Set the pool key so the projectile returns to pool instead of being destroyed.
        /// </summary>
        public void SetPoolKey(string key) => _poolKey = key;

        /// <summary>
        /// Set the VFX color for impact effects.
        /// </summary>
        public void SetVFXColor(Color color) => _vfxColor = color;

        public void Initialize(Vector2 direction, float spd, float dmg, float life, float rng, LayerMask targets)
        {
            _direction = direction.normalized;
            speed = spd;
            damage = dmg;
            lifetime = life;
            range = rng;
            targetLayers = targets;
            _origin = transform.position;

            // Rotate sprite to face direction
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _origin = transform.position;
        }

        private void FixedUpdate()
        {
            if (_expired) return;
            _rb.velocity = _direction * speed;
        }

        private void Update()
        {
            if (_expired) return;

            _timer += Time.deltaTime;

            // Expire by time or range
            float distSq = ((Vector2)transform.position - _origin).sqrMagnitude;
            if (_timer >= lifetime || distSq >= range * range)
            {
                Expire();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_expired) return;
            if (((1 << other.gameObject.layer) & targetLayers) == 0) return;

            var health = other.GetComponent<Health>();
            if (health != null && !health.IsDead)
            {
                health.TakeDamage(Mathf.RoundToInt(damage));
            }

            Expire();
        }

        private void Expire()
        {
            _expired = true;
            _rb.velocity = Vector2.zero;

            // Spawn impact VFX
            if (VFXManager.Instance != null)
                VFXManager.Instance.SpawnImpact(transform.position, _vfxColor, 0.25f, 0.8f);

            // Return to pool or destroy
            if (!string.IsNullOrEmpty(_poolKey) && VFXManager.Instance != null)
            {
                ResetState();
                VFXManager.Instance.Despawn(_poolKey, gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void ResetState()
        {
            _direction = Vector2.zero;
            _origin = Vector2.zero;
            _timer = 0f;
            _expired = false;
            if (_rb != null) _rb.velocity = Vector2.zero;
        }
    }
}
