using System;
using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Experience and leveling component.
    /// Maps to Python's ExperienceComponent (xp, level fields persisted in save metadata).
    /// 
    /// Tracks XP, computes level from a configurable curve, and fires events on level-up.
    /// </summary>
    public class Experience : MonoBehaviour
    {
        [Header("XP Curve")]
        [Tooltip("XP required for level N = baseXP * N^exponent")]
        [SerializeField] private int baseXpPerLevel = 100;
        [SerializeField] private float exponent = 1.5f;

        private int _totalXp;
        private int _level;

        public int TotalXp => _totalXp;
        public int Level => _level;

        /// <summary>XP needed to reach the next level.</summary>
        public int XpForNextLevel => XpRequiredForLevel(_level + 1);

        /// <summary>XP already earned toward the current level.</summary>
        public int XpInCurrentLevel => _totalXp - XpRequiredForLevel(_level);

        /// <summary>Normalized progress toward next level (0..1).</summary>
        public float NormalizedProgress
        {
            get
            {
                int current = XpRequiredForLevel(_level);
                int next = XpRequiredForLevel(_level + 1);
                int range = next - current;
                return range > 0 ? Mathf.Clamp01((float)(_totalXp - current) / range) : 0f;
            }
        }

        public event Action<int> OnXpGained;
        public event Action<int> OnLevelUp;

        public void Initialize(int xp, int level)
        {
            _totalXp = xp;
            _level = level;
        }

        /// <summary>
        /// Add XP and check for level-ups.
        /// </summary>
        public void AddXp(int amount)
        {
            if (amount <= 0) return;

            _totalXp += amount;
            OnXpGained?.Invoke(amount);

            while (_totalXp >= XpRequiredForLevel(_level + 1))
            {
                _level++;
                OnLevelUp?.Invoke(_level);
                Debug.Log($"[Experience] {gameObject.name} leveled up to {_level}!");
            }
        }

        /// <summary>
        /// Total XP required to reach a given level.
        /// </summary>
        public int XpRequiredForLevel(int level)
        {
            if (level <= 0) return 0;
            return Mathf.RoundToInt(baseXpPerLevel * Mathf.Pow(level, exponent));
        }
    }
}
