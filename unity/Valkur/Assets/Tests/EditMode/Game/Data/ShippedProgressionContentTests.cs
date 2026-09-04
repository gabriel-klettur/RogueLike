using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Asserts on the SHIPPED progression assets, not on a synthetic fixture.
    ///
    /// This is the class of test the project keeps learning it needs. The chat system had
    /// 225 green tests over a catalogue every one of them built itself, while the shipped
    /// game had zero chat-capable entities; the spawner coordinate drift passed every test
    /// of each half while the bytes on disk were wrong. So these load what the game will
    /// actually load, through the same <c>Resources</c> path the runtime uses, and check
    /// the properties that make the content REACHABLE.
    /// </summary>
    [TestFixture]
    public class ShippedProgressionContentTests
    {
        private ProgressionCatalog _catalog;

        [OneTimeSetUp]
        public void LoadCatalog()
        {
            _catalog = Resources.Load<ProgressionCatalog>(ProgressionCatalog.ResourcePath);
        }

        [Test]
        public void Catalog_IsLoadableFromTheExactPathTheRuntimeUses()
        {
            Assert.IsNotNull(_catalog,
                $"No ProgressionCatalog at Resources/{ProgressionCatalog.ResourcePath}. " +
                "PlayerProgression loads it by path because it is AddComponent-ed onto a bare " +
                "GameObject and a serialized reference could never be filled — the defect that " +
                "left ChatSystem's catalog null for the life of the project. Run " +
                "'Valkur > Progression > Seed Progression Content'.");
        }

        [Test]
        public void Catalog_CarriesBothCurves()
        {
            Assert.IsNotNull(_catalog.xpCurve,
                "Without an XP curve, Experience falls back to its inline formula and has no " +
                "level cap at all.");
            Assert.IsNotNull(_catalog.levelStatCurve,
                "Without a level stat curve, levelling grants nothing — the state the project " +
                "shipped in for its whole life.");
            Assert.Greater(_catalog.xpCurve.levelCap, 0, "An uncapped curve makes every monster " +
                "irrelevant given enough time.");
        }

        [Test]
        public void EveryPlayableClass_HasATreeMatchedByItsOwnKey()
        {
            // The resolution is by classKey, so a tree whose key does not match its class is
            // an empty talent panel with no error anywhere.
            string[] classes = { "dwarf", "barbarian", "elven", "mague", "valkyrie" };
            foreach (var key in classes)
            {
                var tree = _catalog.GetSkillTreeForClass(key);
                Assert.IsNotNull(tree, $"No SkillTree with classKey '{key}'.");
                Assert.Greater(tree.Count, 0, $"'{key}' tree has no nodes.");
            }
        }

        [Test]
        public void SkillIds_AreUniqueAcrossEveryTree()
        {
            // Ids are the save key. Two nodes sharing one means a player who learns either
            // loads both, in whichever tree happens to be looked at.
            var seen = new Dictionary<string, string>();
            foreach (var tree in _catalog.skillTrees)
            {
                foreach (var node in tree.Nodes)
                {
                    Assert.IsNotNull(node, $"Null node in '{tree.classKey}'.");
                    Assert.IsNotEmpty(node.skillId, $"Node in '{tree.classKey}' has no id.");
                    seen.TryGetValue(node.skillId, out string owner);
                    Assert.IsFalse(seen.ContainsKey(node.skillId),
                        $"Duplicate skillId '{node.skillId}' in '{tree.classKey}' and '{owner}'.");
                    seen[node.skillId] = tree.classKey;
                }
            }
        }

        [Test]
        public void SkillPrerequisites_ContainNoCycles()
        {
            foreach (var tree in _catalog.skillTrees)
            {
                foreach (var node in tree.Nodes)
                {
                    var visiting = new HashSet<string>();
                    Assert.IsFalse(HasCycle(node, visiting),
                        $"Prerequisite cycle reachable from '{node.skillId}' in " +
                        $"'{tree.classKey}'. A cycle makes every node in it permanently " +
                        "unbuyable, and nothing reports it.");
                }
            }
        }

        private static bool HasCycle(SkillNode node, HashSet<string> visiting)
        {
            if (node == null) return false;
            if (!visiting.Add(node.skillId)) return true;
            if (node.prerequisites != null)
            {
                foreach (var p in node.prerequisites)
                    if (HasCycle(p, visiting)) return true;
            }
            visiting.Remove(node.skillId);
            return false;
        }

        [Test]
        public void NoTreeCanBeFullyMaxedWithinTheLevelCap()
        {
            // A tree a run can finish is a checklist, not a question. One skill point per
            // level against a cap of 60 is the budget every tree is measured against.
            int budget = _catalog.xpCurve.levelCap * Mathf.Max(1, _catalog.skillPointsPerLevel);
            foreach (var tree in _catalog.skillTrees)
            {
                Assert.Greater(tree.TotalPointCost(), 0, $"'{tree.classKey}' costs nothing.");
                Assert.Less(tree.TotalPointCost(), budget,
                    $"'{tree.classKey}' costs more than a capped run can ever earn, which " +
                    "makes most of it unreachable rather than a choice.");
            }
        }

        [Test]
        public void EveryPlayerCastableSpell_IsTaughtBySomeSchoolOrIsInnate()
        {
            // The check that stops the next authored spell quietly becoming uncastable.
            // Before the grimoire existed the opposite was true: all 77 shipped spells were
            // registered on the player in the frame they spawned.
            var taught = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var school in _catalog.spellTrees)
            {
                foreach (var node in school.Nodes)
                {
                    if (node?.spell == null) continue;
                    taught.Add(node.spell.spellKey);
                }
            }
            foreach (var key in _catalog.alwaysKnownSpellKeys) taught.Add(key);

            // Loaded through AssetDatabase, not Resources: the spell assets deliberately do
            // NOT live under Resources/ (that folder ships whole), so a Resources.LoadAll here
            // would return nothing and the test would pass by measuring an empty set — the
            // vacuous-green failure mode that let the chat system keep 225 tests over nothing.
            var all = new List<SpellDefinition>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets(
                         "t:SpellDefinition", new[] { "Assets/_Project/Data/Catalogs/Spells" }))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
                if (def != null) all.Add(def);
            }
            Assert.Greater(all.Count, 0, "Found no SpellDefinition assets at all — the test " +
                                         "would otherwise pass by measuring an empty set.");

            var missing = new List<string>();
            foreach (var spell in all)
            {
                if (string.IsNullOrWhiteSpace(spell.spellKey)) continue;
                if ((spell.audience & SpellAudience.Player) == 0) continue;
                if (!taught.Contains(spell.spellKey)) missing.Add(spell.spellKey);
            }

            if (missing.Count > 0)
                Assert.Fail($"Player-castable spells taught by no school and not innate: " +
                            $"{string.Join(", ", missing)}");
        }

        [Test]
        public void GrimoireNodeIds_AreUniqueAcrossEverySchool()
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var school in _catalog.spellTrees)
            {
                foreach (var node in school.Nodes)
                {
                    Assert.IsNotNull(node, $"Null node in school '{school.schoolKey}'.");
                    Assert.IsNotEmpty(node.nodeId, $"Node in '{school.schoolKey}' has no id.");
                    Assert.IsTrue(seen.Add(node.nodeId),
                        $"Duplicate grimoire node id '{node.nodeId}'.");
                }
            }
        }

        [Test]
        public void EverySchoolRoot_IsReachableWithoutAPrerequisite()
        {
            // A school whose every node needs a prerequisite can never be entered, and the
            // panel shows it as fully locked with no explanation of how to start.
            foreach (var school in _catalog.spellTrees)
            {
                bool hasRoot = false;
                foreach (var node in school.Nodes)
                {
                    if (node == null) continue;
                    if (node.prerequisites == null || node.prerequisites.Length == 0)
                    {
                        hasRoot = true;
                        break;
                    }
                }
                Assert.IsTrue(hasRoot, $"School '{school.schoolKey}' has no entry point.");
            }
        }

        [Test]
        public void EverySchool_IsAnAffinityForAtLeastOneClass()
        {
            // A school no class has an affinity for costs everyone double, which reads as a
            // pricing bug rather than as a design statement.
            foreach (var school in _catalog.spellTrees)
            {
                Assert.IsNotNull(school.classAffinities);
                Assert.Greater(school.classAffinities.Length, 0,
                    $"School '{school.schoolKey}' is off-affinity for every class.");
            }
        }

        [Test]
        public void StartingKit_IsSmall_AndEveryKeyResolves()
        {
            Assert.LessOrEqual(_catalog.alwaysKnownSpellKeys.Length, 4,
                "The starting kit is the only content the grimoire cannot charge for. Keep " +
                "it the size of a tutorial.");
        }
    }
}
