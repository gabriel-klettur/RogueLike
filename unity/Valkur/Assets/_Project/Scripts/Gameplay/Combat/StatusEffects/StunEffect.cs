using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Stun effect: prevents movement and attacks for a duration.
    /// Mirrors Python StunComponent + StunSystem.
    /// Zeroes the Rigidbody2D velocity and sets a flag that PlayerController /
    /// FSMMonsterBrain check each frame.
    /// </summary>
    public sealed class StunEffect : StatusEffect
    {
        private Rigidbody2D _rb;

        public StunEffect(float duration, GameObject applier = null)
            : base(duration, applier) { }

        public override void OnApply(StatusEffectManager target)
        {
            _rb = target.GetComponent<Rigidbody2D>();

            // Show stun visual — white stars above head via simple VFX
            var sr = target.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
                target.StartCoroutine(StunTintRoutine(sr, target));
        }

        public override void Tick(StatusEffectManager target)
        {
            // Lock velocity every frame while stunned
            if (_rb != null)
                _rb.velocity = Vector2.zero;
        }

        public override void OnRemove(StatusEffectManager target)
        {
            // Velocity will resume naturally from controller inputs next frame
        }

        private System.Collections.IEnumerator StunTintRoutine(SpriteRenderer sr,
                                                                 StatusEffectManager target)
        {
            Color originalColor = sr.color;
            Color stunColor = new Color(0.9f, 0.9f, 0.2f, 1f);

            while (!IsExpired && target != null)
            {
                float t = Mathf.PingPong(Time.time * 8f, 1f);
                sr.color = Color.Lerp(originalColor, stunColor, t * 0.7f);
                yield return null;
            }

            if (sr != null)
                sr.color = originalColor;
        }
    }
}
