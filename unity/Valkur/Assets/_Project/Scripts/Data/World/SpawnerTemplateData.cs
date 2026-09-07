using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// A spawner PRESET — what a fresh placement is born with, and nothing more.
    ///
    /// <para><b>COPY ON PLACE.</b> A preset is a starting point, not a live link. Placing a
    /// spawner takes a copy of every field below into that placement's own
    /// <see cref="SpawnerInstanceConfig"/>, which is what the runtime reads from then on and
    /// what is written to <c>spawners_instances.json</c>. Editing a preset afterwards reaches
    /// the NEXT placement and none of the existing ones.</para>
    ///
    /// <para>Before that, a placed spawner's whole on-disk record was
    /// <c>template_id</c>/<c>zone</c>/<c>tile</c>/<c>id</c> — a pointer — so the only way to
    /// express a new behaviour was a new asset. The catalogue grew to twenty-five templates,
    /// six of them byte-identical except for one <c>entityId</c> string, and seven never
    /// placed at all, six of those named after their own parameter values
    /// (<c>barbol_auto_pair_3s_max2_restart10</c>). The properties panel, meanwhile, edited
    /// THIS asset, so tuning one spawner retuned every other placement of its kind — in Play
    /// Mode, which Unity keeps until the next domain reload, so the edit followed the author
    /// back into the Editor. Same defect and same remedy as the Particles editor's
    /// <c>ParticleInstanceConfig</c>.</para>
    ///
    /// <para>Every field here is therefore overridable per placement. Nothing is preset-only:
    /// a preset is a DEFAULT SET, so a field it holds that could not be overridden would make
    /// the preset partly a rule. What keeps presets valuable is that "vendor respawn, 5 min,
    /// persistent, max 1" is worth expressing once.</para>
    ///
    /// <para><b>Field initializers must stay identical to
    /// <see cref="SpawnerInstanceConfig"/>'s.</b> The serializer omits any config value equal
    /// to the C# default, so the two declarations are one contract; they are pinned by
    /// <c>SpawnerInstanceConfigDefaultsTests</c>.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "NewSpawnerTemplate", menuName = "Valkur/Spawner/Template")]
    public class SpawnerTemplateData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Preset ID. Recorded on every placement made from it — it names where that " +
                 "configuration came from, groups the picker, and is what 'Reapply preset' " +
                 "reads. It no longer decides how a placed spawner behaves.")]
        public string templateId;

        [Tooltip("Spawn area shape, consulted by SpawnerInstance.ClampToSpawnArea alongside " +
                 "spawnRadius: Circle clamps a wave entry's random offset to a disc, Square " +
                 "clamps each axis independently.")]
        public SpawnerShape spawnerShape = SpawnerShape.Square;

        [Header("Spawn Area")]
        [Tooltip("World-unit radius of the area entities may land in around the spawner. " +
                 "Each wave entry's spreadRadius offset is clamped to this bound (shaped by " +
                 "spawnerShape) before spawning. 0 = unbounded.")]
        public int spawnRadius = 20;

        [Header("Trigger")]
        [Tooltip("Trigger type: Proximity (player enters range) or Auto (on load).")]
        public TriggerType triggerType = TriggerType.Proximity;

        [Tooltip("Trigger radius in world units (for proximity trigger).")]
        public float triggerRadius = 10f;

        [Tooltip("Auto-start spawning when spawner is loaded.")]
        public bool autoStart = true;

        [Tooltip("Re-arm a proximity trigger once the player leaves the radius, so the camp " +
                 "can fire again on the next approach. Off reproduces the historical " +
                 "behaviour, where a proximity spawner fired exactly once per session.")]
        public bool proximityRearms;

        [Header("Policy")]
        [Tooltip("Periodic: one wave entry spawns per cooldownSeconds tick, spreading a " +
                 "multi-entry wave out over time. Burst: every entry in the current wave " +
                 "spawns at once.")]
        public SpawnMode spawnMode = SpawnMode.Periodic;

        [Tooltip("Cooldown between individual spawns in seconds.")]
        public float cooldownSeconds = 1f;

        [Tooltip("Cooldown between waves in seconds.")]
        public float betweenWavesCooldownSeconds = 5f;

        [Tooltip("When to advance to the next wave: Clear (all entities dead) or Cooldown (timer only).")]
        public AdvanceOn advanceOn = AdvanceOn.Clear;

        [Tooltip("Maximum simultaneously active entities. 0 = unlimited.")]
        public int maxActive;

        [Tooltip("Exempts every entity this spawner produces from MonsterSpawner's distance-" +
                 "based despawn sweep (despawnRadius, default 100 world units from the " +
                 "player). Every vendor respawn preset carries this — a banker or blacksmith " +
                 "must not evaporate the moment the player walks to the far side of the map. " +
                 "See MonsterSpawner.IsExemptFromDespawn / PersistentSpawnMarker. It is a " +
                 "live exemption, not a convenience: a world of exempt monsters is a leak " +
                 "with no error.")]
        public bool persistent;

        [Tooltip("If true, restart wave cycle when all waves are completed.")]
        public bool restartOnDone;

        [Tooltip("Cooldown in seconds before the spawner restarts after completing all waves.")]
        public float restartCooldownSeconds;

        [Header("Waves")]
        [Tooltip("The roster a fresh placement is born with — the only source " +
                 "SpawnerInstance.UpdateActive reads (an empty list spawns nothing). A prior " +
                 "'wavesId' field promised an external wave-table lookup that was never " +
                 "built and was removed rather than left as a dangling reference.")]
        public List<WaveDefinition> waves = new List<WaveDefinition>();

        [Header("Difficulty")]
        [Tooltip("Flat levels added to every monster this spawner produces, on top of the " +
                 "MonsterDefinition's own level. This is PLACE: a deeper camp is harder whether " +
                 "the player is level 1 or 40.")]
        [Min(0)] public int levelBonus;

        [Tooltip("Fraction of the player's level above 1 added on top. This is PROGRESS: it " +
                 "keeps a region from becoming furniture once the player outgrows it. 0 " +
                 "disables it; 1 tracks the player exactly, which turns the world into a " +
                 "treadmill — content usually wants 0.3 to 0.6. Resolved once per wave entry, " +
                 "so a pack arrives at one level.")]
        [Range(0f, 1f)] public float scaleWithPlayerLevel;

        [Header("Defend")]
        [Tooltip("Leash radius in world units for every entity this spawner produces: how far " +
                 "from the spawn point it will chase before breaking off and walking home. " +
                 "0 = leave the monster's own leash alone (aggroRange x " +
                 "FSMTuning.DefaultLeashRangeFactor). This is the live half of the old " +
                 "defendSpawn/defendLeash pair, which had no reader at all; it is delivered " +
                 "through SpawnBrain, stamped on the spawned object, so it never touches the " +
                 "shared MonsterDefinition.")]
        [Min(0f)] public float defendLeashRadius;

        [Tooltip("FSM set every entity this spawner produces is built with, e.g. " +
                 "'Monster_Caster'. Empty = resolve as usual. An Entities-editor by_eid " +
                 "override still wins, being the more specific statement; a name that no set " +
                 "declares is refused loudly rather than falling through, because a spawner " +
                 "silently ignoring its authored brain is exactly the authored-and-inert " +
                 "shape this file's history is made of.")]
        public string fsmSetOverride = string.Empty;
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

        public WaveDefinition() { }

        /// <summary>Deep copy — the unit copy-on-place is built from.</summary>
        public WaveDefinition(WaveDefinition other)
        {
            if (other?.spawns == null) return;
            foreach (var entry in other.spawns)
                if (entry != null) spawns.Add(new WaveSpawnEntry(entry));
        }
    }

    [Serializable]
    public class WaveSpawnEntry
    {
        [Tooltip("Entity kind: 'monster' (the default and the fallback for anything " +
                 "unrecognised) or 'building', which routes to BuildingLoader so a fish " +
                 "shoal or a crop is harvestable for the same reason a tree is.")]
        public string kind = "monster";

        [Tooltip("MonsterDefinition.monsterKey, or a numeric BuildingTemplateData id when " +
                 "kind is 'building'.")]
        public string entityId;

        [Tooltip("Number to spawn in this entry.")]
        public int count = 1;

        [Tooltip("Spread radius in world units, clamped to the spawner's own spawnRadius.")]
        public float spreadRadius = 3f;

        [Tooltip("Maximum fallback spread if initial spread fails.")]
        public float spreadFallbackMax = 12f;

        [Tooltip("Minimum pixel distance between spawns.")]
        public float minDistance = 24f;

        public WaveSpawnEntry() { }

        public WaveSpawnEntry(WaveSpawnEntry other)
        {
            if (other == null) return;
            kind              = other.kind;
            entityId          = other.entityId;
            count             = other.count;
            spreadRadius      = other.spreadRadius;
            spreadFallbackMax = other.spreadFallbackMax;
            minDistance       = other.minDistance;
        }
    }
}
