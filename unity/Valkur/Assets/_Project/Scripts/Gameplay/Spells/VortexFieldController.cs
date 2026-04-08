using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Applies pull or push force to enemies within radius every frame.
    /// Optionally follows the caster.
    /// </summary>
    public class VortexFieldController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private float _force;
        private bool _isPull;
        private Transform _followTarget;
        private LayerMask _targetLayers;
        private SpriteRenderer _sr;

        public void Initialize(float duration, float radius, float force, bool isPull,
            Transform followTarget, LayerMask targetLayers)
        {
            _remaining = duration;
            _radius = radius;
            _force = force;
            _isPull = isPull;
            _followTarget = followTarget;
            _targetLayers = targetLayers;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            // Follow caster if configured
            if (_followTarget != null)
                transform.position = _followTarget.position;

            ApplyForce();

            // Rotation animation
            transform.Rotate(0, 0, (_isPull ? 120f : -120f) * Time.deltaTime);

            // Fade out in last second
            if (_sr != null && _remaining < 1f)
            {
                var c = _sr.color;
                c.a = Mathf.Clamp01(_remaining) * 0.3f;
                _sr.color = c;
            }
        }

        private void ApplyForce()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            foreach (var hit in hits)
            {
                var rb = hit.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

                var health = hit.GetComponent<Health>();
                if (health != null && health.IsDead) continue;

                Vector2 dir = ((Vector2)transform.position - rb.position).normalized;
                if (!_isPull) dir = -dir;

                float dist = Vector2.Distance(transform.position, rb.position);
                float falloff = 1f - Mathf.Clamp01(dist / _radius);
                rb.AddForce(dir * _force * falloff * Time.deltaTime, ForceMode2D.Impulse);
            }
        }
    }
}
