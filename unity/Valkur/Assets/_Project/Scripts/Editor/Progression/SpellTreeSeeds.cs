using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Progression
{
    /// <summary>
    /// The authored grimoire: which school each shipped spell belongs to, in what order it
    /// unlocks, and what mastery each school hands out along the way.
    ///
    /// The classification is a hand-written table and not derived from
    /// <c>SpellDefinition.element</c>, because that field cannot carry it: of the 46
    /// player-castable spells, 30 leave it EMPTY and the 16 that fill it disagree on case
    /// and vocabulary ("Fire", "fire", "lava", "physical"). Deriving schools from it would
    /// put three quarters of the game's spells in a school called "".
    ///
    /// Every one of the 46 appears exactly once. <c>ShippedGrimoireCoverageTests</c> fails
    /// when a spell exists that no school teaches — the check that stops the next authored
    /// spell quietly becoming uncastable content, which is the failure mode this project
    /// has recorded eleven times over.
    /// </summary>
    internal static class SpellTreeSeeds
    {
        /// <summary>
        /// Known without spending a point. Kept to two: the swing the character is holding
        /// a weapon for, and the toggle that puts it away. A starting kit is the only
        /// content the grimoire cannot charge for, so it stays the size of a tutorial.
        /// </summary>
        public static readonly string[] InnateSpellKeys = { "slash_regular", "weapon_toggle" };

        private sealed class Entry
        {
            public string SpellKey;
            public int Cost = 1;
            public int Level;
            public string Prerequisite;      // spell key of the node before it, or null
            public StatModifier[] Modifiers = System.Array.Empty<StatModifier>();
        }

        private sealed class SchoolSeed
        {
            public string Key;
            public string DisplayName;
            public string Flavour;
            public Color Accent;
            public string[] Affinities;
            public Entry[] Entries;
        }

        public static List<SpellTree> BuildAll(string root, List<SpellDefinition> spells)
        {
            var byKey = new Dictionary<string, SpellDefinition>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var s in spells)
                if (s != null && !string.IsNullOrWhiteSpace(s.spellKey)) byKey[s.spellKey] = s;

            var trees = new List<SpellTree>();
            var claimed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var seed in All)
            {
                trees.Add(Build(root, seed, byKey, claimed));
            }

            ReportUnclaimed(spells, claimed);
            return trees;
        }

        private static SpellTree Build(string root, SchoolSeed seed,
                                       Dictionary<string, SpellDefinition> byKey,
                                       HashSet<string> claimed)
        {
            string folder = $"{root}/{seed.Key}";
            ProgressionSeeder.EnsureFolder(folder);

            var byNodeId = new Dictionary<string, SpellNode>();
            var ordered = new List<SpellNode>();

            for (int i = 0; i < seed.Entries.Length; i++)
            {
                var entry = seed.Entries[i];
                string nodeId = $"{seed.Key}_{entry.SpellKey}";
                string path = $"{folder}/{nodeId}.asset";

                var node = ProgressionSeeder.LoadOrCreate<SpellNode>(path, out bool created);
                if (created)
                {
                    node.nodeId = nodeId;
                    node.pointCost = entry.Cost;
                    node.levelRequirement = entry.Level;
                    node.modifiers = entry.Modifiers;
                    node.row = i;
                    node.column = 0;
                }

                // The spell link is refreshed even on an existing node: it is the node's
                // whole purpose, and a node pointing at a spell asset that has since been
                // renamed teaches nothing while looking perfectly healthy.
                if (byKey.TryGetValue(entry.SpellKey, out var def))
                {
                    node.spell = def;
                    claimed.Add(entry.SpellKey);
                }
                else
                {
                    Debug.LogWarning($"[SpellTreeSeeds] School '{seed.Key}' references spell " +
                                     $"'{entry.SpellKey}', which is not in the catalog. The node " +
                                     "will exist and teach nothing.");
                }

                EditorUtility.SetDirty(node);
                byNodeId[entry.SpellKey] = node;
                ordered.Add(node);
            }

            // Prerequisites in a second pass, for the same reason the talent seeder needs
            // one: a node cannot point at an asset that does not exist yet.
            foreach (var entry in seed.Entries)
            {
                if (string.IsNullOrEmpty(entry.Prerequisite)) continue;
                var node = byNodeId[entry.SpellKey];
                if (node.prerequisites != null && node.prerequisites.Length > 0) continue;
                if (!byNodeId.TryGetValue(entry.Prerequisite, out var prereq)) continue;

                node.prerequisites = new[] { prereq };
                EditorUtility.SetDirty(node);
            }

            var tree = ProgressionSeeder.LoadOrCreate<SpellTree>(
                $"{folder}/{seed.Key}_school.asset", out bool treeCreated);
            if (treeCreated)
            {
                tree.schoolKey = seed.Key;
                tree.displayName = seed.DisplayName;
                tree.flavour = seed.Flavour;
                tree.accent = seed.Accent;
                tree.classAffinities = seed.Affinities;
                tree.offAffinityCostMultiplier = 2f;
            }
            tree.EditorSetNodes(ordered.ToArray());
            EditorUtility.SetDirty(tree);
            return tree;
        }

        private static void ReportUnclaimed(List<SpellDefinition> spells, HashSet<string> claimed)
        {
            var missing = new List<string>();
            foreach (var s in spells)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.spellKey)) continue;
                // Only player-castable spells need a home. The animation probes exist so an
                // artist can watch a sheet in the Spells Editor and are deliberately not
                // content — see SpellType.AnimationProbe.
                if ((s.audience & SpellAudience.Player) == 0) continue;
                if (claimed.Contains(s.spellKey)) continue;
                if (System.Array.IndexOf(InnateSpellKeys, s.spellKey) >= 0) continue;
                missing.Add(s.spellKey);
            }

            if (missing.Count == 0) return;
            Debug.LogWarning($"[SpellTreeSeeds] {missing.Count} player-castable spell(s) belong " +
                             $"to no school and are therefore unlearnable: {string.Join(", ", missing)}");
        }

        private static StatModifier[] Mods(params StatModifier[] m) => m;

        // ── The schools ─────────────────────────────────────────────────────────
        //
        // Each school opens on a cheap, low-level spell and ends on its signature. Cost
        // rises with position, so depth in one school and breadth across several are
        // genuinely different builds rather than the same points spent in a different
        // order. Masteries are attached to the LATER nodes of a school so that committing
        // is what pays, not dabbling.

        private static readonly SchoolSeed[] All =
        {
            new SchoolSeed
            {
                Key = "martial", DisplayName = "Martial Forms",
                Flavour = "No words. Just the weight of the thing in your hands.",
                Accent = new Color(0.82f, 0.80f, 0.74f),
                Affinities = new[] { "dwarf", "barbarian", "valkyrie" },
                Entries = new[]
                {
                    new Entry { SpellKey = "slash" },
                    new Entry { SpellKey = "dash", Level = 3, Prerequisite = "slash" },
                    new Entry { SpellKey = "slash_stab", Level = 6, Prerequisite = "dash",
                        Modifiers = Mods(StatModifier.Flat(StatKind.MeleeRange, 0.1f)) },
                    new Entry { SpellKey = "slash_cleave", Cost = 2, Level = 12, Prerequisite = "slash_stab",
                        Modifiers = Mods(StatModifier.Percent(StatKind.MeleeDamage, 0.05f)) },
                    new Entry { SpellKey = "slash_combo", Cost = 2, Level = 18, Prerequisite = "slash_cleave",
                        Modifiers = Mods(StatModifier.Percent(StatKind.MeleeCooldown, -0.05f)) },
                },
            },

            new SchoolSeed
            {
                Key = "pyromancy", DisplayName = "Pyromancy",
                Flavour = "Fire is the only school that answers before it is finished being asked.",
                Accent = new Color(1f, 0.45f, 0.15f),
                Affinities = new[] { "mague", "barbarian" },
                Entries = new[]
                {
                    new Entry { SpellKey = "fireball" },
                    new Entry { SpellKey = "laser_beam_red", Level = 4, Prerequisite = "fireball" },
                    new Entry { SpellKey = "firework_launch", Level = 6, Prerequisite = "fireball" },
                    new Entry { SpellKey = "flame_breath", Cost = 2, Level = 10, Prerequisite = "laser_beam_red",
                        Modifiers = Mods(StatModifier.Percent(StatKind.SpellPower, 0.04f)) },
                    new Entry { SpellKey = "puddle_lava", Cost = 2, Level = 14, Prerequisite = "flame_breath" },
                    new Entry { SpellKey = "meteor_shower", Cost = 3, Level = 22, Prerequisite = "puddle_lava",
                        Modifiers = Mods(StatModifier.Percent(StatKind.SpellPower, 0.08f)) },
                },
            },

            new SchoolSeed
            {
                Key = "cryomancy", DisplayName = "Cryomancy",
                Flavour = "Not colder. Slower, until nothing moves at all.",
                Accent = new Color(0.45f, 0.78f, 1f),
                Affinities = new[] { "mague", "elven" },
                Entries = new[]
                {
                    new Entry { SpellKey = "iceball" },
                    new Entry { SpellKey = "wall_ice", Cost = 2, Level = 9, Prerequisite = "iceball",
                        Modifiers = Mods(StatModifier.Flat(StatKind.Defense, 2f)) },
                },
            },

            new SchoolSeed
            {
                Key = "storm", DisplayName = "Stormcalling",
                Flavour = "The sky keeps no ledger and settles all at once.",
                Accent = new Color(0.95f, 0.90f, 0.35f),
                Affinities = new[] { "mague", "valkyrie" },
                Entries = new[]
                {
                    new Entry { SpellKey = "laser_beam_yellow" },
                    new Entry { SpellKey = "lightning", Level = 6, Prerequisite = "laser_beam_yellow" },
                    new Entry { SpellKey = "lightning_beam", Cost = 2, Level = 12, Prerequisite = "lightning" },
                    new Entry { SpellKey = "chain_lightning", Cost = 3, Level = 20, Prerequisite = "lightning_beam",
                        Modifiers = Mods(StatModifier.Flat(StatKind.SpellCooldownReduction, 0.05f)) },
                },
            },

            new SchoolSeed
            {
                Key = "arcane", DisplayName = "Arcana",
                Flavour = "The school that studies the other schools.",
                Accent = new Color(0.70f, 0.45f, 1f),
                Affinities = new[] { "mague", "elven" },
                Entries = new[]
                {
                    new Entry { SpellKey = "laser_beam" },
                    new Entry { SpellKey = "laser_beam_blue", Level = 4, Prerequisite = "laser_beam" },
                    new Entry { SpellKey = "boomerang", Level = 6, Prerequisite = "laser_beam" },
                    new Entry { SpellKey = "teleport", Cost = 2, Level = 8, Prerequisite = "laser_beam_blue",
                        Modifiers = Mods(StatModifier.Percent(StatKind.MoveSpeed, 0.03f)) },
                    new Entry { SpellKey = "mine_basic", Cost = 2, Level = 12, Prerequisite = "boomerang" },
                    new Entry { SpellKey = "arcane_flame", Cost = 2, Level = 15, Prerequisite = "teleport",
                        Modifiers = Mods(StatModifier.Flat(StatKind.ManaCostReduction, 0.04f)) },
                    new Entry { SpellKey = "vortex_pull", Cost = 3, Level = 20, Prerequisite = "arcane_flame" },
                    new Entry { SpellKey = "vortex_push", Cost = 3, Level = 20, Prerequisite = "arcane_flame" },
                },
            },

            new SchoolSeed
            {
                Key = "radiance", DisplayName = "Radiance",
                Flavour = "Keeping people alive is the harder half of any fight.",
                Accent = new Color(1f, 0.95f, 0.72f),
                Affinities = new[] { "valkyrie", "mague" },
                Entries = new[]
                {
                    new Entry { SpellKey = "lightball" },
                    new Entry { SpellKey = "laser_beam_white", Level = 4, Prerequisite = "lightball" },
                    new Entry { SpellKey = "healing_aura", Cost = 2, Level = 8, Prerequisite = "lightball",
                        Modifiers = Mods(StatModifier.Flat(StatKind.MaxHp, 20f)) },
                    new Entry { SpellKey = "sphere_magic_shield", Cost = 2, Level = 14, Prerequisite = "healing_aura",
                        Modifiers = Mods(StatModifier.Flat(StatKind.Defense, 3f)) },
                    new Entry { SpellKey = "healing_totem", Cost = 3, Level = 18, Prerequisite = "sphere_magic_shield" },
                },
            },

            new SchoolSeed
            {
                Key = "shadow", DisplayName = "Umbramancy",
                Flavour = "Not being seen is a form of armour.",
                Accent = new Color(0.45f, 0.35f, 0.55f),
                Affinities = new[] { "elven", "mague" },
                Entries = new[]
                {
                    new Entry { SpellKey = "darkball" },
                    new Entry { SpellKey = "laser_beam_black", Level = 4, Prerequisite = "darkball" },
                    new Entry { SpellKey = "smoke", Cost = 2, Level = 8, Prerequisite = "darkball" },
                    new Entry { SpellKey = "smoke_emitter", Cost = 2, Level = 14, Prerequisite = "smoke",
                        Modifiers = Mods(StatModifier.Flat(StatKind.CritChance, 0.04f)) },
                },
            },

            new SchoolSeed
            {
                Key = "verdant", DisplayName = "Verdant Rites",
                Flavour = "Ask the ground for help. It usually agrees.",
                Accent = new Color(0.45f, 0.85f, 0.40f),
                Affinities = new[] { "elven", "dwarf" },
                Entries = new[]
                {
                    new Entry { SpellKey = "laser_beam_green" },
                    new Entry { SpellKey = "root_whip", Cost = 2, Level = 8, Prerequisite = "laser_beam_green" },
                    new Entry { SpellKey = "summon_barbol", Cost = 3, Level = 16, Prerequisite = "root_whip",
                        Modifiers = Mods(StatModifier.Flat(StatKind.MaxHp, 25f)) },
                },
            },

            new SchoolSeed
            {
                Key = "ki", DisplayName = "Inner Fire",
                Flavour = "Seven ways of burning without being consumed.",
                Accent = new Color(0.55f, 0.85f, 0.95f),
                Affinities = new[] { "dwarf", "barbarian", "valkyrie", "elven", "mague" },
                Entries = new[]
                {
                    new Entry { SpellKey = "charge_ki_spirit" },
                    new Entry { SpellKey = "charge_ki_azure", Level = 5, Prerequisite = "charge_ki_spirit" },
                    new Entry { SpellKey = "charge_ki_verdant", Level = 5, Prerequisite = "charge_ki_spirit" },
                    new Entry { SpellKey = "charge_ki_crimson", Cost = 2, Level = 12, Prerequisite = "charge_ki_azure" },
                    new Entry { SpellKey = "charge_ki_solar", Cost = 2, Level = 12, Prerequisite = "charge_ki_verdant" },
                    new Entry { SpellKey = "charge_ki_violet", Cost = 2, Level = 18, Prerequisite = "charge_ki_crimson" },
                    new Entry { SpellKey = "charge_ki_void", Cost = 3, Level = 25, Prerequisite = "charge_ki_violet",
                        Modifiers = Mods(StatModifier.Multiplicative(StatKind.SpellPower, 0.10f)) },
                },
            },
        };
    }
}
