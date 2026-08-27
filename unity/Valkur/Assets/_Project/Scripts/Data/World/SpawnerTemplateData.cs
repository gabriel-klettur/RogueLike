using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a spawner template.
    /// Maps to Python's data/spawners/spawners_templates.json entries.
    /// Contains trigger, policy, and wave configuration.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSpawnerTemplate", menuName = "Valkur/Spawner/Template")]
    public class SpawnerTemplateData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Template ID matching Python spawner template id (e.g. 'survival_10').")]
        public string templateId;

        [Tooltip("INERT — no runtime code branches on this. Every spawner is created and " +
                 "spawns the same way regardless of the value; a 'Visual' spawner with its " +
                 "own on-map render/HP bar (see the Life Defaults block below) was never " +
                 "built. Kept so authored data isn't silently discarded on load.")]
        public SpawnerType spawnerType = SpawnerType.Invisible;

        [Tooltip("Spawn area shape, consulted by SpawnerInstance.ClampToSpawnArea alongside " +
                 "spawnRadius: Circle clamps a wave entry's random offset to a disc, Square " +
                 "clamps each axis independently. Also drawn as the OnDrawGizmosSelected box.")]
        public SpawnerShape spawnerShape = SpawnerShape.Square;

        [Header("Spawn Area")]
        [Tooltip("World-unit radius of the area entities may land in around the spawner. " +
                 "SpawnerInstance clamps each wave entry's spreadRadius offset to this bound " +
                 "(shaped by spawnerShape) before spawning — a spreadRadius bigger than this " +
                 "no longer scatters entities outside the area the gizmo draws. 0 = unbounded " +
                 "(every shipped template before this was wired effectively had this behaviour, " +
                 "since spreadRadius never exceeded spawnRadius).")]
        public int spawnRadius = 20;

        [Tooltip("INERT — no runtime code reads this. Authored as 'randomise spawnRadius at " +
                 "runtime' but nothing rolls a random value from it; spawnRadius above is used " +
                 "as an authored constant regardless.")]
        public bool randomSpawnRadius;

        [Tooltip("INERT — no runtime code reads this. 'Defend the spawn point' AI behaviour " +
                 "was never wired from the spawner side; the closest existing mechanism is " +
                 "ChaseState's spawn-anchor leash, driven by the MONSTER's own definition, not " +
                 "by this field.")]
        public bool defendSpawn = true;

        [Tooltip("INERT — no runtime code reads this. See defendSpawn.")]
        public bool defendLeash = true;

        [Tooltip("INERT — no runtime code reads this. SpawnerInstance.IsVisible computes it, " +
                 "but nothing renders a spawner's own sprite/marker in-game to show or hide — " +
                 "same unimplemented 'Visual' spawner variant as spawnerType.")]
        public bool visibleInGame;

        [Header("Trigger")]
        [Tooltip("Trigger type: Proximity (player enters range) or Auto (on load).")]
        public TriggerType triggerType = TriggerType.Proximity;

        [Tooltip("Trigger radius in tiles (for proximity trigger).")]
        public float triggerRadius = 10f;

        [Tooltip("Auto-start spawning when spawner is loaded.")]
        public bool autoStart = true;

        [Header("Policy")]
        [Tooltip("Periodic: one wave entry spawns per cooldownSeconds tick, spreading a " +
                 "multi-entry wave out over time. Burst: every entry in the current wave " +
                 "spawns at once (the only behaviour before this was branched on). Every " +
                 "shipped template's waves hold exactly one entry, so this change is a no-op " +
                 "for existing data — it only matters once a wave authors more than one entry.")]
        public SpawnMode spawnMode = SpawnMode.Periodic;

        [Tooltip("Cooldown between individual spawns in seconds.")]
        public float cooldownSeconds = 1f;

        [Tooltip("INERT — no runtime code reads this. A proximity trigger only ever fires " +
                 "once per SpawnerInstance today (SpawnerInstance._triggered never resets), " +
                 "so this field cannot yet express 're-arm after the player leaves and " +
                 "re-enters the radius'.")]
        public bool proximityInitialOnly = true;

        [Tooltip("Cooldown between waves in seconds.")]
        public float betweenWavesCooldownSeconds = 5f;

        [Tooltip("When to advance to the next wave: Clear (all entities dead) or Cooldown (timer only).")]
        public AdvanceOn advanceOn = AdvanceOn.Clear;

        [Tooltip("Maximum simultaneously active entities. 0 = unlimited.")]
        public int maxActive;

        [Tooltip("Exempts every entity this template spawns from MonsterSpawner's distance-" +
                 "based despawn sweep (despawnRadius, default 100 world units from the " +
                 "player). Every shipped vendor respawn template carries this — a banker or " +
                 "blacksmith must not evaporate the moment the player walks to the far side " +
                 "of the map. See MonsterSpawner.IsExemptFromDespawn / PersistentSpawnMarker.")]
        public bool persistent;

        [Tooltip("If true, restart wave cycle when all waves are completed.")]
        public bool restartOnDone;

        [Tooltip("Cooldown in seconds before the spawner restarts after completing all waves.")]
        public float restartCooldownSeconds;

        [Header("Waves")]
        [Tooltip("Inline wave definitions — the only source SpawnerInstance.UpdateActive " +
                 "reads (a template with an empty list spawns nothing). A prior 'wavesId' " +
                 "field promised an external wave-table lookup that was never built — no such " +
                 "table exists anywhere in the project — and was removed rather than left as " +
                 "a dangling reference. 'survival_10' shipped pointing at one and its waves " +
                 "list is still empty; author it here directly.")]
        public List<WaveDefinition> waves = new List<WaveDefinition>();

        // ── Life Defaults (visual spawner HP) ──────────────────────────────────
        // INERT as a group — every field below is written by nothing but the F3
        // properties display. They exist for the 'Visual' spawnerType (a spawner
        // that is itself a damageable, hit-flashing object in the world, e.g. a
        // building attachment), which was never implemented: no MonoBehaviour
        // gives a SpawnerInstance a SpriteRenderer, a Health, or a hit-flash.
        // Kept (not deleted) because damageable/maxHp/hpResetOnEnter read like a
        // real, if small, feature to build rather than data to discard.
        [Header("Life Defaults (visual spawner HP — INERT, see field group comment)")]
        public bool damageable;
        public int maxHp = 1000;
        public bool flashOnHit = true;
        public Color flashColor = Color.white;
        public float flashDurationSeconds = 0.08f;
        public string hpResetOnEnter = "set_to_max";
    }

    public enum SpawnerType
    {
        Invisible,
        Visual
    }

    public enum SpawnerShape
    {
        Square,
        Circle
    }

    public enum TriggerType
    {
        Proximity,
        Auto
    }

    public enum SpawnMode
    {
        Periodic,
        Burst
    }

    public enum AdvanceOn
    {
        Clear,
        Cooldown
    }

    [Serializable]
    public class WaveDefinition
    {
        [Tooltip("Spawn entries for this wave.")]
        public List<WaveSpawnEntry> spawns = new List<WaveSpawnEntry>();
    }

    [Serializable]
    public class WaveSpawnEntry
    {
        [Tooltip("Entity kind: monster, npc, etc.")]
        public string kind = "monster";

        [Tooltip("Monster/entity definition key.")]
        public string entityId;

        [Tooltip("Number to spawn in this entry.")]
        public int count = 1;

        [Tooltip("Spread radius in tiles.")]
        public float spreadRadius = 3f;

        [Tooltip("Maximum fallback spread if initial spread fails.")]
        public float spreadFallbackMax = 12f;

        [Tooltip("Minimum pixel distance between spawns.")]
        public float minDistance = 24f;
    }
}
