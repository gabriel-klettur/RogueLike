using System;

namespace Valkur.Data
{
    /// <summary>
    /// One status effect a spell may inflict on a successful hit, authored on
    /// <see cref="SpellDefinition.statusApplications"/>. Rolled independently per
    /// application by <c>Gameplay.Combat.StatusApplicationFactory</c> — a spell can carry
    /// more than one (e.g. a splash that both slows and poisons), and each rolls its own
    /// <see cref="chance"/> so one missing doesn't cancel the other.
    /// </summary>
    [Serializable]
    public struct StatusApplication
    {
        public StatusEffectKind type;

        [UnityEngine.Tooltip("Seconds the effect lasts once applied.")]
        public float duration;

        [UnityEngine.Tooltip("Effect-specific strength: Burn/Poison damage-per-tick, Slow's " +
                              "speed multiplier (0.5 = half speed). Ignored by Stun/Freeze, " +
                              "which have no magnitude of their own.")]
        public float magnitude;

        [UnityEngine.Range(0f, 1f)]
        [UnityEngine.Tooltip("Probability this application rolls true on a hit that reaches " +
                              "it. 0 (the default for a freshly added array entry) means " +
                              "'never' — an author must set this explicitly, matching " +
                              "duration <= 0 also being a no-op.")]
        public float chance;
    }
}
