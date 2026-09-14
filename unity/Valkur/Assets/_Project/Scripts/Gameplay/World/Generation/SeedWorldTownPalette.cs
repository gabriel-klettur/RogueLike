using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.WorldGen;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Which buildings a generated town is built from, named by asset path.
    ///
    /// <para><b>A curated list, not a folder scan.</b> The catalogue mixes two art scales and two
    /// perspectives: about fifty 80-250 px pixel-art buildings drawn for the game, and a handful
    /// of 1024-1536 px renders (castles, temples, the lobby's bank) that only work with a
    /// per-instance scale override, plus an isometric and a top-down copy of the same house. A
    /// town that drew from <c>houses/</c> blindly would put a render beside a cottage four times
    /// its detail. Every entry here is from the same pixel-art wave, and
    /// <c>SeedWorldTownPaletteTests</c> fails the moment one stops resolving.</para>
    ///
    /// <para><b>Named by PATH, not id,</b> because template ids are assigned by importers and a
    /// re-import can renumber them; the art path is what the name actually describes.</para>
    /// </summary>
    public static class SeedWorldTownPalette
    {
        [Valkur.Core.SelfHealingStatic("Immutable table of asset path strings; never mutated.")]
        public static readonly string[] Houses =
        {
            "Buildings/houses/house_cottage_chimney", "Buildings/houses/house_cottage_green_roof",
            "Buildings/houses/house_cottage_red_roof", "Buildings/houses/house_cottage_slate",
            "Buildings/houses/house_cottage_thatched", "Buildings/houses/house_cottage_woodpile",
            "Buildings/houses/house_farmhouse_woodpile", "Buildings/houses/house_manor_crest",
            "Buildings/houses/house_manor_timber", "Buildings/houses/house_narrow_tall",
            "Buildings/houses/house_semi_detached", "Buildings/houses/house_tenement_laundry_a",
            "Buildings/houses/house_tenement_laundry_b", "Buildings/houses/house_tenement_narrow",
            "Buildings/houses/house_timber_blue_roof", "Buildings/houses/house_timber_blue_shutters",
            "Buildings/houses/house_timber_red_large", "Buildings/houses/house_townhouse_grand_a",
            "Buildings/houses/house_townhouse_grand_b", "Buildings/houses/house_workshop_red",
            "Buildings/houses/house_workshop_slate", "Buildings/houses/chapel_stone_small",
            "Buildings/houses/house_guard_post_blue",
        };

        [Valkur.Core.SelfHealingStatic("Immutable table of asset path strings; never mutated.")]
        public static readonly string[] Shops =
        {
            "Buildings/shops/shop_alchemist_tower_a", "Buildings/shops/shop_alchemist_tower_b",
            "Buildings/shops/shop_bakery_awning_a", "Buildings/shops/shop_bakery_awning_b",
            "Buildings/shops/shop_blacksmith_forge_a", "Buildings/shops/shop_blacksmith_forge_b",
            "Buildings/shops/shop_butcher_awning", "Buildings/shops/shop_carpenter_mill_a",
            "Buildings/shops/shop_carpenter_mill_b", "Buildings/shops/shop_gem_two_storey",
            "Buildings/shops/shop_grocer_produce", "Buildings/shops/shop_market_awning",
            "Buildings/shops/shop_smithy_dark_roof", "Buildings/shops/tavern_three_storey_a",
            "Buildings/shops/tavern_three_storey_b", "Buildings/houses/house_inn_stone_large",
            "Buildings/houses/guildhall_banners_a", "Buildings/houses/guildhall_banners_b",
            "Buildings/houses/building_bank_tall", "Buildings/houses/tower_mage_round",
        };

        [Valkur.Core.SelfHealingStatic("Immutable table of asset path strings; never mutated.")]
        public static readonly string[] Centerpieces =
        {
            "Buildings/statues/fountain_grand_tiered", "Buildings/water/fountain_tiered_stone",
            "Buildings/statues/fountain_round_jet", "Buildings/water/well_roofed_red",
            "Buildings/props/well_stone_tiled_roof",
        };

        [Valkur.Core.SelfHealingStatic("Immutable table of asset path strings; never mutated.")]
        public static readonly string[] Lamps =
        {
            "Buildings/lights/lamp_post_village", "Buildings/lights/lamp_post_classic",
            "Buildings/lights/lamp_post_globe", "Buildings/lights/lamp_post_ornate",
        };

        [Valkur.Core.SelfHealingStatic("Immutable table of asset path strings; never mutated.")]
        public static readonly string[] Stalls =
        {
            "Buildings/market/stall_meat_red_awning", "Buildings/market/stall_produce_blue_awning",
            "Buildings/market/cart_bread_vendor", "Buildings/market/cart_flower_vendor",
        };

        /// <summary>Every entry that resolves in <paramref name="catalog"/>, sized in tiles.</summary>
        public static List<WorldTownBuildingOption> Options(BuildingCatalog catalog, List<string> missing = null)
        {
            var options = new List<WorldTownBuildingOption>();
            if (catalog == null) return options;

            var byPath = new Dictionary<string, BuildingTemplateData>();
            foreach (var t in catalog.Templates)
                if (t != null && !string.IsNullOrEmpty(t.assetPath) && !byPath.ContainsKey(t.assetPath))
                    byPath[t.assetPath] = t;

            Add(options, byPath, Houses, WorldTownPieceKind.House, missing);
            Add(options, byPath, Shops, WorldTownPieceKind.Shop, missing);
            Add(options, byPath, Centerpieces, WorldTownPieceKind.Centerpiece, missing);
            Add(options, byPath, Lamps, WorldTownPieceKind.Lamp, missing);
            Add(options, byPath, Stalls, WorldTownPieceKind.Stall, missing);
            return options;
        }

        private static void Add(List<WorldTownBuildingOption> options, Dictionary<string, BuildingTemplateData> byPath,
                                string[] paths, WorldTownPieceKind kind, List<string> missing)
        {
            foreach (var path in paths)
            {
                if (!byPath.TryGetValue(path, out var t) || t.originalScale.x <= 0 || t.originalScale.y <= 0)
                {
                    missing?.Add(path);
                    continue;
                }
                options.Add(new WorldTownBuildingOption(t.templateId,
                    Mathf.CeilToInt(t.originalScale.x / 32f), Mathf.CeilToInt(t.originalScale.y / 32f), kind));
            }
        }
    }
}
