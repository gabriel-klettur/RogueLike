using UnityEngine;

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
            // TODO: spawn impact VFX here
            Destroy(gameObject);
        }
    }
}
