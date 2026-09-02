using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Lights a sustained energy charge on the caster. Visual only — it deals no damage,
    /// affects no targets and changes no stat; the gameplay it will eventually feed hangs off
    /// the same <see cref="EnergyChargeController"/> when it exists.
    ///
    /// <para>TWO AUTHORED NUMBERS, and that is the whole design. <c>particleColor</c> is the
    /// one swatch the entire aura palette is derived from (see <see cref="KiPalette"/>), and
    /// <c>scale</c> is the INTENSITY — not a size. Intensity decides how many flame tongues
    /// there are, how hard the ki streams off them, whether the ground breaks up at all,
    /// whether lightning crawls over the aura, and how often the camera is hit. A low-intensity
    /// charge is meant to look CALM rather than small, which is why it is not expressed as a
    /// scale multiplier on everything.</para>
    ///
    /// <para><c>scale</c> is reused rather than adding a field because the alternative is a new
    /// serialized float on a definition already shared by 27 spell types, plus a row in the F4
    /// properties panel, plus a column in its table — surface that would exist for one type.
    /// The field's meaning here is documented in <c>SpellFieldRelevance</c> beside the set that
    /// makes it visible.</para>
    /// </summary>
    public class EnergyChargeExecutor : ISpellExecutor
    {
        private const float DefaultIntensity = 0.5f;
        private const float DefaultGroundRadius = 1.8f;
        private const float DefaultDurationSeconds = 5f;

        public void Execute(SpellContext ctx)
        {
            if (ctx.Caster == null || ctx.Spell == null) return;

            float intensity = ctx.Spell.scale > 0f ? Mathf.Clamp01(ctx.Spell.scale) : DefaultIntensity;
            float groundRadius = ctx.Spell.radius > 0f ? ctx.Spell.radius : DefaultGroundRadius;
            float duration = ctx.Spell.infinite
                ? float.PositiveInfinity
                : (ctx.Spell.duration > 0f ? ctx.Spell.duration : DefaultDurationSeconds);

            var go = new GameObject("EnergyCharge_" + ctx.Spell.spellKey);
            // Identity rotation and unit scale, and never parented to the caster: KiAuraFX
            // documents why both matter — a Light2D under a scaled transform renders its
            // authored radius at some other value.
            go.transform.position = ctx.Caster.position;

            var controller = go.AddComponent<EnergyChargeController>();
            controller.Initialize(new EnergyChargeController.Setup
            {
                Caster = ctx.Caster,
                Duration = duration,
                Palette = KiPalette.From(ctx.Spell.particleColor, intensity),
                GroundRadius = groundRadius,
            });

            // Free-standing world object with nothing else able to end it. The registry
            // enforces maxInstances — which is what makes recasting SWAP the charge rather
            // than stacking a second aura on the same body — and clears it on a zone change.
            SpellEffectRegistry.Track(go, ctx.Spell, ctx.Caster.gameObject);
        }
    }
}
