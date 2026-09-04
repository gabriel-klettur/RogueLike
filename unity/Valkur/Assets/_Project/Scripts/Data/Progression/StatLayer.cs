namespace Valkur.Data
{
    /// <summary>
    /// Which source a <see cref="StatModifier"/> came from. A layer is the unit of
    /// REMOVAL, not of ordering — the arithmetic in <see cref="StatOp"/> is the same
    /// whichever layer a modifier sits in.
    ///
    /// This is the same rule <c>SpriteTintStack</c> established for the one thing nine
    /// systems used to fight over: every source writes ONLY its own layer and never the
    /// total. Before that, the pattern was "cache the current value, change it, write
    /// the cache back", which is correct alone and wrong together — a monster hit while
    /// burning captured orange as its baseline and stayed orange forever. A stat store
    /// has exactly the same shape: unequipping a sword must remove the sword's +6 and
    /// nothing else, even if a potion and three talents also touched melee damage while
    /// it was worn.
    ///
    /// Because a layer is replaced wholesale by its owner, removal is exact by
    /// construction and there is no "restore the original" step to get wrong.
    /// </summary>
    public enum StatLayer
    {
        /// <summary>The class's authored numbers. Written once by PlayerDefinition.</summary>
        Base = 0,

        /// <summary>Granted by character level via LevelStatCurve.</summary>
        Level = 1,

        /// <summary>Permanent talents from the skill tree.</summary>
        Skill = 2,

        /// <summary>Permanent grimoire nodes from the spell tree.</summary>
        Grimoire = 3,

        /// <summary>Currently equipped items. Recomputed on every inventory change.</summary>
        Equipment = 4,

        /// <summary>Temporary effects with an expiry — potions, shrines, food.</summary>
        Buff = 5,

        /// <summary>Effects owned by another live object — auras, totems, party buffs.</summary>
        Aura = 6,
    }
}
