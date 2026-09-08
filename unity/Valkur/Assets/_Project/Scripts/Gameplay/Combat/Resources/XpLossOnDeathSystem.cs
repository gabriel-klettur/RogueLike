using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Applies an XP penalty when the player revives after dying.
    ///
    /// <para>Hooked to <see cref="GameEvents.OnPlayerRevived"/> rather than
    /// <see cref="GameEvents.OnPlayerDied"/> so the player has the spirit walk before the penalty
    /// lands. The penalty is a fraction of XP earned within the CURRENT LEVEL, so the loss scales
    /// with progress rather than with lifetime XP, and it is clamped by default so death never
    /// de-levels — a punishing roguelike-classic behaviour kept optional.</para>
    ///
    /// <para><b>It reads the tuning, and it reads HOW the player got up.</b> Both of those were
    /// defects. The fraction was a <c>[SerializeField]</c> on a component <c>GameplaySceneSetup</c>
    /// <c>AddComponent</c>s onto a bare GameObject, so no inspector in the project could reach it.
    /// And <c>DeathSequenceController</c> fires BOTH revive events on every path — including the
    /// DevConsole cheat — with a comment promising the two exist precisely to tell a real revive
    /// from an instant one. Nothing read them apart, so <c>resurrect</c> charged the author 10 %
    /// of their XP: a distinction that was authored and inert, which is the pattern this project
    /// keeps finding. <see cref="DeathSequenceController.LastReviveKind"/> is the reader that
    /// makes it real.</para>
    /// </summary>
    public class XpLossOnDeathSystem : MonoBehaviour
    {
        /// <summary>
        /// The tuning's fraction, unless a test has overridden it. Null means "ask the asset",
        /// which is the shipped path — a nullable rather than a copied float so an editor change
        /// is visible without this component having to be told.
        /// </summary>
        private float? _fractionOverride;
        private bool? _delevelOverride;

        public float LossFraction
        {
            get => _fractionOverride ?? DeathTuning.Active.xpLossFraction;
            set => _fractionOverride = Mathf.Clamp01(value);
        }

        public bool CanDelevel
        {
            get => _delevelOverride ?? DeathTuning.Active.xpLossCanDelevel;
            set => _delevelOverride = value;
        }

        public int LastApplied { get; private set; }

        /// <summary>Why the last revive was not charged, or empty when it was. Read by the Death Editor.</summary>
        public string LastSkipReason { get; private set; } = string.Empty;

        private void OnEnable()  => GameEvents.OnPlayerRevived += OnPlayerRevived;
        private void OnDisable() => GameEvents.OnPlayerRevived -= OnPlayerRevived;

        private void OnPlayerRevived()
        {
            if (!ShouldCharge(out string reason))
            {
                LastApplied = 0;
                LastSkipReason = reason;
                return;
            }

            LastSkipReason = string.Empty;
            ApplyPenalty(EntityRegistry.Player);
        }

        /// <summary>
        /// Whether this revive costs anything.
        ///
        /// <para>A rescue DOES pay: the safety net exists so a run cannot end, not so that dying
        /// becomes free — and it already costs the player most of their health. Only the author's
        /// cheat is exempt, and even that is a setting rather than a rule.</para>
        /// </summary>
        private static bool ShouldCharge(out string reason)
        {
            reason = string.Empty;

            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller == null) return true;

            if (controller.LastReviveKind != DeathSequenceController.ReviveKind.Cheat) return true;
            if (DeathTuning.Active.cheatRevivePaysCost) return true;

            reason = "revivir por consola/editor no cobra coste (DeathTuning.cheatRevivePaysCost)";
            return false;
        }

        /// <summary>
        /// Applies the penalty to a specific entity. Public and parameterised to give tests a
        /// deterministic seam — no need to drive <see cref="EntityRegistry"/> from EditMode.
        /// </summary>
        public int ApplyPenalty(GameObject entity)
        {
            LastApplied = 0;
            float fraction = LossFraction;
            if (entity == null || fraction <= 0f) return 0;

            var xp = entity.GetComponent<Experience>();
            if (xp == null) return 0;

            int loss = Mathf.RoundToInt(xp.XpInCurrentLevel * fraction);
            if (loss <= 0) return 0;

            int actual = xp.RemoveXp(loss, clampToCurrentLevel: !CanDelevel);
            LastApplied = actual;
            return actual;
        }
    }
}
