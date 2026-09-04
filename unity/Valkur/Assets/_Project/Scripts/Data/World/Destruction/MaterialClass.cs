namespace Valkur.Data
{
    /// <summary>
    /// What a destructible building is made of. One axis of
    /// <see cref="DestructionResistanceTable"/>: it decides which
    /// <see cref="DamageClass"/> can actually hurt it.
    ///
    /// <para>Kept deliberately coarse. Six materials times eleven damage classes is
    /// already 66 numbers a designer has to hold in their head; splitting "oak" from
    /// "pine" would double that and change nothing a player can feel.</para>
    /// </summary>
    public enum MaterialClass
    {
        /// <summary>Trunks, planks, crates, fences. The axe's material.</summary>
        Wood = 0,

        /// <summary>Bushes, hedges, vines. Cut by any edge; burns fastest of all.</summary>
        Foliage = 1,

        /// <summary>Rock, brick, statues, walls. The pick's material.</summary>
        Stone = 2,

        /// <summary>Iron gates, anvils, cages. Dented, not cut.</summary>
        Metal = 3,

        /// <summary>Tents, banners, sacks, awnings.</summary>
        Cloth = 4,

        /// <summary>Windows, bottles, lanterns, ice.</summary>
        Glass = 5,
    }
}
