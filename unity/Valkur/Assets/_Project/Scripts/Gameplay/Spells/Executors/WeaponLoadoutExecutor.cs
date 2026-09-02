using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Draws or stows a weapon: toggles the caster's <see cref="PlayerLoadoutController"/>
    /// onto the loadout named by <c>SpellDefinition.loadoutKey</c>.
    ///
    /// A spell rather than a keybind because the spell system already owns everything this
    /// needs and a keybind owns none of it: a cooldown so the swap cannot be spammed into a
    /// strobe, a slot on the spell bar with an icon, the <c>TryCastByKey</c> path that plays
    /// the cast animation, and a place for the player to learn it. It costs no mana and deals
    /// no damage, which is why <c>SpellFieldRelevance</c> hides every combat field for this
    /// type.
    ///
    /// The DRAW ANIMATION is not played here. It is a cast variant on the character reserved
    /// for this spell's key, so <c>PlayerController.TriggerCastAnimation</c> selects it the
    /// same way it selects the fireball's — see <c>CastVariant.spellKeys</c>. Playing it from
    /// the executor would mean the animation happened for the CASTER's own reasons rather
    /// than the animator's, and a monster casting this (nothing does, but nothing stops it)
    /// would drive a player-only component from a shared code path.
    ///
    /// WHEN the art swaps is asymmetric and belongs to the controller, not here. Drawing
    /// lands on the cast frame — the weapon must be in hand for the draw to be showing it.
    /// Stowing is COMMITTED here and lands when the sheathe finishes, because that animation
    /// shows the weapon for its whole length and swapping early would play 1.2 s of putting
    /// away a sword the character no longer has. The flare goes with the swap for the same
    /// reason: its job is to hide a cut, so it fires where the cut is.
    ///
    /// STOWING PLAYS THE SAME SHEET BACKWARDS. There is one motion and it reads either way,
    /// so putting the weapon away is the draw reversed rather than a second animation nobody
    /// drew. The controller records which direction the swap went
    /// (<c>PlayerLoadoutController.LastSwapStowed</c>) and <c>PlayerController</c> asks it
    /// when it opens the cast window — the spell itself cannot tell you, because it is the
    /// same spell both ways.
    /// </summary>
    public class WeaponLoadoutExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            SpellDefinition spell = ctx.Spell;
            if (spell == null || ctx.Caster == null)
                return;

            if (string.IsNullOrEmpty(spell.loadoutKey))
            {
                Debug.LogWarning($"[WeaponLoadoutExecutor] Spell '{spell.spellKey}' is a " +
                                 "WeaponLoadout with an empty loadoutKey, so it can only ever " +
                                 "do nothing. Set it to a Loadout key on the caster's " +
                                 "PlayerDefinition.");
                return;
            }

            var controller = ctx.Caster.GetComponentInChildren<PlayerLoadoutController>();
            if (controller == null)
            {
                // Not an error: a character with no loadouts never gets the component, and
                // that character being handed this spell is a catalog decision, not a bug in
                // the swap. It is worth saying out loud though, because the spell will
                // otherwise consume its cooldown and do nothing visible.
                Debug.LogWarning($"[WeaponLoadoutExecutor] '{ctx.Caster.name}' has no " +
                                 "PlayerLoadoutController — its PlayerDefinition declares no " +
                                 $"loadouts, so '{spell.spellKey}' has nothing to toggle.");
                return;
            }

            // The toggle owns everything from here: which direction the swap goes, when its
            // art lands, and the flare that covers the cut. A refused swap (unknown key)
            // returns false and produces no flare, so the spell cannot look like it worked
            // when it did nothing.
            controller.ToggleLoadout(spell.loadoutKey);
        }
    }
}
