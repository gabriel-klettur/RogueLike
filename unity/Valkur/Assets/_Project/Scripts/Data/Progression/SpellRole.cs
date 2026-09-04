namespace Valkur.Data
{
    /// <summary>
    /// What a spell is FOR, independent of which school teaches it.
    ///
    /// <para>The grimoire is organised by SCHOOL because that is what scales: at the ~100
    /// spell target, nine schools give roughly eleven nodes per tab, which reads as a list,
    /// while seven functional categories would give damage about forty-five and leave the
    /// other six nearly empty. The cost of that choice is that FUNCTION becomes invisible —
    /// a player looking for "what heals me" has to already know healing lives in Radiance.
    /// This tag plus a filter row in the grimoire is what buys it back: structure by school,
    /// search by role.</para>
    ///
    /// <para>Deliberately NOT derived from <c>SpellDefinition.type</c>. The executor a spell
    /// runs through says how it is DELIVERED, not what it is for: <c>frost_nova</c> and
    /// <c>thorn_burst</c> are both <c>SpellType.Area</c> and one is control while the other
    /// is damage, while <c>curse_of_frailty</c> and <c>ice_lance</c> are both
    /// <c>SpellType.Projectile</c> and differ the same way. Deriving would put them in the
    /// same bucket and the filter would be worthless on the exact pairs it exists to
    /// separate.</para>
    ///
    /// <para>APPENDED, never inserted — <see cref="SpellNode"/> serialises this as its
    /// integer, so renumbering repoints every authored node at the wrong role without
    /// touching a file.</para>
    /// </summary>
    public enum SpellRole
    {
        /// <summary>Its point is the damage number. The default, and the largest group.</summary>
        Damage = 0,

        /// <summary>Its point is what the target cannot do. Stuns, roots, slows, area denial.</summary>
        Control = 1,

        /// <summary>Its point is what the CASTER survives. Shields, armour buffs, barriers.</summary>
        Protection = 2,

        /// <summary>Its point is hit points coming back.</summary>
        Healing = 3,

        /// <summary>Its point is where the caster ends up.</summary>
        Mobility = 4,

        /// <summary>Its point is that something else fights for you.</summary>
        Summon = 5,

        /// <summary>Everything else — loadout swaps, animation probes, cosmetics.</summary>
        Utility = 6,
    }
}
