using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Progression
{
    /// <summary>
    /// The authored talent tables, one per playable class.
    ///
    /// A hand-written table rather than anything derived: a talent tree is a statement
    /// about what a class IS, and there is nothing in the data to derive that from. The
    /// same reasoning <c>tools/atlas/wave2/classify.py</c> records for prop categories —
    /// one row per node, checked by a person.
    ///
    /// Each class gets the same SHAPE — three roots, three second-tier nodes gated behind
    /// them, one capstone — so no class is accidentally deeper than another, and each gets
    /// its own numbers so the shape reads differently in play. The five identities:
    ///
    ///   dwarf     — the wall.        Hit points, defense, the capstone that turns being
    ///                                hit into a resource.
    ///   barbarian — the hammer.      Raw melee damage, crit damage, attack speed.
    ///   elven     — the blade.       Move speed, crit chance, experience.
    ///   mague     — the caster.      Mana, spell power, cooldown reduction.
    ///   valkyrie  — the hybrid.      A little of both halves, and less of each.
    ///
    /// Costs total 30 to 34 points against a level cap of 60, so a run cannot max its own
    /// tree — the whole point of a tree being a question rather than a checklist.
    /// </summary>
    internal static class SkillTreeSeeds
    {
        /// <summary>One authored talent row.</summary>
        private sealed class NodeSeed
        {
            public string Id;
            public string Name;
            public string Description;
            public int Cost = 1;
            public int MaxRank = 1;
            public int Level;
            public int LevelPerRank;
            public int Row;
            public int Column;
            public string[] Prerequisites = System.Array.Empty<string>();
            public StatModifier[] Modifiers = System.Array.Empty<StatModifier>();
            public string[] Auras = System.Array.Empty<string>();
        }

        private sealed class TreeSeed
        {
            public string ClassKey;
            public string DisplayName;
            public string Flavour;
            public NodeSeed[] Nodes;
        }

        public static List<SkillTree> BuildAll(string root)
        {
            var trees = new List<SkillTree>();
            foreach (var seed in All)
                trees.Add(Build(root, seed));
            return trees;
        }

        private static SkillTree Build(string root, TreeSeed seed)
        {
            string folder = $"{root}/{seed.ClassKey}";
            ProgressionSeeder.EnsureFolder(folder);

            // Two passes: every node has to exist before any prerequisite can point at one,
            // and a tree with a cycle in its authored table would otherwise deadlock here
            // rather than in the panel where it could be diagnosed.
            var byId = new Dictionary<string, SkillNode>();
            foreach (var nodeSeed in seed.Nodes)
            {
                string path = $"{folder}/{nodeSeed.Id}.asset";
                var node = ProgressionSeeder.LoadOrCreate<SkillNode>(path, out bool created);
                if (created) Fill(node, nodeSeed);
                byId[nodeSeed.Id] = node;
            }

            foreach (var nodeSeed in seed.Nodes)
            {
                var node = byId[nodeSeed.Id];
                if (node.prerequisites != null && node.prerequisites.Length > 0) continue;

                var prereqs = new List<SkillNode>();
                foreach (var id in nodeSeed.Prerequisites)
                    if (byId.TryGetValue(id, out var prereq)) prereqs.Add(prereq);

                node.prerequisites = prereqs.ToArray();
                EditorUtility.SetDirty(node);
            }

            var tree = ProgressionSeeder.LoadOrCreate<SkillTree>(
                $"{folder}/{seed.ClassKey}_skill_tree.asset", out bool treeCreated);
            if (treeCreated)
            {
                tree.classKey = seed.ClassKey;
                tree.displayName = seed.DisplayName;
                tree.flavour = seed.Flavour;
            }

            // The node list is refreshed even on an existing tree: it is the tree's INDEX,
            // and a node asset on disk that the tree does not list is unreachable content.
            var ordered = new SkillNode[seed.Nodes.Length];
            for (int i = 0; i < seed.Nodes.Length; i++) ordered[i] = byId[seed.Nodes[i].Id];
            tree.EditorSetNodes(ordered);

            EditorUtility.SetDirty(tree);
            return tree;
        }

        private static void Fill(SkillNode node, NodeSeed seed)
        {
            node.skillId = seed.Id;
            node.displayName = seed.Name;
            node.description = seed.Description;
            node.pointCost = seed.Cost;
            node.maxRank = seed.MaxRank;
            node.levelRequirement = seed.Level;
            node.levelPerRank = seed.LevelPerRank;
            node.modifiersPerRank = seed.Modifiers;
            node.passiveAuras = seed.Auras;
            node.row = seed.Row;
            node.column = seed.Column;
            EditorUtility.SetDirty(node);
        }

        private static StatModifier[] Mods(params StatModifier[] m) => m;

        // ── The tables ──────────────────────────────────────────────────────────

        private static readonly TreeSeed[] All =
        {
            new TreeSeed
            {
                ClassKey = "dwarf",
                DisplayName = "Path of the Mountain",
                Flavour = "Stone does not dodge. It simply refuses to move.",
                Nodes = new[]
                {
                    new NodeSeed { Id = "dwarf_stoneflesh", Name = "Stoneflesh", Row = 0, Column = 0,
                        Description = "Years underground thicken more than the skin.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Flat(StatKind.MaxHp, 12f)) },
                    new NodeSeed { Id = "dwarf_ironhide", Name = "Iron Hide", Row = 0, Column = 1,
                        Description = "Blows land. They just stop meaning as much.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Flat(StatKind.Defense, 1f)) },
                    new NodeSeed { Id = "dwarf_heavyhands", Name = "Heavy Hands", Row = 0, Column = 2,
                        Description = "A hammer swung by someone who mines for a living.",
                        MaxRank = 4, Modifiers = Mods(StatModifier.Flat(StatKind.MeleeDamage, 2f)) },

                    new NodeSeed { Id = "dwarf_bulwark", Name = "Bulwark", Row = 1, Column = 0, Level = 8,
                        Description = "Ten percent more of you to get through.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "dwarf_stoneflesh" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.MaxHp, 0.06f)) },
                    new NodeSeed { Id = "dwarf_anvilstance", Name = "Anvil Stance", Row = 1, Column = 1, Level = 10,
                        Description = "Planted. Slower, and much harder to shift.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "dwarf_ironhide" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.Defense, 2f),
                                         StatModifier.Percent(StatKind.MoveSpeed, -0.02f)) },
                    new NodeSeed { Id = "dwarf_forgeheat", Name = "Forge Heat", Row = 1, Column = 2, Level = 12,
                        Description = "The swing comes round faster than it has any right to.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "dwarf_heavyhands" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.MeleeCooldown, -0.07f)) },

                    new NodeSeed { Id = "dwarf_unbroken", Name = "Unbroken", Row = 2, Column = 1, Level = 20,
                        Description = "What the mountain gives back.",
                        Cost = 3, MaxRank = 1, Prerequisites = new[] { "dwarf_bulwark", "dwarf_anvilstance" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.MaxHp, 80f),
                                         StatModifier.Flat(StatKind.Defense, 5f),
                                         StatModifier.Multiplicative(StatKind.MeleeDamage, 0.15f)) },
                },
            },

            new TreeSeed
            {
                ClassKey = "barbarian",
                DisplayName = "Path of Fury",
                Flavour = "Every problem is a matter of how hard you hit it.",
                Nodes = new[]
                {
                    new NodeSeed { Id = "barb_brutality", Name = "Brutality", Row = 0, Column = 0,
                        Description = "The axe does not need to be sharp.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Flat(StatKind.MeleeDamage, 3f)) },
                    new NodeSeed { Id = "barb_bloodlust", Name = "Bloodlust", Row = 0, Column = 1,
                        Description = "Openings you did not notice you were looking for.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Flat(StatKind.CritChance, 0.03f)) },
                    new NodeSeed { Id = "barb_thickskin", Name = "Thick Skin", Row = 0, Column = 2,
                        Description = "Being reckless is cheaper with more blood in you.",
                        MaxRank = 4, Modifiers = Mods(StatModifier.Flat(StatKind.MaxHp, 10f)) },

                    new NodeSeed { Id = "barb_frenzy", Name = "Frenzy", Row = 1, Column = 0, Level = 8,
                        Description = "Faster, and not noticeably more careful.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "barb_brutality" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.MeleeCooldown, -0.09f)) },
                    new NodeSeed { Id = "barb_execution", Name = "Execution", Row = 1, Column = 1, Level = 10,
                        Description = "When it lands right, it lands very right.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "barb_bloodlust" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.CritMultiplier, 0.25f)) },
                    new NodeSeed { Id = "barb_warcry", Name = "War Cry", Row = 1, Column = 2, Level = 12,
                        Description = "Loud enough to make the next swing easier.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "barb_thickskin" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.MeleeDamage, 0.06f),
                                         StatModifier.Flat(StatKind.MaxHp, 20f)) },

                    new NodeSeed { Id = "barb_rampage", Name = "Rampage", Row = 2, Column = 1, Level = 20,
                        Description = "There is no defensive half of this talent.",
                        Cost = 3, MaxRank = 1, Prerequisites = new[] { "barb_frenzy", "barb_execution" },
                        Modifiers = Mods(StatModifier.Multiplicative(StatKind.MeleeDamage, 0.25f),
                                         StatModifier.Flat(StatKind.CritChance, 0.10f),
                                         StatModifier.Percent(StatKind.MeleeCooldown, -0.10f)) },
                },
            },

            new TreeSeed
            {
                ClassKey = "elven",
                DisplayName = "Path of the Wind",
                Flavour = "Arrive early. Leave earlier.",
                Nodes = new[]
                {
                    new NodeSeed { Id = "elf_fleetfoot", Name = "Fleet Foot", Row = 0, Column = 0,
                        Description = "The ground barely gets a say.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Percent(StatKind.MoveSpeed, 0.04f)) },
                    new NodeSeed { Id = "elf_keeneye", Name = "Keen Eye", Row = 0, Column = 1,
                        Description = "You see the gap before it opens.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Flat(StatKind.CritChance, 0.025f)) },
                    new NodeSeed { Id = "elf_woodlore", Name = "Woodlore", Row = 0, Column = 2,
                        Description = "Every fight teaches you a little more than it should.",
                        MaxRank = 4, Modifiers = Mods(StatModifier.Percent(StatKind.XpGain, 0.05f)) },

                    new NodeSeed { Id = "elf_quickhands", Name = "Quick Hands", Row = 1, Column = 0, Level = 8,
                        Description = "Two strikes in the time most people manage one.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "elf_fleetfoot" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.MeleeCooldown, -0.08f)) },
                    new NodeSeed { Id = "elf_precision", Name = "Precision", Row = 1, Column = 1, Level = 10,
                        Description = "Where, not how hard.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "elf_keeneye" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.CritMultiplier, 0.20f),
                                         StatModifier.Flat(StatKind.MeleeRange, 0.15f)) },
                    new NodeSeed { Id = "elf_attunement", Name = "Attunement", Row = 1, Column = 2, Level = 12,
                        Description = "The old songs are still worth a little power.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "elf_woodlore" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.SpellPower, 0.05f),
                                         StatModifier.Flat(StatKind.MaxMana, 12f)) },

                    new NodeSeed { Id = "elf_windwalker", Name = "Windwalker", Row = 2, Column = 1, Level = 20,
                        Description = "Nothing about you is where it was a moment ago.",
                        Cost = 3, MaxRank = 1, Prerequisites = new[] { "elf_quickhands", "elf_precision" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.MoveSpeed, 0.12f),
                                         StatModifier.Flat(StatKind.CritChance, 0.12f),
                                         StatModifier.Flat(StatKind.CritMultiplier, 0.35f)) },
                },
            },

            new TreeSeed
            {
                ClassKey = "mague",
                DisplayName = "Path of the Deep Library",
                Flavour = "Power is a reading problem.",
                Nodes = new[]
                {
                    new NodeSeed { Id = "mage_wellspring", Name = "Wellspring", Row = 0, Column = 0,
                        Description = "A deeper pool to draw from.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Flat(StatKind.MaxMana, 15f)) },
                    new NodeSeed { Id = "mage_focus", Name = "Focus", Row = 0, Column = 1,
                        Description = "The same words, said with more behind them.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Percent(StatKind.SpellPower, 0.05f)) },
                    new NodeSeed { Id = "mage_meditation", Name = "Meditation", Row = 0, Column = 2,
                        Description = "Recovery between fights, and during them.",
                        MaxRank = 4, Modifiers = Mods(StatModifier.Flat(StatKind.ManaRegen, 0.6f)) },

                    new NodeSeed { Id = "mage_economy", Name = "Economy of Motion", Row = 1, Column = 0, Level = 8,
                        Description = "Nothing wasted. Not a syllable.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "mage_wellspring" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.ManaCostReduction, 0.06f)) },
                    new NodeSeed { Id = "mage_haste", Name = "Arcane Haste", Row = 1, Column = 1, Level = 10,
                        Description = "The pause between castings, shortened.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "mage_focus" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.SpellCooldownReduction, 0.06f)) },
                    new NodeSeed { Id = "mage_warding", Name = "Warding", Row = 1, Column = 2, Level = 12,
                        Description = "A mage who is hit twice is usually not hit a third time.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "mage_meditation" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.Defense, 2f),
                                         StatModifier.Flat(StatKind.MaxHp, 15f)) },

                    new NodeSeed { Id = "mage_archmage", Name = "Archmage", Row = 2, Column = 1, Level = 20,
                        Description = "The library, finished.",
                        Cost = 3, MaxRank = 1, Prerequisites = new[] { "mage_economy", "mage_haste" },
                        Modifiers = Mods(StatModifier.Multiplicative(StatKind.SpellPower, 0.20f),
                                         StatModifier.Flat(StatKind.SpellCooldownReduction, 0.10f),
                                         StatModifier.Flat(StatKind.MaxMana, 60f)) },
                },
            },

            new TreeSeed
            {
                ClassKey = "valkyrie",
                DisplayName = "Path of the Chooser",
                Flavour = "Half a warrior and half a spell, and neither half apologises.",
                Nodes = new[]
                {
                    new NodeSeed { Id = "valk_swiftness", Name = "Swiftness", Row = 0, Column = 0,
                        Description = "First to the fight, every time.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Percent(StatKind.MoveSpeed, 0.03f)) },
                    new NodeSeed { Id = "valk_spearwork", Name = "Spearwork", Row = 0, Column = 1,
                        Description = "Reach, and what to do with it.",
                        MaxRank = 5, Modifiers = Mods(StatModifier.Flat(StatKind.MeleeDamage, 2f),
                                                      StatModifier.Flat(StatKind.MeleeRange, 0.08f)) },
                    new NodeSeed { Id = "valk_devotion", Name = "Devotion", Row = 0, Column = 2,
                        Description = "The other half of the gift.",
                        MaxRank = 4, Modifiers = Mods(StatModifier.Percent(StatKind.SpellPower, 0.04f),
                                                      StatModifier.Flat(StatKind.MaxMana, 10f)) },

                    new NodeSeed { Id = "valk_shieldmaiden", Name = "Shieldmaiden", Row = 1, Column = 0, Level = 8,
                        Description = "Survive the exchange you started.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "valk_swiftness" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.Defense, 1.5f),
                                         StatModifier.Flat(StatKind.MaxHp, 18f)) },
                    new NodeSeed { Id = "valk_smite", Name = "Smite", Row = 1, Column = 1, Level = 10,
                        Description = "Weapon and word arriving together.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "valk_spearwork" },
                        Modifiers = Mods(StatModifier.Percent(StatKind.MeleeDamage, 0.05f),
                                         StatModifier.Percent(StatKind.SpellPower, 0.05f)) },
                    new NodeSeed { Id = "valk_ascendance", Name = "Ascendance", Row = 1, Column = 2, Level = 12,
                        Description = "Less waiting between miracles.",
                        Cost = 2, MaxRank = 3, Prerequisites = new[] { "valk_devotion" },
                        Modifiers = Mods(StatModifier.Flat(StatKind.SpellCooldownReduction, 0.05f),
                                         StatModifier.Flat(StatKind.ManaRegen, 0.4f)) },

                    new NodeSeed { Id = "valk_chooser", Name = "Chooser of the Slain", Row = 2, Column = 1, Level = 20,
                        Description = "Both halves, at once, without the cost of either.",
                        Cost = 3, MaxRank = 1, Prerequisites = new[] { "valk_shieldmaiden", "valk_smite" },
                        Modifiers = Mods(StatModifier.Multiplicative(StatKind.MeleeDamage, 0.15f),
                                         StatModifier.Multiplicative(StatKind.SpellPower, 0.15f),
                                         StatModifier.Percent(StatKind.MoveSpeed, 0.08f)) },
                },
            },
        };
    }
}
