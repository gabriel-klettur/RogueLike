using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// What the generator knows about one biome: how it is chosen, where it sits in the
    /// climate plane, and what colour it is drawn in the Seed World preview.
    ///
    /// <para>The climate point is the biome's IDEAL temperature and humidity, both 0..1. A land
    /// cell takes the nearest enabled point, so the numbers are relative to each other rather
    /// than thresholds — moving Desert drier widens Plains, it does not open a gap.</para>
    /// </summary>
    public readonly struct WorldBiomeInfo
    {
        public readonly WorldBiome Biome;
        public readonly WorldBiomeKind Kind;

        /// <summary>Spanish, because every author-facing string in the editors is.</summary>
        public readonly string DisplayName;

        public readonly float IdealTemperature;
        public readonly float IdealHumidity;

        /// <summary>The swatch in the preview and its legend. Opaque.</summary>
        public readonly Color32 PreviewColor;

        /// <summary>
        /// The auto-tile TERRAIN name this biome's ground is painted with when the world is
        /// built — one of the names the shipped <c>TerrainCatalog</c> rulesets declare
        /// (grass, dirt, sand, rock, stone, lava, water, water_deep). Several biomes share one
        /// until their own ground art exists: there is no snow, swamp or desert pack, which the
        /// Seed World roadmap records as an art gap rather than something this table can fix.
        /// </summary>
        public readonly string GroundTerrain;

        public WorldBiomeInfo(WorldBiome biome, WorldBiomeKind kind, string displayName,
                              float idealTemperature, float idealHumidity, Color32 previewColor,
                              string groundTerrain)
        {
            Biome = biome;
            Kind = kind;
            DisplayName = displayName;
            IdealTemperature = idealTemperature;
            IdealHumidity = idealHumidity;
            PreviewColor = previewColor;
            GroundTerrain = groundTerrain;
        }
    }
}
