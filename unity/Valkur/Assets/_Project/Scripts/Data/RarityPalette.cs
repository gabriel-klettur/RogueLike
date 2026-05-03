using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Canonical UI colour for each <see cref="ItemRarity"/> tier. Mirrors the
    /// genre conventions (grey/green/blue/purple/orange) so players read the
    /// rarity at a glance without learning a custom palette.
    ///
    /// Centralised here so item tooltips, drop-pickup floating text, and the
    /// inventory grid all agree on the same colour. Editing a single value
    /// updates every UI surface.
    /// </summary>
    public static class RarityPalette
    {
        // Hex values picked once and pinned by tests so a future edit
        // doesn't silently re-skin every tooltip in the game.
        private static readonly Color CommonColor    = new Color(0.78f, 0.78f, 0.78f); // #C7C7C7
        private static readonly Color UncommonColor  = new Color(0.30f, 0.85f, 0.30f); // #4CD94C — green
        private static readonly Color RareColor      = new Color(0.30f, 0.55f, 1.00f); // #4D8CFF — blue
        private static readonly Color EpicColor      = new Color(0.65f, 0.30f, 1.00f); // #A64DFF — purple
        private static readonly Color LegendaryColor = new Color(1.00f, 0.55f, 0.10f); // #FF8C1A — orange

        public static Color Color(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common:    return CommonColor;
                case ItemRarity.Uncommon:  return UncommonColor;
                case ItemRarity.Rare:      return RareColor;
                case ItemRarity.Epic:      return EpicColor;
                case ItemRarity.Legendary: return LegendaryColor;
                default:                   return CommonColor;
            }
        }

        public static string DisplayName(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common:    return "Common";
                case ItemRarity.Uncommon:  return "Uncommon";
                case ItemRarity.Rare:      return "Rare";
                case ItemRarity.Epic:      return "Epic";
                case ItemRarity.Legendary: return "Legendary";
                default:                   return rarity.ToString();
            }
        }

        /// <summary>
        /// Drop-rate weight for ItemRarity tiers, useful for loot table
        /// generators. Tuning is genre-typical (Common 60% / Uncommon 25%
        /// / Rare 10% / Epic 4% / Legendary 1%) — designers can override
        /// per-encounter via a custom table.
        /// </summary>
        public static int DefaultDropWeight(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common:    return 60;
                case ItemRarity.Uncommon:  return 25;
                case ItemRarity.Rare:      return 10;
                case ItemRarity.Epic:      return 4;
                case ItemRarity.Legendary: return 1;
                default:                   return 0;
            }
        }
    }
}
