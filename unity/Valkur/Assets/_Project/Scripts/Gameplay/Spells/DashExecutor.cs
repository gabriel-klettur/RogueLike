using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a dash spell: teleport via Rigidbody2D + optional collision damage + trail VFX.
    /// Python parity: dash_trail_emitter particles + knockback on hit.
    /// </summary>
    public class DashExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;
            Vector2 startPos = ctx.Caster.position;
            var rb = ctx.Caster.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.MovePosition(rb.position + ctx.Direction * dist);

            // VFX: dash trail particles along path
            var vfx = ServiceLocator.Get<IVFXService>();
            if (vfx != null)
            {
                string preset = !string.IsNullOrEmpty(ctx.Spell.vfxPreset) ? ctx.Spell.vfxPreset : "dash_trail_emitter";
                vfx.SpawnParticlePreset(preset, startPos, 0.5f);
                // Midpoint trail
                Vector3 midPos = (Vector3)startPos + (Vector3)(ctx.Direction * dist * 0.5f);
                vfx.SpawnImpact(midPos, new Color(0.5f, 0.7f, 1f, 0.6f), 0.25f, dist * 0.3f);
            }

            // Collision damage + knockback on enemies along dash path
            if (ctx.Spell.collisionDamage > 0)
            {
                var hits = Physics2D.OverlapCircleAll(ctx.Caster.position, 1f, ctx.TargetLayers);
                foreach (var hit in hits)
                {
                    if (hit.gameObject == ctx.Caster.gameObject) continue;
                    var health = hit.GetComponent<Health>();
                    if (health != null && !health.IsDead)
                    {
                        health.TakeDamage(Mathf.RoundToInt(ctx.Spell.collisionDamage));
                        // Knockback
                        if (ctx.Spell.knockback > 0)
                        {
                            var hitRb = hit.GetComponent<Rigidbody2D>();
                            if (hitRb != null)
                            {
                                Vector2 knockDir = ((Vector2)hit.transform.position - (Vector2)ctx.Caster.position).normalized;
                                hitRb.AddForce(knockDir * ctx.Spell.knockback, ForceMode2D.Impulse);
                            }
                        }
                    }
                }
            }

            Debug.Log($"[SpellDebug] Dash from {startPos} dist={dist:F1}, collisionDmg={ctx.Spell.collisionDamage}");
        }
    }
}
