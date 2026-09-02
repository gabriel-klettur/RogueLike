using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns a vortex force field that drags bodies in or shoves them out.
    ///
    /// <para>The whole look belongs to <see cref="VortexFunnelFX"/>, built by
    /// <see cref="VortexFieldController"/>. This used to draw a spiral sprite of its own AND
    /// spawn a particle preset on top of the rig — three uncoordinated layers for one spell,
    /// and the sprite was disabled by the controller on the very next line, so its 64x64
    /// texture was generated and leaked once per cast for nothing.</para>
    /// </summary>
    public class VortexFieldExecutor : ISpellExecutor
    {
        // Fallbacks in WORLD UNITS, for a definition that authors none. The old ones (17.5 and
        // 87.5) were the Python build's numbers in its own units; carried across unchanged they
        // drew a circle wider than the screen and applied a force nothing bounded.
        private const float FallbackRadius = 3.6f;
        private const float FallbackForce = 24f;
        private const float FallbackDuration = 2f;

        /// <summary>How far a cursor-aimed vortex may be placed from its caster when the
        /// definition authors no <c>range</c>. Both shipped vortices author one.</summary>
        private const float FallbackCastRange = 10f;

        /// <summary>Where a NON-aimed vortex sits, when it authors no <c>distance</c>.</summary>
        private const float PlacedFallbackDistance = 2f;

        public void Execute(SpellContext ctx)
        {
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : FallbackRadius;
            float force = ctx.Spell.force > 0 ? ctx.Spell.force : FallbackForce;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : FallbackDuration;
            bool isPull = string.IsNullOrEmpty(ctx.Spell.forceMode) || ctx.Spell.forceMode == "pull";

            // `|| isPull` used to be here, forcing the offset whichever way the flag was set.
            // A hard-coded override of authored data makes the field unfalsifiable for half the
            // spells that carry it: vortex_pull could not be placed on its caster even by
            // clearing the box.
            Vector2 spawnPos = SpellTargeting.ResolveGroundTarget(
                ctx, FallbackCastRange, PlacedFallbackDistance);

            var vortexGo = new GameObject(isPull ? "VortexPull" : "VortexPush");
            vortexGo.transform.position = (Vector3)spawnPos;

            var controller = vortexGo.AddComponent<VortexFieldController>();
            controller.Initialize(duration, radius, force, isPull,
                ctx.Spell.followCaster ? ctx.Caster : null, ctx.TargetLayers,
                ResolveSwatch(ctx.Spell));

            // Free-standing world object: nothing else can end it. The registry enforces
            // maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(vortexGo, ctx.Spell,
                ctx.Caster != null ? ctx.Caster.gameObject : null);
        }

        /// <summary>
        /// The colour the funnel is drawn in. Public because the cast flourish asks the same
        /// question of the same spell and the two answering differently is exactly the split
        /// that made a red gather hand over to a violet field.
        /// </summary>
        public static Color ResolveSwatch(SpellDefinition spell)
        {
            return spell == null ? Color.white : spell.particleColor;
        }
    }
}
