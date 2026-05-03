using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Pins <see cref="LevelUpSkillPointSystem"/>: grants pointsPerLevel on
    /// each OnLevelUp, adds bonus points at configured levels, ignores
    /// entities without a LearnedSkills component, and disables cleanly
    /// when pointsPerLevel is 0 with no bonus levels configured.
    /// </summary>
    [TestFixture]
    public class LevelUpSkillPointSystemTests
    {
        private GameObject _systemGo;
        private LevelUpSkillPointSystem _system;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _systemGo = new GameObject("LevelUpSkillPointSystem");
            _system = _systemGo.AddComponent<LevelUpSkillPointSystem>();

            // Force OnEnable subscription manually since AddComponent
            // doesn't reliably fire OnEnable in EditMode.
            var onEnable = typeof(LevelUpSkillPointSystem).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);
            onEnable.Invoke(_system, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemGo != null) Object.DestroyImmediate(_systemGo);
            GameEvents.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static void SetField(object obj, string name, object value)
        {
            var f = obj.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(obj, value);
        }

        private static (GameObject go, LearnedSkills skills) MakePlayer(SkillTree tree)
        {
            var go = new GameObject("Player");
            var skills = go.AddComponent<LearnedSkills>();
            skills.SetTree(tree);
            return (go, skills);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void LevelUp_GrantsConfiguredPoints()
        {
            var tree = ScriptableObject.CreateInstance<SkillTree>();
            var (player, skills) = MakePlayer(tree);
            try
            {
                Assert.AreEqual(0, skills.AvailablePoints, "Sanity: starts at 0.");

                GameEvents.FireLevelUp(player, newLevel: 2);
                Assert.AreEqual(1, skills.AvailablePoints,
                    "Default 1 point per level → 1 SP after one level-up.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void BonusLevel_AddsBonusPoints()
        {
            SetField(_system, "pointsPerLevel", 1);
            SetField(_system, "bonusLevels",   new[] { 5, 10 });
            SetField(_system, "bonusPoints",   2);

            var tree = ScriptableObject.CreateInstance<SkillTree>();
            var (player, skills) = MakePlayer(tree);
            try
            {
                GameEvents.FireLevelUp(player, 4);
                Assert.AreEqual(1, skills.AvailablePoints, "Level 4 is not a bonus level.");

                GameEvents.FireLevelUp(player, 5);
                Assert.AreEqual(1 + 1 + 2, skills.AvailablePoints,
                    "Level 5 grants base (1) + bonus (2) = 3, on top of the previous 1.");

                GameEvents.FireLevelUp(player, 10);
                Assert.AreEqual(1 + 1 + 2 + 1 + 2, skills.AvailablePoints,
                    "Level 10 grants another base+bonus = 3, on top of the previous total.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void NoLearnedSkillsComponent_IsSilentSkip()
        {
            // NPCs don't carry a LearnedSkills component. Must not throw,
            // must not log error, must not affect anyone else.
            var npc = new GameObject("NPC");
            try
            {
                Assert.DoesNotThrow(() => GameEvents.FireLevelUp(npc, 2),
                    "OnLevelUp on an entity without LearnedSkills must be a silent no-op.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        [Test]
        public void Disabled_WhenPointsPerLevelZeroAndNoBonus_NoOp()
        {
            SetField(_system, "pointsPerLevel", 0);
            SetField(_system, "bonusLevels",    System.Array.Empty<int>());

            var tree = ScriptableObject.CreateInstance<SkillTree>();
            var (player, skills) = MakePlayer(tree);
            try
            {
                GameEvents.FireLevelUp(player, 2);
                Assert.AreEqual(0, skills.AvailablePoints,
                    "When the system is fully disabled (0 base + no bonuses), " +
                    "level-ups must not grant any points — the designer chose " +
                    "to drive SP rewards via quests instead.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void ComputeRewardForLevel_MatchesPolicy()
        {
            SetField(_system, "pointsPerLevel", 2);
            SetField(_system, "bonusLevels",    new[] { 7 });
            SetField(_system, "bonusPoints",    3);

            Assert.AreEqual(2, _system.ComputeRewardForLevel(6));
            Assert.AreEqual(2 + 3, _system.ComputeRewardForLevel(7),
                "Bonus level adds bonus on top of base.");
            Assert.AreEqual(2, _system.ComputeRewardForLevel(8));
        }
    }
}
