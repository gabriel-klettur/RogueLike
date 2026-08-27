using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Base stats shared by all entities (players and NPCs).
    /// Maps to Python's stats dict in new_hostiles.json / new_players.json.
    /// </summary>
    [Serializable]
    public struct EntityStats
    {
        [Header("Vitals")]
        public int hp;
        public float speed;
        public float chasingSpeed;
        [Tooltip("Flat damage mitigation, consulted by Health.TakeDamage before HP is " +
                 "reduced. See Health.MitigateDamage for the exact formula (flat subtraction " +
                 "with a floor) and why it was chosen over a percentage.")]
        public int defense;

        [Tooltip("Read by DeathDropSystem.ResolveXpReward as the '+ power' half of the legacy " +
                 "XP fallback (hp/5 + power), used only when MonsterDefinition.xpReward is 0. " +
                 "Also shown read-only in the F5 Properties panel and DevConsole 'monster info'. " +
                 "No other reader — it does not affect combat.")]
        public int power;

        [Header("Combat")]
        [Tooltip("World units. Float, not an integer tile count, so 'knife range' (e.g. 0.6) " +
                 "is authorable alongside 'polearm range' (e.g. 3.0) — the previous int field " +
                 "could only express whole-tile steps.")]
        public float meleeRange;
        public int meleeDamage;
        public float meleeCooldown;
        public float aggroRange;
        public float damageDuration;
        public float damageStopProbability;
        public float attackWindupSeconds;

        [Header("Spawn")]
        public int spawnCount;
        public int spawnPadding;

        [Tooltip("INERT — shown read-only in the F5 Properties panel, but no spawn-placement " +
                 "code reads it (MonsterSpawner only consults spawnPadding); every shipped " +
                 "definition carries the 'new definition' template's default of 0, unlike its " +
                 "spawnPadding sibling. Kept rather than deleted since it IS displayed to a " +
                 "designer as if it mattered; wire it into MonsterSpawner's placement query, " +
                 "or fold it into spawnPadding and remove the row, before either is true.")]
        public int spawnMargin;
        public float deathDisappearTime;

        [Header("Collision")]
        [Tooltip("INERT — no runtime reader anywhere in the project, not even a display row " +
                 "in the F5 Properties panel. Genuinely authored, not just a template default: " +
                 "most shipped definitions carry 0.5, presumably from an earlier import pass. " +
                 "Kept rather than deleted because that authored intent is real; wire it into " +
                 "EntityColliderConfigurator's feet-hitbox sizing before relying on it.")]
        public float feetWidthFactor;

        [Tooltip("INERT — same status as feetWidthFactor immediately above (most shipped " +
                 "definitions carry 0.2); see that tooltip.")]
        public float feetHeightFactor;

        [Header("Faction")]
        public string faction;

        [Header("NPC / Vendor")]
        [Tooltip("INERT for monsters — no runtime reader consults THIS field. The working " +
                 "'how close before an NPC can be chatted with' knob is a completely different " +
                 "field of the same name, NPCPersonaDefinition.chatRange, read by ChatSystem. " +
                 "Genuinely authored here too (vendors ship 2, hostiles 0), just never wired to " +
                 "the system that shares its name. Kept rather than deleted because that intent " +
                 "is real.")]
        public float chatRange;

        [Header("Resistances / Immunities")]
        [Tooltip("Per-element damage multipliers, consulted by Health.TakeDamage. An element " +
                 "with no entry here defaults to 1.0 (no change) — a monster authored before " +
                 "this field existed takes every element at full damage, exactly as before.")]
        public ElementResistance[] resistances;

        [Tooltip("Status effect kinds this entity refuses outright — StatusEffectManager.Apply " +
                 "returns before OnApply runs. Empty (the default) means immune to nothing, " +
                 "exactly as before this field existed.")]
        public StatusEffectKind[] statusImmunities;
    }
}
