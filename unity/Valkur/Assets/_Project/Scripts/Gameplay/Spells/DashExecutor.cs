using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a dash spell: teleport via Rigidbody2D + optional collision damage.
    /// </summary>
    public class DashExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;
            var rb = ctx.Caster.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.MovePosition(rb.position + ctx.Direction * dist);

            if (ctx.Spell.collisionDamage > 0)
            {
                var hits = Physics2D.OverlapCircleAll(ctx.Caster.position, 1f, ctx.TargetLayers);
                foreach (var hit in hits)
                {
                    if (hit.gameObject == ctx.Caster.gameObject) continue;
                    var health = hit.GetComponent<Health>();
                    if (health != null && !health.IsDead)
                        health.TakeDamage(Mathf.RoundToInt(ctx.Spell.collisionDamage));
                }
            }
        }
    }
}
