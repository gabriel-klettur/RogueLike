using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Plants a persistent arcane flame zone. Everything the effect LOOKS like belongs to
    /// <see cref="ArcaneFlameController"/>; this executor only resolves where the zone
    /// lands, sizes it, and punctuates the cast.
    ///
    /// <para>Two things this file used to do, and no longer does. It generated a fresh
    /// 48x48 <c>Texture2D</c> per cast, wrapped it in a Sprite, hung it on a root
    /// <c>SpriteRenderer</c> — and the controller disabled that renderer four lines into
    /// its own build. Every cast leaked a texture that never drew a pixel. It also wrote
    /// <c>localScale = radius * 0.4f</c>, which the controller overwrote on the next line,
    /// so the number looked like it configured the zone's size and configured nothing.</para>
    ///
    /// <para>Two more, removed later. It resolved its own landing point from a private
    /// constant, so it was the only ground-placed spell in the project that could not be
    /// aimed — <c>spawnAtMouse</c> and <c>range</c> on its definition meant nothing, and
    /// clearing or setting them changed nothing on screen. And it divided <c>radius</c> by
    /// 16, the Python pixel scale this game was ported from: the fifth sighting after
    /// <c>wallWidth</c>, the totem, the vortex and the puddle. Both are gone —
    /// <see cref="SpellTargeting.ResolveGroundTarget"/> owns the first and the shipped
    /// definition authors world units for the second.</para>
    /// </summary>
    public class ArcaneFlameExecutor : ISpellExecutor
    {
        /// <summary>
        /// Cast reach when the definition authors no <c>range</c>. Also the clamp on the
        /// cursor, so the spell keeps a distance the player can learn.
        /// </summary>
        private const float FallbackCastRange = 6f;

        /// <summary>
        /// Where a NON-aimed flame sits, when the definition authors no <c>distance</c>.
        /// This is the constant the executor used to apply unconditionally.
        /// </summary>
        private const float PlacedFallbackDistance = 2f;

        /// <summary>Zone radius in WORLD UNITS when the definition authors none.</summary>
        private const float FallbackRadius = 2.5f;

        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 5f;
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : FallbackRadius;
            float damagePerTick = ctx.Spell.damagePerTick > 0 ? ctx.Spell.damagePerTick : 5f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.5f;

            Vector2 pos = SpellTargeting.ResolveGroundTarget(
                ctx, FallbackCastRange, PlacedFallbackDistance);

            var flameGo = new GameObject("ArcaneFlame");
            flameGo.transform.position = (Vector3)pos;
            // The root stays at identity scale: ArcaneFlameController derives every child's
            // absolute world size from the radius instead. A scaled root is what made the
            // old Light2D render at 2.5x its authored radius.

            var controller = flameGo.AddComponent<ArcaneFlameController>();
            GameObject casterGo = ctx.Caster != null ? ctx.Caster.gameObject : null;
            controller.Initialize(duration, radius, Mathf.RoundToInt(damagePerTick), tickPeriod,
                ctx.TargetLayers, casterGo, ProjectileExecutor.ResolveElement(ctx.Spell),
                // The authored swatch is what the fire is coloured with. It reaches the F4
                // panel through SpellFieldRelevance, so a designer changing it there changes
                // the flames — which is the whole reason that field stopped being the
                // opaque-white "nobody authored this" sentinel.
                ctx.Spell.particleColor);

            // Optional authored layer ON TOP of the controller's own rig. The shipped
            // arcane_flame deliberately authors no vfxPreset — the controller owns the
            // whole visual, and two emitters over one spot with independently authored
            // sizes and rates is the exact pattern SmokeExecutor removed. Kept as a seam
            // so a future preset can be layered in deliberately, PARENTED to the flame so
            // it dies with it instead of being orphaned at the abandoned spot.
            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
            {
                var fx = VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, (Vector3)pos, duration);
                if (fx != null) fx.transform.SetParent(flameGo.transform, worldPositionStays: true);
            }

            // One punch, at placement. Deliberately NOT per damage tick: ImpactLight's
            // authored min interval is 0.08 s, well under this spell's beat, so the
            // throttle would not hold it back and the only backstop left would be
            // MaxTraumaPerSecond. A real direction is required — Cue with Vector2.zero
            // produces no kick at all, only trauma.
            Feel.CameraFeel.Cue(Data.Feel.CameraFeelCue.ImpactMedium, ctx.Direction);

            // Free-standing world object: nothing else can end it. The registry enforces
            // maxInstances and clears it on a zone change. ArcaneFlameController implements
            // ISpellEffectDissipates, so an eviction here fades rather than cutting.
            SpellEffectRegistry.Track(flameGo, ctx.Spell, casterGo);
        }
    }
}
