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

            [Tooltip("Optional music track id (AudioCatalog) to crossfade to when " +
                     "this phase activates. Empty = keep whatever is already " +
                     "playing. Phase 0's entry is applied when the boss spawns, " +
                     "so a boss can open on its own theme.")]
            public string musicTrackId;

            [Tooltip("Crossfade length in seconds for musicTrackId. " +
                     "-1 = use the catalog's configured CrossfadeSec.")]
            public float musicCrossfadeSec = -1f;

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

            [Header("Adds")]
            [Tooltip("Monsters summoned when this phase begins. Empty (every phase shipped " +
                     "before this field) summons nothing. \n\n" +
                     "A boss fight with no adds is a duel, and a duel is the one encounter " +
                     "shape where none of the group layer does anything: no shout, no ring, no " +
                     "threat contest, no reason to look away from the boss. Adds are the " +
                     "cheapest thing that makes a phase a different FIGHT rather than a " +
                     "different spell list.")]
            public AddSpawn[] adds = Array.Empty<AddSpawn>();

            [Header("Phase AI")]
            [Tooltip("Standoff this phase wants, in world units. 0 keeps whatever the boss's " +
                     "own aiTuning says. A boss that closes in phase 1 and kites in phase 2 is " +
                     "two fights out of one asset, and it is the knob ChaseState already reads.")]
            [Min(0f)] public float desiredRange;

            [Tooltip("Chase speed multiplier for this phase. 0 or 1 keeps the authored speed. " +
                     "The plainest way to say 'it is angry now'.")]
            [Min(0f)] public float chaseSpeedMultiplier;

            [Tooltip("Probability 0..1 that this phase answers an inbound projectile with a " +
                     "sidestep. 0 keeps the boss's own dodgeChance, which for every shipped " +
                     "boss is 0 — a boss that starts dodging only once it is hurt is a " +
                     "readable escalation that costs one number.")]
            [Range(0f, 1f)] public float dodgeChance;
        }

        /// <summary>
        /// One group of minions a phase brings with it.
        ///
        /// <para>Spawned through the ordinary <c>MonsterSpawner</c> rather than a boss-specific
        /// path, so an add is a normal monster in every respect: it has a brain, a faction, a
        /// threat table, a slot on the engagement ring, and it can be levelled by the same
        /// difficulty layer as anything else. A bespoke spawner would have been a second way to
        /// create a monster, and the two would have drifted the first time either changed.</para>
        /// </summary>
        [Serializable]
        public class AddSpawn
        {
            [Tooltip("monsterKey from the MonsterCatalog.")]
            public string monsterKey;

            [Tooltip("How many. Spawned in a ring around the boss so they do not arrive stacked.")]
            [Min(1)] public int count = 1;

            [Tooltip("World units from the boss they appear at.")]
            [Min(0.5f)] public float spawnRadius = 4f;

            [Tooltip("Levels added on top of the add's own. The boss's phase is already a " +
                     "difficulty statement; this lets late-phase adds be worse than early ones.")]
            [Min(0)] public int levelBonus;
        }
    }
}
