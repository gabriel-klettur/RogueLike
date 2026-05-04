using System;

namespace Valkur.Data.Biomes
{
    /// <summary>
    /// Designer-tunable definition of how a single biome paints a zone.
    /// Lives inside <see cref="BiomeCatalog"/>; not a standalone asset.
    ///
    /// Tile painting:
    ///   • Picks a tile from <see cref="baseTileCategory"/> (matches a TileCatalog category).
    ///   • If <see cref="variantTileCategory"/> is non-empty, Perlin noise &gt;
    ///     <see cref="variantNoiseThreshold"/> swaps in a tile from that category.
    ///
    /// Building scattering:
    ///   • Filters the BuildingCatalog templates whose <c>assetPath</c> contains any
    ///     of <see cref="buildingPathFilters"/> (case-insensitive substring).
    ///   • Spawns templates uniformly inside the zone with at least
    ///     <see cref="minBuildingSpacingWu"/> world units between any two centres.
    /// </summary>
    [Serializable]
    public struct BiomeRecipe
    {
        public BiomeKind kind;
        public string displayName;

        public string baseTileCategory;
        public string variantTileCategory;
        public float variantNoiseThreshold;
        public float variantNoiseScale;

        public string[] buildingPathFilters;
        public float buildingDensityPer100Cells;
        public float minBuildingSpacingWu;
    }
}
