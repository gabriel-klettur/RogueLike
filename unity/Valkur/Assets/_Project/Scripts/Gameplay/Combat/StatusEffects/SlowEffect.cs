using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Slow effect: reduces movement speed by a percentage for a duration.
    /// Mirrors Python's SlowComponent + slow_system.
    /// </summary>
    public sealed class SlowEffect : StatusEffect
    {
        private readonly float _slowFactor;
        private Rigidbody2D _rb;
        private float _originalDrag;

        /// <param name="duration">Duration in seconds.</param>
        /// <param name="slowFactor">Speed multiplier (0.5 = 50% speed).</param>
        /// <param name="applier">The source GameObject.</param>
        public SlowEffect(float duration, float slowFactor = 0.5f, GameObject applier = null)
            : base(duration, applier)
        {
            _slowFactor = Mathf.Clamp01(slowFactor);
        }

        public float SlowFactor => _slowFactor;

        public override void OnApply(StatusEffectManager target)
        {
            _rb = target.GetComponent<Rigidbody2D>();
            if (_rb != null)
            {
                _originalDrag = _rb.drag;
                // Increase drag to simulate slow — larger drag = slower movement
                _rb.drag = _originalDrag + (1f - _slowFactor) * 10f;
            }

            // Visual: blue tint
            var tint = SpriteTintStack.Attach(target);
            if (tint != null)
                target.StartCoroutine(SlowTintRoutine(tint, target));
        }

        public override void Tick(StatusEffectManager target)
        {
            // Drag-based slow stays applied; no per-frame work needed
        }

        public override void OnRemove(StatusEffectManager target)
        {
            if (_rb != null)
                _rb.drag = _originalDrag;
        }

        private System.Collections.IEnumerator SlowTintRoutine(SpriteTintStack tint,
                                                                 StatusEffectManager target)
        {
            Color slowColor = new Color(0.4f, 0.6f, 1f, 1f); // Ice-blue tint

            while (!IsExpired && target != null)
            {
                tint.Set(TintLayer.Slow, Color.Lerp(Color.white, slowColor, 0.3f));
                yield return null;
            }

            if (tint != null) tint.Clear(TintLayer.Slow);
        }
    }
}
