using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The single owner of hand-placed entities: what the author put on the map, what is standing
    /// there now, and what this run has killed.
    ///
    /// <para><b>Three records, three owners, and they used to be one.</b> The Entities editor loaded
    /// the placement file, spawned it, and SAVED IT BY SCANNING THE SCENE for live placements. That
    /// made the scene the source of truth for authored data, which is wrong in three measurable ways:
    /// a killed monster reached the file only if some unrelated edit later triggered a write (and
    /// then it deleted the placement forever); a monster that had WALKED was saved at wherever it
    /// wandered to; and none of it ran in a build without the editors, because the loader lived in
    /// an authoring tool. Now:</para>
    /// <list type="bullet">
    /// <item><b>Authored</b> — <see cref="_records"/>, written to <c>entities_instances.json</c>. Only
    /// explicit authoring (place, move, delete, respawn time) changes it. Nothing a player does can.</item>
    /// <item><b>Live</b> — <see cref="_live"/>, the GameObjects currently standing. Derived; never saved.</item>
    /// <item><b>Run</b> — <see cref="PlacedEntityRunState"/>, which placements this playthrough has
    /// killed and when each returns. Rides the save's metadata bag.</item>
    /// </list>
    ///
    /// <para>Not an editor, and deliberately not under <c>Editors/</c>: it is created by the boot
    /// sequence in every build. The Entities editor is a client of it.</para>
    /// </summary>
    public partial class PlacedEntityService : MonoBehaviour
    {
        private static PlacedEntityService _instance;

        /// <summary>The live service, or null.</summary>
        public static PlacedEntityService Instance => _instance;
        public static bool HasInstance => _instance != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        /// <summary>
        /// The service, created on demand. The boot sequence creates it in play; this exists for the
        /// editor opened in a scene without that boot, and for EditMode fixtures, where Unity sends
        /// no Awake and the instance has to be claimed explicitly.
        /// </summary>
        public static PlacedEntityService GetOrCreate()
        {
            if (_instance != null) return _instance;

            var existing = FindObjectOfType<PlacedEntityService>();
            if (existing != null)
            {
                existing.Claim();
                return existing;
            }

            var go = new GameObject("PlacedEntityService");
            var service = go.AddComponent<PlacedEntityService>();
            service.Claim();
            return service;
        }

        private MonsterCatalog _catalog;

        /// <summary>Authored placements in file order, and the same records by id.</summary>
        private readonly List<EntityInstanceRecord> _records = new List<EntityInstanceRecord>();
        private readonly Dictionary<string, EntityInstanceRecord> _recordsById =
            new Dictionary<string, EntityInstanceRecord>(StringComparer.Ordinal);

        /// <summary>
        /// Records the last load could not resolve (unknown monster key, or a zone that no longer
        /// exists). Written back verbatim so a temporarily missing catalogue entry never deletes
        /// the placements that use it.
        /// </summary>
        private readonly List<EntityInstanceRecord> _unresolved = new List<EntityInstanceRecord>();

        private readonly Dictionary<string, PersistedEntityInstance> _live =
            new Dictionary<string, PersistedEntityInstance>(StringComparer.Ordinal);

        private readonly PlacedEntityRunState _run = new PlacedEntityRunState();

        /// <summary>
        /// Test seam: replaces the production spawn (MonsterSpawner / prefab), which needs a full
        /// entity rig no EditMode fixture has.
        /// </summary>
        internal Func<MonsterDefinition, Vector3, GameObject> SpawnOverride;

        /// <summary>Raised after anything a probe or the editor displays has changed.</summary>
        public event Action Changed;

        /// <summary>Raised when a placement is killed, with its id.</summary>
        public event Action<string> PlacementDefeated;

        public IReadOnlyList<EntityInstanceRecord> Records => _records;
        public IReadOnlyList<EntityInstanceRecord> UnresolvedRecords => _unresolved;
        public PlacedEntityRunState RunState => _run;

        private void Awake() => Claim();

        private void Claim()
        {
            if (_instance != null && _instance != this)
            {
                // Destroy is an error in Edit Mode, so the loser is left inert rather than removed.
                Debug.LogWarning($"[PlacedEntities] Duplicate service on '{name}' ignored.");
                if (Application.isPlaying) Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            // Stopping Play Mode must not lose an authoring edit still inside the debounce window.
            FlushSave();
            if (_instance == this) _instance = null;
        }

        /// <summary>Hand the service its catalogue. A null argument keeps what it has.</summary>
        public void SetMonsterCatalog(MonsterCatalog catalog)
        {
            if (catalog != null) _catalog = catalog;
        }

        private MonsterCatalog ResolveCatalog()
        {
            if (_catalog == null && ServiceLocator.TryGet<MonsterCatalog>(out var located))
                _catalog = located;
            return _catalog;
        }

        private void Update()
        {
            TickRespawns();
            TickAutosave();
        }

        // ── Queries ──────────────────────────────────────────────────────────────

        public bool TryGetRecord(string placementId, out EntityInstanceRecord record)
        {
            record = null;
            return !string.IsNullOrEmpty(placementId) &&
                   _recordsById.TryGetValue(placementId, out record);
        }

        /// <summary>The standing GameObject for a placement, or null when it is dead, unspawned or unknown.</summary>
        public PersistedEntityInstance LiveInstance(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return null;
            if (!_live.TryGetValue(placementId, out var marker)) return null;
            if (marker == null || !marker.gameObject.activeSelf)
            {
                _live.Remove(placementId);
                return null;
            }
            return marker;
        }

        /// <summary>What a placement is doing right now, in the terms an author reads.</summary>
        public PlacedEntityStatus StatusOf(string placementId, out double respawnAtUnix)
        {
            respawnAtUnix = 0d;
            if (!TryGetRecord(placementId, out _)) return PlacedEntityStatus.Unknown;
            if (_run.TryGetRespawnAt(placementId, out respawnAtUnix))
                return respawnAtUnix > 0d ? PlacedEntityStatus.Respawning : PlacedEntityStatus.Defeated;
            return LiveInstance(placementId) != null ? PlacedEntityStatus.Alive : PlacedEntityStatus.NotSpawned;
        }

        // ── Authoring ────────────────────────────────────────────────────────────

        /// <summary>
        /// Put a new placement on the map (or restore one by id, for undo). Authored: it is written to
        /// the file. Returns the standing instance, or null when the monster key does not resolve.
        /// </summary>
        public PersistedEntityInstance Place(string monsterKey, Vector3 worldPos,
                                             string existingPlacementId = null, float respawnSeconds = 0f)
        {
            if (string.IsNullOrEmpty(monsterKey)) return null;
            EnsureLoaded();

            // GetByKey throws on a null key, so every lookup here goes through a non-empty string.
            var def = ResolveCatalog()?.GetByKey(monsterKey);
            if (def == null) return null;

            string id = string.IsNullOrEmpty(existingPlacementId)
                ? Guid.NewGuid().ToString("N")
                : existingPlacementId;

            var zm = FindObjectOfType<ZoneManager>();
            var record = BuildRecord(zm, id, monsterKey, worldPos, respawnSeconds);

            if (_recordsById.TryGetValue(id, out var previous)) _records.Remove(previous);
            _records.Add(record);
            _recordsById[id] = record;

            // A restore is a statement that the placement exists and stands — any kill the run
            // held against that id belongs to the version the author just undid.
            _run.Forget(id);

            var marker = SpawnRecord(record, def);
            MarkDirty();
            RaiseChanged();
            return marker;
        }

        /// <summary>Take a placement off the map and out of the file. Also forgets any kill against it.</summary>
        public bool Remove(string placementId)
        {
            if (!TryGetRecord(placementId, out var record)) return false;

            _records.Remove(record);
            _recordsById.Remove(placementId);
            _run.Forget(placementId);
            DespawnQuietly(placementId);
            _removalsSinceSync++;

            MarkDirty();
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Record where the author put a placement. The caller moves the GameObject; this moves the
        /// authored record, so a monster that later walks away is still saved where it was put.
        /// </summary>
        public bool SetAuthoredPosition(string placementId, Vector3 worldPos)
        {
            if (!TryGetRecord(placementId, out var record)) return false;

            var zm = FindObjectOfType<ZoneManager>();
            var moved = BuildRecord(zm, record.Id, record.MonsterKey, worldPos, record.RespawnSeconds);
            CopyInto(moved, record);

            MarkDirty();
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// How long this placement stays dead once killed; 0 keeps it dead for the run. A kill already
        /// on the clock keeps its deadline — retuning a respawn time is not a way to revive something.
        /// </summary>
        public bool SetRespawnSeconds(string placementId, float seconds)
        {
            if (!TryGetRecord(placementId, out var record)) return false;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;

            seconds = Mathf.Max(0f, seconds);
            if (Mathf.Approximately(record.RespawnSeconds, seconds)) return true;

            record.RespawnSeconds = seconds;
            MarkDirty();
            RaiseChanged();
            return true;
        }

        // ── Spawning ─────────────────────────────────────────────────────────────

        private PersistedEntityInstance SpawnRecord(EntityInstanceRecord record, MonsterDefinition def)
        {
            var existing = LiveInstance(record.Id);
            if (existing != null) return existing;

            var pos = new Vector3(record.WorldPos.x, record.WorldPos.y, 0f);
            GameObject go = SpawnOverride != null ? SpawnOverride(def, pos) : SpawnProduction(def, pos);
            if (go == null) return null;

            var marker = go.GetComponent<PersistedEntityInstance>() ?? go.AddComponent<PersistedEntityInstance>();
            marker.Initialize(record.Id, record.MonsterKey);

            var health = go.GetComponent<Health>();
            if (health != null)
            {
                string id = record.Id;
                health.OnDeath += () => HandleDeath(id, marker);
            }

            _live[record.Id] = marker;
            return marker;
        }

        private static GameObject SpawnProduction(MonsterDefinition def, Vector3 pos)
        {
            // MonsterSpawner first, so the entity is tracked with every other monster.
            var spawner = FindObjectOfType<MonsterSpawner>();
            if (spawner != null) return spawner.SpawnEntity(def, pos);

            var setup = FindObjectOfType<GameplaySceneSetup>();
            var prefab = setup != null ? setup.MonsterPrefab : null;
            if (prefab == null)
            {
                Debug.LogWarning($"[PlacedEntities] Cannot spawn '{def.monsterKey}': no MonsterSpawner and no monsterPrefab.");
                return null;
            }

            var go = Instantiate(prefab, pos, Quaternion.identity);
            var container = GameObject.Find("[Entities]")?.transform;
            if (container != null) go.transform.SetParent(container, true);
            EntitySetup.ConfigureMonster(go, def);
            return go;
        }

        /// <summary>
        /// Take a placement's GameObject away WITHOUT it counting as a kill: a delete, a world swap,
        /// or a loaded save that says it was already dead. Deactivated first, because Destroy is
        /// deferred to end of frame and a same-frame restore must not find the doomed object.
        /// </summary>
        private void DespawnQuietly(string placementId)
        {
            if (!_live.TryGetValue(placementId, out var marker)) return;
            _live.Remove(placementId);
            if (marker == null) return;

            var go = marker.gameObject;
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go);
            else                       DestroyImmediate(go);
        }

        /// <summary>
        /// A placement was killed. Its corpse stays for the FSM's despawn window, but from this frame
        /// it is no longer the placement: the run owns the fact, and the file never hears about it.
        /// </summary>
        private void HandleDeath(string placementId, PersistedEntityInstance marker)
        {
            // Only the instance currently standing for that id counts. A corpse from an earlier
            // life, or an instance of a placement the author has since deleted, is not a kill.
            if (marker == null || !TryGetRecord(placementId, out var record)) return;
            if (!_live.TryGetValue(placementId, out var current) || current != marker) return;

            _live.Remove(placementId);
            marker.MarkDefeated();
            _run.MarkDefeated(placementId, record.RespawnSeconds, PlacedEntityRunState.UnixNow());

            // The kill is run state, so the save has to hear about it even if nothing else changes.
            if (SaveService.HasInstance) SaveService.Instance.MarkDirty("placed entity defeated");

            PlacementDefeated?.Invoke(placementId);
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke();

        private static void CopyInto(EntityInstanceRecord from, EntityInstanceRecord to)
        {
            to.Zone           = from.Zone;
            to.TileCol        = from.TileCol;
            to.TileRow        = from.TileRow;
            to.WorldPos       = from.WorldPos;
            to.ZoneResolved   = from.ZoneResolved;
            to.RespawnSeconds = from.RespawnSeconds;
        }
    }

    /// <summary>What a placement is doing, as the editor and the probe report it.</summary>
    public enum PlacedEntityStatus
    {
        Unknown,
        Alive,
        /// <summary>Authored and not defeated, but nothing is standing (the spawn failed).</summary>
        NotSpawned,
        /// <summary>Killed this run and stays dead.</summary>
        Defeated,
        /// <summary>Killed this run, returns at a deadline.</summary>
        Respawning,
    }
}
