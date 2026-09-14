using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Where the fights are. Pure and deterministic.
    ///
    /// <para><b>Danger grows with distance from where the run begins</b> — the roguelike reading of
    /// Minecraft's "the further you go, the stranger it gets". Difficulty is the distance from the
    /// spawn divided by the farthest distance any point of the world can be, so it spans the same
    /// 0..1 on a small world and a large one.</para>
    ///
    /// <para><b>Never inside a town, never beside one.</b> A spawner that fires inside a town turns
    /// every vendor into a bystander in a fight they cannot survive, and a camp at the gate makes
    /// the starting town the most dangerous place on the map.</para>
    /// </summary>
    public static class WorldEncounters
    {
        private const int EncounterSalt = 9;

        /// <summary>Clear ground between an encounter and a town's edge, in tiles.</summary>
        public const int TownClearance = 14;

        /// <summary>Minimum distance between two encounters, in tiles.</summary>
        public const int Spacing = 26;

        /// <summary>No encounter this close to the spawn, whatever the towns are doing.</summary>
        public const int SpawnClearance = 30;

        private const int AttemptsPerEncounter = 40;

        public static List<WorldEncounterSite> Plan(WorldClimate climate, HashSet<Vector2Int> riverTiles,
                                                     IReadOnlyList<WorldTown> towns, Vector2 spawnTile)
        {
            var sites = new List<WorldEncounterSite>();
            var s = climate.Settings;
            if (s.encounterCount <= 0) return sites;

            var rng = new System.Random(WorldSeed.Derive(s.seed, EncounterSalt));
            var spawn = Vector2Int.FloorToInt(spawnTile);
            float farthest = Mathf.Max(1f, FarthestFrom(spawn, s));

            int attempts = s.encounterCount * AttemptsPerEncounter;
            for (int a = 0; a < attempts && sites.Count < s.encounterCount; a++)
            {
                var t = new Vector2Int(rng.Next(4, s.widthTiles - 4), rng.Next(4, s.heightTiles - 4));
                if ((t - spawn).sqrMagnitude < SpawnClearance * SpawnClearance) continue;
                if (NearTown(t, towns)) continue;
                if (NearOther(t, sites)) continue;
                if (!OpenGround(climate, riverTiles, t)) continue;

                float difficulty = Mathf.Clamp01(Vector2Int.Distance(t, spawn) / farthest);
                sites.Add(new WorldEncounterSite(t, difficulty));
            }
            return sites;
        }

        private static float FarthestFrom(Vector2Int p, WorldGenSettings s)
        {
            float dx = Mathf.Max(p.x, s.widthTiles - p.x);
            float dy = Mathf.Max(p.y, s.heightTiles - p.y);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static bool NearTown(Vector2Int t, IReadOnlyList<WorldTown> towns)
        {
            if (towns == null) return false;
            for (int i = 0; i < towns.Count; i++)
                if (towns[i].Contains(t, TownClearance)) return true;
            return false;
        }

        private static bool NearOther(Vector2Int t, List<WorldEncounterSite> sites)
        {
            for (int i = 0; i < sites.Count; i++)
                if ((sites[i].Tile - t).sqrMagnitude < Spacing * Spacing) return true;
            return false;
        }

        /// <summary>A 5x5 patch of buildable ground: a camp's monsters spawn in a ring around its tile.</summary>
        private static bool OpenGround(WorldClimate climate, HashSet<Vector2Int> riverTiles, Vector2Int t)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (!WorldTowns.IsBuildable(climate, riverTiles, t + new Vector2Int(dx, dy))) return false;
            return true;
        }
    }
}
