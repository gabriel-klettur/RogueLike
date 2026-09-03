using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Launches a firework shell: it arcs from the caster's hand toward whatever they are
    /// aiming at, opens into a chrysanthemum of coloured stars, and its report arrives a few
    /// frames late.
    ///
    /// <para>The executor's whole job is to turn authored data into three numbers and hand
    /// them to <see cref="FireworkShellController"/>, which owns the timeline. It is
    /// deliberately not a projectile any more — see that class for why riding
    /// <c>ProjectileExecutor</c> cost this spell its entire second half.</para>
    ///
    /// <para>WHAT THE FIELDS MEAN HERE. <c>range</c> is the FLIGHT DISTANCE in world units,
    /// <c>speed</c> is the FLIGHT SPEED in world units per second, and <c>radius</c> is the
    /// burst radius in world units. All three are read straight through with no divisor: this
    /// spell is a cosmetic one and had no reason to be, but five other spells in this project
    /// shipped authored in the Python pixel scale and silently divided by 16 somewhere, so the
    /// absence of a divide is worth saying out loud.</para>
    /// </summary>
    public class FireworkLaunchExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            if (ctx.Spell == null) return;

            // The canonical launch point every caster-emitted spell uses: hand height plus the
            // spell's own forward clearance.
            Vector3 origin = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            // ctx.Direction is the cursor bearing for a player — PlayerController resolves it
            // through PlayerFacingResolver, which reads the mouse via MouseInputManager. Handing
            // it straight to the shell is what makes this spell aim like every other one; the
            // version before it used only the x component, as a 35% lateral nudge on a climb
            // that was always straight up, so aiming barely moved the burst and never moved it
            // down or behind.

            var palette = FireworkPalette.From(ctx.Spell.particleColor);

            FireworkShellController.Launch(
                origin,
                ctx.Direction,
                palette,
                flightDistance: Resolve(ctx.Spell.range, FireworkShellController.DEFAULT_FLIGHT_DISTANCE),
                flightSpeed: Resolve(ctx.Spell.speed, FireworkShellController.DEFAULT_FLIGHT_SPEED),
                burstRadius: Resolve(ctx.Spell.radius, FireworkShellController.DEFAULT_BURST_RADIUS));

            // No PlaySfxById here on purpose. It used to ask the catalog for
            // "spell_firework_launch", an id AudioCatalog.asset has never contained — the call
            // produced one warning per session and no sound, and BossDefinitionDataIntegrityTests
            // already forbids that id for the same reason. FireworkAudio synthesises the four
            // one-shots instead, and the controller plays them on the beats they belong to.
        }

        /// <summary>
        /// An unauthored numeric field reads 0, which is never a meaningful distance, speed or
        /// radius — so 0 means "use the system default" and every other value is literal.
        /// </summary>
        private static float Resolve(float authored, float fallback)
            => authored > 0f ? authored : fallback;
    }
}
