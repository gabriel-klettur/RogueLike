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

        // Overrides
        private bool _visibleOverride;
        private bool _hasVisibleOverride;
        private bool _damageableOverride;
        private int _maxHpOverride;

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
                var playerT = EntityRegistry.PlayerTransform;
                if (playerT == null) return;

                float dist = Vector2.Distance(playerT.position, transform.position);
                if (dist <= _template.triggerRadius)
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

            // Spawn all entries in the current wave
            foreach (var entry in wave.spawns)
            {
                SpawnWaveEntry(entry);
            }

            _cooldownTimer = _template.cooldownSeconds;
            AdvanceWave();
        }

        private void AdvanceWave()
        {
            _currentWaveIndex++;
            if (_currentWaveIndex >= (_template.waves?.Count ?? 0))
            {
                _state = SpawnerState.Done;
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
            if (_template != null && _template.restartOnDone && _activeEntities.Count == 0)
            {
                _currentWaveIndex = 0;
                _state = SpawnerState.Active;
            }
        }

        private void SpawnWaveEntry(WaveSpawnEntry entry)
        {
            if (_monsterSpawner == null || string.IsNullOrEmpty(entry.entityId)) return;

            // Look up MonsterDefinition by key
            var monsterDef = Resources.Load<MonsterDefinition>($"Monsters/{entry.entityId}");
            if (monsterDef == null)
            {
                Debug.LogWarning($"[SpawnerInstance] Monster definition '{entry.entityId}' not found for spawner '{_instanceId}'.");
                return;
            }

            _monsterSpawner.RequestSpawnBatch(monsterDef, entry.count, transform.position, entry.spreadRadius);
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
        WaveCooldown,
        Done
    }
}
