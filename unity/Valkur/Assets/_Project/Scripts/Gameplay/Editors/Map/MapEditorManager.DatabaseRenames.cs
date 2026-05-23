using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Database-zone rename overlay for <see cref="MapEditorManager"/>.
    ///
    /// Zones that originate from <c>StreamingAssets/Maps/zones_database.json</c>
    /// are reloaded by <see cref="Valkur.Gameplay.World.ZoneDatabaseLoader"/>
    /// every boot under their canonical catalog name. The Map Editor can rename
    /// those zones in memory but the catalog is never rewritten, so without an
    /// overlay every rename of a catalog zone gets silently reverted on the
    /// next launch — the persisted entry under its new name doesn't match the
    /// database by name (Case B) and is shelved off the side because its
    /// offset collides with the catalog zone.
    ///
    /// This overlay stores <c>(originalName → currentName)</c> pairs in the
    /// slot file and re-applies them via <see cref="Valkur.Gameplay.World.ZoneManager.RenameZone"/>
    /// at boot, BEFORE the normal persisted-zones diff runs. After the overlay
    /// is applied the live ZoneManager already shows the renamed names, so the
    /// persisted <c>zones</c> entries land on Case A (flag restore) — the
    /// existing diff logic handles them correctly.
    /// </summary>
    public partial class MapEditorManager
    {
        // Identity comparer for original-name lookups. The validator already
        // enforces case-insensitive uniqueness across zones, so the overlay
        // matches its semantics — "Dungeon" and "dungeon" are the same key.
        private readonly List<DatabaseZoneRenameEntry> _databaseZoneRenames =
            new List<DatabaseZoneRenameEntry>();

        // ── Public-ish helpers callers use via the partial seams ────────────

        /// <summary>
        /// Apply the overlay stored in <paramref name="data"/> against the
        /// live <see cref="zoneManager"/>. Renames whose <c>originalName</c>
        /// no longer exists in the database (the catalog zone was removed)
        /// are dropped silently — keeping them would just bloat the slot
        /// file forever with stale entries.
        /// Returns the number of renames actually applied to ZoneManager.
        /// </summary>
        private int ApplyDatabaseZoneRenamesFromPersistence(ZonePersistenceFile data)
        {
            _databaseZoneRenames.Clear();
            if (data == null || data.databaseZoneRenames == null || zoneManager == null)
                return 0;

            int appliedCount = 0;
            for (int i = 0; i < data.databaseZoneRenames.Count; i++)
            {
                var entry = data.databaseZoneRenames[i];
                if (entry == null) continue;
                if (string.IsNullOrWhiteSpace(entry.originalName) ||
                    string.IsNullOrWhiteSpace(entry.currentName))
                    continue;
                // Same name on both sides is a no-op — could only happen with
                // hand-edited files. Drop instead of registering.
                if (string.Equals(entry.originalName, entry.currentName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // The overlay is best-effort: if the original DB zone is gone
                // (catalog edited out from under us) drop the pair instead of
                // letting it linger and confuse future renames.
                if (!zoneManager.TryGetZone(entry.originalName, out _))
                {
                    Debug.LogWarning($"[MapEditor] Database rename overlay dropped stale entry " +
                                     $"'{entry.originalName}' → '{entry.currentName}' " +
                                     $"(original zone no longer exists in catalog).");
                    continue;
                }

                if (zoneManager.RenameZone(entry.originalName, entry.currentName))
                {
                    _databaseZoneRenames.Add(new DatabaseZoneRenameEntry
                    {
                        originalName = entry.originalName,
                        currentName  = entry.currentName,
                    });
                    appliedCount++;
                }
                else
                {
                    Debug.LogWarning($"[MapEditor] Database rename overlay failed to apply " +
                                     $"'{entry.originalName}' → '{entry.currentName}' " +
                                     $"(target name may already exist) — entry dropped.");
                }
            }
            return appliedCount;
        }

        /// <summary>
        /// Copy the in-memory rename pairs into <paramref name="data"/> so
        /// they round-trip to disk. The list is cloned so future mutations
        /// don't reach back into the just-serialised snapshot.
        /// </summary>
        private void WriteDatabaseZoneRenamesIntoPersistence(ZonePersistenceFile data)
        {
            if (data == null) return;
            data.databaseZoneRenames = new List<DatabaseZoneRenameEntry>(_databaseZoneRenames.Count);
            for (int i = 0; i < _databaseZoneRenames.Count; i++)
            {
                var src = _databaseZoneRenames[i];
                data.databaseZoneRenames.Add(new DatabaseZoneRenameEntry
                {
                    originalName = src.originalName,
                    currentName  = src.currentName,
                });
            }
        }

        /// <summary>
        /// Record a rename in the overlay. Handles three cases:
        ///   1. <paramref name="oldName"/> is the <c>currentName</c> of an
        ///      existing pair — update the pair's <c>currentName</c> (rename
        ///      chain). If <paramref name="newName"/> equals the pair's
        ///      <c>originalName</c> the pair is removed (revert to canonical).
        ///   2. <paramref name="oldName"/> is itself a DB-original name (was
        ///      in the live ZoneManager BEFORE the rename committed AND is
        ///      not currently registered as the <c>currentName</c> of any pair)
        ///      — add a new <c>(oldName, newName)</c> pair.
        ///   3. <paramref name="oldName"/> is a purely user-created zone —
        ///      no overlay entry needed (Case B in LoadZonesFromDisk already
        ///      restores user zones by name).
        ///
        /// <paramref name="oldNameWasInDatabase"/> is the snapshot taken by
        /// the caller BEFORE <see cref="Valkur.Gameplay.World.ZoneManager.RenameZone"/>
        /// committed — true iff <paramref name="oldName"/> existed in
        /// ZoneManager at that point AND wasn't already tracked by the
        /// overlay (so it must be a fresh DB-origin zone the user is
        /// renaming for the first time).
        /// </summary>
        private void RecordDatabaseRename(string oldName, string newName, bool oldNameWasInDatabase)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;

            int chainIdx = FindRenameByCurrentName(oldName);
            if (chainIdx >= 0)
            {
                var chained = _databaseZoneRenames[chainIdx];
                if (string.Equals(newName, chained.originalName, StringComparison.OrdinalIgnoreCase))
                {
                    // Renamed back to the catalog name — drop the overlay
                    // entry entirely so on next boot the catalog name wins
                    // unchanged.
                    _databaseZoneRenames.RemoveAt(chainIdx);
                }
                else
                {
                    chained.currentName = newName;
                    _databaseZoneRenames[chainIdx] = chained;
                }
                return;
            }

            if (!oldNameWasInDatabase) return; // user-created zone, no overlay needed

            _databaseZoneRenames.Add(new DatabaseZoneRenameEntry
            {
                originalName = oldName,
                currentName  = newName,
            });
        }

        /// <summary>
        /// Drop any overlay entry that currently maps to <paramref name="zoneName"/>.
        /// Called when a zone is deleted so the next boot doesn't try to
        /// rename a non-existent catalog zone and so the slot file doesn't
        /// accumulate dead overlay pairs.
        /// </summary>
        private void ForgetDatabaseRenameForCurrentName(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName)) return;
            int idx = FindRenameByCurrentName(zoneName);
            if (idx >= 0) _databaseZoneRenames.RemoveAt(idx);
        }

        /// <summary>
        /// Mirror <paramref name="data"/>'s overlay list into the in-memory
        /// copy WITHOUT re-applying it to <see cref="zoneManager"/>. Used
        /// when a slot's full zone list (already containing the renamed
        /// names) is replayed via <see cref="ApplySlotToZoneManager"/>: the
        /// rename is already reflected in the live state, but the overlay
        /// still needs to travel with subsequent persists so chained
        /// renames stay coherent.
        /// </summary>
        private void AdoptDatabaseRenamesFromSlot(ZonePersistenceFile data)
        {
            _databaseZoneRenames.Clear();
            if (data == null || data.databaseZoneRenames == null) return;
            for (int i = 0; i < data.databaseZoneRenames.Count; i++)
            {
                var src = data.databaseZoneRenames[i];
                if (src == null) continue;
                if (string.IsNullOrWhiteSpace(src.originalName) ||
                    string.IsNullOrWhiteSpace(src.currentName)) continue;
                _databaseZoneRenames.Add(new DatabaseZoneRenameEntry
                {
                    originalName = src.originalName,
                    currentName  = src.currentName,
                });
            }
        }

        /// <summary>
        /// Forget the overlay entirely. Called by <c>BeginNewMap</c> so a
        /// brand-new blank slot doesn't inherit the outgoing slot's renames.
        /// </summary>
        private void ClearDatabaseRenames() => _databaseZoneRenames.Clear();

        private int FindRenameByCurrentName(string currentName)
        {
            for (int i = 0; i < _databaseZoneRenames.Count; i++)
            {
                if (string.Equals(_databaseZoneRenames[i].currentName, currentName,
                                  StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private bool IsTrackedAsRenameCurrentName(string name)
            => FindRenameByCurrentName(name) >= 0;
    }
}
