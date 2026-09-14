using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spawners;

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

        [Header("Monster Catalog")]
        [Tooltip("Catalog of all MonsterDefinition SOs for runtime lookup.")]
        [SerializeField] private MonsterCatalog _monsterCatalog;

        [Header("Spawn Config")]
#pragma warning disable CS0414
        [SerializeField] private float spawnRadius = 15f;
#pragma warning restore CS0414
        [SerializeField] private float despawnRadius = 100f;
#pragma warning disable CS0414
        [SerializeField] private float minSpawnDistance = 8f;
#pragma warning restore CS0414

        private readonly Queue<SpawnRequest> _spawnQueue = new Queue<SpawnRequest>();
        private readonly List<GameObject> _activeMonsters = new List<GameObject>();
        private readonly SpatialHash<GameObject> _spatialHash = new SpatialHash<GameObject>(3f);
        private readonly List<(GameObject item, Vector2 pos)> _queryResults = new List<(GameObject, Vector2)>(16);

        private Transform _playerTransform;
        private Transform _entitiesContainer;

        private Transform GetEntitiesContainer()
        {
            if (_entitiesContainer == null)
                _entitiesContainer = GameObject.Find("[Entities]")?.transform;
            return _entitiesContainer;
        }

        private struct SpawnRequest
        {
            public MonsterDefinition Definition;
            public Vector2 Position;
        }

        /// <summary>
        /// Wire the monster prefab and catalog from code (e.g. GameplaySceneSetup).
        /// </summary>
        public void Initialize(GameObject prefab, MonsterCatalog catalog = null)
        {
            monsterPrefab = prefab;
            if (catalog != null) _monsterCatalog = catalog;
        }

        /// <summary>
        /// Look up a MonsterDefinition by key. Returns null if not found.
        /// </summary>
        public MonsterDefinition GetDefinition(string monsterKey)
        {
            if (_monsterCatalog == null)
            {
                Debug.LogWarning($"[MonsterSpawner] GetDefinition('{monsterKey}'): catalog is null!");
                return null;
            }
            var result = _monsterCatalog.GetByKey(monsterKey);
            if (result == null)
                Debug.LogWarning($"[MonsterSpawner] GetDefinition('{monsterKey}'): not found in catalog.");
            return result;
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
        /// Immediately spawn a single entity and return it.
        /// Used by SpawnerInstance to track active entities.
        /// </summary>
        /// <param name="persistent">
        /// When true, attaches <see cref="PersistentSpawnMarker"/> so <see cref="ProcessDespawns"/>
        /// exempts this entity from the distance-based despawn sweep — wired from
        /// <see cref="Valkur.Data.SpawnerTemplateData.persistent"/> by
        /// <see cref="Valkur.Gameplay.Spawners.SpawnerInstance"/> for every entity a persistent
        /// template spawns (vendors, "defend the spawn point forever" packs). Defaults to false
        /// so every other caller (F5 drag, BossCueDispatcher) is unaffected.
        /// </param>
        /// <param name="resolvedLevel">
        /// The level this particular spawn arrives at, from <see cref="EncounterDifficulty"/>.
        /// 0 (every caller that predates encounter difficulty) means "the definition's own
        /// level", which for every shipped monster is 1 and therefore unscaled.
        /// </param>
        /// <param name="fsmSetOverride">
        /// The FSM set this ENCOUNTER wants, overriding the archetype's and the definition's.
        /// Null/empty (every caller that predates it) resolves exactly as before. See
        /// <see cref="SpawnBrain"/> for why this travels on the object rather than being
        /// written onto the shared <see cref="MonsterDefinition"/>.
        /// </param>
        /// <param name="leashOverride">
        /// How far from home this spawn chases before breaking off, in world units. 0 keeps
        /// the monster's own default. This is the live half of the spawner's old
        /// <c>defendSpawn</c>/<c>defendLeash</c> pair, which had no reader at all.
        /// </param>
        public GameObject SpawnEntity(MonsterDefinition def, Vector2 position, bool persistent = false,
                                      int resolvedLevel = 0, string fsmSetOverride = null,
                                      float leashOverride = 0f)
        {
            if (monsterPrefab == null) return null;

            var go = Instantiate(monsterPrefab, position, Quaternion.identity);
            var container = GetEntitiesContainer();
            if (container != null) go.transform.SetParent(container, true);

            // BEFORE ConfigureMonster, which is the first of the two places that scale stats
            // off it. Stamping afterwards would give the entity a level nothing had read.
            SpawnLevel.Stamp(go, resolvedLevel);

            // Same ordering constraint, and a stricter one: ConfigureMonster is where
            // FSMMonsterBrain builds the machine and fills the context, so a brain stamped
            // after this line is read by nobody and fails in silence.
            SpawnBrain.Stamp(go, fsmSetOverride, leashOverride);

            EntitySetup.ConfigureMonster(go, def);
            if (persistent) go.AddComponent<PersistentSpawnMarker>();

            _activeMonsters.Add(go);
            return go;
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
            var container = GetEntitiesContainer();
            if (container != null) go.transform.SetParent(container, true);
            EntitySetup.ConfigureMonster(go, req.Definition);

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
                if (distSq <= despawnSq) continue;

                // A monster placed for a test — by hand through F5, or spawned from a
                // template the designer marked persistent (every shipped vendor respawn
                // template is) — must not evaporate just because the player walked away
                // from it. See IsExemptFromDespawn.
                if (IsExemptFromDespawn(m)) continue;

                Destroy(m);
                _activeMonsters.RemoveAt(i);
            }
        }

        /// <summary>
        /// Whether an active monster is exempt from the distance-based despawn sweep in
        /// <see cref="ProcessDespawns"/>. Two independent sources, both markers rather than a
        /// shared field because they answer different questions that only happen to overlap
        /// today:
        /// <list type="bullet">
        /// <item><see cref="PersistentSpawnMarker"/> — spawned from a
        /// <see cref="Valkur.Data.SpawnerTemplateData"/> with <c>persistent = true</c>.</item>
        /// <item><see cref="PersistedEntityInstance"/> — placed by hand through the Entities
        /// runtime editor (F5); it is going to be saved to
        /// <c>entities_instances.json</c>, so it must survive being walked away from long
        /// enough to test.</item>
        /// </list>
        /// Internal so EditMode tests can assert the rule directly rather than depending on
        /// <see cref="Object.Destroy"/>'s edit-mode-vs-play-mode behaviour.
        /// </summary>
        internal static bool IsExemptFromDespawn(GameObject go)
        {
            if (go == null) return false;
            return go.GetComponent<PersistentSpawnMarker>() != null
                || go.GetComponent<PersistedEntityInstance>() != null;
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

        /// <summary>
        /// Destroys every monster this spawner produced, PERSISTENT ones included, for a world swap.
        ///
        /// <para>The distance despawn exempts persistent spawns on purpose (a vendor must not vanish
        /// because the player walked away), and nothing else ever removed them — so switching map
        /// slots carried the previous world's six vendors into the new one, measured on the first
        /// Seed World build: twelve vendors, two of each, half of them standing where the lobby used
        /// to be. The spawners that made them are destroyed by the swap; their monsters have to go
        /// with them.</para>
        ///
        /// <para>Two kinds are left alone: allies (they follow the PLAYER, not the map) and entities
        /// placed through the Entities editor, which <c>PlacedEntityService.ClearSpawned</c> owns.</para>
        /// </summary>
        public int DespawnAllForWorldSwap()
        {
            int removed = 0;
            for (int i = _activeMonsters.Count - 1; i >= 0; i--)
            {
                var m = _activeMonsters[i];
                if (m == null) { _activeMonsters.RemoveAt(i); continue; }
                if (m.GetComponent<AlliedUnit>() != null) continue;
                if (m.GetComponent<PersistedEntityInstance>() != null) continue;

                _activeMonsters.RemoveAt(i);
                if (Application.isPlaying) Destroy(m);
                else DestroyImmediate(m);
                removed++;
            }
            _spatialHash.Clear();
            return removed;
        }
    }
}
