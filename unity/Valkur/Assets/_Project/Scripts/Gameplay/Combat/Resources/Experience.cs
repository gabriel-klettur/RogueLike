using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Experience and leveling component.
    /// Maps to Python's ExperienceComponent (xp, level fields persisted in save metadata).
    ///
    /// Tracks XP, computes level from a configurable curve, and fires events on level-up.
    /// When <see cref="curve"/> is assigned the SO's formula / lookup table /
    /// level cap take precedence over the inline <see cref="baseXpPerLevel"/> +
    /// <see cref="exponent"/> defaults — so existing prefabs without a curve
    /// asset keep their pre-curve behaviour unchanged.
    /// </summary>
    public class Experience : MonoBehaviour
    {
        [Header("XP Curve")]
        [Tooltip("Optional ScriptableObject curve. When assigned, replaces the " +
                 "inline baseXp/exponent fields below.")]
        [SerializeField] private XpCurveDefinition curve;

        [Tooltip("XP required for level N = baseXP * N^exponent (used when curve is null)")]
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
        public event Action<int> OnXpLost;
        public event Action<int> OnLevelUp;
        public event Action<int> OnLevelLost;

        /// <summary>
        /// Fired whenever the XP or Level fields are written through any path
        /// (gain, loss, save-load <see cref="Initialize"/>). Lets the HUD,
        /// telemetry and FX layers re-read the current state on a single hook
        /// regardless of which mutation triggered it. Crucial after
        /// <see cref="Initialize"/> because that path does NOT fire
        /// <see cref="OnXpGained"/> or <see cref="OnLevelUp"/>, and yet UI
        /// bound before the Restore would otherwise stay at 0/0 visually.
        /// </summary>
        public event Action OnStateChanged;

        /// <summary>True iff the entity has reached the curve's level cap.</summary>
        public bool IsAtLevelCap => curve != null && curve.IsAtCap(_level);

        /// <summary>Test seam — assign a curve at runtime.</summary>
        public void SetCurve(XpCurveDefinition newCurve) => curve = newCurve;

        public void Initialize(int xp, int level)
        {
            _totalXp = xp;
            _level = level;
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Add XP and check for level-ups. Honours the curve's level cap when
        /// one is configured: at the cap, further XP is ignored and the
        /// <see cref="OnXpGained"/> event is suppressed (no UI / telemetry
        /// noise once the player has plateaued).
        /// </summary>
        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            if (IsAtLevelCap) return;

            _totalXp += amount;
            OnXpGained?.Invoke(amount);
            GameEvents.FireXpGained(gameObject, amount);

            while (_totalXp >= XpRequiredForLevel(_level + 1))
            {
                if (IsAtLevelCap) break; // never cross the cap mid-loop
                _level++;
                OnLevelUp?.Invoke(_level);
                GameEvents.FireLevelUp(gameObject, _level);
                Debug.Log($"[Experience] {gameObject.name} leveled up to {_level}!");
            }
        }

        /// <summary>
        /// Subtract XP — used by the death-penalty system. Positive amounts
        /// only; zero / negative are no-ops. When
        /// <paramref name="clampToCurrentLevel"/> is true the floor is the
        /// XP required to be at the current level (the player never
        /// de-levels from the penalty); otherwise the floor is 0 and the
        /// entity may de-level, in which case <see cref="OnLevelLost"/> +
        /// <see cref="GameEvents.OnLevelUp"/> are NOT fired (de-level is a
        /// distinct concept from level-up regression). Fires
        /// <see cref="OnXpLost"/> + <see cref="GameEvents.OnXpLost"/> with
        /// the actual amount removed so HUD / telemetry can react.
        /// </summary>
        public int RemoveXp(int amount, bool clampToCurrentLevel = true)
        {
            if (amount <= 0) return 0;

            int floor = clampToCurrentLevel ? XpRequiredForLevel(_level) : 0;
            int newTotal = Mathf.Max(floor, _totalXp - amount);
            int actualLoss = _totalXp - newTotal;
            if (actualLoss <= 0) return 0;

            _totalXp = newTotal;
            OnXpLost?.Invoke(actualLoss);
            GameEvents.FireXpLost(gameObject, actualLoss);

            if (!clampToCurrentLevel)
            {
                while (_level > 0 && _totalXp < XpRequiredForLevel(_level))
                {
                    _level--;
                    OnLevelLost?.Invoke(_level);
                }
            }
            return actualLoss;
        }

        /// <summary>
        /// Total XP required to reach a given level. Delegates to the curve
        /// SO when one is assigned; otherwise uses the inline formula.
        /// </summary>
        public int XpRequiredForLevel(int level)
        {
            if (level <= 0) return 0;
            if (curve != null) return curve.XpRequiredForLevel(level);
            return Mathf.RoundToInt(baseXpPerLevel * Mathf.Pow(level, exponent));
        }
    }
}
