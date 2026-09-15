using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Quests
{
    /// <summary>
    /// Reads the ten SHIPPED quests off disk and checks that every id they point at
    /// resolves against the catalogues that actually exist.
    ///
    /// <para><b>Why this and not a unit test.</b> Every field on a quest is a plain
    /// string: a monsterKey, an itemId, a recipeId, a spellKey, a zone name, a
    /// personaId. Nothing validates them at author time, and every single one fails the
    /// same way — SILENTLY. A quest naming a monster that does not exist is a kill
    /// counter that never moves; a delivery naming an item that does not exist takes
    /// nothing from the bag and pays out anyway; a giver naming a persona nobody has is
    /// a quest no character in the world will ever mention. All of that looks exactly
    /// like a quest the player has not worked on yet.</para>
    ///
    /// <para>This is the same shape as <c>ShippedSpawnerRosterTests</c> and
    /// <c>ShippedDeathDataTests</c>, and for the same reason recorded in
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>: assert the COMPOSITION and assert the
    /// shipped bytes, because each half can be internally consistent while the join
    /// between them is wrong.</para>
    /// </summary>
    [TestFixture]
    public class ShippedQuestDataTests
    {
        private const string CatalogPath = "Assets/_Project/Resources/Quests/QuestCatalog.asset";
        private const int ExpectedQuestCount = 11;

        private QuestCatalog _catalog;

        private static HashSet<string> _monsterKeys;
        private static HashSet<string> _itemIds;
        private static HashSet<string> _recipeIds;
        private static HashSet<string> _spellKeys;
        private static HashSet<string> _personaIds;
        private static HashSet<string> _zoneNames;

        [OneTimeSetUp]
        public void LoadCatalogues()
        {
            // Built ONCE per fixture, not per test: each of these is an AssetDatabase
            // sweep, and paying six of them per test is what took
            // EntitiesCatalogAuthoringTests from 33 ms to 250.
            _monsterKeys = Ids<MonsterDefinition>(d => d.monsterKey);
            _itemIds     = Ids<ItemDefinition>(d => d.itemId);
            _recipeIds   = Ids<RecipeDefinition>(d => d.recipeId);
            _spellKeys   = Ids<SpellDefinition>(d => d.spellKey);
            _personaIds  = Ids<NPCPersonaDefinition>(d => d.personaId);
            _zoneNames   = LoadZoneNames();
        }

        [SetUp]
        public void LoadQuests()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<QuestCatalog>(CatalogPath);
        }

        private static HashSet<string> Ids<T>(Func<T, string> selector) where T : ScriptableObject
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                string id = selector(asset);
                if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
            return set;
        }

        /// <summary>
        /// Zone names as the world declares them. Read out of the shipped
        /// <c>zones_database.json</c> rather than off the map filenames, because
        /// <c>ZoneManager</c> reports the DECLARED name ("Lobby") and the file is called
        /// something else ("lobby.overlay.json") — comparing against the filenames would
        /// pass a quest that names a zone the game never reports.
        /// </summary>
        private static HashSet<string> LoadZoneNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(Application.streamingAssetsPath, "Maps", "zones_database.json");
            if (!File.Exists(path)) return set;

            foreach (var raw in System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(path), "\"name\"\\s*:\\s*\"([^\"]+)\"")
                     .Cast<System.Text.RegularExpressions.Match>())
                set.Add(raw.Groups[1].Value);

            return set;
        }

        // ── The catalogue itself ───────────────────────────────────────────

        [Test]
        public void TheCatalogue_Exists_AndIsLoadableFromResources()
        {
            Assert.IsNotNull(_catalog,
                $"No QuestCatalog at {CatalogPath}. Run Valkur > Quests > Seed Quest Content.");

            // The path matters as much as the asset: QuestService is AddComponent-ed with
            // no inspector slot, so Resources.Load is the ONLY way it can find this.
            Assert.IsTrue(CatalogPath.Contains("/Resources/"),
                "the catalogue must live under Resources or the runtime cannot load it");
            Assert.AreEqual("Quests/QuestCatalog", QuestCatalog.ResourcePath,
                "the declared resource path drifted from where the asset actually is");
        }

        [Test]
        public void TenQuestsShip_AndNoneIsNull()
        {
            Assert.AreEqual(ExpectedQuestCount, _catalog.quests.Count);
            CollectionAssert.DoesNotContain(_catalog.quests, null,
                "a null entry is a quest asset that was deleted without rebuilding the catalogue");
        }

        [Test]
        public void QuestIds_AreUniqueAndNonEmpty()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var q in _catalog.quests)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.questId), $"{q.name} has no questId");
                Assert.IsTrue(seen.Add(q.questId),
                    $"duplicate questId '{q.questId}' — the save layer keys on this");
            }
        }

        [Test]
        public void EveryQuest_HasAtLeastOneObjective()
        {
            // A quest with zero objectives auto-completes inside Quest.Begin: accepting it
            // would pay it out on the same frame.
            foreach (var q in _catalog.quests)
                Assert.Greater(q.objectives.Length, 0, $"'{q.questId}' has no objectives");
        }

        // ── Every id resolves ──────────────────────────────────────────────

        [Test]
        public void EveryGiverAndTurnIn_IsARealPersona()
        {
            foreach (var q in _catalog.quests)
            {
                if (!string.IsNullOrEmpty(q.giverPersonaId))
                    Assert.Contains(q.giverPersonaId, _personaIds.ToList(),
                        $"'{q.questId}' is offered by '{q.giverPersonaId}', which no NPCPersonaDefinition declares — " +
                        "no character in the world would ever mention it");

                if (!string.IsNullOrEmpty(q.turnInPersonaId))
                    Assert.Contains(q.turnInPersonaId, _personaIds.ToList(),
                        $"'{q.questId}' is handed in to '{q.turnInPersonaId}', which no persona declares — " +
                        "the generated turn-in objective could never tick and the quest could never close");
            }
        }

        [Test]
        public void EveryObjectiveTarget_ResolvesAgainstItsOwnCatalogue()
        {
            var problems = new List<string>();

            foreach (var q in _catalog.quests)
            {
                for (int i = 0; i < q.objectives.Length; i++)
                {
                    var o = q.objectives[i];
                    string where = $"{q.questId}.obj{i} ({o.kind})";

                    switch (o.kind)
                    {
                        case ObjectiveKind.KillCount:
                            // Empty is legal and means "anything" — the vigil uses it.
                            if (!string.IsNullOrEmpty(o.targetId) && !_monsterKeys.Contains(o.targetId))
                                problems.Add($"{where}: no monster with key '{o.targetId}'");
                            break;

                        case ObjectiveKind.Collect:
                            if (!_itemIds.Contains(o.targetId))
                                problems.Add($"{where}: no item with id '{o.targetId}'");
                            break;

                        case ObjectiveKind.Craft:
                            if (!string.IsNullOrEmpty(o.targetId) && !_recipeIds.Contains(o.targetId))
                                problems.Add($"{where}: no recipe with id '{o.targetId}'");
                            break;

                        case ObjectiveKind.CastSpell:
                            if (!string.IsNullOrEmpty(o.targetId) && !_spellKeys.Contains(o.targetId))
                                problems.Add($"{where}: no spell with key '{o.targetId}'");
                            break;

                        case ObjectiveKind.Talk:
                            if (!_personaIds.Contains(o.targetId))
                                problems.Add($"{where}: no persona with id '{o.targetId}'");
                            break;

                        case ObjectiveKind.ReachSkill:
                            if (Valkur.Data.SkillCatalog.Shared == null ||
                                Valkur.Data.SkillCatalog.Shared.Find(o.targetId) == null)
                                problems.Add($"{where}: no gathering skill with key '{o.targetId}'");
                            if (o.count < 1 || o.count > 100)
                                problems.Add($"{where}: a skill percent of {o.count} can never be reached");
                            break;

                        case ObjectiveKind.Reach:
                            // Only checked when the world actually shipped a zone database;
                            // a missing file is a broken fixture, not a broken quest.
                            if (_zoneNames.Count > 0 && !_zoneNames.Contains(o.targetId))
                                problems.Add($"{where}: no zone named '{o.targetId}'");
                            break;
                    }
                }
            }

            CollectionAssert.IsEmpty(problems,
                "quest objectives pointing at ids that do not exist — every one of these " +
                "is a counter that never moves:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void EveryItemReward_Exists_AndItsCountLinesUp()
        {
            foreach (var q in _catalog.quests)
            {
                for (int i = 0; i < q.itemRewards.Length; i++)
                {
                    string id = q.itemRewards[i];
                    Assert.IsTrue(_itemIds.Contains(id),
                        $"'{q.questId}' rewards '{id}', which no ItemDefinition declares — " +
                        "the player would be paid a warning");
                }

                Assert.LessOrEqual(q.itemRewardCounts.Length, q.itemRewards.Length,
                    $"'{q.questId}' declares more reward COUNTS than rewards — the extras are silently ignored");
            }
        }

        // ── The chain holds together ───────────────────────────────────────

        [Test]
        public void EveryPrerequisite_NamesAQuestThatShips()
        {
            var ids = new HashSet<string>(_catalog.quests.Select(q => q.questId), StringComparer.OrdinalIgnoreCase);
            foreach (var q in _catalog.quests)
                foreach (string pre in q.prerequisiteQuestIds)
                    Assert.IsTrue(ids.Contains(pre),
                        $"'{q.questId}' requires '{pre}', which is not in the catalogue — " +
                        "the quest could never become eligible");
        }

        [Test]
        public void NoQuest_RequiresItself_DirectlyOrThroughOneHop()
        {
            // A cycle is a quest nothing can ever unlock, and it looks exactly like a
            // quest whose giver has nothing to say.
            var byId = _catalog.quests.ToDictionary(q => q.questId, StringComparer.OrdinalIgnoreCase);
            foreach (var q in _catalog.quests)
            {
                CollectionAssert.DoesNotContain(q.prerequisiteQuestIds, q.questId,
                    $"'{q.questId}' requires itself");

                foreach (string pre in q.prerequisiteQuestIds)
                    if (byId.TryGetValue(pre, out var parent))
                        CollectionAssert.DoesNotContain(parent.prerequisiteQuestIds, q.questId,
                            $"'{q.questId}' and '{pre}' require each other");
            }
        }

        [Test]
        public void AQuestIsNeverGatedBelowItsOwnPrerequisite()
        {
            // A level gate lower than the quest that unlocks it is not wrong, but it is
            // always a mistake in practice: it says a step of the chain is EASIER than
            // the step before it, which no player will ever experience because the
            // prerequisite is what actually holds them back.
            var byId = _catalog.quests.ToDictionary(q => q.questId, StringComparer.OrdinalIgnoreCase);
            foreach (var q in _catalog.quests)
                foreach (string pre in q.prerequisiteQuestIds)
                    if (byId.TryGetValue(pre, out var parent))
                        Assert.GreaterOrEqual(q.requiredLevel, parent.requiredLevel,
                            $"'{q.questId}' (level {q.requiredLevel}) unlocks after " +
                            $"'{pre}' (level {parent.requiredLevel}) but is gated lower");
            }

        // ── The content is a spread, not ten of the same quest ─────────────

        [Test]
        public void TheShippedTen_ExerciseEveryObjectiveKind()
        {
            // The point of ten quests rather than one is coverage of the LAYER as well
            // as content: a kind no shipped quest uses is a kind nothing has ever run.
            var used = new HashSet<ObjectiveKind>(
                _catalog.quests.SelectMany(q => q.objectives).Select(o => o.kind));

            var missing = Enum.GetValues(typeof(ObjectiveKind))
                              .Cast<ObjectiveKind>()
                              .Where(k => !used.Contains(k))
                              .ToList();

            CollectionAssert.IsEmpty(missing,
                "objective kinds no shipped quest exercises: " + string.Join(", ", missing));
        }

        [Test]
        public void TheShippedTen_SpreadAcrossLengths()
        {
            // Length is objective COUNT here, which is the only proxy the data carries.
            // The spread is the design: a starter with one step and a capstone with five.
            var counts = _catalog.quests.Select(q => q.objectives.Length).ToList();
            Assert.AreEqual(1, counts.Min(), "no single-objective quest — nothing to open the game with");
            Assert.GreaterOrEqual(counts.Max(), 5, "no long quest — nothing to close the chain with");
            Assert.GreaterOrEqual(counts.Distinct().Count(), 4, "the ten are too uniform in length");
        }

        [Test]
        public void RewardsGrowWithTheLevelGate()
        {
            // Not a curve, just a floor: the level-10 capstone must not pay less than the
            // level-0 starter. It is the cheapest possible guard against a retune that
            // moves one number and forgets the rest.
            var starter = _catalog.quests.OrderBy(q => q.requiredLevel).First();
            var capstone = _catalog.quests.OrderByDescending(q => q.requiredLevel).First();

            Assert.Greater(capstone.xpReward, starter.xpReward);
            Assert.Greater(capstone.coinReward, starter.coinReward);
        }

        [Test]
        public void EveryQuest_CarriesItsProse()
        {
            // A quest with no hook line is one whose giver mentions nothing when the
            // player walks up — the only thing that makes it discoverable in game.
            foreach (var q in _catalog.quests)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.displayName), $"'{q.questId}' has no name");
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.description), $"'{q.questId}' has no description");
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.hookLine),
                    $"'{q.questId}' has no hook line — nothing would announce it in a conversation");
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.completionLine),
                    $"'{q.questId}' has no completion line");
            }
        }

        [Test]
        public void EveryDeliveredItem_IsMarkedToBeConsumed()
        {
            // A Collect objective on a quest that also pays for it, without
            // consumeOnComplete, is a fetch-and-keep: the player hands over nothing and
            // walks away with the goods AND the fee. Every shipped Collect is a delivery.
            foreach (var q in _catalog.quests)
                foreach (var o in q.objectives)
                    if (o.kind == ObjectiveKind.Collect)
                        Assert.IsTrue(o.consumeOnComplete,
                            $"'{q.questId}' collects '{o.targetId}' without taking it — " +
                            "either mark it consumed or say in the description that it is kept");
        }
    }
}
