using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns a projectile in the cast direction with speed, damage, and lifetime from the spell definition.
    /// Uses VFXManager pool when available for frequently spawned projectiles.
    /// Python parity: fireball_trail / iceball_trail_strong / etc. particle presets.
    /// </summary>
    public class ProjectileExecutor : ISpellExecutor
    {
        private const string POOL_PREFIX = "proj_";

        public void Execute(SpellContext ctx)
        {
            if (ctx.ProjectilePrefab == null) return;

            string poolKey = POOL_PREFIX + ctx.Spell.spellKey;
            Vector3 spawnPos = ctx.Caster.position + (Vector3)(ctx.Direction * 0.5f);

            // Try pool-based spawn, fall back to Instantiate
            GameObject go = null;
            var vfxMgr = VFXManager.Instance;
            if (vfxMgr != null)
            {
                vfxMgr.RegisterPrefab(poolKey, ctx.ProjectilePrefab, 4);
                go = vfxMgr.Spawn(poolKey, spawnPos, Quaternion.identity);
            }
            if (go == null)
                go = Object.Instantiate(ctx.ProjectilePrefab, spawnPos, Quaternion.identity);
            go.SetActive(true);

            var proj = go.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.SetPoolKey(poolKey);
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

            // Apply scale from spell definition
            if (ctx.Spell.scale > 0 && ctx.Spell.scale != 1f)
                go.transform.localScale = Vector3.one * ctx.Spell.scale;

            // Apply particle color tint to sprite
            if (ctx.Spell.particleColor != Color.white)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.color = ctx.Spell.particleColor;
            }

            // VFX: spawn trail particle preset if defined
            if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset))
            {
                var vfx = ServiceLocator.Get<IVFXService>();
                if (vfx != null)
                    vfx.SpawnParticlePreset(ctx.Spell.vfxPreset, spawnPos, ctx.Spell.lifetime > 0 ? ctx.Spell.lifetime : 3f);
            }

            // VFX: spawn muzzle flash at caster
            var vfxService = ServiceLocator.Get<IVFXService>();
            if (vfxService != null)
            {
                Color flashColor = ctx.Spell.particleColor != Color.white ? ctx.Spell.particleColor : new Color(1f, 0.8f, 0.3f, 0.8f);
                vfxService.SpawnImpact(spawnPos, flashColor, 0.15f, 0.5f);
            }

            Debug.Log($"[SpellDebug] Projectile '{ctx.Spell.spellKey}' spawned at {spawnPos}, speed={ctx.Spell.speed}, dmg={ctx.Spell.damage}, lifetime={ctx.Spell.lifetime}");
        }
    }
}
