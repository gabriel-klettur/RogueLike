using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Grants skill points to the levelled entity's <see cref="LearnedSkills"/>
    /// component on every <see cref="GameEvents.OnLevelUp"/> event. Closes
    /// the progression loop: XP → level → skill points → tree progression.
    ///
    /// Sibling to <see cref="LevelUpRestoreSystem"/>. Both subscribe to the
    /// same event independently — heal and skill-points are orthogonal
    /// concerns that designers often want to tune separately (some games
    /// give 1 SP per level, others 3 every 5 levels, others 0 SP and
    /// rely on quest rewards).
    ///
    /// Entities without a <see cref="LearnedSkills"/> component (i.e.
    /// every NPC) silently skip — only the player carries the component
    /// in current builds.
    /// </summary>
    public class LevelUpSkillPointSystem : MonoBehaviour
    {
        [Header("Reward policy")]
        [Tooltip("Skill points granted per level-up. 1 = the genre standard. " +
                 "0 disables this system entirely (designer can do skill-point " +
                 "rewards via quests instead).")]
        [Min(0)] [SerializeField] private int pointsPerLevel = 1;

        [Tooltip("Levels at which to grant a BONUS point on top of the base " +
                 "pointsPerLevel reward (e.g. {5, 10, 15} = bonus skill point " +
                 "every 5 levels). Empty = no bonuses, just the flat rate.")]
        [SerializeField] private int[] bonusLevels = System.Array.Empty<int>();

        [Tooltip("Bonus point amount granted on each bonusLevel hit. Stacked on " +
                 "top of pointsPerLevel — so at level 5 with pointsPerLevel=1 and " +
                 "bonusPoints=2, the player gets 3 points.")]
        [Min(0)] [SerializeField] private int bonusPoints = 1;

        private void OnEnable()
        {
            GameEvents.OnLevelUp += OnLevelUp;
        }

        private void OnDisable()
        {
            GameEvents.OnLevelUp -= OnLevelUp;
        }

        private void OnLevelUp(GameObject entity, int newLevel)
        {
            if (entity == null) return;
            if (pointsPerLevel <= 0 && (bonusLevels == null || bonusLevels.Length == 0)) return;

            var skills = entity.GetComponent<LearnedSkills>();
            if (skills == null) return; // NPCs without skill trees — silent skip.

            int reward = pointsPerLevel;
            if (IsBonusLevel(newLevel))
                reward += bonusPoints;

            if (reward > 0)
                skills.AddPoints(reward);
        }

        // Public + internal seam for tests so they can verify the reward
        // calculation without driving a real OnLevelUp.
        public int ComputeRewardForLevel(int level)
        {
            return pointsPerLevel + (IsBonusLevel(level) ? bonusPoints : 0);
        }

        private bool IsBonusLevel(int level)
        {
            if (bonusLevels == null) return false;
            for (int i = 0; i < bonusLevels.Length; i++)
                if (bonusLevels[i] == level) return true;
            return false;
        }
    }
}
