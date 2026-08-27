using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Persists monster placements made through F5 to
    /// <c>StreamingAssets/Entities/entities_instances.json</c>, closing the other half of the
    /// audit's Dimension-3 gap ("no repository, no serializer, everything placed in F5 dies with
    /// the Play session").
    ///
    /// DESIGN DECISION — an F5 placement is its OWN entity-instance record, not a one-shot
    /// spawner record. The audit floated the spawner route as defensible since F3 already
    /// persists monsters, but a spawner instance is a RECIPE (waves, a trigger type, a cooldown,
    /// a max-active cap) for entities that do not exist yet; an F5 placement is an
    /// ALREADY-MATERIALISED entity a designer put down to fight right now. Routing it through
    /// <c>spawners_instances.json</c> would mean inventing dummy wave/trigger data every save,
    /// and would entangle this editor's writes with Spawners' own anti-wipe guard, autosave
    /// debounce and per-slot file — a save bug in one editor could then corrupt the other's data.
    /// Every other placement editor (Buildings, Lights, Particles, Spawners) already owns its own
    /// instance file for exactly this isolation reason; this repo follows that same shape rather
    /// than the one exception the audit merely floated as "defensible", not "recommended".
    ///
    /// Round trip: <see cref="LoadPlacedEntities"/> runs once, deferred from <c>Start()</c> to
    /// the first <c>Update()</c> tick so every other object's own <c>Start()</c> — including
    /// whatever populates <c>ZoneManager</c>'s zone list — has already run (see the comment on
    /// <c>EntitiesRuntimeEditor.Start()</c>). Independent of whether F5 is ever opened, since
    /// <see cref="EntitiesRuntimeEditor"/> already exists as a scene-wide singleton regardless —
    /// and spawns through the exact same
    /// <see cref="SpawnMonsterAt"/> path interactive placement uses, tagging the result with
    /// <see cref="PersistedEntityInstance"/>. <see cref="SavePlacedEntities"/> enumerates every
    /// live instance of that marker via <c>FindObjectsOfType</c> (the same "the scene is the
    /// source of truth" pattern <c>SpawnerInstance</c> / <c>PersistedParticleInstance</c> use)
    /// plus any records the loader could not resolve, and writes both back through
    /// <see cref="EntityInstanceSerializer"/>. One place (that serializer, via
    /// <c>SpawnerTileMapping</c>) owns the zone/tile transform for both directions.
    ///
    /// KNOWN LIMITATION, documented rather than silently absent: unlike Buildings / Spawners /
    /// Lights / Particles, this file does not hook into
    /// <c>MapEditorManager.ClearAllSpawnedWorldContent</c> / <c>ReloadAllWorldContent</c> — that
    /// class lives under <c>Gameplay/Editors/Map/</c>, outside the folders this change is scoped
    /// to. In practice this is not a regression: verified by reading
    /// <c>MapEditorManager.MapSlots.cs</c>, those two methods already do not touch monsters at
    /// all (only Buildings/Spawners/Lights/Particles/ItemDrops) — a placed monster's lifecycle
    /// across a map-slot switch or an interior transition is a pre-existing gap for every
    /// monster, spawner-placed or not, not something this change introduces or was asked to
    /// close. What IS wired is the guard that gap exists next to:
    /// <see cref="WorldTransitionService.RefuseWorldContentWrite"/> is a plain static read, so
    /// this save path benefits from it without needing MapEditorManager to know this editor
    /// exists.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // Repository handle. Tests inject InMemoryEntityInstanceRepository via
        // SetEntityInstanceRepository(); production falls back to the JSON file backend on
        // first use, mirroring SpawnerInstanceLoader / ParticlesRuntimeEditor.
        private IEntityInstanceRepository _entityRepository;

        public void SetEntityInstanceRepository(IEntityInstanceRepository repository)
            => _entityRepository = repository;

        private IEntityInstanceRepository ResolveEntityRepository()
            => _entityRepository ?? (_entityRepository = new JsonFileEntityInstanceRepository());

        /// <summary>
        /// Records the last load could not spawn — an unknown monster key, or a zone that no
        /// longer resolves. Carried through unchanged on the next save so a catalog or zone that
        /// is temporarily missing an entry can never delete the placements that use it. Mirrors
        /// <c>ParticleInstanceSerializer</c>'s "records the loader could not spawn pass through
        /// unchanged" contract.
        /// </summary>
        private readonly List<EntityInstanceRecord> _unresolvedEntityRecords = new List<EntityInstanceRecord>();

        private bool  _entityPlacementsDirty;
        private float _entityPlacementsSaveDueAt;

        /// <summary>
        /// Seconds of quiet after the last placement/deletion before the map is written.
        /// Mirrors <c>SpawnerEditorManager.AUTOSAVE_DEBOUNCE_SECONDS</c> — placing a run of
        /// monsters fires one edit per click, and a delete goes through <c>Destroy</c>, which in
        /// Play Mode defers to end-of-frame, so a save that ran immediately after Delete would
        /// still find the doomed GameObject via <c>FindObjectsOfType</c> and write it straight
        /// back.
        /// </summary>
        private const float ENTITY_AUTOSAVE_DEBOUNCE_SECONDS = 0.75f;

        /// <summary>Below this many on-disk records a shrink is not treated as catastrophic —
        /// deleting two of three placements is ordinary editing.</summary>
        private const int ENTITY_CATASTROPHIC_DROP_FLOOR = 10;

        /// <summary>A save keeping less than this fraction of what the file holds is refused.</summary>
        private const float ENTITY_CATASTROPHIC_DROP_RATIO = 0.5f;

        // ── Load ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns every placement recorded in the active map slot's
        /// <c>entities_instances.json</c>, called once from the first <c>Update()</c> tick after
        /// entering Play Mode. A record whose
        /// monster key is not in <see cref="_monsterCatalog"/>, or whose zone does not resolve
        /// against the live <see cref="ZoneManager"/>, is kept in
        /// <see cref="_unresolvedEntityRecords"/> instead of being dropped.
        /// </summary>
        /// <summary>
        /// Flush pending placements, then destroy every entity this editor placed.
        ///
        /// Called by the Map editor's world swap. Until this existed, F5 placements were the
        /// one kind of world content <c>ClearAllSpawnedWorldContent</c> did not take down —
        /// buildings, spawner instances, lights and particles all had a clear, monsters did
        /// not — so a placement from map A floated over map B, exactly the failure CLAUDE.md
        /// records for `WorldGridBuilder.ClearWorld` ("a world swap is not a tile repaint").
        ///
        /// The save happens FIRST and against the OUTGOING slot, because the active-slot
        /// pointer has not flipped yet when the swap calls this — the same ordering the
        /// lighting hand-off already documents.
        /// </summary>
        public void ClearPlacedEntities()
        {
            FlushEntityPlacementAutosave();

            var placed = FindObjectsOfType<PersistedEntityInstance>();
            for (int i = 0; i < placed.Length; i++)
            {
                if (placed[i] == null) continue;
                var go = placed[i].gameObject;
                if (Application.isPlaying) Destroy(go);
                else                       DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Re-spawn this slot's placements. Resolves the file through the ACTIVE slot at
        /// call time, so calling it after the pointer has flipped loads the incoming map's
        /// entities and never the outgoing map's.
        /// </summary>
        public void ReloadPlacedEntities() => LoadPlacedEntities();

        private void LoadPlacedEntities()
        {
            _unresolvedEntityRecords.Clear();

            string json = ResolveEntityRepository().ReadRawJson(WorldId.Base);
            if (string.IsNullOrEmpty(json)) return;

            var zm = FindObjectOfType<ZoneManager>();
            var zoneOffsets = BuildZoneOffsets(zm);
            int zoneHeightTiles = zm != null ? zm.ZoneHeightTiles : 50;

            var records = EntityInstanceSerializer.Deserialize(json, zoneOffsets, zoneHeightTiles);

            int spawned = 0;
            foreach (var r in records)
            {
                bool catalogHasKey = _monsterCatalog != null && _monsterCatalog.GetByKey(r.MonsterKey) != null;
                if (!r.ZoneResolved || !catalogHasKey)
                {
                    _unresolvedEntityRecords.Add(r);
                    continue;
                }

                SpawnMonsterAt(r.MonsterKey, new Vector3(r.WorldPos.x, r.WorldPos.y, 0f),
                               existingPlacementId: r.Id, markDirty: false);
                spawned++;
            }

            if (spawned > 0 || _unresolvedEntityRecords.Count > 0)
            {
                Debug.Log($"[EntitiesEditor] Loaded {spawned}/{records.Count} placed entities " +
                          $"({_unresolvedEntityRecords.Count} carried through unresolved).");
            }
        }

        // ── Save ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes every live <see cref="PersistedEntityInstance"/> plus any carried-through
        /// unresolved records to the active map slot's instances file. Returns false — and
        /// writes nothing — when the guard below trips, so an automatic autosave cannot turn a
        /// bad session into data loss on disk.
        /// </summary>
        internal bool SavePlacedEntities()
        {
            // Refused while the base world is torn down for an interior transition: the scene
            // legitimately holds zero placed entities in that window (nothing here destroys
            // them today — see the class doc's KNOWN LIMITATION — but a future wipe hook, or a
            // stray autosave racing the swap, must not persist that emptiness over the authored
            // world). RefuseWorldContentWrite is a plain static read, so this benefits from the
            // guard without MapEditorManager needing to know this editor exists.
            if (WorldTransitionService.RefuseWorldContentWrite("entities"))
            {
                SetStatus("Entity save skipped — inside an interior.");
                return false;
            }

            _entityPlacementsDirty = false;

            var live = new List<PersistedEntityInstance>(FindObjectsOfType<PersistedEntityInstance>());

            var zm = FindObjectOfType<ZoneManager>();
            int zoneHeightTiles = zm != null ? zm.ZoneHeightTiles : 50;

            int wouldWrite = live.Count + _unresolvedEntityRecords.Count;
            int onDisk = CountRecordsOnDisk();
            string abortReason = AbortReason(wouldWrite, onDisk);
            if (abortReason != null)
            {
                Debug.LogWarning($"[EntitiesEditor] ABORTING entity save — {abortReason} File NOT " +
                                 "written. If the drop is intentional, delete the placements explicitly.");
                SetStatus("Entity save ABORTED — see console.");
                return false;
            }

            var records = new List<EntityInstanceRecord>(wouldWrite);
            foreach (var marker in live)
            {
                if (marker == null) continue;
                Vector2 pos    = marker.transform.position;
                string  zone   = ResolveZoneForSave(zm, pos);
                Vector2 offset = ResolveZoneOffset(zm, zone);
                records.Add(EntityInstanceSerializer.FromWorldPosition(
                    marker.PlacementId, marker.MonsterKey, zone, pos, offset, zoneHeightTiles));
            }
            records.AddRange(_unresolvedEntityRecords);

            string json = EntityInstanceSerializer.Serialize(records);
            ResolveEntityRepository().WriteRawJson(WorldId.Base, json);

            SetStatus($"Saved {records.Count} placed entities.");
            Debug.Log($"[EntitiesEditor] Saved {records.Count} placed entities " +
                      $"({live.Count} live, {_unresolvedEntityRecords.Count} carried through).");
            return true;
        }

        /// <summary>
        /// Anti-wipe guard, same shape as <c>ParticlesRuntimeEditor.SaveInstancesToJson</c>:
        /// refuses an empty write over a populated file, and refuses a save that keeps less than
        /// half of a file holding at least <see cref="ENTITY_CATASTROPHIC_DROP_FLOOR"/> records.
        /// A load failure (no catalog, no ZoneManager, a parse error) would otherwise leave zero
        /// live markers, and the very next autosave would erase every placement ever authored.
        /// </summary>
        private static string AbortReason(int wouldWrite, int onDisk)
        {
            if (wouldWrite == 0 && onDisk != 0)
                return onDisk < 0
                    ? "scene holds 0 placed entities and the file could not be parsed."
                    : $"scene holds 0 placed entities but the file holds {onDisk}.";

            if (onDisk >= ENTITY_CATASTROPHIC_DROP_FLOOR && wouldWrite < onDisk * ENTITY_CATASTROPHIC_DROP_RATIO)
                return $"scene would write {wouldWrite} placed entities but the file holds " +
                       $"{onDisk} — too large a drop to be an edit.";

            return null;
        }

        /// <summary>How many records the on-disk file currently holds, or -1 when it cannot be
        /// parsed. Counted off the raw JSON shape rather than through
        /// <see cref="EntityInstanceSerializer.Deserialize"/>, which needs zone offsets and would
        /// under-report exactly the situation this guard exists to catch.</summary>
        private int CountRecordsOnDisk()
        {
            try
            {
                string json = ResolveEntityRepository().ReadRawJson(WorldId.Base);
                if (string.IsNullOrEmpty(json)) return 0;

                var parsed = MiniJsonRuntime.Deserialize(json);
                if (parsed is List<object> bare) return bare.Count;
                if (parsed is Dictionary<string, object> obj &&
                    obj.TryGetValue("instances", out var inst) && inst is List<object> list)
                    return list.Count;
                return -1;
            }
            catch
            {
                return -1;
            }
        }

        // ── Autosave ─────────────────────────────────────────────────────────────

        /// <summary>Records that a placement changed. Called by every mutation (place, delete)
        /// rather than saving directly, so a new interaction path cannot forget to persist —
        /// exactly the gap that used to leave the spawner editor with no automatic save at all.</summary>
        private void MarkEntityPlacementsDirty()
        {
            _entityPlacementsDirty = true;
            _entityPlacementsSaveDueAt = Time.unscaledTime + ENTITY_AUTOSAVE_DEBOUNCE_SECONDS;
        }

        /// <summary>
        /// Ticked from <c>Update()</c> unconditionally (not only while F5 is open) — a
        /// placement must survive the author closing the editor and walking away, not only a
        /// Save click while the panel happens to be visible.
        /// </summary>
        private void TickEntityPlacementAutosave()
        {
            if (!_entityPlacementsDirty) return;
            if (Time.unscaledTime < _entityPlacementsSaveDueAt) return;
            SavePlacedEntities();
        }

        /// <summary>Writes immediately if a placement/deletion is still pending. Called on
        /// Deactivate and OnDestroy so closing F5 — or stopping Play Mode without closing it
        /// first — cannot lose the last few seconds of edits to the debounce window.</summary>
        private void FlushEntityPlacementAutosave()
        {
            if (_entityPlacementsDirty) SavePlacedEntities();
        }

        // ── Zone helpers ─────────────────────────────────────────────────────────

        /// <summary>Every registered zone's name → grid offset, built once per save/load call.
        /// Mirrors <c>ParticleInstanceSerializer.BuildZoneOffsets</c>.</summary>
        private static Dictionary<string, Vector2> BuildZoneOffsets(ZoneManager zm)
        {
            var result = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            if (zm == null) return result;
            foreach (var zone in zm.GetZonesSnapshot())
                result[zone.zoneName] = zone.gridOffset;
            return result;
        }

        /// <summary>Resolves the zone a live position currently sits in, matching
        /// <c>SpawnerEditorManager.ResolveZone</c>'s fallback (a position outside every
        /// registered zone still round-trips against "Lobby" rather than being dropped).</summary>
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
