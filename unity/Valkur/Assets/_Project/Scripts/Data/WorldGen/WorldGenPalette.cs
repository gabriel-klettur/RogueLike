using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// The colours of the Seed World preview's scalar layers (elevation, temperature, humidity,
    /// rarity). Biome colours live on <see cref="WorldBiomeTable"/>, beside the rules.
    ///
    /// <para>Kept in Data rather than in the editor folder because they describe what the value
    /// MEANS (sea is blue, cold is blue, dry is sand), and the editor theme has no business
    /// deciding that.</para>
    /// </summary>
    public static class WorldGenPalette
    {
        public static readonly Color32 DeepWater = new Color32(18, 40, 86, 255);
        public static readonly Color32 ShallowWater = new Color32(64, 124, 196, 255);
        public static readonly Color32 Lowland = new Color32(96, 160, 76, 255);
        public static readonly Color32 Highland = new Color32(150, 138, 110, 255);
        public static readonly Color32 Peak = new Color32(244, 244, 248, 255);

        public static readonly Color32 Cold = new Color32(70, 120, 230, 255);
        public static readonly Color32 Mild = new Color32(240, 236, 210, 255);
        public static readonly Color32 Hot = new Color32(220, 70, 40, 255);

        public static readonly Color32 Dry = new Color32(214, 178, 110, 255);
        public static readonly Color32 Wet = new Color32(30, 110, 150, 255);

        public static readonly Color32 Common = new Color32(40, 40, 48, 255);
        public static readonly Color32 Rare = new Color32(190, 120, 240, 255);

        /// <summary>The marker drawn on the spawn point.</summary>
        public static readonly Color32 Spawn = new Color32(255, 216, 64, 255);

        public static Color32 ElevationColor(float elevation, float seaLevel, float mountainLevel)
        {
            if (elevation < seaLevel)
            {
                float t = seaLevel > 0f ? elevation / seaLevel : 1f;
                return Color32.Lerp(DeepWater, ShallowWater, t);
            }
            if (elevation < mountainLevel)
            {
                float span = mountainLevel - seaLevel;
                float t = span > 0f ? (elevation - seaLevel) / span : 1f;
                return Color32.Lerp(Lowland, Highland, t);
            }
            float top = 1f - mountainLevel;
            return Color32.Lerp(Highland, Peak, top > 0f ? (elevation - mountainLevel) / top : 1f);
        }

        public static Color32 TemperatureColor(float t)
            => t < 0.5f ? Color32.Lerp(Cold, Mild, t * 2f) : Color32.Lerp(Mild, Hot, (t - 0.5f) * 2f);

        public static Color32 HumidityColor(float h) => Color32.Lerp(Dry, Wet, h);

        public static Color32 RarityColor(float r, float threshold)
            => r > threshold ? Rare : Color32.Lerp(Common, Mild, r * 0.35f);
    }
}
