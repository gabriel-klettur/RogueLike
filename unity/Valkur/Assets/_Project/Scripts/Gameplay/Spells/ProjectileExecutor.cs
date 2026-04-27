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
            // Spawn slightly in front of the caster along the fire direction so the
            // projectile clears the caster's own collider and doesn't start overlapping
            // adjacent walls/buildings (which would make the swept-collision detect a
            // distance==0 hit and detonate the fireball at the player's feet).
            const float SPAWN_FORWARD_OFFSET = 0.5f;
            Vector3 spawnPos = ctx.Caster.position
                + (Vector3)(ctx.Direction.normalized * SPAWN_FORWARD_OFFSET);

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

            // Attach the correct procedural visual for this element. Idempotent: if the
            // prefab already has the right component (e.g. fireball comes pre-configured)
            // we don't add another one.
            AttachElementalVisual(go, ctx.Spell.spellKey);

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
                if (!string.IsNullOrEmpty(ctx.Spell.impactPreset))
                    proj.SetImpactPreset(ctx.Spell.impactPreset);
            }

            if (ctx.Spell.sprite != null)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = ctx.Spell.sprite;
                    // Apply visual scale to the sprite child only — never to the root GO.
                    // Python's spell.scale is a sprite pixel-scale factor (e.g. 0.05 for fireball),
                    // not a world-unit scale. Applying it to the root GO would shrink physics
                    // colliders and make procedural visuals (FireballVisual) invisible.
                    if (ctx.Spell.scale > 0 && ctx.Spell.scale != 1f)
                        sr.transform.localScale = Vector3.one * ctx.Spell.scale;
                }
            }

            // Apply particle color tint to sprite
            if (ctx.Spell.particleColor != Color.white)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.color = ctx.Spell.particleColor;
            }

            // VFX: spawn muzzle flash at caster
            // NOTE: vfxPreset (trail) is handled by FireballVisual on the projectile itself.
            // impactPreset is applied at impact position via Projectile.Expire().
            var vfxService = ServiceLocator.Get<IVFXService>();
            if (vfxService != null)
            {
                Color flashColor = ctx.Spell.particleColor != Color.white ? ctx.Spell.particleColor : new Color(1f, 0.8f, 0.3f, 0.8f);
                vfxService.SpawnImpact(spawnPos, flashColor, 0.15f, 0.5f);
            }

            Debug.Log($"[SpellDebug] Projectile '{ctx.Spell.spellKey}' spawned at {spawnPos}, speed={ctx.Spell.speed}, dmg={ctx.Spell.damage}, lifetime={ctx.Spell.lifetime}");
        }

        /// <summary>
        /// Attach the element-specific procedural visual based on spellKey. Each
        /// element (dark/ice/light/lightning/arcane) gets its own palette-driven
        /// <see cref="ElementalProjectileVisual"/>. Fireball keeps the bespoke
        /// <see cref="FireballVisual"/> already configured on its prefab.
        /// </summary>
        private static void AttachElementalVisual(GameObject go, string spellKey)
        {
            // If the prefab already carries any IProjectileVisual (e.g. FireballVisual),
            // respect it.
            if (go.GetComponent<IProjectileVisual>() != null) return;

            SpellElement? element = MapSpellKeyToElement(spellKey);
            if (!element.HasValue) return;

            var v = go.AddComponent<ElementalProjectileVisual>();
            v.SetElement(element.Value);
        }

        private static SpellElement? MapSpellKeyToElement(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey)) return null;
            switch (spellKey)
            {
                case "darkball":  return SpellElement.Dark;
                case "iceball":   return SpellElement.Ice;
                case "lightball": return SpellElement.Light;
                case "arcane_flame": return SpellElement.Arcane;
                case "firework_launch": return SpellElement.Fire;
                // boomerang/chain_lightning use their own controllers, not ProjectileExecutor
                default: return null;
            }
        }
    }
}
