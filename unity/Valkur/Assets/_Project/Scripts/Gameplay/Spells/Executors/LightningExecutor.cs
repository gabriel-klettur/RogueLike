using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Chain lightning: hits the nearest target, then jumps to successive nearby enemies.
    /// Number of jumps = spell.maxInstances (default 3).
    /// Jump distance = spell.radius (default 4).
    /// Mirrors Python's LightningResolver: sequential chain with VFX per arc.
    /// </summary>
    public class LightningExecutor : ISpellExecutor
    {
        private const float DEFAULT_JUMP_RANGE = 4f;
        private const int   DEFAULT_MAX_CHAINS = 3;

        public void Execute(SpellContext ctx)
        {
            int maxChains  = ctx.Spell.maxInstances > 0 ? ctx.Spell.maxInstances : DEFAULT_MAX_CHAINS;
            float range    = ctx.Spell.range > 0 ? ctx.Spell.range : 8f;
            float jumpDist = ctx.Spell.radius > 0 ? ctx.Spell.radius : DEFAULT_JUMP_RANGE;

            // The first arc leaves from the exact same hand point as Fireball.
            Vector2 castPos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
            var candidates = Physics2D.OverlapCircleAll(castPos, range, ctx.TargetLayers);

            // Sort by distance, excluding caster
            var sorted = new List<(Collider2D col, float dist)>();
            foreach (var c in candidates)
            {
                if (c.gameObject == ctx.Caster.gameObject) continue;
                var h = c.GetComponent<Health>();
                if (h == null || h.IsDead) continue;
                sorted.Add((c, ((Vector2)c.transform.position - castPos).sqrMagnitude));
            }
            sorted.Sort((a, b) => a.dist.CompareTo(b.dist));

            if (sorted.Count == 0) return;

            Color col = ctx.Spell.particleColor != Color.clear
                ? ctx.Spell.particleColor
                : new Color(0.6f, 0.8f, 1f, 1f);

            var hit = new HashSet<GameObject>();
            Vector2 chainOrigin = castPos;
            int chains = 0;

            foreach (var (c, _) in sorted)
            {
                if (chains >= maxChains) break;
                if (hit.Contains(c.gameObject)) continue;

                float d = ((Vector2)c.transform.position - chainOrigin).magnitude;
                if (chains > 0 && d > jumpDist) continue;

                var health = c.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    // Damage falls off per jump: 100% → 75% → 56% ...
                    float falloff = Mathf.Pow(0.75f, chains);
                    int dealt = Mathf.RoundToInt(ctx.Spell.damage * falloff);
                    health.TakeDamage(dealt);
                    Valkur.Core.GameEvents.FireHitDealt(ctx.Caster.gameObject, c.gameObject, dealt);
                }

                hit.Add(c.gameObject);

                // Epic procedural lightning bolt from previous link to this target
                LightningBoltFX.Spawn(
                    new Vector3(chainOrigin.x, chainOrigin.y, 0f),
                    c.transform.position,
                    col,
                    shake: chains == 0); // shake only on first arc to avoid spam

                if (VFXManager.Instance != null)
                    VFXManager.Instance.SpawnImpact(c.transform.position, col, 0.2f);

                chainOrigin = c.transform.position;
                chains++;
            }

            if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset) && VFXManager.Instance != null)
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, castPos);
        }
    }
}
