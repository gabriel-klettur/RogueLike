using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Closes the XP feedback loop with player-visible juice:
    ///   • OnXpGained → floating "+N XP" above the player (uses the player's
    ///     <see cref="FloatingDamageSpawner"/> via the new <c>ShowXp</c> entry).
    ///   • OnLevelUp  → "LEVEL UP! Lvl N" toast (via <see cref="ToastSystem"/>).
    ///
    /// Audio is already handled by <see cref="CombatAudioSystem"/> through the
    /// same <see cref="GameEvents.OnLevelUp"/> event, so this component does
    /// not duplicate the SFX wiring.
    ///
    /// Filters by player tag — XP gained by NPCs (rare, but possible if
    /// monsters ever get an Experience component) does not spam the toast.
    /// Test seams expose the last formatted toast and the last XP shown so
    /// EditMode tests can verify wiring without a Canvas.
    /// </summary>
    public class XpFeedbackSystem : MonoBehaviour
    {
        [Header("Toast")]
        [Tooltip("Format string for level-up toast. {0} = new level.")]
        [SerializeField] private string levelUpToastFormat = "LEVEL UP!  Lvl {0}";
        [Tooltip("Toast display duration in seconds.")]
        [SerializeField] private float levelUpToastDuration = 3f;

        [Header("Floating XP")]
        [Tooltip("If true, floating '+N XP' only appears for the player. " +
                 "If false, any entity with an Experience + FloatingDamageSpawner gets one.")]
        [SerializeField] private bool playerOnly = true;

        // Test seams — these only update when an event is processed.
        public string LastToastMessage { get; private set; }
        public int LastToastedLevel    { get; private set; } = -1;
        public int LastXpShown         { get; private set; }
        public GameObject LastXpEntity { get; private set; }

        private void OnEnable()
        {
            GameEvents.OnXpGained += OnXpGained;
            GameEvents.OnLevelUp  += OnLevelUp;
        }

        private void OnDisable()
        {
            GameEvents.OnXpGained -= OnXpGained;
            GameEvents.OnLevelUp  -= OnLevelUp;
        }

        private void OnXpGained(GameObject entity, int amount)
        {
            if (entity == null || amount <= 0) return;
            if (playerOnly && !entity.CompareTag("Player")) return;

            var spawner = entity.GetComponent<Combat.FloatingDamageSpawner>();
            if (spawner != null) spawner.ShowXp(amount);

            LastXpShown = amount;
            LastXpEntity = entity;
        }

        private void OnLevelUp(GameObject entity, int newLevel)
        {
            if (entity == null) return;
            if (playerOnly && !entity.CompareTag("Player")) return;

            string message = string.Format(levelUpToastFormat, newLevel);
            Combat.ToastSystem.Show(message, levelUpToastDuration);

            LastToastMessage = message;
            LastToastedLevel = newLevel;
        }
    }
}
