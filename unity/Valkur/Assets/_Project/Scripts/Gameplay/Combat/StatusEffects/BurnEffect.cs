using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Fire Damage-over-Time effect.
    /// Mirrors Python BurnComponent + BurnSystem / DoTSystem.
    /// Ticks every <c>tickPeriod</c> seconds dealing <c>damagePerTick</c> fire damage.
    /// Stacks: re-applying Burn refreshes duration and replaces previous instance.
    /// </summary>
    public sealed class BurnEffect : StatusEffect
    {
        // Python defaults: damage_per_tick=2, tick_period=1.0
        public int   DamagePerTick { get; }
        public float TickPeriod    { get; }

        private float _nextTickTime;

        public BurnEffect(float duration, int damagePerTick = 2, float tickPeriod = 1f,
                          GameObject applier = null)
            : base(duration, applier)
        {
            DamagePerTick  = damagePerTick;
            TickPeriod     = Mathf.Max(0.05f, tickPeriod);
            _nextTickTime  = StartTime + TickPeriod;
        }

        public override void OnApply(StatusEffectManager target)
        {
            // Tint orange while burning
            var tint = SpriteTintStack.Attach(target);
            if (tint != null)
                target.StartCoroutine(BurnTintRoutine(tint, target));
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

        private System.Collections.IEnumerator BurnTintRoutine(SpriteTintStack tint,
                                                                 StatusEffectManager target)
        {
            Color burnColor = new Color(1f, 0.4f, 0.1f, 1f);

            while (!IsExpired && target != null)
            {
                float t = Mathf.PingPong((Time.time - StartTime) * 3f, 1f);
                tint.Set(TintLayer.Burn, Color.Lerp(Color.white, burnColor, t * 0.6f));
                yield return null;
            }

            if (tint != null) tint.Clear(TintLayer.Burn);
        }
    }
}
