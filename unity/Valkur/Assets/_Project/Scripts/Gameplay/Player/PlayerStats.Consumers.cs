using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The output half of <see cref="PlayerStats"/>: where each resolved number actually
    /// goes, and the derived multipliers spells read directly.
    ///
    /// Every <see cref="StatKind"/> must appear here. That is not a style rule — it is
    /// the thing that stops this system becoming the eleventh authored-and-inert layer in
    /// the project, next to <c>animation_map.json</c>, the FSM's <c>Actions</c> block and
    /// the four casting flags nothing reads. <c>PlayerStatsWiringTests</c> walks the enum
    /// and fails when a value has no consumer.
    /// </summary>
    public sealed partial class PlayerStats
    {
        private Health _health;
        private Mana _mana;
        private MeleeCombat _melee;
        private PlayerController _controller;
        private Experience _experience;

        private void Awake() => ResolveComponents();

        private void ResolveComponents()
        {
            if (_health == null)     _health     = GetComponent<Health>();
            if (_mana == null)       _mana       = GetComponent<Mana>();
            if (_melee == null)      _melee      = GetComponent<MeleeCombat>();
            if (_controller == null) _controller = GetComponent<PlayerController>();
            if (_experience == null) _experience = GetComponent<Experience>();
        }

        // ── Derived multipliers read directly by the spell layer ────────────────
        //
        // These are NOT pushed anywhere: a spell's damage is a property of the spell, so
        // scaling it at the definition would corrupt the shared asset for every caster
        // including monsters. SpellCaster asks for the multiplier at cast time instead.

        /// <summary>Multiplier applied to every spell's damage and heal.</summary>
        public float SpellDamageMultiplier => Get(StatKind.SpellPower);

        /// <summary>Multiplier applied to every spell's cooldown. Always ≤ 1.</summary>
        public float SpellCooldownMultiplier => 1f - Get(StatKind.SpellCooldownReduction);

        /// <summary>Multiplier applied to every spell's mana cost. Always ≤ 1.</summary>
        public float SpellManaCostMultiplier => 1f - Get(StatKind.ManaCostReduction);

        /// <summary>Multiplier applied to XP awarded to this character.</summary>
        public float XpMultiplier => Get(StatKind.XpGain);

        public float CritChance => Get(StatKind.CritChance);
        public float CritMultiplier => Get(StatKind.CritMultiplier);

        // ── The push ────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes every resolved stat into the live components. Called on every recompute.
        ///
        /// It must be IDEMPOTENT: recomputing with unchanged inputs has to leave the world
        /// unchanged, or a recompute triggered by an unrelated layer (a potion expiring)
        /// would heal the player a little each time. That is why the setters it calls take
        /// an absolute value rather than a delta — <c>Health.IncreaseMaxHp</c>, the API the
        /// old skill layer used, cannot be called from here at all.
        /// </summary>
        private void PushToComponents()
        {
            ResolveComponents();

            // Nothing is pushed until a base has actually been authored. Before that every
            // base is its NEUTRAL value, and writing those into the live components does not
            // merely waste a pass — for mana it is unrecoverable. See the note on
            // PlayerStats._basesSeeded for the measurement; the short version is that
            // Health.SetMaxHp floors at 1 while Mana.SetMaxMana correctly accepts 0 (a class
            // with no mana is a legitimate thing to author), so a neutral push emptied the
            // mana pool and the old MaxMana > 0 guard below then latched it shut forever.
            if (!_basesSeeded) return;

            // The guard these two used to carry was `MaxHp > 0` / `MaxMana > 0`, standing in
            // for "has Initialize run yet" so that spawn order could not silently decide the
            // character's pools. The intent was right and the proxy was not: a VALUE cannot
            // distinguish an uninitialised pool from a legitimately empty one, so the test
            // is unfalsifiable from outside — and for mana it was self-latching, since a pool
            // this push had zeroed could never be pushed to again. `_basesSeeded` answers the
            // question the comment was actually asking, and answers it about the right
            // object: what must have happened first is the class seeding, not the component's
            // own Initialize.
            if (_health != null)
            {
                _health.SetMaxHp(GetInt(StatKind.MaxHp));
                _health.SetDefense(GetInt(StatKind.Defense));
            }

            if (_mana != null)
            {
                _mana.SetMaxMana(GetInt(StatKind.MaxMana));
                _mana.SetRegenPerSecond(Get(StatKind.ManaRegen));
            }

            if (_melee != null)
            {
                _melee.SetDamage(GetInt(StatKind.MeleeDamage));
                _melee.SetRange(Get(StatKind.MeleeRange));
                _melee.SetCooldown(Get(StatKind.MeleeCooldown));
            }

            if (_controller != null)
                _controller.SetMoveSpeed(Get(StatKind.MoveSpeed));

            if (_experience != null)
                _experience.SetXpMultiplier(Get(StatKind.XpGain));
        }

        /// <summary>
        /// Re-pushes without recomputing. The bootstrap calls this once after every
        /// component has been initialised, because the first recompute usually runs before
        /// Health and Mana exist and is therefore refused by the guards above.
        /// </summary>
        public void ForcePush()
        {
            PushToComponents();
            RaiseStatsChanged();
        }
    }
}
