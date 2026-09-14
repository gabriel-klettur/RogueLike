using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Where trees grow, and which kind. Pure and deterministic.
    ///
    /// <para><b>A jittered grid, not random points.</b> One candidate per cell of a grid sized from
    /// the biome's density, moved randomly inside its cell. Uniform random points clump and leave
    /// bald patches at the same density; a jittered grid reads as a wood.</para>
    ///
    /// <para><b>The biome picks the FAMILY,</b> which is what makes the woodcutting skill's ten
    /// families (and their wood tiers) exist somewhere in a generated world: taiga grows winter
    /// trees, swamps swamp trees, a corrupted patch corrupted ones.</para>
    /// </summary>
    public static class WorldTrees
    {
        private const int TreeSalt = 10;

        /// <summary>The finest grid cell used, in tiles — the densest woods get one tree per cell.</summary>
        public const int CellTiles = 5;

        /// <summary>Clear ground kept around a town, so its streets are not planted over.</summary>
        public const int TownClearance = 3;

        public static List<WorldTreeSite> Plan(WorldClimate climate, HashSet<Vector2Int> riverTiles,
                                                IReadOnlyList<WorldTown> towns)
            => Plan(climate, riverTiles, towns, null);

        /// <summary>As above, keeping the roads clear: no trunk on a road tile or beside one.</summary>
        public static List<WorldTreeSite> Plan(WorldClimate climate, HashSet<Vector2Int> riverTiles,
                                                IReadOnlyList<WorldTown> towns, HashSet<Vector2Int> roadTiles)
        {
            var sites = new List<WorldTreeSite>();
            var s = climate.Settings;
            if (s.treeDensity <= 0f) return sites;

            var rng = new System.Random(WorldSeed.Derive(s.seed, TreeSalt));
            for (int cy = 0; cy < s.heightTiles; cy += CellTiles)
                for (int cx = 0; cx < s.widthTiles; cx += CellTiles)
                {
                    // Drawn in a fixed order for every cell, used or not, so one biome's density
                    // never shifts where another biome's trees land.
                    int jx = rng.Next(CellTiles), jy = rng.Next(CellTiles);
                    double roll = rng.NextDouble();

                    var t = new Vector2Int(cx + jx, cy + jy);
                    if (t.x >= s.widthTiles || t.y >= s.heightTiles) continue;

                    var biome = climate.BiomeAt(t.x + 0.5f, t.y + 0.5f);
                    float chance = ChancePerCell(biome) * s.treeDensity;
                    if (chance <= 0f || roll >= chance) continue;
                    if (riverTiles != null && riverTiles.Contains(t)) continue;
                    if (NearTown(t, towns)) continue;
                    if (NearRoad(t, roadTiles)) continue;
                    if (!WorldTowns.IsBuildable(climate, riverTiles, t)) continue;

                    sites.Add(new WorldTreeSite(t, FamilyFor(biome)));
                }
            return sites;
        }

        /// <summary>Chance that a grid cell of this biome holds a tree. Dense woods near 1, open ground near 0.</summary>
        public static float ChancePerCell(WorldBiome biome)
        {
            switch (biome)
            {
                case WorldBiome.Forest:
                case WorldBiome.Jungle:
                case WorldBiome.Taiga:
                case WorldBiome.AutumnForest: return 0.75f;
                case WorldBiome.Enchanted:
                case WorldBiome.Corrupted: return 0.6f;
                case WorldBiome.Swamp: return 0.45f;
                case WorldBiome.Plains: return 0.12f;
                case WorldBiome.Snow: return 0.15f;
                case WorldBiome.Volcanic: return 0.1f;
                default: return 0f;
            }
        }

        public static TreeFamily FamilyFor(WorldBiome biome)
        {
            switch (biome)
            {
                case WorldBiome.AutumnForest: return TreeFamily.Autumn;
                case WorldBiome.Taiga:
                case WorldBiome.Snow: return TreeFamily.Winter;
                case WorldBiome.Jungle: return TreeFamily.Tropical;
                case WorldBiome.Swamp: return TreeFamily.Swamp;
                case WorldBiome.Volcanic: return TreeFamily.Volcanic;
                case WorldBiome.Enchanted: return TreeFamily.Enchanted;
                case WorldBiome.Corrupted: return TreeFamily.Corrupted;
                default: return TreeFamily.Common;
            }
        }

        /// <summary>A trunk carries a one-tile collider: on a road, or right beside it, it blocks the road.</summary>
        private static bool NearRoad(Vector2Int t, HashSet<Vector2Int> roadTiles)
        {
            if (roadTiles == null || roadTiles.Count == 0) return false;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (roadTiles.Contains(new Vector2Int(t.x + dx, t.y + dy))) return true;
            return false;
        }

        private static bool NearTown(Vector2Int t, IReadOnlyList<WorldTown> towns)
        {
            if (towns == null) return false;
            for (int i = 0; i < towns.Count; i++)
                if (towns[i].Contains(t, TownClearance)) return true;
            return false;
        }
    }
}
