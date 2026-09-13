using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The authored half: <c>StreamingAssets/Entities/entities_instances.json</c>, per map slot.
    ///
    /// <para><b>The save writes the TABLE, never the scene.</b> The previous save enumerated live
    /// <see cref="PersistedEntityInstance"/>s, so absence in the scene — a kill, a world torn down
    /// for an interior, a monster still inside Destroy's deferral — was indistinguishable from an
    /// author deleting the placement. A table only an authoring call can shrink cannot observe
    /// absence, which is the same property <c>WorldDamageService</c> relies on.</para>
    /// </summary>
    public partial class PlacedEntityService
    {
        /// <summary>Seconds of quiet after the last authoring edit before the file is written.</summary>
        private const float AUTOSAVE_DEBOUNCE_SECONDS = 0.75f;

        /// <summary>Below this many on-disk records a shrink is ordinary editing.</summary>
        private const int CATASTROPHIC_DROP_FLOOR = 10;

        /// <summary>A save keeping less than this fraction of the file is refused.</summary>
        private const float CATASTROPHIC_DROP_RATIO = 0.5f;

        private IEntityInstanceRepository _repository;
        private bool  _loaded;
        private bool  _dirty;
        private float _saveDueAt;

        /// <summary>Placements the author deleted since the file was last read or written.</summary>
        private int _removalsSinceSync;

        /// <summary>True once the active slot's file has been read into the table.</summary>
        public bool IsLoaded => _loaded;

        /// <summary>True while an authoring edit is waiting for the debounce.</summary>
        public bool IsDirty => _dirty;

        public void SetRepository(IEntityInstanceRepository repository) => _repository = repository;

        /// <summary>
        /// The JSON file in play; an in-memory store outside it. Reading the shipped file from an
        /// EditMode fixture would spawn the shipped world into the test scene, and a fixture that
        /// forgets to inject a store must be harmless rather than one flag away from the real file.
        /// </summary>
        private IEntityInstanceRepository ResolveRepository()
        {
            if (_repository == null)
                _repository = Application.isPlaying
                    ? new JsonFileEntityInstanceRepository()
                    : (IEntityInstanceRepository)new InMemoryEntityInstanceRepository();
            return _repository;
        }

        // ── Load ─────────────────────────────────────────────────────────────────

        public void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        /// <summary>
        /// Read the active slot's file into the table and spawn every placement the run has not
        /// killed. Anything already standing is taken down first, so calling it twice cannot double
        /// the world. Returns how many placements were spawned.
        /// </summary>
        public int Load()
        {
            DespawnAllQuietly();
            _records.Clear();
            _recordsById.Clear();
            _unresolved.Clear();
            _dirty = false;
            _removalsSinceSync = 0;
            _loaded = true;

            string json = ResolveRepository().ReadRawJson(WorldId.Base);
            if (string.IsNullOrEmpty(json))
            {
                RaiseChanged();
                return 0;
            }

            var zm = FindObjectOfType<ZoneManager>();
            var parsed = EntityInstanceSerializer.Deserialize(json, BuildZoneOffsets(zm), ZoneHeight(zm));
            var catalog = ResolveCatalog();
            double now = PlacedEntityRunState.UnixNow();

            int spawned = 0;
            foreach (var record in parsed)
            {
                if (record == null) continue;

                var def = catalog != null ? catalog.GetByKey(record.MonsterKey) : null;
                if (!record.ZoneResolved || def == null || _recordsById.ContainsKey(record.Id))
                {
                    _unresolved.Add(record);
                    continue;
                }

                _records.Add(record);
                _recordsById[record.Id] = record;

                if (_run.IsDefeated(record.Id, now)) continue;
                // A deadline that passed while the world was not loaded (a closed game, another
                // map, an interior) is simply over.
                _run.Forget(record.Id);

                if (SpawnRecord(record, def) != null) spawned++;
            }

            Debug.Log($"[PlacedEntities] Loaded {_records.Count} placement(s): {spawned} standing, " +
                      $"{_records.Count - spawned} defeated or unspawnable, {_unresolved.Count} carried through unresolved.");
            RaiseChanged();
            return spawned;
        }

        /// <summary>
        /// World swap, outgoing half: write what is pending against the OUTGOING slot (the active-slot
        /// pointer has not flipped yet when the Map editor calls this), then take every placement down.
        /// The table is dropped with it, and <see cref="Save"/> refuses until the next load — so a
        /// write landing while the world is empty cannot persist the emptiness.
        /// </summary>
        public void ClearSpawned()
        {
            FlushSave();
            if (_dirty)
            {
                // The flush was refused (an interior transition already under way, or the anti-wipe
                // guard). The table is about to be dropped, so the edit cannot be retried — say so
                // rather than lose it in silence.
                Debug.LogWarning("[PlacedEntities] A pending placement edit could not be written before the " +
                                 "world was torn down and is lost. Re-place it after the transition.");
            }
            DespawnAllQuietly();
            _records.Clear();
            _recordsById.Clear();
            _unresolved.Clear();
            _loaded = false;
            _dirty = false;
            RaiseChanged();
        }

        /// <summary>World swap, incoming half: the active slot's placements.</summary>
        public void Reload() => Load();

        private void DespawnAllQuietly()
        {
            var ids = new List<string>(_live.Keys);
            foreach (var id in ids) DespawnQuietly(id);
            _live.Clear();
        }

        // ── Save ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Write the table. Returns false, writing nothing, while the base world is torn down for an
        /// interior, before anything was loaded, or when the anti-wipe guard trips.
        /// </summary>
        public bool Save()
        {
            if (!_loaded) return false;

            if (WorldTransitionService.RefuseWorldContentWrite("entities")) return false;

            int wouldWrite = _records.Count + _unresolved.Count;
            int onDisk = CountRecordsOnDisk();
            string abort = AbortReason(wouldWrite, onDisk, _removalsSinceSync);
            if (abort != null)
            {
                Debug.LogWarning($"[PlacedEntities] ABORTING entity save — {abort} File NOT written. " +
                                 "If the drop is intentional, delete the placements explicitly.");
                return false;
            }

            var all = new List<EntityInstanceRecord>(wouldWrite);
            all.AddRange(_records);
            all.AddRange(_unresolved);

            ResolveRepository().WriteRawJson(WorldId.Base, EntityInstanceSerializer.Serialize(all));
            _dirty = false;
            _removalsSinceSync = 0;   // the file now holds exactly the table
            return true;
        }

        /// <summary>Write now if an authoring edit is pending.</summary>
        public void FlushSave()
        {
            if (_dirty) Save();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _saveDueAt = Time.unscaledTime + AUTOSAVE_DEBOUNCE_SECONDS;
        }

        private void TickAutosave()
        {
            if (_dirty && Time.unscaledTime >= _saveDueAt) Save();
        }

        /// <summary>
        /// Anti-wipe guard. A shrink the author asked for — <paramref name="explicitRemovals"/>
        /// deletes since the file was last read or written — is an edit and passes, so deleting the
        /// last placement on a map works. A shrink nobody asked for (a load that resolved nothing, a
        /// catalogue missing at boot) is refused, as is emptying a file that could not be parsed.
        /// </summary>
        internal static string AbortReason(int wouldWrite, int onDisk, int explicitRemovals)
        {
            if (onDisk < 0)
                return wouldWrite == 0
                    ? "the table holds 0 placements and the file could not be parsed."
                    : null;

            int accounted = wouldWrite + Mathf.Max(0, explicitRemovals);

            if (onDisk > 0 && accounted == 0)
                return $"the table holds 0 placements but the file holds {onDisk}.";

            if (onDisk >= CATASTROPHIC_DROP_FLOOR && accounted < onDisk * CATASTROPHIC_DROP_RATIO)
                return $"the table would write {wouldWrite} placements ({explicitRemovals} deleted) but the " +
                       $"file holds {onDisk} — too large a drop to be an edit.";

            return null;
        }

        private int CountRecordsOnDisk()
        {
            try
            {
                string json = ResolveRepository().ReadRawJson(WorldId.Base);
                if (string.IsNullOrEmpty(json)) return 0;

                var parsed = MiniJsonRuntime.Deserialize(json);
                if (parsed is List<object> bare) return bare.Count;
                if (parsed is Dictionary<string, object> obj &&
                    obj.TryGetValue("instances", out var inst) && inst is List<object> list)
                    return list.Count;
                return -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // ── Zones ────────────────────────────────────────────────────────────────

        private static EntityInstanceRecord BuildRecord(ZoneManager zm, string id, string monsterKey,
                                                        Vector3 worldPos, float respawnSeconds)
        {
            Vector2 pos = worldPos;
            string zone = ResolveZoneForSave(zm, pos);
            var record = EntityInstanceSerializer.FromWorldPosition(
                id, monsterKey, zone, pos, ResolveZoneOffset(zm, zone), ZoneHeight(zm));
            record.RespawnSeconds = Mathf.Max(0f, respawnSeconds);
            return record;
        }

        private static int ZoneHeight(ZoneManager zm) => zm != null ? zm.ZoneHeightTiles : 50;

        private static Dictionary<string, Vector2> BuildZoneOffsets(ZoneManager zm)
        {
            var result = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            if (zm == null) return result;
            foreach (var zone in zm.GetZonesSnapshot())
                result[zone.zoneName] = zone.gridOffset;
            return result;
        }

        /// <summary>The zone a position sits in; "Lobby" when it is outside every zone, so the record still round-trips.</summary>
        private static string ResolveZoneForSave(ZoneManager zm, Vector2 worldPos)
        {
            if (zm != null && zm.TryGetZoneAtTile(
                    new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y)),
                    out var zone) && !string.IsNullOrEmpty(zone.zoneName))
                return zone.zoneName;
            return "Lobby";
        }

        private static Vector2 ResolveZoneOffset(ZoneManager zm, string zoneName)
        {
            if (zm != null && zm.TryGetZone(zoneName, out var zoneDef))
                return zoneDef.gridOffset;
            return Vector2.zero;
        }
    }
}
