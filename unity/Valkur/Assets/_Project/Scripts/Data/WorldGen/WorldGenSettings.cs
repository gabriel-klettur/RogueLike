using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Every parameter of the world generator, as one serializable value.
    ///
    /// <para><b>A value, not an asset.</b> The Seed World editor edits a copy, snapshots it for
    /// undo as JSON and carries it in its workspace; a <see cref="WorldGenProfile"/> wraps one
    /// for presets. Keeping the generator's input a plain class is what lets the whole pipeline
    /// be tested in EditMode without a scene.</para>
    ///
    /// <para><b>Distances are in TILES</b> (one tile = one world unit). Every 0..1 dial is a
    /// fraction of the noise range, not of the map.</para>
    /// </summary>
    [Serializable]
    public sealed class WorldGenSettings
    {
        public const int MinSizeTiles = 64;
        public const int MaxWidthTiles = 2048;
        public const int MaxRivers = 40;
        public const int MaxRiverWidth = 3;

        /// <summary>
        /// The tallest world the Y-sort can hold without an origin shift. Sorting orders are a
        /// 16-bit short and the Y term is clamped at <see cref="SortingConfig.MAX_SAFE_WORLD_Y"/>
        /// either side of zero, so a world centred on the origin may span twice that — minus a
        /// margin, because a building's canopy is drawn above its own row.
        /// </summary>
        public static int MaxHeightTiles => SortingConfig.MAX_SAFE_WORLD_Y * 2 - 34;

        [Tooltip("The number every other decision derives from. Same seed + same settings = same world.")]
        public int seed = 1337;

        [Tooltip("World width in tiles.")]
        public int widthTiles = 400;

        [Tooltip("World height in tiles. Capped by the Y-sort budget (see MaxHeightTiles).")]
        public int heightTiles = 400;

        [Tooltip("Typical size of a continent, in tiles. Larger = fewer, bigger landmasses.")]
        public float continentScale = 150f;

        [Tooltip("Noise octaves layered over the continents. More = rougher coasts.")]
        public int detailOctaves = 5;

        [Tooltip("Elevation below which a cell is water. 0.5 is about half the map underwater.")]
        public float seaLevel = 0.36f;

        [Tooltip("Elevation above which a cell is mountain.")]
        public float mountainLevel = 0.78f;

        [Tooltip("How strongly the edges of the map sink into the sea. 0 = the world ends in a cut.")]
        public float edgeFalloff = 0.6f;

        [Tooltip("Typical size of a climate region, in tiles.")]
        public float climateScale = 110f;

        [Tooltip("Shifts the whole world warmer (+) or colder (-).")]
        public float temperatureBias = 0f;

        [Tooltip("Shifts the whole world wetter (+) or drier (-).")]
        public float humidityBias = 0f;

        [Tooltip("How much colder the north of the map is than the south. 0 = no latitude.")]
        public float latitude = 0.35f;

        [Tooltip("How much land the rare biomes (enchanted, corrupted) may claim.")]
        public float rarity = 0.3f;

        [Tooltip("How many rivers the generator tries to run from the highlands to the sea.")]
        public int riverCount = 8;

        [Tooltip("River width in tiles.")]
        public int riverWidth = 1;

        [Tooltip("Which biomes may appear and how much of the climate plane each claims.")]
        public List<WorldBiomeWeight> biomes = new List<WorldBiomeWeight>();

        public WorldGenSettings()
        {
            EnsureAllBiomes();
        }

        /// <summary>
        /// One row per biome, in enum order, with nothing duplicated. Called on construction and
        /// after every deserialisation: a profile written before a biome was appended simply
        /// gains the new row, enabled at weight 1, rather than failing to mention it — which the
        /// generator would otherwise have to read as "disabled" or "enabled" by guessing.
        /// </summary>
        public void EnsureAllBiomes()
        {
            if (biomes == null) biomes = new List<WorldBiomeWeight>();

            var byBiome = new WorldBiomeWeight[WorldBiomeTable.Count];
            foreach (var row in biomes)
            {
                if (row == null) continue;
                int i = (int)row.biome;
                if (i < 0 || i >= byBiome.Length || byBiome[i] != null) continue;
                byBiome[i] = row;
            }

            biomes.Clear();
            for (int i = 0; i < byBiome.Length; i++)
                biomes.Add(byBiome[i] ?? new WorldBiomeWeight((WorldBiome)i));
        }

        public WorldBiomeWeight WeightOf(WorldBiome biome)
        {
            int i = (int)biome;
            if (biomes == null || biomes.Count != WorldBiomeTable.Count) EnsureAllBiomes();
            return biomes[i];
        }

        /// <summary>
        /// Brings every field back inside the range the generator is defined on. Idempotent, and
        /// applied by the generator itself rather than only by the editor, because a profile can
        /// arrive from a workspace file or an asset written by hand.
        /// </summary>
        public void Clamp()
        {
            widthTiles = Mathf.Clamp(widthTiles, MinSizeTiles, MaxWidthTiles);
            heightTiles = Mathf.Clamp(heightTiles, MinSizeTiles, MaxHeightTiles);
            continentScale = Mathf.Clamp(continentScale, 20f, 1000f);
            detailOctaves = Mathf.Clamp(detailOctaves, 1, 8);
            seaLevel = Mathf.Clamp01(seaLevel);
            mountainLevel = Mathf.Clamp(mountainLevel, seaLevel, 1f);
            edgeFalloff = Mathf.Clamp01(edgeFalloff);
            climateScale = Mathf.Clamp(climateScale, 30f, 1500f);
            temperatureBias = Mathf.Clamp(temperatureBias, -0.5f, 0.5f);
            humidityBias = Mathf.Clamp(humidityBias, -0.5f, 0.5f);
            latitude = Mathf.Clamp01(latitude);
            rarity = Mathf.Clamp01(rarity);
            riverCount = Mathf.Clamp(riverCount, 0, MaxRivers);
            riverWidth = Mathf.Clamp(riverWidth, 1, MaxRiverWidth);

            EnsureAllBiomes();
            foreach (var row in biomes)
                row.weight = Mathf.Clamp(row.weight, WorldBiomeWeight.MinWeight, WorldBiomeWeight.MaxWeight);
        }

        public string ToJson() => JsonUtility.ToJson(this);

        /// <summary>Null on malformed input, never a half-read object.</summary>
        public static WorldGenSettings FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var s = JsonUtility.FromJson<WorldGenSettings>(json);
                if (s == null) return null;
                s.Clamp();
                return s;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public WorldGenSettings Clone() => FromJson(ToJson());
    }
}
