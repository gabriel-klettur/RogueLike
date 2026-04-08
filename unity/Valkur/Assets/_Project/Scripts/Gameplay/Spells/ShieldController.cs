using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Controls the magic shield: sets caster invincible for duration, pulses visual, then removes.
    /// </summary>
    public class ShieldController : MonoBehaviour
    {
        private float _remaining;
        private Transform _caster;
        private Health _casterHealth;
        private SpriteRenderer _sr;

        public void Initialize(float duration, Transform caster)
        {
            _remaining = duration;
            _caster = caster;
            _casterHealth = caster.GetComponent<Health>();
            _sr = GetComponent<SpriteRenderer>();

            if (_casterHealth != null)
                _casterHealth.SetInvincible(true);
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;

            // Pulse effect
            if (_sr != null)
            {
                float pulse = 0.25f + Mathf.Sin(Time.time * 6f) * 0.1f;
                var c = _sr.color;
                c.a = pulse;
                _sr.color = c;
            }

            // Fade in last second
            if (_remaining < 1f && _sr != null)
            {
                var c = _sr.color;
                c.a *= Mathf.Clamp01(_remaining);
                _sr.color = c;
            }

            if (_remaining <= 0f)
            {
                if (_casterHealth != null)
                    _casterHealth.SetInvincible(false);
                Debug.Log($"[SpellDebug] Shield expired on {(_caster != null ? _caster.name : "?")}");
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            // Safety: always restore vulnerability
            if (_casterHealth != null)
                _casterHealth.SetInvincible(false);
        }
    }
}
