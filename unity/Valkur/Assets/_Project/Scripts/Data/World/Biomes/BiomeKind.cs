namespace Valkur.Data.Biomes
{
    /// <summary>
    /// Catalog of biome archetypes a Map Editor zone can be painted as.
    /// Each value is paired with a <see cref="BiomeRecipe"/> in <see cref="BiomeCatalog"/>.
    /// </summary>
    public enum BiomeKind
    {
        Plains = 0,
        Forest = 1,
        CursedForest = 2,
        Desert = 3,
        Rocky = 4,
        River = 5,
        Coastal = 6,
        Garden = 7,
    }
}
