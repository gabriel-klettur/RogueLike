using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Poison Damage-over-Time effect.
    /// Mirrors Python PoisonComponent + DoTSystem.
    /// Identical tick logic to Burn but with a green tint.
    /// </summary>
    public sealed class PoisonEffect : StatusEffect
    {
        public int   DamagePerTick { get; }
        public float TickPeriod    { get; }

        private float _nextTickTime;

        public PoisonEffect(float duration, int damagePerTick = 1, float tickPeriod = 1f,
                            GameObject applier = null)
            : base(duration, applier)
        {
            DamagePerTick  = damagePerTick;
            TickPeriod     = Mathf.Max(0.05f, tickPeriod);
            _nextTickTime  = StartTime + TickPeriod;
        }

        public override void OnApply(StatusEffectManager target)
        {
            var tint = SpriteTintStack.Attach(target);
            if (tint != null)
                target.StartCoroutine(PoisonTintRoutine(tint, target));
        }

        public override void Tick(StatusEffectManager target)
        {
            float now = Time.time;
            if (now < _nextTickTime) return;

            int ticks = Mathf.FloorToInt((now - _nextTickTime) / TickPeriod) + 1;
            _nextTickTime += ticks * TickPeriod;

            var hp = target.GetComponent<Health>();
            if (hp != null && !hp.IsDead)
                hp.TakeDamage(DamagePerTick * ticks);
        }

        private System.Collections.IEnumerator PoisonTintRoutine(SpriteTintStack tint,
                                                                   StatusEffectManager target)
        {
            Color poisonColor = new Color(0.3f, 1f, 0.3f, 1f);

            while (!IsExpired && target != null)
            {
                float t = Mathf.PingPong((Time.time - StartTime) * 2f, 1f);
                tint.Set(TintLayer.Poison, Color.Lerp(Color.white, poisonColor, t * 0.5f));
                yield return null;
            }

            if (tint != null) tint.Clear(TintLayer.Poison);
        }
    }
}
