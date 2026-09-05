using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Places a healing totem that periodically mends everything friendly inside its circle.
    ///
    /// <para><b>radius is WORLD UNITS.</b> It used to be <c>ctx.Spell.radius / 16f</c> — the
    /// SEVENTH sighting of the Python pixel scale in this project, after <c>wallWidth</c>, the
    /// vortex radius, <c>range</c> on three executors, <c>coneLength</c>, <c>arcane_flame</c>'s
    /// radius and <c>AuraExecutor</c>'s. The tell was the usual one: the fallback for an
    /// unauthored field (13.75 WORLD units) was sixty-four times anything the divide could
    /// produce from a sane asset. Shipped <c>sanctuary</c> authors 3.4 and was resolving to a
    /// heal circle <b>0.21 units</b> across — a fifth of a tile.</para>
    ///
    /// <para><b>NO COMPATIBILITY SHIM, and <c>healing_totem.asset</c> was re-authored in the
    /// same change</b> (13.75 -> 3.0, the world value its divide had been producing, rounded to
    /// something a designer can read). <c>PuddleExecutor</c> made exactly this call when its own
    /// divide was removed, and for the stated reason: a silent factor of sixteen is worse than a
    /// value that is obviously wrong.</para>
    /// </summary>
    public class TotemExecutor : ISpellExecutor
    {
        /// <summary>
        /// Tint used when a totem leaves <c>particleColor</c> unset. The gold this shipped as
        /// before the colour became authorable, so an untouched totem looks exactly as it did.
        /// </summary>
        private static readonly Color DefaultTint = new Color(1f, 0.9f, 0.3f, 1f);

        /// <summary>Heal radius used when the definition authors none, WORLD units.</summary>
        private const float DEFAULT_RADIUS = 3f;

        /// <summary>
        /// The colour this totem draws with — its band, its ring, its light, and the cast gather
        /// that precedes it. Same shape as <see cref="SlashExecutor.ResolveTint"/>, and for the
        /// same reason: the swatch reaches the flourish now, so a totem that ignored it would
        /// be announced in one colour and arrive in another.
        /// </summary>
        public static Color ResolveTint(SpellDefinition spell)
        {
            if (spell == null || KiPalette.IsUnauthored(spell.particleColor)) return DefaultTint;
            return spell.particleColor;
        }

        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 10f;
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : DEFAULT_RADIUS;
            float healPerTick = SpellPower.Scale(
                ctx.Spell.healPerTick > 0 ? ctx.Spell.healPerTick : 6f, ctx.Caster);
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.5f;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;

            Vector2 spawnPos = SpellTargeting.ResolveGroundTarget(ctx, 5f, dist);

            var totemGo = new GameObject("SpellTotem");
            totemGo.transform.position = (Vector3)spawnPos;

            // A totem is something you can bump into. The collider is unchanged from the
            // triangle-sprite version so the obstacle the player learned is the obstacle they
            // keep, even though the art it stood behind is gone.
            var col = totemGo.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, 0.7f);

            var controller = totemGo.AddComponent<TotemController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(healPerTick), tickPeriod,
                ctx.Caster, ResolveTint(ctx.Spell), ProjectileExecutor.ResolveElement(ctx.Spell));

            // No SpawnAreaIndicator. The rig draws its own ground ring, pinned to exactly the
            // circle the heal sweep queries; a second translucent halo over it would be a
            // second, contradictory promise about the same number.

            // Free-standing world object: nothing else can end it. The registry enforces
            // maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(totemGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }
    }
}
