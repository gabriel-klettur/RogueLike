namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Does nothing, on purpose.
    ///
    /// An <c>AnimationProbe</c> spell exists so that every animation a character has authored
    /// can be selected and watched in the Spells Editor — including the ones no gameplay spell
    /// will ever play, because the state they live in is owned by locomotion, the damage flow
    /// or the death flow rather than by casting.
    ///
    /// Its whole behaviour is the animation the cast triggers, which is chosen by the
    /// reservation on the character (for the states that carry variants) or by
    /// <c>SpellDefinition.animState</c> (for the ones that do not). Giving it an
    /// executor that did anything would make it a spell, and a spell needs damage, cost and
    /// balance — none of which a probe should have an opinion about.
    ///
    /// It is registered like any other type rather than special-cased in
    /// <c>SpellCaster.ExecuteSpell</c>, because an unregistered type falls back to the
    /// PROJECTILE executor: a probe meant to do nothing would fire a fireball.
    /// </summary>
    public class AnimationProbeExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            // Deals nothing and spawns nothing. The one thing it DOES do is put the caster
            // into the loadout the probed animation belongs to, because a loadout's
            // locomotion only exists while the loadout is worn: without this,
            // `anim_armed_idle` asks for the Idle state and correctly receives the UNARMED
            // idle, which reads as the probe playing the wrong animation.
            //
            // Set, not toggle — a probe must land on the same animation every time it is
            // cast, and a toggle would alternate. It runs BEFORE PlayerController opens the
            // cast window (the executor is called inside TryCastByKey), so the sprites are
            // already swapped when the animation state is chosen.
            if (ctx.Spell == null || string.IsNullOrEmpty(ctx.Spell.loadoutAnimKey)) return;
            if (ctx.Caster == null) return;

            var loadouts = ctx.Caster.GetComponentInChildren<PlayerLoadoutController>();
            if (loadouts == null || !loadouts.HasLoadout(ctx.Spell.loadoutAnimKey)) return;

            loadouts.SetLoadout(ctx.Spell.loadoutAnimKey);
        }
    }
}
