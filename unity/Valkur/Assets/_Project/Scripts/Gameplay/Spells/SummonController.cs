using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Controls a summoned unit: limited duration, auto-destroy, basic follow behavior.
    /// </summary>
    public class SummonController : MonoBehaviour
    {
        private float _remaining;
        private Transform _owner;
        private Health _health;
        private SpriteRenderer _sr;

        public void Initialize(float duration, Transform owner)
        {
            _remaining = duration;
            _owner = owner;
            _health = GetComponent<Health>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;

            if (_health != null && _health.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            if (_remaining <= 0f)
            {
                Debug.Log($"[SpellDebug] Summon expired: {gameObject.name}");
                Destroy(gameObject);
                return;
            }

            // Basic follow: move toward owner if too far
            if (_owner != null)
            {
                float dist = Vector2.Distance(transform.position, _owner.position);
                if (dist > 4f)
                {
                    Vector2 dir = ((Vector2)_owner.position - (Vector2)transform.position).normalized;
                    var rb = GetComponent<Rigidbody2D>();
                    if (rb != null)
                        rb.velocity = dir * 3f;
                }
                else
                {
                    var rb = GetComponent<Rigidbody2D>();
                    if (rb != null)
                        rb.velocity = Vector2.zero;
                }
            }

            // Fade out in last 2 seconds
            if (_sr != null && _remaining < 2f)
            {
                var c = _sr.color;
                c.a = Mathf.Clamp01(_remaining * 0.5f);
                _sr.color = c;
            }
        }
    }
}
