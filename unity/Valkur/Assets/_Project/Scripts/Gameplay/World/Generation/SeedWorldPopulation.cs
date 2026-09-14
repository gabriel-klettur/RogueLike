using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.Spawners;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Who lives in a generated world, and what grows in it: the starting town's vendors, the
    /// hostile camps outside the towns, and the trees. Phase 4 of <c>.github/SEED_WORLD_ROADMAP.md</c>.
    ///
    /// <para><b>Spawners are written through <see cref="SpawnerInstanceSerializer"/>, the one write
    /// path the Spawners editor uses,</b> as v2 rows that each carry a SNAPSHOT of their preset. A
    /// row that only named a preset would be frozen against it by the loader — right for authored
    /// data, but a generated camp also needs its own level bonus, and a v2 config that set only the
    /// bonus would read every other field as a class default and lose the roster.</para>
    ///
    /// <para><b>The vendors go only to the starting town.</b> Each is a single named character with
    /// a persona and a memory of the player; four Gatitas in four towns is not a population, it is a
    /// continuity error.</para>
    /// </summary>
    public static class SeedWorldPopulation
    {
        private const int TreePickSalt = 11;
        private const int EncounterPickSalt = 12;

        /// <summary>Extra levels at the far edge of the world, on top of the preset's own bonus.</summary>
        public const int DistanceLevelBonus = 3;

        /// <summary>Below this difficulty the dragon's lair is never chosen, however the roll lands.</summary>
        public const float BossDifficulty = 0.8f;

        /// <summary>Chance a common tree in the wild is an ancient one instead.</summary>
        public const double AncientChance = 0.04;

        [Valkur.Core.SelfHealingStatic("Immutable table of preset id strings; never mutated.")]
        public static readonly string[] VendorPresets =
        {
            "vendor_gatita_respawn_5m", "vendor_smith_respawn_5m", "vendor_valeria_respawn_5m",
            "vendor_roberto_respawn_5m", "vendor_abigail_respawn_5m", "vendor_pavel_respawn_5m",
        };

        /// <summary>Hostile presets from least to most dangerous, by their own authored level bonus.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of preset id strings; never mutated.")]
        public static readonly string[] HostilePresets =
        {
            "mon1_scouts", "barbol_saplings", "barbol_grove", "barbol_raiders", "dark_scout_pair",
            "barbol_grove_elder", "dark_shield_line", "dark_coven", "dark_warband", "dark_vampire_court",
            "red_dragon_lair",
        };

        // ── Spawners ───────────────────────────────────────────────────────────

        public static List<SpawnerInstanceRecord> SpawnerRecords(WorldGenMap map, SpawnerTemplateCatalog catalog,
                                                                 string[,] zoneNames, int zoneSize)
        {
            var records = new List<SpawnerInstanceRecord>();
            if (map == null || catalog == null) return records;

            foreach (var town in map.Towns)
            {
                if (!town.IsStart) continue;
                var tiles = WorldTowns.ResidentTiles(town, VendorPresets.Length);
                for (int i = 0; i < tiles.Count; i++)
                    AddRecord(records, catalog, VendorPresets[i], tiles[i], 0, zoneNames, zoneSize);
            }

            var rng = new System.Random(WorldSeed.Derive(map.Climate.Settings.seed, EncounterPickSalt));
            foreach (var site in map.Encounters)
            {
                string preset = HostileFor(site.Difficulty, rng);
                int bonus = Mathf.RoundToInt(site.Difficulty * DistanceLevelBonus);
                AddRecord(records, catalog, preset, site.Tile, bonus, zoneNames, zoneSize);
            }
            return records;
        }

        /// <summary>
        /// The preset for a difficulty: its place on the ladder plus or minus one step, so two camps
        /// at the same distance are not always the same camp. The last step (the dragon) is kept for
        /// the far edge of the world.
        /// </summary>
        public static string HostileFor(float difficulty, System.Random rng)
        {
            int last = HostilePresets.Length - 1;
            int ladder = difficulty >= BossDifficulty ? last : last - 1;
            int index = Mathf.RoundToInt(Mathf.Clamp01(difficulty) * ladder) + rng.Next(-1, 2);
            index = Mathf.Clamp(index, 0, ladder);
            return HostilePresets[index];
        }

        private static void AddRecord(List<SpawnerInstanceRecord> records, SpawnerTemplateCatalog catalog,
                                      string presetId, Vector2Int tile, int extraLevels,
                                      string[,] zoneNames, int z)
        {
            var preset = catalog.GetById(presetId);
            if (preset == null) return;

            int zx = Mathf.Clamp(tile.x / z, 0, zoneNames.GetLength(0) - 1);
            int zy = Mathf.Clamp(tile.y / z, 0, zoneNames.GetLength(1) - 1);

            var config = SpawnerInstanceConfig.SnapshotOf(preset);
            config.levelBonus += extraLevels;

            records.Add(new SpawnerInstanceRecord
            {
                TemplateId = presetId,
                Zone = zoneNames[zx, zy],
                // Zone-relative, row 0 at the TOP — SpawnerTileMapping's convention, and the one the
                // spawner coordinate-drift incident was about.
                Tile = new Vector2Int(tile.x - zx * z, (z - 1) - (tile.y - zy * z)),
                InstanceId = $"sw_{presetId}_{records.Count}",
                Config = config,
                HadConfig = true,
            });
        }

        // ── Trees ──────────────────────────────────────────────────────────────

        /// <summary>Tree templates grouped by family, skipping anything too large to read as a single tree.</summary>
        public static Dictionary<TreeFamily, List<BuildingTemplateData>> TreeTemplates(BuildingCatalog catalog, int maxPixels = 320)
        {
            var byFamily = new Dictionary<TreeFamily, List<BuildingTemplateData>>();
            if (catalog == null) return byFamily;

            var seen = new HashSet<string>();
            foreach (var t in catalog.Templates)
            {
                if (t == null || string.IsNullOrEmpty(t.assetPath) || !seen.Add(t.assetPath)) continue;
                if (t.originalScale.x <= 0 || t.originalScale.y <= 0) continue;
                if (Mathf.Max(t.originalScale.x, t.originalScale.y) > maxPixels) continue;
                var family = TreeFamilyClassifier.Classify(t.assetPath);
                if (family == TreeFamily.None || family == TreeFamily.Small) continue;
                if (!byFamily.TryGetValue(family, out var list)) byFamily[family] = list = new List<BuildingTemplateData>();
                list.Add(t);
            }
            foreach (var list in byFamily.Values) list.Sort((a, b) => a.templateId.CompareTo(b.templateId));
            return byFamily;
        }

        /// <summary>
        /// Turns tree sites into placements. The TRUNK footprint (the sprite's width by two rows at
        /// its base) must be free of every other placement; canopies may overlap, because that is
        /// what a wood looks like.
        /// </summary>
        public static List<WorldTownPlacement> TreePlacements(WorldGenMap map, BuildingCatalog catalog,
                                                              IReadOnlyList<WorldTownPlacement> existing)
        {
            var placements = new List<WorldTownPlacement>();
            var byFamily = TreeTemplates(catalog);
            if (byFamily.Count == 0) return placements;

            var occupied = new HashSet<Vector2Int>();
            if (existing != null)
                foreach (var p in existing)
                    for (int y = p.Rect.yMin - 1; y < p.Rect.yMax + 1; y++)
                        for (int x = p.Rect.xMin - 1; x < p.Rect.xMax + 1; x++)
                            occupied.Add(new Vector2Int(x, y));

            var rng = new System.Random(WorldSeed.Derive(map.Climate.Settings.seed, TreePickSalt));
            var sites = WorldTrees.Plan(map.Climate, map.RiverTiles, map.Towns);
            foreach (var site in sites)
            {
                double ancientRoll = rng.NextDouble();
                int pick = rng.Next(int.MaxValue);

                var family = site.Family == TreeFamily.Common && ancientRoll < AncientChance ? TreeFamily.Ancient : site.Family;
                if (!byFamily.TryGetValue(family, out var pool) && !byFamily.TryGetValue(TreeFamily.Common, out pool)) continue;
                var template = pool[pick % pool.Count];

                int w = Mathf.CeilToInt(template.originalScale.x / 32f);
                int h = Mathf.CeilToInt(template.originalScale.y / 32f);
                var rect = new RectInt(site.Tile.x - w / 2, site.Tile.y, w, h);

                bool free = true;
                for (int y = rect.yMin; y < rect.yMin + 2 && free; y++)
                    for (int x = rect.xMin; x < rect.xMax; x++)
                        if (occupied.Contains(new Vector2Int(x, y))) { free = false; break; }
                if (!free) continue;

                for (int y = rect.yMin; y < rect.yMin + 2; y++)
                    for (int x = rect.xMin; x < rect.xMax; x++)
                        occupied.Add(new Vector2Int(x, y));

                placements.Add(new WorldTownPlacement(-1,
                    new WorldTownBuildingOption(template.templateId, w, h, WorldTownPieceKind.Tree), rect));
            }
            return placements;
        }
    }
}
