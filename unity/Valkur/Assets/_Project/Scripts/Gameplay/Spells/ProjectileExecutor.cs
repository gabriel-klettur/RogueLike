using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns a projectile in the cast direction with speed, damage, and lifetime from the spell definition.
    /// </summary>
    public class ProjectileExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            if (ctx.ProjectilePrefab == null) return;

            Vector3 spawnPos = ctx.Caster.position + (Vector3)(ctx.Direction * 0.5f);
            var go = Object.Instantiate(ctx.ProjectilePrefab, spawnPos, Quaternion.identity);
            go.SetActive(true);

            var proj = go.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.Initialize(
                    ctx.Direction,
                    ctx.Spell.speed,
                    ctx.Spell.damage,
                    ctx.Spell.lifetime > 0 ? ctx.Spell.lifetime : 3f,
                    ctx.Spell.range > 0 ? ctx.Spell.range : 20f,
                    ctx.TargetLayers
                );
            }

            if (ctx.Spell.sprite != null)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.sprite = ctx.Spell.sprite;
            }
        }
    }
}
