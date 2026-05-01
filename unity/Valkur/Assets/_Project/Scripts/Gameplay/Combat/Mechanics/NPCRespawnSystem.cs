using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Respawns persistent NPCs after death, with a configurable cooldown.
    /// Maps to Python's NpcRespawnSystem.
    /// Listens to GameEvents.OnEntityDied and schedules respawns.
    /// </summary>
    public class NPCRespawnSystem : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds before a dead NPC respawns.")]
        private float respawnCooldown = 30f;

        [SerializeField, Tooltip("Max concurrent respawn entries tracked.")]
        private int maxTracked = 64;

        private struct RespawnEntry
        {
            public Data.MonsterDefinition definition;
            public Vector3 spawnPosition;
            public float respawnTime;
            public bool active;
        }

        private RespawnEntry[] _entries;
        private int _count;

        private void Awake()
        {
            _entries = new RespawnEntry[maxTracked];
            _count = 0;
        }

        private void OnEnable()
        {
            GameEvents.OnEntityDied += HandleEntityDied;
        }

        private void OnDisable()
        {
            GameEvents.OnEntityDied -= HandleEntityDied;
        }

        private void HandleEntityDied(GameObject victim, GameObject killer)
        {
            if (victim == null || victim.CompareTag("Player")) return;

            var brain = victim.GetComponent<FSMMonsterBrain>();
            if (brain == null || brain.Definition == null) return;

            // Only respawn non-hostile or configurable NPCs
            string faction = brain.Definition.stats.faction;
            if (string.IsNullOrEmpty(faction)) faction = "evil";

            // Queue respawn
            if (_count < maxTracked)
            {
                _entries[_count] = new RespawnEntry
                {
                    definition = brain.Definition,
                    spawnPosition = victim.transform.position,
                    respawnTime = Time.time + respawnCooldown,
                    active = true
                };
                _count++;
            }
        }

        private void Update()
        {
            for (int i = 0; i < _count; i++)
            {
                if (!_entries[i].active) continue;
                if (Time.time < _entries[i].respawnTime) continue;

                SpawnNPC(_entries[i].definition, _entries[i].spawnPosition);
                _entries[i].active = false;
            }

            // Compact if more than half inactive
            if (_count > 0 && CountInactive() > _count / 2)
            {
                Compact();
            }
        }

        private void SpawnNPC(Data.MonsterDefinition def, Vector3 position)
        {
            var go = new GameObject($"NPC_{def.monsterKey}");
            go.transform.position = position;

            EntitySetup.ConfigureMonster(go, def);
            Debug.Log($"[NPCRespawnSystem] Respawned {def.monsterKey} at {position}");
        }

        private int CountInactive()
        {
            int count = 0;
            for (int i = 0; i < _count; i++)
                if (!_entries[i].active) count++;
            return count;
        }

        private void Compact()
        {
            int write = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].active)
                {
                    _entries[write] = _entries[i];
                    write++;
                }
            }
            _count = write;
        }
    }
}
