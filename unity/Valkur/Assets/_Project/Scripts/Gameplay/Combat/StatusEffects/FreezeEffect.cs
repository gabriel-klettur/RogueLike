using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Freeze effect: completely immobilizes the entity and applies a blue tint.
    /// Mirrors Python's FreezeComponent + freeze_system.
    /// Essentially a stun with ice-blue visual.
    /// </summary>
    public sealed class FreezeEffect : StatusEffect
    {
        private Rigidbody2D _rb;

        public FreezeEffect(float duration, GameObject applier = null)
            : base(duration, applier) { }

        public override void OnApply(StatusEffectManager target)
        {
            _rb = target.GetComponent<Rigidbody2D>();

            var sr = target.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
                target.StartCoroutine(FreezeTintRoutine(sr, target));
        }

        public override void Tick(StatusEffectManager target)
        {
            // Lock velocity every frame
            if (_rb != null)
                _rb.velocity = Vector2.zero;
        }

        public override void OnRemove(StatusEffectManager target)
        {
            // Velocity resumes from controller/FSM next frame
        }

        private System.Collections.IEnumerator FreezeTintRoutine(SpriteRenderer sr,
                                                                    StatusEffectManager target)
        {
            Color original = sr.color;
            // Solid ice-blue (Python: distinct from slow tint)
            Color freezeColor = new Color(0.3f, 0.5f, 1f, 1f);

            if (sr != null)
                sr.color = Color.Lerp(original, freezeColor, 0.6f);

            while (!IsExpired && target != null)
                yield return null;

            if (sr != null)
                sr.color = original;
        }
    }
}
