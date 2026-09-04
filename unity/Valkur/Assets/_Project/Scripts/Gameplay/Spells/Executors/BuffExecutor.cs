using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A timed stat change on the caster: <see cref="SpellDefinition.statModifiers"/> handed
    /// to <see cref="TimedBuffSource"/> for <see cref="SpellDefinition.duration"/> seconds.
    ///
    /// <para>This is the executor that turns the whole support category into DATA. Every
    /// "+X for Y seconds" spell from here to the hundred-spell target costs one asset and no
    /// code, because the layered stat store already answers the three hard questions: a buff
    /// writes only its own layer so removal is exact by construction, the push to Health and
    /// Mana is idempotent so a recompute cannot grant a bonus twice, and a repeated key
    /// REFRESHES rather than stacks.</para>
    ///
    /// <para>It deliberately does nothing at all for a non-player caster. The Buff layer lives
    /// on <c>PlayerStats</c>, and an NPC has <c>EntityStats</c> with no equivalent — building
    /// a second composition rule on the monster side is how a project ends up with two that
    /// disagree. A monster that wants to buff itself needs a status effect, which is the same
    /// answer <c>VulnerableEffect</c> gives for the debuff direction.</para>
    /// </summary>
    public sealed class BuffExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            var spell = ctx.Spell;
            if (spell == null || ctx.Caster == null) return;

            if (spell.duration <= 0f)
            {
                // A permanent stat change belongs in a layer with an owner who can remove
                // it, and the Buff layer's owner is a clock. TimedBuffSource refuses this
                // silently, so say it here where the author can act on it.
                Debug.LogWarning($"[BuffExecutor] '{spell.spellKey}' has no duration, so the " +
                                 "buff was refused. A buff with no clock is a permanent stat " +
                                 "change and belongs in the Skill or Grimoire layer instead.");
                return;
            }

            if (spell.statModifiers == null || spell.statModifiers.Length == 0)
            {
                // The exact failure this project has recorded eleven times: data that
                // round-trips perfectly and reaches no runtime effect. The cast would spend
                // mana, play its flourish and change nothing.
                Debug.LogWarning($"[BuffExecutor] '{spell.spellKey}' authors no statModifiers, " +
                                 "so the cast produced no effect at all.");
                return;
            }

            var buffs = ctx.Caster.GetComponent<TimedBuffSource>();
            if (buffs == null)
            {
                Debug.LogWarning($"[BuffExecutor] '{spell.spellKey}' was cast by " +
                                 $"'{ctx.Caster.name}', which has no TimedBuffSource. Buffs are " +
                                 "a PlayerStats layer; a monster needs a status effect instead.");
                return;
            }

            buffs.Apply(spell.ResolveBuffKey(), spell.statModifiers, spell.duration);
            BuffAuraFX.Attach(ctx.Caster, spell);
        }
    }
}
