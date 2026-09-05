using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a healing aura: creates a lingering area that heals allies within radius.
    /// Mirrors Python's AuraResolver with heal_per_second buff. All visuals are built
    /// procedurally inside <see cref="AuraController"/> for an "epic" holy/nature look
    /// (ground rune, light pillar, rising sparkles, pulsing rings, Light2D glow).
    /// </summary>
    public class AuraExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration    = ctx.Spell.duration    > 0 ? ctx.Spell.duration : 6f;
            // WORLD UNITS. This used to divide by 16 -- the sixth sighting of the Python
            // pixel scale in this project, after wallWidth, the totem's radius, the vortex's
            // radius, coneLength and arcane_flame's radius. It survived because the value was
            // then DISCARDED by InitializeHealing ("reserved for future logic"), so the wrong
            // number was never read: shipped healing_aura authors 0.625 and resolved to a
            // gameplay radius of 0.039 world units, under a fortieth of a tile.
            float radius      = ctx.Spell.radius      > 0 ? ctx.Spell.radius : 1.5f;
            float tickPeriod  = ctx.Spell.tickPeriod  > 0 ? ctx.Spell.tickPeriod : 0.5f;

            // Ensure a minimum on-screen footprint so the rune is always readable.
            float visualRadius = Mathf.Max(radius, 1.25f);

            // damagePerTick is the discriminator, not a new SpellType. An aura is a circle
            // that ticks on whatever is inside it; whether that tick heals or hurts is the
            // only difference, and a second enum value would have forced a second executor,
            // a second flourish family entry and a second row in every table that lists them.
            bool damaging = ctx.Spell.damagePerTick > 0f;

            var auraGo = new GameObject(damaging ? "SpellAura_Static" : "SpellAura_Healing");

            // A DAMAGING field FOLLOWS its caster; it is not parented to them. Parenting
            // inherits the entity's scale, and a scaled parent renders a Light2D at
            // `authored x lossyScale` — the failure that once put a spell light at an effective
            // 367 world units — while also scaling a dome whose radius is supposed to BE the
            // damage radius. The healing variant stays parented: nothing under it carries a
            // world radius, and it is literally an effect on the caster's own body.
            if (damaging)
            {
                auraGo.transform.position = ctx.Caster != null
                    ? ctx.Caster.position
                    : (Vector3)ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
                auraGo.transform.localScale = Vector3.one;
            }
            else
            {
                auraGo.transform.SetParent(ctx.Caster, false);
                auraGo.transform.localPosition = Vector3.zero;
            }

            var controller = auraGo.AddComponent<AuraController>();

            if (damaging)
            {
                controller.InitializeDamaging(
                    duration:      duration,
                    gameRadius:    radius,
                    visualRadius:  visualRadius,
                    damagePerTick: Mathf.RoundToInt(SpellPower.Scale(ctx.Spell.damagePerTick, ctx.Caster)),
                    tickPeriod:    tickPeriod,
                    caster:        ctx.Caster,
                    targetLayers:  ctx.TargetLayers,
                    statuses:      ctx.Spell.statusApplications,
                    // The raw swatch, resolved ONE level down through
                    // ElementPalette.RecolouredTo — which already answers all three meanings of
                    // particleColor in the right order: opaque white is the "nobody authored
                    // this" sentinel, an achromatic value is a request for the ABSENCE of
                    // colour, and near-black adds nothing on an additive material.
                    tint:          ctx.Spell.particleColor,
                    element:       ProjectileExecutor.ResolveElement(ctx.Spell));
            }
            else
            {
                float healPerTick = SpellPower.Scale(
                    ctx.Spell.healPerTick > 0 ? ctx.Spell.healPerTick : 20f, ctx.Caster);
                controller.InitializeHealing(
                    duration:     duration,
                    gameRadius:   radius,
                    visualRadius: visualRadius,
                    healPerTick:  Mathf.RoundToInt(healPerTick),
                    tickPeriod:   tickPeriod,
                    caster:       ctx.Caster);
            }

            // Keep data-driven preset support (extra particles on top of procedural FX).
            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell), duration);

        }
    }
}
