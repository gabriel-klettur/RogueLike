using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Applies an XP penalty when the player revives after dying.
    /// Hooked to <see cref="GameEvents.OnPlayerRevived"/> (post-spirit revive
    /// flow) rather than <see cref="GameEvents.OnPlayerDied"/> so the player
    /// has the spirit-walk grace period before the penalty hits — matches
    /// the design comment in <c>GameEvents.OnPlayerRevived</c> ("XP loss
    /// hook, audio, telemetry should use this one").
    ///
    /// Penalty is a fraction of XP earned within the current level so the
    /// loss scales with progress, not with total lifetime XP. By default
    /// the loss is clamped so the player never de-levels — losing a level
    /// after death is a punishing roguelike-classic behaviour that we keep
    /// optional behind <see cref="canDelevel"/>.
    /// </summary>
    public class XpLossOnDeathSystem : MonoBehaviour
    {
        [Header("Penalty")]
        [Tooltip("Fraction of in-current-level XP removed on revive. " +
                 "0 = no penalty, 1 = lose all progress in this level.")]
        [Range(0f, 1f)] [SerializeField] private float lossFraction = 0.10f;

        [Tooltip("If true, the penalty may de-level the player. If false " +
                 "(default), the loss is clamped so the player keeps their " +
                 "current level.")]
        [SerializeField] private bool canDelevel;

        public float LossFraction { set { lossFraction = Mathf.Clamp01(value); } get => lossFraction; }
        public bool CanDelevel { set { canDelevel = value; } get => canDelevel; }
        public int LastApplied { get; private set; }

        private void OnEnable()  => GameEvents.OnPlayerRevived += OnPlayerRevived;
        private void OnDisable() => GameEvents.OnPlayerRevived -= OnPlayerRevived;

        private void OnPlayerRevived()
        {
            ApplyPenalty(EntityRegistry.Player);
        }

        /// <summary>
        /// Applies the penalty to a specific entity. Public + parameterised
        /// to give tests a deterministic seam (no need to drive
        /// <see cref="EntityRegistry"/> from EditMode).
        /// </summary>
        public int ApplyPenalty(GameObject entity)
        {
            LastApplied = 0;
            if (entity == null || lossFraction <= 0f) return 0;

            var xp = entity.GetComponent<Experience>();
            if (xp == null) return 0;

            int loss = Mathf.RoundToInt(xp.XpInCurrentLevel * lossFraction);
            if (loss <= 0) return 0;

            int actual = xp.RemoveXp(loss, clampToCurrentLevel: !canDelevel);
            LastApplied = actual;
            return actual;
        }
    }
}
