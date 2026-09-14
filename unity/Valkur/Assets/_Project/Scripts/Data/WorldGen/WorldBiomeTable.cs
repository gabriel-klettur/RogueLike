using System;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// One <see cref="WorldBiomeInfo"/> per <see cref="WorldBiome"/>, indexed by the enum value.
    ///
    /// <para>A static table rather than an asset: these are the RULES of the vocabulary (which
    /// family a biome belongs to, where it sits in the climate plane), and a profile that could
    /// move Ocean into the Land family would make the elevation guarantees above meaningless.
    /// What a profile may change — enabled, weight — lives on <see cref="WorldBiomeWeight"/>.</para>
    /// </summary>
    public static class WorldBiomeTable
    {
        [Valkur.Core.SelfHealingStatic("Immutable table of constant biome rules, written once at type " +
            "initialisation and never mutated; holds no Unity objects, so it cannot go stale across a Play session.")]
        private static readonly WorldBiomeInfo[] Entries =
        {
            new WorldBiomeInfo(WorldBiome.DeepOcean,    WorldBiomeKind.Water,    "Oceano profundo", 0.50f, 1.00f, new Color32( 20,  46,  96, 255), "water_deep"),
            new WorldBiomeInfo(WorldBiome.Ocean,        WorldBiomeKind.Water,    "Oceano",          0.50f, 1.00f, new Color32( 38,  86, 156, 255), "water"),
            new WorldBiomeInfo(WorldBiome.Beach,        WorldBiomeKind.Shore,    "Costa",           0.60f, 0.50f, new Color32(222, 204, 140, 255), "sand"),
            new WorldBiomeInfo(WorldBiome.Plains,       WorldBiomeKind.Land,     "Llanura",         0.55f, 0.40f, new Color32(128, 186,  84, 255), "grass"),
            new WorldBiomeInfo(WorldBiome.Forest,       WorldBiomeKind.Land,     "Bosque",          0.52f, 0.66f, new Color32( 52, 132,  62, 255), "grass"),
            new WorldBiomeInfo(WorldBiome.AutumnForest, WorldBiomeKind.Land,     "Bosque otonal",   0.38f, 0.52f, new Color32(190, 110,  46, 255), "dirt"),
            new WorldBiomeInfo(WorldBiome.Taiga,        WorldBiomeKind.Land,     "Taiga",           0.22f, 0.62f, new Color32( 58, 102,  86, 255), "grass"),
            new WorldBiomeInfo(WorldBiome.Snow,         WorldBiomeKind.Land,     "Nieve",           0.06f, 0.40f, new Color32(232, 240, 246, 255), "rock"),
            new WorldBiomeInfo(WorldBiome.Desert,       WorldBiomeKind.Land,     "Desierto",        0.90f, 0.10f, new Color32(236, 196, 104, 255), "sand"),
            new WorldBiomeInfo(WorldBiome.Jungle,       WorldBiomeKind.Land,     "Selva",           0.88f, 0.82f, new Color32( 28, 150,  74, 255), "grass"),
            new WorldBiomeInfo(WorldBiome.Swamp,        WorldBiomeKind.Land,     "Pantano",         0.64f, 0.94f, new Color32( 76, 102,  66, 255), "dirt"),
            new WorldBiomeInfo(WorldBiome.Mountain,     WorldBiomeKind.Highland, "Montana",         0.30f, 0.50f, new Color32(128, 124, 120, 255), "rock"),
            new WorldBiomeInfo(WorldBiome.Volcanic,     WorldBiomeKind.Highland, "Volcanico",       0.95f, 0.10f, new Color32( 96,  40,  34, 255), "stone"),
            new WorldBiomeInfo(WorldBiome.Enchanted,    WorldBiomeKind.Rare,     "Encantado",       0.50f, 0.80f, new Color32(150,  96, 210, 255), "grass"),
            new WorldBiomeInfo(WorldBiome.Corrupted,    WorldBiomeKind.Rare,     "Corrupto",        0.60f, 0.20f, new Color32( 78,  44,  88, 255), "dirt"),
            new WorldBiomeInfo(WorldBiome.River,        WorldBiomeKind.Water,    "Rio",             0.50f, 1.00f, new Color32( 82, 150, 214, 255), "water"),
        };

        /// <summary>How many biomes the vocabulary holds.</summary>
        public static int Count => Entries.Length;

        public static WorldBiomeInfo Get(WorldBiome biome)
        {
            int i = (int)biome;
            if (i < 0 || i >= Entries.Length)
                throw new ArgumentOutOfRangeException(nameof(biome), biome, "Unknown biome.");
            return Entries[i];
        }

        public static WorldBiomeInfo GetAt(int index) => Entries[index];
    }
}
