using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Disk persistence (save / load zone data) for <see cref="MapEditorManager"/>.
    /// </summary>
    public partial class MapEditorManager
    {
        private void PersistZonesToDisk()
        {
            if (zoneManager == null) return;

            var data = new ZonePersistenceFile
            {
                restrictTileEditingToEditableZones = _state.RestrictTileEditingToEditableZones,
                nextZoneIndex = _state.NextZoneIndex
            };

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

            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                AtomicWriteWithSidecarBackup(PersistencePath, json);
                if (shelvedPreserved > 0)
                    Debug.Log($"[MapEditor] Persisted {data.zones.Count} zone(s) " +
                              $"({shelvedPreserved} shelved preserved) to '{PersistencePath}'.");
                else
                    Debug.Log($"[MapEditor] Persisted {data.zones.Count} zone(s) to '{PersistencePath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to persist zones to '{PersistencePath}': {ex.Message}");
            }
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

        // Atomic write + sidecar .bak. Path A: file exists → File.Replace
        // promotes the temp file and bumps the previous content into .bak in
        // a single OS-level atomic step. Path B: first save → simple rename.
        // The .bak surviving alongside the primary is the runtime safety net
        // for a crash mid-write or a test that File.Move's the primary away
        // and never restores it (LoadZonesFromDisk falls back to the .bak).
        private static void AtomicWriteWithSidecarBackup(string targetPath, string content)
        {
            string tmpPath = targetPath + ".tmp";
            string bakPath = targetPath + ".bak";
            File.WriteAllText(tmpPath, content);
            if (File.Exists(targetPath))
            {
                // Replace bumps current target → .bak, promotes tmp → target.
                File.Replace(tmpPath, targetPath, bakPath);
            }
            else
            {
                File.Move(tmpPath, targetPath);
                // First save with no prior file: still seed a .bak so the
                // very next write isn't unprotected.
                try { File.Copy(targetPath, bakPath, overwrite: true); } catch { /* best-effort */ }
            }
        }

        // Reads the persistence file from the primary path; if missing or
        // corrupt, transparently retries the sidecar .bak. Returns the path
        // it actually loaded from (or null if neither was usable). Runs the
        // shape-migration chain on the parsed document so a v1.x file from
        // a previous build is upgraded to the current schema before any
        // other code touches it.
        private string TryReadPersistenceFile(out ZonePersistenceFile data)
        {
            data = null;
            string[] candidates = { PersistencePath, PersistencePath + ".bak" };
            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                if (!File.Exists(path)) continue;
                try
                {
                    string json = File.ReadAllText(path);
                    var parsed = JsonUtility.FromJson<ZonePersistenceFile>(json);
                    if (parsed == null)
                    {
                        Debug.LogWarning($"[MapEditor] '{path}' parsed as null — trying next candidate. " +
                                         $"Head: {(json.Length > 200 ? json.Substring(0, 200) : json)}");
                        continue;
                    }
                    MapZonesMigrations.Migrate(parsed);
                    data = parsed;
                    if (i > 0)
                        Debug.LogWarning($"[MapEditor] Primary persistence missing/corrupt — recovered zones from sidecar '{path}'.");
                    return path;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MapEditor] Failed to read '{path}': {ex.Message} — trying next candidate.");
                }
            }
            return null;
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
