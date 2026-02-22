using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes an arc slash attack: Physics2D overlap + angle filter + VFX.
    /// </summary>
    public class SlashExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float arc = ctx.Spell.arcRangeDegrees > 0 ? ctx.Spell.arcRangeDegrees : 90f;
            float hitRadius = ctx.Spell.hitRadius > 0 ? ctx.Spell.hitRadius : ctx.Spell.range;
            if (hitRadius <= 0) hitRadius = 1.5f;

            Vector2 center = (Vector2)ctx.Caster.position + ctx.Direction * (hitRadius * 0.5f);
            var hits = Physics2D.OverlapCircleAll(center, hitRadius, ctx.TargetLayers);

            foreach (var hit in hits)
            {
                if (hit.gameObject == ctx.Caster.gameObject) continue;
                var health = hit.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                Vector2 toTarget = (hit.transform.position - ctx.Caster.position).normalized;
                float angle = Vector2.Angle(ctx.Direction, toTarget);
                if (angle <= arc * 0.5f)
                {
                    health.TakeDamage(Mathf.RoundToInt(ctx.Spell.damage));

                    var feedback = hit.GetComponent<CombatFeedback>();
                    if (feedback != null)
                        feedback.ApplyKnockback(ctx.Caster.position);
                }
            }

            if (VFXManager.Instance != null)
            {
                Color slashColor = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(1f, 1f, 1f, 0.7f);
                Vector3 vfxPos = ctx.Caster.position + (Vector3)(ctx.Direction * (hitRadius * 0.5f));
                VFXManager.Instance.SpawnSlashArc(vfxPos, ctx.Direction, slashColor, arc, hitRadius, 0.2f);
            }
        }
    }
}
