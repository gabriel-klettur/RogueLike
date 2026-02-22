using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes an area-of-effect spell: Physics2D overlap at target position + VFX indicator.
    /// </summary>
    public class AreaExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : 2f;
            Vector2 center = (Vector2)ctx.Caster.position + ctx.Direction * radius;

            var hits = Physics2D.OverlapCircleAll(center, radius, ctx.TargetLayers);
            foreach (var hit in hits)
            {
                if (hit.gameObject == ctx.Caster.gameObject) continue;
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    health.TakeDamage(Mathf.RoundToInt(ctx.Spell.damage));
            }

            if (VFXManager.Instance != null)
            {
                Color areaColor = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(0.8f, 0.3f, 0.1f, 0.5f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)center, areaColor, radius, 0.5f);
            }
        }
    }
}
