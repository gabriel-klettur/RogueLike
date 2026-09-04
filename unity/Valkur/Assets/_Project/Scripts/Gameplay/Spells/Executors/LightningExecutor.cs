using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Both electric spells.
    ///
    /// <c>SpellType.Lightning</c> is a directed strike: the bolt leaves the caster's hand
    /// along the cast direction, latches onto the first enemy inside a corridor, and
    /// discharges into the ground at maximum range when there is nobody there. It used to
    /// share the chain implementation, whose first act is <c>if (sorted.Count == 0) return;</c>
    /// — so casting it with no enemy nearby consumed mana, played nothing and drew nothing.
    /// That is the whole reason it looked broken: most casts had no target yet.
    ///
    /// <c>SpellType.ChainLightning</c> still jumps between enemies, but it too always draws
    /// something. A spell that can silently do nothing cannot be learned.
    /// </summary>
    public class LightningExecutor : ISpellExecutor
    {
        private const float DEFAULT_STRIKE_RANGE = 9f;
        private const float DEFAULT_SPLASH_RADIUS = 1.6f;
        private const float DEFAULT_CHAIN_RANGE = 8f;
        private const float DEFAULT_JUMP_RANGE = 4f;

        /// <summary>
        /// Links a chain may form. Deliberately a constant rather than the spell's
        /// <c>maxInstances</c>, which the previous version borrowed: <c>chain_lightning</c>'s
        /// authored value of 1 would have capped the chain at a single jump. Note that
        /// <c>maxInstances</c> and <c>allowOverlap</c> are today authored metadata — stored on
        /// the asset, editable in the F4 Spells editor, and read by NOTHING: neither SpellCaster
        /// nor any executor counts live instances or destroys a previous one. The only real
        /// concurrency limit is <c>prepareDuration + channelDuration + cooldownDuration</c>
        /// measured against how long the effect lives. The real limiter here is the jump
        /// distance and how many enemies are standing within it.
        /// </summary>
        private const int MAX_CHAIN_LINKS = 4;

        /// <summary>Narrowest corridor a strike will accept a target inside, in world units.</summary>
        private const float MIN_CORRIDOR_HALF_WIDTH = 0.75f;

        /// <summary>Damage retained per extra jump: 100%, 75%, 56%, …</summary>
        private const float CHAIN_FALLOFF = 0.75f;

        private static readonly Color DefaultTint = new Color(0.6f, 0.85f, 1f, 1f);

        public void Execute(SpellContext ctx)
        {
            // The arc leaves from the exact same hand point as Fireball.
            Vector2 castPos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
            Color tint = ctx.Spell.particleColor != Color.clear ? ctx.Spell.particleColor : DefaultTint;

            if (ctx.Spell.type == SpellType.ChainLightning) ExecuteChain(ctx, castPos, tint);
            else ExecuteStrike(ctx, castPos, tint);

            if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset) && VFXManager.Instance != null)
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, castPos);
        }

        // ── Directed strike ─────────────────────────────────────────────────

        private static void ExecuteStrike(SpellContext ctx, Vector2 castPos, Color tint)
        {
            float range = ctx.Spell.range > 0f ? ctx.Spell.range : DEFAULT_STRIKE_RANGE;
            float splash = ctx.Spell.radius > 0f ? ctx.Spell.radius : DEFAULT_SPLASH_RADIUS;
            Vector2 direction = ctx.Direction.sqrMagnitude > 0.0001f
                ? ctx.Direction.normalized
                : Vector2.right;

            Vector2 impact = ResolveStrikePoint(ctx, castPos, direction, range, splash);

            LightningBoltFX.Spawn(castPos, impact, tint);
            LightningImpactFX.Spawn(impact, tint, splash * 0.85f);

            ApplySplash(ctx, impact, splash);
        }

        /// <summary>
        /// Nearest enemy inside the corridor swept by the cast direction, or the far end of
        /// the range when there is none. Returning a point either way is what guarantees the
        /// spell is always visible.
        /// </summary>
        private static Vector2 ResolveStrikePoint(SpellContext ctx, Vector2 castPos,
                                                  Vector2 direction, float range, float splash)
        {
            Vector2 fallback = castPos + direction * range;
            if (ctx.TargetLayers.value == 0) return fallback;

            float corridor = Mathf.Max(MIN_CORRIDOR_HALF_WIDTH, splash);
            var candidates = Physics2D.OverlapCircleAll(castPos, range, ctx.TargetLayers);

            Vector2 best = fallback;
            float bestForward = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                if (!IsLiveTarget(ctx, candidates[i])) continue;

                Vector2 point = candidates[i].bounds.center;
                Vector2 delta = point - castPos;
                float forward = Vector2.Dot(delta, direction);
                if (forward <= 0f || forward > range) continue;

                float lateral = Mathf.Abs(delta.x * direction.y - delta.y * direction.x);
                if (lateral > corridor) continue;

                if (forward >= bestForward) continue;
                bestForward = forward;
                best = point;
            }

            return best;
        }

        private static void ApplySplash(SpellContext ctx, Vector2 impact, float splash)
        {
            if (ctx.TargetLayers.value == 0) return;

            int damage = SpellPower.ScaleToInt(ctx.Spell.damage, ctx.Caster);
            if (damage <= 0) return;

            var struck = Physics2D.OverlapCircleAll(impact, splash, ctx.TargetLayers);
            for (int i = 0; i < struck.Length; i++)
            {
                if (!IsLiveTarget(ctx, struck[i])) continue;
                Deal(ctx, struck[i], damage);
            }
        }

        // ── Chain ───────────────────────────────────────────────────────────

        private static void ExecuteChain(SpellContext ctx, Vector2 castPos, Color tint)
        {
            float range = ctx.Spell.range > 0f ? ctx.Spell.range : DEFAULT_CHAIN_RANGE;
            float jumpDistance = ctx.Spell.radius > 0f ? ctx.Spell.radius : DEFAULT_JUMP_RANGE;

            var remaining = new List<Collider2D>();
            if (ctx.TargetLayers.value != 0)
            {
                var candidates = Physics2D.OverlapCircleAll(castPos, range, ctx.TargetLayers);
                for (int i = 0; i < candidates.Length; i++)
                    if (IsLiveTarget(ctx, candidates[i])) remaining.Add(candidates[i]);
            }

            if (remaining.Count == 0)
            {
                DischargeIntoTheGround(ctx, castPos, range, tint);
                return;
            }

            Vector2 origin = castPos;
            int baseDamage = SpellPower.ScaleToInt(ctx.Spell.damage, ctx.Caster);

            for (int link = 0; link < MAX_CHAIN_LINKS; link++)
            {
                // Each jump is measured from the link it leaves, not from the caster, so a
                // chain follows the shape of the pack instead of a ring around the player.
                float reach = link == 0 ? range : jumpDistance;
                Collider2D next = TakeNearest(remaining, origin, reach);
                if (next == null) break;

                Vector2 point = next.bounds.center;
                float thickness = Mathf.Pow(0.82f, link);

                LightningBoltFX.Spawn(origin, point, tint, shake: link == 0, thickness: thickness);
                LightningImpactFX.Spawn(point, tint, thickness);

                if (baseDamage > 0)
                    Deal(ctx, next, Mathf.Max(1, Mathf.RoundToInt(baseDamage * Mathf.Pow(CHAIN_FALLOFF, link))));

                origin = point;
            }
        }

        /// <summary>
        /// Nothing in reach: the charge still has to go somewhere. Discharging forward keeps
        /// the cast readable and tells the player the spell fired and simply found nobody.
        /// </summary>
        private static void DischargeIntoTheGround(SpellContext ctx, Vector2 castPos,
                                                   float range, Color tint)
        {
            Vector2 direction = ctx.Direction.sqrMagnitude > 0.0001f
                ? ctx.Direction.normalized
                : Vector2.right;
            Vector2 end = castPos + direction * (range * 0.5f);

            LightningBoltFX.Spawn(castPos, end, tint, shake: true, thickness: 0.7f);
            LightningImpactFX.Spawn(end, tint, 0.7f);
        }

        private static Collider2D TakeNearest(List<Collider2D> pool, Vector2 origin, float reach)
        {
            int bestIndex = -1;
            float bestSqr = reach * reach;

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null) continue;
                float sqr = ((Vector2)pool[i].bounds.center - origin).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                bestIndex = i;
            }

            if (bestIndex < 0) return null;
            Collider2D found = pool[bestIndex];
            pool.RemoveAt(bestIndex);
            return found;
        }

        // ── Shared ──────────────────────────────────────────────────────────

        private static bool IsLiveTarget(SpellContext ctx, Collider2D collider)
        {
            if (collider == null) return false;
            if (ctx.Caster != null && collider.transform.IsChildOf(ctx.Caster)) return false;

            Health health = collider.GetComponentInParent<Health>();
            return health != null && !health.IsDead;
        }

        private static void Deal(SpellContext ctx, Collider2D collider, int damage)
        {
            Health health = collider.GetComponentInParent<Health>();
            if (health == null || health.IsDead) return;

            // Attributed + typed: an unattributed hit leaves CameraFeelDirector's Hurt cue
            // with no direction and PlayerHurtReaction with nothing to face (see Health.cs's
            // "anything with a location should use the overload" doc).
            GameObject casterGo = ctx.Caster != null ? ctx.Caster.gameObject : null;
            health.TakeDamage(damage, casterGo, ProjectileExecutor.ResolveElement(ctx.Spell));
            GameEvents.FireHitDealt(casterGo, health.gameObject, damage);
            Combat.StatusApplicationFactory.ApplyAll(ctx.Spell.statusApplications, health.gameObject, casterGo);
        }
    }
}
