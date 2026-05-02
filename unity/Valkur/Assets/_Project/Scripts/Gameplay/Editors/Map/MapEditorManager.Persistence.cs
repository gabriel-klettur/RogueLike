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

            var zones = zoneManager.GetZonesSnapshot();
            for (int i = 0; i < zones.Length; i++)
            {
                data.zones.Add(new ZonePersistenceEntry
                {
                    zoneName         = zones[i].zoneName,
                    gridOffsetX      = zones[i].gridOffset.x,
                    gridOffsetY      = zones[i].gridOffset.y,
                    editableInTileEditor = zones[i].editableInTileEditor
                });
            }

            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                AtomicWriteWithSidecarBackup(PersistencePath, json);
                Debug.Log($"[MapEditor] Persisted {data.zones.Count} zone(s) to '{PersistencePath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to persist zones to '{PersistencePath}': {ex.Message}");
            }
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
        // it actually loaded from (or null if neither was usable).
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

                int duplicatesDropped = 0;
                int newZonesAdded     = 0;
                int flagsRestored     = 0;
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenOffsets = new HashSet<Vector2Int>(dbOffsets);

                for (int i = 0; i < data.zones.Count; i++)
                {
                    var entry = data.zones[i];
                    if (string.IsNullOrWhiteSpace(entry.zoneName))
                    {
                        duplicatesDropped++;
                        continue;
                    }

                    if (!seenNames.Add(entry.zoneName))
                    {
                        Debug.LogWarning($"[MapEditor] Dropping duplicate persisted zone '{entry.zoneName}' (already seen).");
                        duplicatesDropped++;
                        continue;
                    }

                    // Case A: zone already exists in the database → restore editable flag only.
                    if (dbByName.TryGetValue(entry.zoneName, out _))
                    {
                        if (zoneManager.SetZoneEditable(entry.zoneName, entry.editableInTileEditor))
                            flagsRestored++;
                        continue;
                    }

                    // Case B: persisted zone NOT in database (user-created). Add only if its
                    // offset doesn't collide with an existing zone.
                    var offset = new Vector2Int(entry.gridOffsetX, entry.gridOffsetY);
                    if (!seenOffsets.Add(offset))
                    {
                        Debug.LogWarning($"[MapEditor] Dropping persisted zone '{entry.zoneName}' — offset {offset} collides with an existing zone.");
                        duplicatesDropped++;
                        continue;
                    }
                    if (zoneManager.AddZone(entry.zoneName, offset, entry.editableInTileEditor))
                        newZonesAdded++;
                }

                // Defensive sweep in case the database itself slipped duplicates through.
                int dbDup = zoneManager.RemoveDuplicateZones();

                _state.RestrictTileEditingToEditableZones = data.restrictTileEditingToEditableZones;
                _state.NextZoneIndex = Mathf.Max(1, data.nextZoneIndex);

                Debug.Log($"[MapEditor] Loaded persisted zones: +{newZonesAdded} new, " +
                          $"{flagsRestored} flags restored, {duplicatesDropped} duplicates dropped, " +
                          $"{dbDup} extra DB duplicates removed.");

                // Rewrite the persistence file in clean form so duplicates don't accumulate.
                if (duplicatesDropped > 0 || dbDup > 0)
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
