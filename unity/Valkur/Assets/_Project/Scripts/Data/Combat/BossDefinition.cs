using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Designer-authored boss configuration. Layers on top of
    /// <see cref="MonsterDefinition"/> with phase-specific overrides:
    /// each phase has its own spell rotation (autoCastList) and a HP
    /// threshold at which it activates. The runtime <c>BossConfigurator</c>
    /// reads this asset and drives <c>BossPhaseController</c> +
    /// <c>NPCAutoCast</c> when the phase changes.
    ///
    /// Why a separate SO instead of stretching MonsterDefinition: bosses
    /// are rare (a few per game) and carry data that 95% of monsters
    /// never need. Putting phase data on every MonsterDefinition would
    /// bloat the per-monster asset and confuse designers authoring
    /// regular NPCs.
    ///
    /// Reference flow:
    ///   BossDefinition.baseMonster -> MonsterDefinition (HP, speed, …)
    ///   BossDefinition.phases[i]   -> per-phase spell rotation + HP threshold
    ///
    /// At runtime, BossConfigurator wires:
    ///   1. BossPhaseController phases from this asset's HP thresholds.
    ///   2. NPCAutoCast.Clear() + AddEntry per spell when phase changes.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBoss", menuName = "Valkur/Data/Boss Definition")]
    public sealed class BossDefinition : ScriptableObject
    {
        [Header("Base monster")]
        [Tooltip("Stats, sprites, FSM hooks all come from this MonsterDefinition. " +
                 "BossDefinition only adds phase-specific behaviour on top.")]
        public MonsterDefinition baseMonster;

        [Header("Phases (descending HP fraction)")]
        [Tooltip("Phase 0 is the entry phase (HP fraction 1.0). Subsequent " +
                 "phases trigger at their HP threshold. List ordered descending; " +
                 "BossConfigurator normalises if designer authored out of order.")]
        public Phase[] phases = Array.Empty<Phase>();

        [Header("Loot")]
        [Tooltip("Optional loot table rolled on death. Stack with the standard " +
                 "DeathDropSystem path — bosses can drop guaranteed items via " +
                 "this table AND the regular monster drop pool simultaneously.")]
        public LootTable bossLoot;

        [Serializable]
        public class Phase
        {
            [Tooltip("HP fraction at which this phase activates. 1.0 = full HP, " +
                     "0.5 = half HP, 0.0 = death. List ordered descending.")]
            [Range(0f, 1f)] public float hpThreshold = 1f;

            [Tooltip("Designer-readable label for logs / tooltips ('Enraged', " +
                     "'Phase 2', 'Final Stand'). Empty = synthesise as 'Phase N'.")]
            public string label;

            [Tooltip("Spell keys this phase auto-casts. Replaces the previous " +
                     "phase's rotation entirely — empty = no casting in this phase.")]
            public string[] autoCastList = Array.Empty<string>();

            [Tooltip("Override the auto-cast period (seconds). 0 = use NPCAutoCast " +
                     "default of 3 seconds.")]
            [Min(0)] public float autoCastPeriod;

            [Tooltip("Optional one-shot SFX id played when this phase activates " +
                     "(typically an enrage roar).")]
            public string activationSfxId;

            [Header("Rhythmic charts (optional)")]
            [Tooltip("Beat-anchored attack charts for this phase. One chart " +
                     "per song (matched on MusicTrackEntry.id). When the active " +
                     "music matches a chart, the boss casts in lock-step with " +
                     "the song; otherwise it falls back to the auto-cast rotation.")]
            public BossChart[] charts = Array.Empty<BossChart>();

            [Tooltip("If true, the cooldown-based NPCAutoCast rotation is paused " +
                     "while a chart is actively driving casts. Prevents the boss " +
                     "from double-casting when a chart already covers the phase.")]
            public bool suppressAutoCastWhenChartActive = true;
        }
    }
}
