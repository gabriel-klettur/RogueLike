using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Disk persistence (save / load zone data) for <see cref="MapEditorManager"/>.
    /// </summary>
    public partial class MapEditorManager
    {
        // Repository handle. Tests inject an InMemoryMapEditorZonesRepository
        // through SetZonesRepository(); production paths fall back to the
        // JSON-file backend on first use, preserving the legacy
        // persistentDataPath/map_editor_zones.json layout the
        // MapEditorDataGuard relies on for recovery.
        private IMapEditorZonesRepository _zonesRepository;

        // Phase 1 per-world routing. SetWorld() lets a future bootstrap
        // pipeline scope this MapEditorManager instance to a specific
        // dimension. Defaults to WorldId.Base so the legacy single-world
        // boot path continues to read/write
        // persistentDataPath/map_editor_zones.json byte-for-byte.
        private WorldId _persistenceWorldId = WorldId.Base;

        public WorldId PersistenceWorldId => _persistenceWorldId;

        public void SetZonesRepository(IMapEditorZonesRepository repository)
            => _zonesRepository = repository;

        public void SetPersistenceWorld(WorldId worldId)
            => _persistenceWorldId = worldId;

        private IMapEditorZonesRepository ResolveZonesRepository()
            => _zonesRepository ?? (_zonesRepository = new JsonFileMapEditorZonesRepository());

        private void PersistZonesToDisk()
        {
            if (zoneManager == null) return;

            var data = new ZonePersistenceFile
            {
                restrictTileEditingToEditableZones = _state.RestrictTileEditingToEditableZones,
                nextZoneIndex = _state.NextZoneIndex
            };

            // Capture the player's current world position so the active slot
            // file always remembers where the player was last seen on this map.
            // Reading back happens in LoadMapSlot via ApplySlotToZoneManager →
            // TeleportPlayerToWorldPosition. Captured every persist (zone op,
            // slot save, slot load completion) so the value is never stale.
            var playerT = Valkur.Core.EntityRegistry.PlayerTransform;
            if (playerT != null)
            {
                var p = playerT.position;
                data.hasLastPlayerPosition = true;
                data.lastPlayerWorldX = p.x;
                data.lastPlayerWorldY = p.y;
            }

            var liveZones = zoneManager.GetZonesSnapshot();
            var liveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var liveOffsets = new HashSet<Vector2Int>();
            for (int i = 0; i < liveZones.Length; i++)
            {
                if (string.IsNullOrEmpty(liveZones[i].zoneName)) continue;
                liveNames.Add(liveZones[i].zoneName);
                liveOffsets.Add(liveZones[i].gridOffset);
                data.zones.Add(new ZonePersistenceEntry
                {
                    zoneName             = liveZones[i].zoneName,
                    gridOffsetX          = liveZones[i].gridOffset.x,
                    gridOffsetY          = liveZones[i].gridOffset.y,
                    editableInTileEditor = liveZones[i].editableInTileEditor
                });
            }

            // Preserve "shelved" entries: zones present in the on-disk file
            // but not in the live ZoneManager because their offset collides
            // with a database zone (a non-explicit eviction). If the next
            // database load releases that offset, LoadZonesFromDisk can
            // restore them. Without this merge, every PersistZonesToDisk call
            // would silently delete shelved zones the moment the user makes
            // any unrelated edit.
            int shelvedPreserved = MergeShelvedZonesFromDisk(data, liveNames, liveOffsets);

            // Mirror the in-memory portals list (managed by Portals partial)
            // into the document so it travels with the slot file.
            WritePortalsIntoPersistence(data);
            // Same for biome-generated buildings — clone-on-write so future
            // edits to the in-memory list don't mutate the just-serialised
            // snapshot through reference sharing.
            data.biomeBuildings = new List<BiomeBuildingPersistenceEntry>(_biomeBuildings);

            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                ResolveZonesRepository().WriteAtomic(_persistenceWorldId, json);
                // Auto-save mirror: the slot file for the currently-active map
                // tracks the working copy on every persist, so zone Add /
                // Delete / Rename / ToggleEditable / Biome generation are all
                // saved instantly with no explicit "Save As" step required.
                MirrorWorkingCopyToActiveSlot(json);
                if (shelvedPreserved > 0)
                    Debug.Log($"[MapEditor] Persisted {data.zones.Count} zone(s) " +
                              $"({shelvedPreserved} shelved preserved) via repository.");
                else
                    Debug.Log($"[MapEditor] Persisted {data.zones.Count} zone(s) via repository.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to persist zones via repository: {ex.Message}");
            }
        }

        // Mirrors the just-written working-copy JSON to the slot file of the
        // currently-active map. Silent no-op when no active slot is set yet
        // (e.g. very early boot, before the slot store is constructed).
        private void MirrorWorkingCopyToActiveSlot(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var store = ResolveSlotStore();
            if (store == null) return;
            string active = store.ActiveSlot;
            if (string.IsNullOrEmpty(active)) return;
            store.WriteSlot(active, json);
        }

        // Reads the current persistence file (if any) and appends to `data`
        // every entry that meets ALL of:
        //   1. Has a non-empty zone name not already in `liveNames`.
        //   2. Has a grid offset that collides with a live zone (so it was
        //      almost certainly shelved, not explicitly deleted).
        // Returns the count of shelved entries that survived this round.
        private int MergeShelvedZonesFromDisk(ZonePersistenceFile data,
                                              HashSet<string> liveNames,
                                              HashSet<Vector2Int> liveOffsets)
        {
            string source = TryReadPersistenceFile(out var existing);
            if (source == null || existing == null || existing.zones == null) return 0;

            int preserved = 0;
            for (int i = 0; i < existing.zones.Count; i++)
            {
                var entry = existing.zones[i];
                if (string.IsNullOrWhiteSpace(entry.zoneName)) continue;
                if (liveNames.Contains(entry.zoneName)) continue;
                var off = new Vector2Int(entry.gridOffsetX, entry.gridOffsetY);
                if (!liveOffsets.Contains(off)) continue; // not shelved → user-deleted
                data.zones.Add(entry);
                preserved++;
            }
            return preserved;
        }

        // Atomic-write + sidecar-fallback semantics now live in the
        // IMapEditorZonesRepository contract; this method just adapts the
        // raw JSON the repo returns into a parsed + migrated DTO. Returns
        // a non-null source tag when read succeeded, null otherwise.
        private string TryReadPersistenceFile(out ZonePersistenceFile data)
        {
            data = null;
            string json = ResolveZonesRepository().ReadWithSidecarFallback(_persistenceWorldId, out bool fromSidecar);
            if (json == null) return null;
            try
            {
                var parsed = JsonUtility.FromJson<ZonePersistenceFile>(json);
                if (parsed == null)
                {
                    Debug.LogWarning($"[MapEditor] persistence parsed as null — head: " +
                                     $"{(json.Length > 200 ? json.Substring(0, 200) : json)}");
                    return null;
                }
                MapZonesMigrations.Migrate(parsed);
                data = parsed;
                if (fromSidecar)
                    Debug.LogWarning($"[MapEditor] Primary persistence missing/corrupt — recovered zones from sidecar.");
                return fromSidecar ? "sidecar" : "primary";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor] Failed to deserialize persistence: {ex.Message}");
                return null;
            }
        }

        private void LoadZonesFromDisk()
        {
            if (zoneManager == null) { Debug.LogWarning("[MapEditor] LoadZonesFromDisk skipped — zoneManager is null."); return; }

            string source = TryReadPersistenceFile(out var data);
            if (source == null)
            {
                Debug.Log($"[MapEditor] No persisted zones file at '{PersistencePath}' (or sidecar) — first run / file deleted.");
                return;
            }

            try
            {
                if (data.zones == null || data.zones.Count == 0)
                {
                    Debug.Log($"[MapEditor] Persistence file '{source}' has no zones to restore.");
                    return;
                }
                Debug.Log($"[MapEditor] Reading {data.zones.Count} persisted zone(s) from '{source}'.");

                // Existing zones come from ZoneDatabaseLoader (the source of truth, with
                // correct Y-flipped offsets). Treat them as authoritative — don't override
                // their offsets with potentially-stale persisted values. Only:
                //   1. Restore "editableInTileEditor" flags for zones that already exist.
                //   2. Add brand-new zones that the user created and that are NOT in the DB.
                //   3. Drop persisted entries with duplicate names or offsets.
                var existingZones = zoneManager.GetZonesSnapshot();
                var dbByName = new Dictionary<string, ZoneManager.ZoneDefinition>(StringComparer.OrdinalIgnoreCase);
                var dbOffsets = new HashSet<Vector2Int>();
                for (int i = 0; i < existingZones.Length; i++)
                {
                    dbByName[existingZones[i].zoneName] = existingZones[i];
                    dbOffsets.Add(existingZones[i].gridOffset);
                }

                int intraFileDuplicates = 0;  // same name appearing twice in the file
                int userZonesShelved    = 0;  // user zones that didn't fit (offset clash with DB)
                int newZonesAdded       = 0;
                int flagsRestored       = 0;
                int addZoneFailures     = 0;
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenOffsets = new HashSet<Vector2Int>(dbOffsets);

                for (int i = 0; i < data.zones.Count; i++)
                {
                    var entry = data.zones[i];
                    if (string.IsNullOrWhiteSpace(entry.zoneName))
                    {
                        intraFileDuplicates++;
                        continue;
                    }

                    if (!seenNames.Add(entry.zoneName))
                    {
                        Debug.LogWarning($"[MapEditor] Dropping duplicate persisted zone '{entry.zoneName}' (already seen).");
                        intraFileDuplicates++;
                        continue;
                    }

                    // Case A: zone already exists in the database → restore editable flag only.
                    if (dbByName.TryGetValue(entry.zoneName, out _))
                    {
                        if (zoneManager.SetZoneEditable(entry.zoneName, entry.editableInTileEditor))
                            flagsRestored++;
                        else
                            Debug.LogWarning($"[MapEditor] SetZoneEditable returned false for '{entry.zoneName}' — flag not restored.");
                        continue;
                    }

                    // Case B: persisted zone NOT in database (user-created). Add only if its
                    // offset doesn't collide with an existing zone.
                    var offset = new Vector2Int(entry.gridOffsetX, entry.gridOffsetY);
                    if (!seenOffsets.Add(offset))
                    {
                        // The user's zone can't be re-registered at this offset because
                        // a database zone already occupies it. We do NOT count this as
                        // a duplicate-to-prune: the entry might become valid again if
                        // the database changes (e.g. a zone is removed). Keep it in
                        // the persistence file untouched — see the rewrite guard below.
                        Debug.LogWarning($"[MapEditor] Persisted zone '{entry.zoneName}' offset {offset} " +
                                         $"collides with an existing zone — kept in persistence file but " +
                                         $"not registered this session.");
                        userZonesShelved++;
                        continue;
                    }
                    if (zoneManager.AddZone(entry.zoneName, offset, entry.editableInTileEditor))
                    {
                        newZonesAdded++;
                    }
                    else
                    {
                        Debug.LogWarning($"[MapEditor] AddZone returned false for '{entry.zoneName}' at {offset} " +
                                         $"despite passing the collision check — entry kept in persistence file.");
                        addZoneFailures++;
                    }
                }

                // Defensive sweep in case the database itself slipped duplicates through.
                int dbDup = zoneManager.RemoveDuplicateZones();

                _state.RestrictTileEditingToEditableZones = data.restrictTileEditingToEditableZones;
                _state.NextZoneIndex = Mathf.Max(1, data.nextZoneIndex);

                // Spawn portals from disk into runtime objects.
                HydratePortalsFromPersistence(data);
                // Same for biome-generated buildings so a session restart
                // sees the same biome scene the user left.
                HydrateBiomeBuildingsFromPersistence(data);

                Debug.Log($"[MapEditor] Loaded persisted zones: +{newZonesAdded} new, " +
                          $"{flagsRestored} flags restored, {intraFileDuplicates} intra-file duplicates dropped, " +
                          $"{userZonesShelved} user zones shelved (DB collision), " +
                          $"{addZoneFailures} unexpected AddZone failures, " +
                          $"{dbDup} extra DB duplicates removed.");

                // Rewrite ONLY when the file itself is dirty (intra-file duplicates,
                // dbDup, or empty zoneName entries). Never rewrite when the only
                // "loss" is user zones shelved by a DB collision — that would
                // permanently delete a user zone whose offset might become free
                // again later. Same for AddZone failures: keep the entry pending.
                if (intraFileDuplicates > 0 || dbDup > 0)
                    PersistZonesToDisk();

                // Re-apply tile overrides now that the ZoneManager is fully
                // populated. WorldLoader.LoadFullWorld runs ApplyAllOverrides
                // before our Start, so any override file whose zone wasn't
                // registered yet was logged as "skipped" and its tiles never
                // painted. A second call here covers those — the call is
                // idempotent for already-applied zones (it just repaints the
                // same tiles into the same cells).
                //
                // Always run, regardless of newZonesAdded: even when the
                // persistence file only restores flags on existing zones, an
                // override file may exist that the WorldLoader pass missed
                // (e.g. a base zone that was missing from the DB at the time
                // WorldLoader iterated, but is present now). Robustness > the
                // negligible cost of one extra directory scan at boot.
                if (worldGridBuilder != null)
                {
                    int reapplied = Valkur.Gameplay.TileEditor.TileOverlayPersistence
                        .ApplyAllOverrides(worldGridBuilder, zoneManager);
                    if (reapplied > 0)
                        Debug.Log($"[MapEditor] Re-applied {reapplied} tile override(s) after LoadZonesFromDisk.");
                }
                else
                {
                    Debug.LogWarning("[MapEditor] worldGridBuilder is null — cannot re-apply tile overrides for newly-registered zones.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to load persisted zones from '{PersistencePath}': {ex.Message}");
            }
        }
    }
}
