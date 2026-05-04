using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Biomes
{
    /// <summary>
    /// Collection of <see cref="BiomeRecipe"/>s consumed by the Map Editor's
    /// biome generator. Designers can author a custom asset, but if none is
    /// wired, <see cref="BuildDefault"/> returns a runtime catalog tuned
    /// against Valkur's existing tile categories (grass_dirt, sand_rock, …)
    /// and Buildings/{vegetation,forest_decoration,gardens,totems} paths.
    /// </summary>
    [CreateAssetMenu(fileName = "BiomeCatalog", menuName = "Valkur/Biomes/Catalog")]
    public class BiomeCatalog : ScriptableObject
    {
        [SerializeField] private List<BiomeRecipe> recipes = new List<BiomeRecipe>();

        public IReadOnlyList<BiomeRecipe> Recipes => recipes;

        public bool TryGet(BiomeKind kind, out BiomeRecipe recipe)
        {
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i].kind == kind)
                {
                    recipe = recipes[i];
                    return true;
                }
            }
            recipe = default;
            return false;
        }

        /// <summary>
        /// Build the built-in default catalog at runtime. Used by the Map
        /// Editor biome generator when no asset is assigned, so the feature
        /// works out of the box on any project.
        /// </summary>
        public static BiomeCatalog BuildDefault()
        {
            var c = CreateInstance<BiomeCatalog>();
            c.recipes = new List<BiomeRecipe>
            {
                new BiomeRecipe
                {
                    kind = BiomeKind.Plains,
                    displayName = "Plains",
                    baseTileCategory = "grass_dirt",
                    variantTileCategory = "",
                    variantNoiseThreshold = 1f,
                    variantNoiseScale = 0.08f,
                    buildingPathFilters = new[] { "gardens/flowers" },
                    buildingDensityPer100Cells = 0.4f,
                    minBuildingSpacingWu = 2.5f,
                },
                new BiomeRecipe
                {
                    kind = BiomeKind.Forest,
                    displayName = "Forest",
                    baseTileCategory = "grass_dirt",
                    variantTileCategory = "",
                    variantNoiseThreshold = 1f,
                    variantNoiseScale = 0.08f,
                    buildingPathFilters = new[] { "vegetation/tree", "forest_decoration/natural" },
                    buildingDensityPer100Cells = 4.0f,
                    minBuildingSpacingWu = 1.6f,
                },
                new BiomeRecipe
                {
                    kind = BiomeKind.CursedForest,
                    displayName = "Cursed Forest",
                    baseTileCategory = "grass_rock",
                    variantTileCategory = "",
                    variantNoiseThreshold = 1f,
                    variantNoiseScale = 0.08f,
                    buildingPathFilters = new[] { "forest_decoration/corrupto", "vegetation/tree" },
                    buildingDensityPer100Cells = 3.2f,
                    minBuildingSpacingWu = 1.8f,
                },
                new BiomeRecipe
                {
                    kind = BiomeKind.Desert,
                    displayName = "Desert",
                    baseTileCategory = "sand_rock",
                    variantTileCategory = "sand_grass",
                    variantNoiseThreshold = 0.78f,
                    variantNoiseScale = 0.06f,
                    buildingPathFilters = new[] { "totems/totem_destruido", "totems/totem_riendo" },
                    buildingDensityPer100Cells = 0.18f,
                    minBuildingSpacingWu = 5f,
                },
                new BiomeRecipe
                {
                    kind = BiomeKind.Rocky,
                    displayName = "Rocky",
                    baseTileCategory = "grass_rock",
                    variantTileCategory = "rock_water",
                    variantNoiseThreshold = 0.62f,
                    variantNoiseScale = 0.10f,
                    buildingPathFilters = new[] { "totems/" },
                    buildingDensityPer100Cells = 0.30f,
                    minBuildingSpacingWu = 4f,
                },
                new BiomeRecipe
                {
                    kind = BiomeKind.River,
                    displayName = "River",
                    baseTileCategory = "rock_water",
                    variantTileCategory = "",
                    variantNoiseThreshold = 1f,
                    variantNoiseScale = 0.08f,
                    buildingPathFilters = new[] { "gardens/flowers" },
                    buildingDensityPer100Cells = 0.5f,
                    minBuildingSpacingWu = 3f,
                },
                new BiomeRecipe
                {
                    kind = BiomeKind.Coastal,
                    displayName = "Coastal",
                    baseTileCategory = "sand_ocean",
                    variantTileCategory = "sand_grass",
                    variantNoiseThreshold = 0.7f,
                    variantNoiseScale = 0.07f,
                    buildingPathFilters = new[] { "gardens/flowers" },
                    buildingDensityPer100Cells = 0.6f,
                    minBuildingSpacingWu = 2.5f,
                },
                new BiomeRecipe
                {
                    kind = BiomeKind.Garden,
                    displayName = "Garden",
                    baseTileCategory = "grass_dirt",
                    variantTileCategory = "",
                    variantNoiseThreshold = 1f,
                    variantNoiseScale = 0.08f,
                    buildingPathFilters = new[] { "gardens/flowers", "gardens/garden" },
                    buildingDensityPer100Cells = 1.5f,
                    minBuildingSpacingWu = 2f,
                },
            };
            return c;
        }
    }
}
