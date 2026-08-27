using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Runtime spawner instance with wave management, trigger detection, and spawn policy.
    /// Maps to Python's spawner FSM states (WaitClear, Active, Cooldown, Done).
    ///
    /// Each spawner instance references a SpawnerTemplateData for settings and
    /// manages its own wave progression, cooldowns, and active entity tracking.
    /// </summary>
    public class SpawnerInstance : MonoBehaviour
    {
        private SpawnerTemplateData _template;
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

        // Overrides
        private bool _visibleOverride;
        private bool _hasVisibleOverride;
        private bool _damageableOverride;
        private int _maxHpOverride;

        // Proximity-trigger throttle. Polling every frame across 18+ active
        // spawners adds up; checking 10x/sec is enough for a feature the
        // player only notices when crossing the trigger ring.
        private const float ProximityPollInterval = 0.1f;
        private float _proximityNextPoll;

        public string InstanceId => _instanceId;
        public string Zone => _zone;
        public SpawnerTemplateData Template => _template;
        public SpawnerState State => _state;
        public int CurrentWaveIndex => _currentWaveIndex;
        public int ActiveEntityCount => _activeEntities.Count;

        public void Initialize(SpawnerTemplateData template, string instanceId, string zone, MonsterSpawner spawner)
        {
            _template = template;
            _instanceId = instanceId;
            _zone = zone;
            _monsterSpawner = spawner;

            if (template.triggerType == TriggerType.Auto && template.autoStart)
            {
                _triggered = true;
                _state = SpawnerState.Active;
            }
        }

        public void ApplyOverrides(Dictionary<string, object> overrides)
        {
            if (overrides.TryGetValue("visible_in_game", out var vis) && vis is bool visB)
            {
                _visibleOverride = visB;
                _hasVisibleOverride = true;
            }

            if (overrides.TryGetValue("life_defaults", out var lifeObj) &&
                lifeObj is Dictionary<string, object> life)
            {
                if (life.TryGetValue("damageable", out var dmg) && dmg is bool dmgB)
                    _damageableOverride = dmgB;
                if (life.TryGetValue("max_hp", out var hp))
                    _maxHpOverride = System.Convert.ToInt32(hp);
            }
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
        }

        private void UpdateIdle()
        {
            if (_template == null) return;

            if (_template.triggerType == TriggerType.Proximity && !_triggered)
            {
                // 10 Hz throttle: the proximity check fires across all spawners
                // every frame in the original, which means 18+ Vector2.Distance
                // calls per frame just to test a feature the player only
                // notices once per crossing. Polling at 10 Hz drops that to
                // ≤2 calls per frame across the active set, and the worst-case
                // 100ms latency on entering a trigger radius is imperceptible.
                if (Time.unscaledTime < _proximityNextPoll) return;
                _proximityNextPoll = Time.unscaledTime + ProximityPollInterval;

                var playerT = EntityRegistry.PlayerTransform;
                if (playerT == null) return;

                // sqrMagnitude avoids the per-frame sqrt that Vector2.Distance
                // implies — radius² is constant per template, so the compare
                // is mathematically equivalent.
                float dx = playerT.position.x - transform.position.x;
                float dy = playerT.position.y - transform.position.y;
                float sqrDist = dx * dx + dy * dy;
                float radius = _template.triggerRadius;
                if (sqrDist <= radius * radius)
                {
                    _triggered = true;
                    _state = SpawnerState.Active;
                }
            }
        }

        private void UpdateActive()
        {
            if (_template == null) return;

            // Check max_active cap
            if (_template.maxActive > 0 && _activeEntities.Count >= _template.maxActive)
                return;

            // Cooldown between individual spawns
            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer > 0f) return;

            // Get current wave
            var waves = _template.waves;
            if (waves == null || waves.Count == 0) return;
            if (_currentWaveIndex >= waves.Count)
            {
                _state = SpawnerState.Done;
                return;
            }

            var wave = waves[_currentWaveIndex];
            if (wave.spawns == null || wave.spawns.Count == 0)
            {
                AdvanceWave();
                return;
            }

            if (_template.spawnMode == SpawnMode.Periodic)
            {
                // One entry per cooldown tick, spreading the wave out over time instead of
                // dumping every entry into the world simultaneously — the difference the
                // field's own tooltip promises ("cooldown between INDIVIDUAL spawns") and
                // which nothing branched on before this. Every shipped wave holds exactly
                // one entry, so this is byte-for-byte the old behaviour for existing data.
                if (_periodicEntryIndex >= wave.spawns.Count)
                {
                    _periodicEntryIndex = 0;
                    if (_template.advanceOn == AdvanceOn.Clear)
                        _state = SpawnerState.WaitClear;
                    else
                        AdvanceWave();
                    return;
                }

                SpawnWaveEntry(wave.spawns[_periodicEntryIndex]);
                _periodicEntryIndex++;
                _cooldownTimer = _template.cooldownSeconds;
                return;
            }

            // Burst: every entry in the current wave spawns at once.
            foreach (var entry in wave.spawns)
            {
                SpawnWaveEntry(entry);
            }

            _cooldownTimer = _template.cooldownSeconds;

            // For "clear" mode, wait for all entities to die before advancing
            if (_template.advanceOn == AdvanceOn.Clear)
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
            if (_currentWaveIndex >= (_template.waves?.Count ?? 0))
            {
                _state = SpawnerState.Done;
                _cooldownTimer = _template.restartCooldownSeconds;
            }
            else
            {
                _waveCooldownTimer = _template.betweenWavesCooldownSeconds;
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
            if (_template == null || !_template.restartOnDone) return;

            // Wait for all spawned entities to die before restarting
            if (_activeEntities.Count > 0) return;

            // Apply restart cooldown
            if (_template.restartCooldownSeconds > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer > 0f) return;
            }

            _currentWaveIndex = 0;
            _periodicEntryIndex = 0;
            _cooldownTimer = _template.restartCooldownSeconds;
            _state = SpawnerState.Active;
        }

        private void SpawnWaveEntry(WaveSpawnEntry entry)
        {
            if (_monsterSpawner == null || string.IsNullOrEmpty(entry.entityId)) return;

            var monsterDef = _monsterSpawner.GetDefinition(entry.entityId);
            if (monsterDef == null)
            {
                Debug.LogWarning($"[SpawnerInstance] Monster definition '{entry.entityId}' not found for spawner '{_instanceId}'.");
                return;
            }

            for (int i = 0; i < entry.count; i++)
            {
                Vector2 offset = entry.spreadRadius > 0 ? Random.insideUnitCircle * entry.spreadRadius : Vector2.zero;
                offset = ClampToSpawnArea(offset);
                var go = _monsterSpawner.SpawnEntity(monsterDef, (Vector2)transform.position + offset,
                    persistent: _template.persistent);
                if (go != null)
                    _activeEntities.Add(go);
            }
        }

        /// <summary>
        /// Bounds a wave entry's random offset to the template's authored spawn area
        /// (<c>spawnRadius</c> + <c>spawnerShape</c>) — previously drawn as a gizmo and
        /// nothing else, so a <c>spreadRadius</c> larger than <c>spawnRadius</c> could scatter
        /// entities well outside the area the gizmo showed. <c>spawnRadius &lt;= 0</c> means
        /// unbounded, which reproduces the exact pre-existing behaviour: every shipped
        /// template's <c>spreadRadius</c> is well under its <c>spawnRadius</c>, so this clamp
        /// is a no-op for all of them.
        /// </summary>
        private Vector2 ClampToSpawnArea(Vector2 offset)
        {
            if (_template.spawnRadius <= 0) return offset;
            float r = _template.spawnRadius;

            if (_template.spawnerShape == SpawnerShape.Circle)
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

        public bool IsVisible
        {
            get
            {
                if (_hasVisibleOverride) return _visibleOverride;
                return _template != null && _template.visibleInGame;
            }
        }

        // ------------------------------------------------------------------
        // Editor gizmos
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_template == null) return;

            // Trigger radius
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _template.triggerRadius);

            // Spawn radius
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            if (_template.spawnerShape == SpawnerShape.Circle)
                Gizmos.DrawWireSphere(transform.position, _template.spawnRadius);
            else
                Gizmos.DrawWireCube(transform.position, Vector3.one * _template.spawnRadius * 2f);
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
