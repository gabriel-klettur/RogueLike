namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// The closed biome vocabulary of the world generator.
    ///
    /// <para><b>Append, never renumber.</b> The value is written into saved profiles (as the
    /// key of a <see cref="WorldBiomeWeight"/>) and, from phase 5, into generated chunk data;
    /// a reordered enum silently turns every saved forest into whatever now sits at its
    /// index. <c>WorldBiomeTableTests</c> pins one table entry per value.</para>
    /// </summary>
    public enum WorldBiome
    {
        DeepOcean = 0,
        Ocean = 1,
        Beach = 2,
        Plains = 3,
        Forest = 4,
        AutumnForest = 5,
        Taiga = 6,
        Snow = 7,
        Desert = 8,
        Jungle = 9,
        Swamp = 10,
        Mountain = 11,
        Volcanic = 12,
        Enchanted = 13,
        Corrupted = 14,

        /// <summary>Carved by <see cref="WorldRivers"/>, never chosen by the climate.</summary>
        River = 15,
    }
}
