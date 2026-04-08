using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Controls a spell wall lifetime: auto-destroys after duration or when HP reaches zero.
    /// </summary>
    public class WallController : MonoBehaviour
    {
        private float _remainingTime;
        private Health _health;
        private float _fadeStartTime;
        private SpriteRenderer _sr;

        public void Initialize(float duration, Health health)
        {
            _remainingTime = duration;
            _health = health;
            _sr = GetComponent<SpriteRenderer>();
            _fadeStartTime = Mathf.Max(0f, duration - 1f);
        }

        private void Update()
        {
            _remainingTime -= Time.deltaTime;

            if (_health != null && _health.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            if (_remainingTime <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            // Fade out in final second
            if (_sr != null && _remainingTime < 1f)
            {
                var c = _sr.color;
                c.a = Mathf.Clamp01(_remainingTime);
                _sr.color = c;
            }
        }
    }
}
