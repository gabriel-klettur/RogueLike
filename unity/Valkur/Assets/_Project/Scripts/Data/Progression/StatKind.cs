namespace Valkur.Data
{
    /// <summary>
    /// The vocabulary of every number that describes a character. This enum is the
    /// single answer to "what can a skill node, a piece of equipment or a potion
    /// modify", and it is deliberately CLOSED: adding a value here is a commitment
    /// to wire a consumer for it in <c>PlayerStats.PushToComponents</c>.
    ///
    /// Why an enum rather than the string keys the first skill layer used: a typo in
    /// a string key surfaced as a runtime warning nobody read, on an asset nobody
    /// opened — exactly the failure mode CLAUDE.md records for <c>animation_map.json</c>
    /// and the FSM's inert <c>Actions</c> block. An enum cannot be misspelled, the
    /// inspector renders it as a dropdown, and <c>PlayerStatsWiringTests</c> can walk
    /// every value and fail when one has no consumer.
    ///
    /// Values are APPENDED, never inserted: every authored SkillNode / SpellNode
    /// serializes this as its integer, so renumbering repoints existing assets at the
    /// wrong stat without touching a file. Same contract as <c>SpellType</c>.
    /// </summary>
    public enum StatKind
    {
        /// <summary>Maximum hit points. Consumer: <c>Health.SetMaxHp</c>.</summary>
        MaxHp = 0,

        /// <summary>Maximum mana pool. Consumer: <c>Mana.SetMaxMana</c>.</summary>
        MaxMana = 1,

        /// <summary>Mana regenerated per second. Consumer: <c>Mana.SetRegenPerSecond</c>.</summary>
        ManaRegen = 2,

        /// <summary>World units per second. Consumer: <c>PlayerController.SetMoveSpeed</c>.</summary>
        MoveSpeed = 3,

        /// <summary>Damage of one melee swing. Consumer: <c>MeleeCombat.SetDamage</c>.</summary>
        MeleeDamage = 4,

        /// <summary>Reach of a melee swing, world units. Consumer: <c>MeleeCombat.SetRange</c>.</summary>
        MeleeRange = 5,

        /// <summary>Seconds between swings. Lower is better, so buffs author a NEGATIVE
        /// percent here. Consumer: <c>MeleeCombat.SetCooldown</c>.</summary>
        MeleeCooldown = 6,

        /// <summary>Flat damage subtracted from every incoming blow. Consumer:
        /// <c>Health.SetDefense</c> — the same seam monsters have always used.</summary>
        Defense = 7,

        /// <summary>Probability 0..1 that a blow crits. Consumer: <c>CritResolver</c>.</summary>
        CritChance = 8,

        /// <summary>Damage multiplier applied on a crit. Consumer: <c>CritResolver</c>.</summary>
        CritMultiplier = 9,

        /// <summary>Multiplier on all spell damage. 1 = unmodified. Consumer:
        /// <c>SpellCaster</c> via <c>PlayerStats.SpellDamageMultiplier</c>.</summary>
        SpellPower = 10,

        /// <summary>Fraction 0..cap subtracted from every spell cooldown. Consumer:
        /// <c>SpellCaster</c>.</summary>
        SpellCooldownReduction = 11,

        /// <summary>Fraction 0..cap subtracted from every spell's mana cost. Consumer:
        /// <c>SpellCaster</c>.</summary>
        ManaCostReduction = 12,

        /// <summary>Multiplier on XP awarded. Consumer: <c>Experience.AddXp</c>.</summary>
        XpGain = 13,
    }
}
