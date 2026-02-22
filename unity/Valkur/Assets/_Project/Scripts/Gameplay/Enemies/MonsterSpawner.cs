using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Monster spawner with per-frame budget to avoid FPS spikes.
    /// Maps to Python's spawn_system.py with MAX_SPAWNS_PER_FRAME = 3.
    /// Uses spatial hash for spawn padding validation.
    /// </summary>
    public class MonsterSpawner : MonoBehaviour
    {
        [Header("Budget")]
        [SerializeField] private int maxSpawnsPerFrame = 3;

        [Header("Prefab")]
        [SerializeField] private GameObject monsterPrefab;

        [Header("Spawn Config")]
#pragma warning disable CS0414
        [SerializeField] private float spawnRadius = 15f;
#pragma warning restore CS0414
        [SerializeField] private float despawnRadius = 25f;
#pragma warning disable CS0414
        [SerializeField] private float minSpawnDistance = 8f;
#pragma warning restore CS0414

        private readonly Queue<SpawnRequest> _spawnQueue = new Queue<SpawnRequest>();
        private readonly List<GameObject> _activeMonsters = new List<GameObject>();
        private readonly SpatialHash<GameObject> _spatialHash = new SpatialHash<GameObject>(3f);
        private readonly List<(GameObject item, Vector2 pos)> _queryResults = new List<(GameObject, Vector2)>(16);

        private Transform _playerTransform;

        private struct SpawnRequest
        {
            public MonsterDefinition Definition;
            public Vector2 Position;
        }

        private void Update()
        {
            if (_playerTransform == null)
            {
                _playerTransform = EntityRegistry.PlayerTransform;
                if (_playerTransform == null) return;
            }

            ProcessDespawns();
            ProcessSpawnQueue();
        }

        /// <summary>
        /// Enqueue a monster to be spawned. Will be processed within budget.
        /// </summary>
        public void RequestSpawn(MonsterDefinition def, Vector2 position)
        {
            _spawnQueue.Enqueue(new SpawnRequest { Definition = def, Position = position });
        }

        /// <summary>
        /// Batch spawn request for multiple monsters.
        /// </summary>
        public void RequestSpawnBatch(MonsterDefinition def, int count, Vector2 center, float radius)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                float padding = def.stats.spawnPadding > 0 ? def.stats.spawnPadding : 1f;

                // Validate spawn padding with spatial hash
                _spatialHash.Clear();
                foreach (var m in _activeMonsters)
                {
                    if (m != null)
                        _spatialHash.Insert(m, m.transform.position);
                }

                _spatialHash.QueryRadius(center + offset, padding, _queryResults);
                if (_queryResults.Count == 0)
                {
                    RequestSpawn(def, center + offset);
                }
            }
        }

        private void ProcessSpawnQueue()
        {
            int spawned = 0;
            while (_spawnQueue.Count > 0 && spawned < maxSpawnsPerFrame)
            {
                var req = _spawnQueue.Dequeue();
                SpawnMonster(req);
                spawned++;
            }
        }

        private void SpawnMonster(SpawnRequest req)
        {
            if (monsterPrefab == null) return;

            var go = Instantiate(monsterPrefab, req.Position, Quaternion.identity);

            // Initialize FSMMonsterBrain
            var brain = go.GetComponent<FSMMonsterBrain>();
            if (brain != null)
                brain.Initialize(req.Definition);

            _activeMonsters.Add(go);
        }

        private void ProcessDespawns()
        {
            if (_playerTransform == null) return;

            Vector2 playerPos = _playerTransform.position;
            float despawnSq = despawnRadius * despawnRadius;

            for (int i = _activeMonsters.Count - 1; i >= 0; i--)
            {
                var m = _activeMonsters[i];
                if (m == null)
                {
                    _activeMonsters.RemoveAt(i);
                    continue;
                }

                float distSq = ((Vector2)m.transform.position - playerPos).sqrMagnitude;
                if (distSq > despawnSq)
                {
                    Destroy(m);
                    _activeMonsters.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Rebuild spatial hash from active monsters. Call before proximity queries.
        /// </summary>
        public void RebuildSpatialHash()
        {
            _spatialHash.Clear();
            for (int i = _activeMonsters.Count - 1; i >= 0; i--)
            {
                var m = _activeMonsters[i];
                if (m == null)
                {
                    _activeMonsters.RemoveAt(i);
                    continue;
                }
                _spatialHash.Insert(m, m.transform.position);
            }
        }

        /// <summary>
        /// Query nearby monsters using spatial hash.
        /// </summary>
        public void QueryNearby(Vector2 center, float radius, List<(GameObject item, Vector2 pos)> results)
        {
            _spatialHash.QueryRadius(center, radius, results);
        }

        public int ActiveMonsterCount => _activeMonsters.Count;
    }
}
