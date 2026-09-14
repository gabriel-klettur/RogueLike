using System;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// What a profile says about one biome: whether it may appear, and how much of the climate
    /// plane it claims against its neighbours.
    /// </summary>
    [Serializable]
    public sealed class WorldBiomeWeight
    {
        public const float MinWeight = 0.1f;
        public const float MaxWeight = 5f;

        [Tooltip("Which biome this row configures.")]
        public WorldBiome biome;

        [Tooltip("A disabled biome never appears; its share of the climate plane goes to its neighbours.")]
        public bool enabled = true;

        [Tooltip("1 is neutral. 2 claims roughly twice the climate area against a weight-1 neighbour.")]
        public float weight = 1f;

        public WorldBiomeWeight() { }

        public WorldBiomeWeight(WorldBiome biome, bool enabled = true, float weight = 1f)
        {
            this.biome = biome;
            this.enabled = enabled;
            this.weight = weight;
        }
    }
}
