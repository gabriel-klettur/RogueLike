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
            // Resolve caster center from the visual sprite bounds so projectiles originate
            // from the middle of the character, not the pivot (which is at the feet for 2D
            // sprites with bottom-center pivot). Falls back to collider bounds, then to a
            // fixed +0.5 Y offset.
            Vector3 casterCenter = ResolveCasterCenter(ctx.Caster);

            // Spawn slightly in front of the caster along the fire direction so the
            // projectile clears the caster's own collider and doesn't start overlapping
            // adjacent walls/buildings (which would make the swept-collision detect a
            // distance==0 hit and detonate the fireball at the player's feet).
            const float SPAWN_FORWARD_OFFSET = 0.5f;
            Vector3 spawnPos = casterCenter
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
            AttachElementalVisual(go, ctx.Spell);

            var proj = go.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.SetPoolKey(poolKey);
                // Bind the caster BEFORE Initialize/FixedUpdate so the very first
                // sweep can already filter caster-owned colliders. Without this,
                // a caster with a child collider on the target layer (perception
                // trigger, hurtbox) would self-damage on spawn frame ("fireball
                // blew up in my face").
                proj.SetCaster(ctx.Caster);
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
                // Acceleration: reuses the SpellDefinition.distance field (unused for projectiles).
                if (ctx.Spell.distance > 0f)
                    proj.SetAcceleration(ctx.Spell.distance);
                // AOE explosion on impact.
                if (ctx.Spell.explosionRadius > 0f)
                    proj.SetExplosion(ctx.Spell.explosionRadius,
                        ctx.Spell.explosionDamage > 0f ? ctx.Spell.explosionDamage : ctx.Spell.damage);
            }

            if (ctx.Spell.sprite != null)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = ctx.Spell.sprite;
                    // Apply visual scale to the sprite child only â€” never to the root GO.
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

        }

        /// <summary>
        /// Resolves the world-space "center" of a caster for projectile spawning.
        /// Priority: SpriteRenderer.bounds.center (visual center) â†’ any Collider2D
        /// bounds.center â†’ transform.position. A guaranteed minimum upward lift of
        /// <see cref="MIN_LIFT_ABOVE_PIVOT"/> world units is applied so the spawn
        /// never sits at the feet of a 2D character (handles sprites with
        /// center pivot, null sprite frames, and centered colliders).
        /// Public + static so it can be unit-tested independently from spell execution.
        /// </summary>
        public const float MIN_LIFT_ABOVE_PIVOT = 0.5f;

        public static Vector3 ResolveCasterCenter(Transform caster)
        {
            if (caster == null) return Vector3.zero;

            Vector3 fallback = caster.position + new Vector3(0f, MIN_LIFT_ABOVE_PIVOT, 0f);
            Vector3 result = fallback;

            // 1. Prefer the visual sprite bounds. Use root component first so we
            //    don't accidentally pick up a child shadow / aura SpriteRenderer.
            var sr = caster.GetComponent<SpriteRenderer>();
            if (sr == null) sr = caster.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                result = sr.bounds.center;
            }
            else
            {
                // 2. Fall back to any Collider2D bounds.
                var col2d = caster.GetComponent<Collider2D>();
                if (col2d == null) col2d = caster.GetComponentInChildren<Collider2D>();
                if (col2d != null) result = col2d.bounds.center;
            }

            // 3. Enforce a minimum lift above the transform pivot. This handles
            //    center-pivot sprites and centered colliders (offset 0,0) where
            //    bounds.center coincides with the character's feet.
            float minY = caster.position.y + MIN_LIFT_ABOVE_PIVOT;
            if (result.y < minY) result.y = minY;
            return result;
        }

        /// <summary>
        /// Attach the element-specific procedural visual for this spell.
        /// Reads <see cref="SpellDefinition.element"/> first (data-driven path
        /// that lets designers add new spells without recompiling), falls
        /// back to the legacy spellKey switch for spells whose JSON imports
        /// haven't been re-run with the element column populated.
        ///
        /// The shared projectile prefab is pre-stamped with
        /// <see cref="FireballVisual"/> (orange/red flame) by
        /// <c>ProjectilePrefabFactory</c>. That bespoke visual is correct for
        /// fireball but WRONG for iceball / lightball / darkball / arcane —
        /// without an explicit swap they all render as orange flame in-game
        /// AND in the F4 Spells Editor preview. So when the resolved element
        /// disagrees with the existing visual we tear the wrong rig down and
        /// build the matching <see cref="ElementalProjectileVisual"/> palette.
        /// Idempotent across pool re-spawns.
        /// </summary>
        private static void AttachElementalVisual(GameObject go, SpellDefinition spell)
        {
            SpellElement? element = ResolveElement(spell);
            var existing = go.GetComponent<IProjectileVisual>();

            // Already running ElementalProjectileVisual — just retune the palette.
            // Cheap when the element already matches (SetElement early-outs).
            if (existing is ElementalProjectileVisual epv)
            {
                if (element.HasValue) epv.SetElement(element.Value);
                return;
            }

            // FireballVisual is the right rig for Fire (and the safe default when
            // the spell has no element hint at all).
            if (existing is FireballVisual)
            {
                if (!element.HasValue || element.Value == SpellElement.Fire) return;
                // Wrong element — strip the fire rig (component + its child layers)
                // so the new visual builds against an empty projectile root.
                ClearProjectileVisualChildren(go);
                Object.Destroy((Component)existing);
            }

            if (!element.HasValue) return;

            var v = go.AddComponent<ElementalProjectileVisual>();
            v.SetElement(element.Value);
        }

        /// <summary>
        /// Removes every child GameObject under <paramref name="go"/>'s root —
        /// covers Halo / Glow / Core / HotCore / Ghost0..N / FireballLight that
        /// <see cref="FireballVisual.BuildVisual"/> creates as direct children.
        /// The projectile root itself (Rigidbody2D / collider / Projectile) stays
        /// intact; only visual scaffolding is wiped.
        /// </summary>
        private static void ClearProjectileVisualChildren(GameObject go)
        {
            var t = go.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var ch = t.GetChild(i);
                if (ch != null) Object.Destroy(ch.gameObject);
            }
        }

        // Public + static so tests can pin the precedence: SO field wins,
        // legacy spellKey switch is the fallback.
        public static SpellElement? ResolveElement(SpellDefinition spell)
        {
            if (spell == null) return null;

            // Prefer the SO's `element` field â€” data-driven, designer-editable.
            if (!string.IsNullOrWhiteSpace(spell.element))
            {
                if (System.Enum.TryParse<SpellElement>(spell.element, ignoreCase: true, out var parsed))
                    return parsed;
            }

            // Legacy fallback: per-key switch for spells whose data hasn't yet
            // been migrated to the SO `element` field. New spell keys should
            // populate `element` instead of growing this table.
            return MapSpellKeyToElement(spell.spellKey);
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
