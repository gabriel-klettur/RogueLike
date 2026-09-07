using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The single answer to "how far does THIS caster's melee swing reach, relative to the
    /// reach its spell was authored for".
    ///
    /// <para>WHY THIS EXISTS. A slash's reach comes off <see cref="Data.SpellDefinition"/>'s
    /// <c>hitRadius</c>, and a <c>SpellDefinition</c> knows nothing about who is casting it —
    /// so all five playable characters swung `slash_regular` at exactly 2.6 units regardless
    /// of how big they are. That was invisible while every character was the same height. It
    /// stopped being invisible when the vampire shipped at 2.698 world units against the
    /// dwarf's 1.859: the same 2.6 u arc is 1.40x the dwarf's body height and 0.96x hers, so
    /// the swing reads as a proper extended sweep on one character and as a tight jab on the
    /// other, from one number neither of them can influence.</para>
    ///
    /// <para>MONSTERS ARE DELIBERATELY EXCLUDED, and that is the whole subtlety here. They
    /// already express per-creature reach the only way that was available — by authoring a
    /// SEPARATE slash spell (`hostile_slash` 2.4, `hostile_slash_giant` 5.0,
    /// `boss_barbol_slash` 6.5). Scaling them by their own <c>meleeRange</c> on top of that
    /// would apply the same quantity twice, and the shipped data makes that catastrophic
    /// rather than merely wrong: monster <c>meleeRange</c> runs from 0 to 7, so the seven
    /// vendors (0) would swing at radius ZERO and `barbol_boss` (7) would take its 6.5 u
    /// slash to <b>30.3 u</b> — most of the screen. The mechanism is needed exactly where the
    /// expression is missing, which is the player, and nowhere else.</para>
    ///
    /// <para>The gate is the <c>Player</c> tag, the same test
    /// <c>SpellTargeting.ResolveGroundTarget</c> uses to refuse a monster the cursor.</para>
    ///
    /// <para>The neutral answer is <b>1</b>, and it is what every character in the game gets
    /// today: all four melee classes author <c>meleeRange</c> 1.5, which is
    /// <see cref="BaselineReach"/>, so this multiplies by exactly 1.0 and no shipped reach
    /// moves. Only a class authored away from the baseline changes.</para>
    ///
    /// <para>SIDE EFFECT, and it is the good kind: <c>StatKind.MeleeRange</c> was authored
    /// end to end — a display name, a description, 0.2 units per point in
    /// <c>StatCatalog</c> — and reached no player damage at all. <c>MeleeCombat.TryAttack</c>
    /// is called only from the monster FSM's <c>AttackState</c>, so for a player the whole
    /// chain terminated at <c>CombatRangeVisualizer</c>, a debug overlay. Reading the stat
    /// here makes every melee-reach talent and equipment roll live for the first time.</para>
    /// </summary>
    public static class MeleeReachScale
    {
        /// <summary>
        /// The reach every slash in the catalogue was tuned against. It is the constant
        /// <c>PlayerStats</c> falls back to when a class authors no <c>meleeRange</c>, and the
        /// default on <c>PlayerDefinition.meleeRange</c> — the three must agree or a class
        /// that authors nothing would silently scale.
        /// </summary>
        public const float BaselineReach = 1.5f;

        /// <summary>
        /// Multiplier to apply to an authored melee <c>hitRadius</c> for this caster.
        /// Returns 1 for anything that is not a player, and for a player whose reach stat is
        /// unresolved.
        /// </summary>
        public static float For(Transform caster)
        {
            if (caster == null) return 1f;
            // A monster's reach is already in its own slash asset; scaling again double-counts.
            if (!caster.CompareTag("Player")) return 1f;

            var melee = caster.GetComponent<MeleeCombat>();
            // MeleeCombat is where PlayerStats pushes the COMPOSED MeleeRange — base plus
            // level, talents, grimoire, equipment, buffs and auras — so reading it here picks
            // all of those up rather than only the class's authored base.
            if (melee == null) return 1f;

            float reach = melee.Range;
            // A non-positive reach is a component that has not been seeded yet, not a request
            // for a zero-radius swing. PlayerStats runs on its own bootstrap order and
            // MeleeCombat.SetRange clamps to 0.01, so this really can be observed mid-boot.
            if (reach <= 0f) return 1f;

            return reach / BaselineReach;
        }
    }
}
