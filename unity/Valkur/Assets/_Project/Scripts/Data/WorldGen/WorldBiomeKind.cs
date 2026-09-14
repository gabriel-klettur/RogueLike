namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// How the generator decides a biome, which is a different question for different biomes.
    ///
    /// <para>Minecraft's multi-noise lookup treats every biome the same way; in a top-down world
    /// with no vertical axis that is wrong for exactly three families. Water and mountains are
    /// questions about HEIGHT, and nothing about the climate should be able to put a desert
    /// under the sea. Rare biomes are questions about a separate noise channel, or they are
    /// either everywhere or nowhere. Only the remaining land biomes compete in the
    /// temperature/humidity plane.</para>
    /// </summary>
    public enum WorldBiomeKind
    {
        /// <summary>Below sea level. Decided by elevation.</summary>
        Water = 0,

        /// <summary>The thin band just above sea level. Decided by elevation.</summary>
        Shore = 1,

        /// <summary>Competes in the climate plane with every other enabled land biome.</summary>
        Land = 2,

        /// <summary>Above the mountain line. Decided by elevation (and heat, for volcanic).</summary>
        Highland = 3,

        /// <summary>Carved out of land by the rarity channel.</summary>
        Rare = 4,
    }
}
