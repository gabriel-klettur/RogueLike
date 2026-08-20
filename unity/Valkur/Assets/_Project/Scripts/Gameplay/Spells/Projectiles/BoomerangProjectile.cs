using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Runtime behaviour for the boomerang spell.
    /// Phase 1 (Outbound): travels in direction at speed until maxRange.
    /// Phase 2 (Return): chases caster back at returnSpeed.
    /// Deals damage to enemies on contact. If passeThrough=false, stops on first hit.
    /// Mirrors Python BoomerangComponent + BoomerangSystem.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class BoomerangProjectile : MonoBehaviour
    {
        private enum Phase { Outbound, Returning }

        private Transform _caster;
        private Vector2 _origin;
        private Vector2 _direction;
        private float _speed;
        private float _returnSpeed;
        private float _damage;
        private float _maxRange;
        private float _hitRadius;
        private bool _passesThrough;
        private LayerMask _targetLayers;
        private Color _vfxColor;

        private Phase _phase = Phase.Outbound;
        private Rigidbody2D _rb;
        private bool _expired;

        public void Initialize(Transform caster, Vector2 direction, float speed, float returnSpeed,
                               float damage, float maxRange, float hitRadius, bool passesThrough,
                               LayerMask targetLayers, Color vfxColor)
        {
            _caster      = caster;
            _direction   = direction.normalized;
            _speed       = speed > 0 ? speed : 8f;
            _returnSpeed = returnSpeed > 0 ? returnSpeed : _speed;
            _damage      = damage;
            _maxRange    = maxRange > 0 ? maxRange : 6f;
            _hitRadius   = hitRadius > 0 ? hitRadius : 0.25f;
            _passesThrough = passesThrough;
            _targetLayers = targetLayers;
            _vfxColor    = vfxColor;
            _origin      = transform.position;

            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = false; // allow spin VFX
            _origin = transform.position;
        }

        private void FixedUpdate()
        {
            if (_expired) return;

            if (_phase == Phase.Outbound)
            {
                _rb.velocity = _direction * _speed;

                // Spin for visual flair
                _rb.angularVelocity = 720f;

                float distSq = ((Vector2)transform.position - _origin).sqrMagnitude;
                if (distSq >= _maxRange * _maxRange)
                    _phase = Phase.Returning;
            }
            else // Returning
            {
                if (_caster == null) { Expire(); return; }

                Vector2 toOwner = (Vector2)_caster.position - (Vector2)transform.position;
                float distToOwner = toOwner.magnitude;

                if (distToOwner < 0.5f)
                {
                    // Caught by caster
                    Expire();
                    return;
                }

                _rb.velocity = toOwner.normalized * _returnSpeed;
            }
        }

        private void Update()
        {
            if (_expired) return;
            if (_caster == null) { Expire(); return; }

            // Hitbox: overlap check each frame
            var hits = Physics2D.OverlapCircleAll(transform.position, _hitRadius, _targetLayers);
            bool hitSomething = false;
            foreach (var hit in hits)
            {
                if (hit.gameObject == _caster.gameObject) continue;
                var health = hit.GetComponentInParent<Health>();
                if (health != null && !health.IsDead)
                {
                    int dealt = Mathf.RoundToInt(_damage);
                    health.TakeDamage(dealt);
                    if (_caster != null)
                        Valkur.Core.GameEvents.FireHitDealt(_caster.gameObject, hit.gameObject, dealt);
                    if (VFXManager.Instance != null)
                        VFXManager.Instance.SpawnImpact(hit.transform.position, _vfxColor, 0.25f);
                    hitSomething = true;
                }
            }

            if (hitSomething && !_passesThrough && _phase == Phase.Outbound)
                _phase = Phase.Returning;
        }

        private void Expire()
        {
            if (_expired) return;
            _expired = true;
            Object.Destroy(gameObject);
        }
    }
}
