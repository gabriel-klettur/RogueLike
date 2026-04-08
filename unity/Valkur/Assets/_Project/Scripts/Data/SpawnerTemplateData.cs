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

        [Tooltip("Spawner type: invisible (no visual) or visual (building attachment).")]
        public SpawnerType spawnerType = SpawnerType.Invisible;

        [Tooltip("Spawn area shape.")]
        public SpawnerShape spawnerShape = SpawnerShape.Square;

        [Header("Spawn Area")]
        [Tooltip("Radius in tiles for the spawn area. Ignored when randomSpawnRadius is true.")]
        public int spawnRadius = 20;

        [Tooltip("When true, spawn radius is randomised at runtime (Python value: 'random').")]
        public bool randomSpawnRadius;

        [Tooltip("If true, spawned entities will defend the spawn point.")]
        public bool defendSpawn = true;

        [Tooltip("If true, spawned entities have a leash back to the spawn point.")]
        public bool defendLeash = true;

        [Tooltip("If true, a visual indicator is shown in-game.")]
        public bool visibleInGame;

        [Header("Trigger")]
        [Tooltip("Trigger type: Proximity (player enters range) or Auto (on load).")]
        public TriggerType triggerType = TriggerType.Proximity;

        [Tooltip("Trigger radius in tiles (for proximity trigger).")]
        public float triggerRadius = 10f;

        [Tooltip("Auto-start spawning when spawner is loaded.")]
        public bool autoStart = true;

        [Header("Policy")]
        [Tooltip("Spawning mode: Periodic or Burst.")]
        public SpawnMode spawnMode = SpawnMode.Periodic;

        [Tooltip("Cooldown between individual spawns in seconds.")]
        public float cooldownSeconds = 1f;

        [Tooltip("Only trigger once on first proximity entry.")]
        public bool proximityInitialOnly = true;

        [Tooltip("Cooldown between waves in seconds.")]
        public float betweenWavesCooldownSeconds = 5f;

        [Tooltip("When to advance to the next wave: Clear (all entities dead) or Cooldown (timer only).")]
        public AdvanceOn advanceOn = AdvanceOn.Clear;

        [Tooltip("Maximum simultaneously active entities. 0 = unlimited.")]
        public int maxActive;

        [Tooltip("If true, entities persist through zone transitions.")]
        public bool persistent;

        [Tooltip("If true, restart wave cycle when all waves are completed.")]
        public bool restartOnDone;

        [Tooltip("Cooldown in seconds before the spawner restarts after completing all waves.")]
        public float restartCooldownSeconds;

        [Header("Waves")]
        [Tooltip("Wave ID for external wave lookup, or use inline waves below.")]
        public string wavesId;

        [Tooltip("Inline wave definitions. Used when wavesId is empty.")]
        public List<WaveDefinition> waves = new List<WaveDefinition>();

        [Header("Life Defaults (visual spawner HP)")]
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
