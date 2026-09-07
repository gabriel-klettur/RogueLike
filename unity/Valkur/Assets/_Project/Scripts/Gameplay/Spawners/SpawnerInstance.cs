using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Runtime spawner instance with wave management, trigger detection, and spawn policy.
    ///
    /// <para><b>It reads its OWN <see cref="SpawnerInstanceConfig"/>, never the preset.</b>
    /// Every field this class used to take off the shared <see cref="SpawnerTemplateData"/>
    /// now comes from a copy made when the spawner was placed, so two placements of one
    /// preset are independent and the properties panel can edit one of them without touching
    /// the other. <see cref="Preset"/> is kept for identity — grouping, outlines, and the
    /// "reapply preset" actions — and decides nothing.</para>
    /// </summary>
    public class SpawnerInstance : MonoBehaviour
    {
        private SpawnerTemplateData _preset;
        private SpawnerInstanceConfig _config = new SpawnerInstanceConfig();
        private string _instanceId;
        private string _zone;
        private MonsterSpawner _monsterSpawner;

        // State
        private SpawnerState _state = SpawnerState.Idle;
        private int _currentWaveIndex;
        private float _cooldownTimer;
        private float _waveCooldownTimer;
        private bool _triggered;
        private readonly List<GameObject> _activeEntities = new List<GameObject>();

        // Index into the current wave's spawns list for SpawnMode.Periodic — one entry
        // spawns per cooldown tick instead of the whole wave landing at once. Reset
        // whenever the wave advances (see AdvanceWave) or the cycle restarts (UpdateDone).
        private int _periodicEntryIndex;

        // Proximity-trigger throttle. Polling every frame across 18+ active
        // spawners adds up; checking 10x/sec is enough for a feature the
        // player only notices when crossing the trigger ring.
        private const float ProximityPollInterval = 0.1f;
        private float _proximityNextPoll;

        /// <summary>
        /// Hysteresis on the re-arm ring, as a fraction of <c>triggerRadius</c>. A camp that
        /// re-armed the instant the player crossed back out would fire again on the next
        /// step, so a player standing on the boundary would be spawned at forever. 1.35 puts
        /// the re-arm ring comfortably outside the fire ring without making the player walk
        /// the map to reset it.
        /// </summary>
        private const float RearmRadiusFactor = 1.35f;

        public string InstanceId => _instanceId;
        public string Zone => _zone;

        /// <summary>The preset this placement came FROM. It names an origin, not a behaviour.</summary>
        public SpawnerTemplateData Preset => _preset;

        /// <summary>This placement's own configuration — the only thing the runtime reads.</summary>
        public SpawnerInstanceConfig Config => _config;

        public SpawnerState State => _state;
        public int CurrentWaveIndex => _currentWaveIndex;
        public int ActiveEntityCount => _activeEntities.Count;

        /// <summary>
        /// Place a spawner from a preset — the editor's placement path. Takes a snapshot, so
        /// the new spawner is independent of the asset from the first frame.
        /// </summary>
        public void Initialize(SpawnerTemplateData preset, string instanceId, string zone,
                               MonsterSpawner spawner)
            => Initialize(preset, SpawnerInstanceConfig.SnapshotOf(preset), instanceId, zone, spawner);

        /// <summary>
        /// Load a spawner from its persisted configuration — the loader's path. A null config
        /// falls back to a snapshot of the preset rather than to a blank one, which is what
        /// keeps a v1 row whose freeze could not run behaving exactly as it did.
        /// </summary>
        public void Initialize(SpawnerTemplateData preset, SpawnerInstanceConfig config,
                               string instanceId, string zone, MonsterSpawner spawner)
        {
            _preset         = preset;
            _instanceId     = instanceId;
            _zone           = zone;
            _monsterSpawner = spawner;

            ApplyConfig(config ?? SpawnerInstanceConfig.SnapshotOf(preset));
        }

        /// <summary>
        /// Install a configuration and re-seed the state machine from it.
        ///
        /// <para>Re-seeding is the half that is easy to forget: flipping a live spawner from
        /// Proximity to Auto in the properties panel has to start it, and flipping it back has
        /// to stop it. Leaving <c>_state</c> alone would make the author's edit take effect
        /// only after a reload, which reads as the control not working.</para>
        ///
        /// <para>Already-spawned entities are deliberately NOT withdrawn. They are creatures
        /// in the world with their own brains and their own fights; retracting them because
        /// somebody widened a radius would be a far stranger thing to watch than a camp whose
        /// next wave differs from its last.</para>
        /// </summary>
        public void ApplyConfig(SpawnerInstanceConfig config)
        {
            _config = config ?? new SpawnerInstanceConfig();

            _currentWaveIndex   = 0;
            _periodicEntryIndex = 0;
            _cooldownTimer      = 0f;
            _waveCooldownTimer  = 0f;

            bool auto = _config.triggerType == TriggerType.Auto && _config.autoStart;
            _triggered = auto;
            _state     = auto ? SpawnerState.Active : SpawnerState.Idle;
        }

        private void Update()
        {
            CleanupDeadEntities();

            switch (_state)
            {
                case SpawnerState.Idle:
                    UpdateIdle();
                    break;
                case SpawnerState.Active:
                    UpdateActive();
                    break;
                case SpawnerState.WaitClear:
                    UpdateWaitClear();
                    break;
                case SpawnerState.WaveCooldown:
                    UpdateWaveCooldown();
                    break;
                case SpawnerState.Done:
                    UpdateDone();
                    break;
            }

            TickProximityRearm();
        }

        private void UpdateIdle()
        {
            if (_config.triggerType != TriggerType.Proximity || _triggered) return;

            // 10 Hz throttle: the proximity check fires across all spawners every frame in
            // the original, which means 18+ distance calls per frame just to test a feature
            // the player only notices once per crossing. The worst-case 100 ms latency on
            // entering a trigger radius is imperceptible.
            if (Time.unscaledTime < _proximityNextPoll) return;
            _proximityNextPoll = Time.unscaledTime + ProximityPollInterval;

            if (!TryGetPlayerSqrDistance(out float sqrDist)) return;

            float radius = _config.triggerRadius;
            if (sqrDist <= radius * radius)
            {
                _triggered = true;
                _state     = SpawnerState.Active;
            }
        }

        /// <summary>
        /// Re-arms a fired proximity trigger once the player has left the ring, when the
        /// config asks for it.
        ///
        /// <para><c>_triggered</c> was never reset, so a proximity spawner fired exactly once
        /// per session and the authored <c>proximityInitialOnly</c> could not express its own
        /// opposite — it was one of the twelve inert fields. Re-arming is what a wandering-
        /// monster region needs, and it is off by default so every spawner shipped before this
        /// behaves exactly as it did.</para>
        ///
        /// <para>It runs OUTSIDE the state switch on purpose: a spawner that fired is in
        /// Active/WaitClear/Done, not Idle, so a re-arm folded into <see cref="UpdateIdle"/>
        /// could never run. It also refuses while entities from the last wave are still
        /// alive, or walking out and back in would stack a second camp on top of the first.</para>
        /// </summary>
        private void TickProximityRearm()
        {
            if (!_config.proximityRearms) return;
            if (_config.triggerType != TriggerType.Proximity) return;
            if (!_triggered || _activeEntities.Count > 0) return;

            if (Time.unscaledTime < _proximityNextPoll) return;
            _proximityNextPoll = Time.unscaledTime + ProximityPollInterval;

            if (!TryGetPlayerSqrDistance(out float sqrDist)) return;

            float rearmRadius = _config.triggerRadius * RearmRadiusFactor;
            if (sqrDist <= rearmRadius * rearmRadius) return;

            _triggered          = false;
            _currentWaveIndex   = 0;
            _periodicEntryIndex = 0;
            _cooldownTimer      = 0f;
            _state              = SpawnerState.Idle;
        }

        private bool TryGetPlayerSqrDistance(out float sqrDist)
        {
            sqrDist = 0f;
            var playerT = EntityRegistry.PlayerTransform;
            if (playerT == null) return false;

            // sqrMagnitude avoids the per-frame sqrt that Vector2.Distance implies — radius²
            // is constant per config, so the compare is mathematically equivalent.
            float dx = playerT.position.x - transform.position.x;
            float dy = playerT.position.y - transform.position.y;
            sqrDist = dx * dx + dy * dy;
            return true;
        }

        private void UpdateActive()
        {
            // Check max_active cap
            if (_config.maxActive > 0 && _activeEntities.Count >= _config.maxActive)
                return;

            // Cooldown between individual spawns
            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer > 0f) return;

            var waves = _config.waves;
            if (waves == null || waves.Count == 0) return;
            if (_currentWaveIndex >= waves.Count)
            {
                _state = SpawnerState.Done;
                return;
            }

            var wave = waves[_currentWaveIndex];
            if (wave?.spawns == null || wave.spawns.Count == 0)
            {
                AdvanceWave();
                return;
            }

            if (_config.spawnMode == SpawnMode.Periodic)
            {
                // One entry per cooldown tick, spreading the wave out over time instead of
                // dumping every entry into the world simultaneously — the difference the
                // field's own tooltip promises ("cooldown between INDIVIDUAL spawns").
                if (_periodicEntryIndex >= wave.spawns.Count)
                {
                    _periodicEntryIndex = 0;
                    if (_config.advanceOn == AdvanceOn.Clear)
                        _state = SpawnerState.WaitClear;
                    else
                        AdvanceWave();
                    return;
                }

                SpawnWaveEntry(wave.spawns[_periodicEntryIndex]);
                _periodicEntryIndex++;
                _cooldownTimer = _config.cooldownSeconds;
                return;
            }

            // Burst: every entry in the current wave spawns at once.
            foreach (var entry in wave.spawns)
                SpawnWaveEntry(entry);

            _cooldownTimer = _config.cooldownSeconds;

            // For "clear" mode, wait for all entities to die before advancing
            if (_config.advanceOn == AdvanceOn.Clear)
                _state = SpawnerState.WaitClear;
            else
                AdvanceWave();
        }

        private void UpdateWaitClear()
        {
            // Wait until all active entities from this wave are dead
            if (_activeEntities.Count > 0) return;
            AdvanceWave();
        }

        private void AdvanceWave()
        {
            _periodicEntryIndex = 0;
            _currentWaveIndex++;
            if (_currentWaveIndex >= (_config.waves?.Count ?? 0))
            {
                _state = SpawnerState.Done;
                _cooldownTimer = _config.restartCooldownSeconds;
            }
            else
            {
                _waveCooldownTimer = _config.betweenWavesCooldownSeconds;
                _state = SpawnerState.WaveCooldown;
            }
        }

        private void UpdateWaveCooldown()
        {
            _waveCooldownTimer -= Time.deltaTime;
            if (_waveCooldownTimer <= 0f)
                _state = SpawnerState.Active;
        }

        private void UpdateDone()
        {
            if (!_config.restartOnDone) return;

            // Wait for all spawned entities to die before restarting
            if (_activeEntities.Count > 0) return;

            // Apply restart cooldown
            if (_config.restartCooldownSeconds > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer > 0f) return;
            }

            _currentWaveIndex = 0;
            _periodicEntryIndex = 0;
            _cooldownTimer = _config.restartCooldownSeconds;
            _state = SpawnerState.Active;
        }

        /// <summary>
        /// Spawn one wave entry, dispatched on <see cref="WaveSpawnEntry.kind"/>.
        ///
        /// <para>That field shipped authored, round-tripped to disk and READ BY NOTHING: this
        /// method went straight to the monster spawner whatever it said, so a designer could
        /// set "npc" or anything else and get a monster.</para>
        ///
        /// <para>Anything unrecognised, empty included, keeps the monster path, so every one of
        /// the shipped presets behaves exactly as before.</para>
        /// </summary>
        private void SpawnWaveEntry(WaveSpawnEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.entityId)) return;

            if (string.Equals(entry.kind, "building", System.StringComparison.OrdinalIgnoreCase))
            {
                SpawnBuildingEntry(entry);
                return;
            }

            if (_monsterSpawner == null) return;

            var monsterDef = _monsterSpawner.GetDefinition(entry.entityId);
            if (monsterDef == null)
            {
                Debug.LogWarning($"[SpawnerInstance] Monster definition '{entry.entityId}' not found for spawner '{_instanceId}'.");
                return;
            }

            // Resolved ONCE per wave entry rather than per body, so a pack that arrives
            // together arrives at one level. Rolling it inside the loop would let the player
            // level up mid-wave and produce a squad whose members differ for a reason nothing
            // on screen explains.
            int level = EncounterDifficulty.ResolveLevel(
                monsterDef, _config.levelBonus, _config.scaleWithPlayerLevel);

            for (int i = 0; i < entry.count; i++)
            {
                Vector2 offset = entry.spreadRadius > 0 ? Random.insideUnitCircle * entry.spreadRadius : Vector2.zero;
                offset = ClampToSpawnArea(offset);
                // The brain override is passed IN rather than applied to the return value:
                // SpawnEntity runs EntitySetup, which is where FSMMonsterBrain resolves the
                // set and fills the context, so a stamp applied after the call would be read
                // by nobody. Same ordering constraint as the resolved level.
                var go = _monsterSpawner.SpawnEntity(monsterDef, (Vector2)transform.position + offset,
                    persistent: _config.persistent, resolvedLevel: level,
                    fsmSetOverride: _config.fsmSetOverride, leashOverride: _config.defendLeashRadius);
                if (go != null) _activeEntities.Add(go);
            }
        }

        /// <summary>
        /// Place BUILDINGS rather than entities — a shoal of fish, a crop, anything that is a
        /// placed object the player can work rather than something that walks.
        ///
        /// <para>It goes through <c>BuildingLoader.SpawnAtWorldPosition</c>, the same entry the
        /// Map Editor's biome generator uses, so the spawned object gets its collision scope,
        /// its Y-sort, its light and — the part that matters here — its
        /// <c>BuildingDurability</c> and <c>HarvestNode</c>, because those are attached by the
        /// loader for any template that declares a DestructionProfile. A fish shoal is
        /// therefore harvestable for exactly the same reason a tree is, with no code of its
        /// own.</para>
        ///
        /// <para>Instance ids are drawn from a high private range. The authored world numbers
        /// its buildings from 1 upward in buildings_instances.json, and the harvest save keys
        /// on (slot, zone, instanceId) — so a spawned shoal colliding with an authored id would
        /// make felling a tree mark a shoal as fished, and neither file would look wrong.</para>
        /// </summary>
        private void SpawnBuildingEntry(WaveSpawnEntry entry)
        {
            if (!int.TryParse(entry.entityId, out int templateId))
            {
                Debug.LogWarning($"[SpawnerInstance] Wave entry kind='building' needs a numeric " +
                                 $"template id in entityId, got '{entry.entityId}' " +
                                 $"(spawner '{_instanceId}').");
                return;
            }

            var loader = ResolveBuildingLoader();
            if (loader == null)
            {
                Debug.LogWarning($"[SpawnerInstance] No BuildingLoader in the scene; spawner " +
                                 $"'{_instanceId}' cannot place template {templateId}.");
                return;
            }

            for (int i = 0; i < entry.count; i++)
            {
                Vector2 offset = entry.spreadRadius > 0
                    ? Random.insideUnitCircle * entry.spreadRadius
                    : Vector2.zero;
                offset = ClampToSpawnArea(offset);

                var placed = loader.SpawnAtWorldPosition(
                    templateId, _zone, (Vector2)transform.position + offset, NextSpawnedInstanceId());
                if (placed != null) _activeEntities.Add(placed.gameObject);
            }
        }

        /// <summary>
        /// The first instance id a SPAWNED building may take. Comfortably above anything the
        /// authored world uses — buildings_instances.json ships 302 records numbered from 1 —
        /// and low enough to stay readable in a save file.
        /// </summary>
        private const int SPAWNED_BUILDING_ID_BASE = 900000;

        // Domain Reload is OFF, so a counter left mid-run would keep climbing into the next
        // Play session. Assigning the base back is a plain stsfld, the only reset shape
        // DomainReloadStaticResetTests recognises.
        private static int s_nextSpawnedBuildingId = SPAWNED_BUILDING_ID_BASE;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpawnedBuildingIds() => s_nextSpawnedBuildingId = SPAWNED_BUILDING_ID_BASE;

        private static int NextSpawnedInstanceId() => s_nextSpawnedBuildingId++;

        // Resolved lazily and cached: BuildingLoader is not in the ServiceLocator, and the
        // spawner may well run before it would have been registered anyway.
        private World.BuildingLoader _buildingLoader;

        private World.BuildingLoader ResolveBuildingLoader()
        {
            if (_buildingLoader != null) return _buildingLoader;
            _buildingLoader = Object.FindObjectOfType<World.BuildingLoader>();
            return _buildingLoader;
        }

        /// <summary>
        /// Bounds a wave entry's random offset to the configured spawn area
        /// (<c>spawnRadius</c> + <c>spawnerShape</c>) — previously drawn as a gizmo and
        /// nothing else, so a <c>spreadRadius</c> larger than <c>spawnRadius</c> could scatter
        /// entities well outside the area the gizmo showed. <c>spawnRadius &lt;= 0</c> means
        /// unbounded.
        /// </summary>
        private Vector2 ClampToSpawnArea(Vector2 offset)
        {
            if (_config.spawnRadius <= 0) return offset;
            float r = _config.spawnRadius;

            if (_config.spawnerShape == SpawnerShape.Circle)
                return offset.sqrMagnitude > r * r ? offset.normalized * r : offset;

            // Square: clamp each axis independently.
            return new Vector2(Mathf.Clamp(offset.x, -r, r), Mathf.Clamp(offset.y, -r, r));
        }

        private void CleanupDeadEntities()
        {
            for (int i = _activeEntities.Count - 1; i >= 0; i--)
            {
                if (_activeEntities[i] == null)
                    _activeEntities.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------
        // Editor gizmos
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Trigger radius
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _config.triggerRadius);

            // Spawn radius
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            if (_config.spawnerShape == SpawnerShape.Circle)
                Gizmos.DrawWireSphere(transform.position, _config.spawnRadius);
            else
                Gizmos.DrawWireCube(transform.position, Vector3.one * _config.spawnRadius * 2f);
        }
#endif
    }

    public enum SpawnerState
    {
        Idle,
        Active,
        WaitClear,
        WaveCooldown,
        Done
    }
}
