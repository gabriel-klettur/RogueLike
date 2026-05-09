using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.MapEditor.Backups;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Map Editor "Maps" explorer — saves the current zone universe as a named
    /// slot, lists existing slots, loads them back, renames or deletes them.
    /// Each slot is a self-contained <see cref="ZonePersistenceFile"/> JSON
    /// snapshot under <c>Application.persistentDataPath/Maps/</c>; the
    /// live working copy (<c>map_editor_zones.json</c>) is unchanged so
    /// existing recovery / migration paths keep working.
    ///
    /// Tile overrides ARE routed per slot: the active slot resolves to a
    /// <see cref="Valkur.Core.Coordinates.WorldId"/> (via
    /// <see cref="MapEditorMapSlots.ResolveWorldId"/>), and every overlay write
    /// / read goes through that world's subdirectory under
    /// <c>persistentDataPath/MapOverrides/</c>. The implicit "default" slot
    /// keeps the legacy flat layout for byte-compat with pre-multi-map saves.
    /// </summary>
    public partial class MapEditorManager
    {
        public event Action OnMapSlotsChanged;

        private MapEditorMapSlots _slotStore;

        public string ActiveMapSlot => ResolveSlotStore().ActiveSlot;
        public string[] ListMapSlots() => ResolveSlotStore().ListSlots().ToArray();

        /// <summary>
        /// <see cref="WorldId"/> of the currently-active slot. Persistence
        /// systems that route per-slot (tile overlays today; buildings /
        /// lights / spawners as they migrate) read this to pick the right
        /// directory for reads and writes.
        /// </summary>
        public WorldId ActiveWorldId => ResolveSlotStore().ActiveWorldId;

        private MapEditorMapSlots ResolveSlotStore()
        {
            if (_slotStore == null) _slotStore = new MapEditorMapSlots();
            return _slotStore;
        }

        // ── Load a named slot into the live ZoneManager ──────────────────────────

        public bool LoadMapSlot(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName)) return false;
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) return false;

            var store = ResolveSlotStore();
            string json = store.ReadSlot(clean);

            // The "default" slot is the implicit blank baseline; if no file
            // exists for it yet, treat the load as "revert to factory blank"
            // rather than failing — that way the synthetic entry surfaced in
            // ListSlots() is actually selectable.
            bool isDefault = string.Equals(clean,
                MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
            bool isDefaultBlankLoad = json == null && isDefault;
            if (json == null && !isDefaultBlankLoad) return false;

            // Snapshot the current state into its existing slot first so the
            // user doesn't silently lose unsaved edits — except when reloading
            // 'default' onto itself, which would otherwise freeze the very
            // edits we're about to discard into a brand-new default.zones.json.
            bool skipBackup = isDefaultBlankLoad
                && string.Equals(store.ActiveSlot,
                    MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
            if (!skipBackup) BackupCurrentToActiveSlot();
            // Flush tile-overlay edits AND persist Buildings-Editor edits to
            // the OUTGOING slot before any wipe / flip. Without these the
            // pending edits would either be discarded (overlays) or silently
            // re-written into the new slot (buildings) — see BeginNewMap.
            FlushTileOverlayEditsForOutgoingSlot();
            NotifyBuildingsEditorOfSlotChange();

            // Position to teleport the player to once the new slot is active.
            // Defaults to world origin; replaced by the slot file's last-known
            // position when the file exists and was previously visited.
            Vector2 spawnPos = Vector2.zero;

            if (isDefaultBlankLoad)
            {
                zoneManager?.ReplaceZones(Array.Empty<ZoneManager.ZoneDefinition>());
                if (_state != null)
                {
                    _state.RestrictTileEditingToEditableZones = false;
                    _state.NextZoneIndex = 1;
                }
                // No portals on the synthetic blank-default load — drop any
                // runtime portal objects from the outgoing slot.
                HydratePortalsFromPersistence(null);
            }
            else
            {
                ZonePersistenceFile data;
                try { data = JsonUtility.FromJson<ZonePersistenceFile>(json); }
                catch (Exception ex)
                {
                    Debug.LogError($"[MapEditor] Slot '{clean}' parse failed: {ex.Message}");
                    return false;
                }
                if (data == null) return false;
                MapZonesMigrations.Migrate(data);
                ApplySlotToZoneManager(data);
                // Respawn portal objects from the new slot's record.
                HydratePortalsFromPersistence(data);
                spawnPos = GetSavedPlayerPosition(data);
            }

            // Flip the active-slot pointer FIRST so the visual repaint below
            // resolves the new slot's WorldId. Tile-overlay routing keys off
            // the live store value via RebindTileEditorToActiveWorld().
            store.SetActive(clean);
            // Re-bind the in-game tile editor's persistence layer to the new
            // world id BEFORE repainting so any subsequent dirty-tracking lands
            // in the new slot's directory.
            RebindTileEditorToActiveWorld();

            // Visual swap: drop any tiles painted for the previous slot, then
            // repaint the overrides whose zones live in this slot. Without this
            // the new ZoneManager state is correct but the user still sees the
            // previous map's tiles.
            RefreshTilemapForActiveSlot();

            ResolveBuildingLoader()?.ClearGeneratedAbove(BIOME_INSTANCE_ID_BASE);
            // Wipe and re-spawn the rest of the world (buildings, spawners, …).
            // For the blank-load default branch the reload step is effectively
            // a no-op because the disk file still represents the same shared
            // world content, but for explicit slot loads the clear step is
            // critical to avoid carrying ghost buildings between slots.
            ClearAllSpawnedWorldContent();
            ReloadAllWorldContent();
            // Teleport BEFORE the final PersistZonesToDisk so the auto-save
            // captures the freshly-restored player position into this slot's
            // file (instead of the stale outgoing-slot position).
            TeleportPlayerToWorldPosition(spawnPos);
            PersistZonesToDisk();
            OnMapSlotsChanged?.Invoke();
            return true;
        }

        // Clears the live tilemap and reapplies the override files that belong
        // to the currently-active slot's <see cref="WorldId"/>. Each slot now
        // owns its own directory under <c>persistentDataPath/MapOverrides/</c>
        // (the implicit "default" slot keeps the legacy flat root for byte-
        // compat), so the visual swap between maps is fully isolated — no
        // interference even when two slots share a zone name.
        private void RefreshTilemapForActiveSlot()
        {
            if (worldGridBuilder == null) return;
            worldGridBuilder.ClearWorld();
            if (zoneManager == null) return;
            WorldId worldId = ResolveSlotStore().ActiveWorldId;
            Valkur.Gameplay.TileEditor.TileOverlayPersistence
                .ApplyAllOverrides(worldGridBuilder, zoneManager, worldId);
        }

        // Flushes any in-flight tile-overlay edits to the OUTGOING slot's
        // directory so they aren't silently rerouted into the new slot when
        // the active-slot pointer flips. Best-effort: a missing/inactive
        // tile editor is a no-op, never blocks the slot switch.
        private void FlushTileOverlayEditsForOutgoingSlot()
        {
            try
            {
                var tileEditor = TileEditorManager.Instance != null
                    ? TileEditorManager.Instance
                    : FindObjectOfType<TileEditorManager>();
                if (tileEditor != null && tileEditor.Persistence != null
                    && tileEditor.Persistence.HasUnsavedChanges)
                {
                    int saved = tileEditor.Persistence.SaveAllDirty();
                    if (saved > 0)
                        Debug.Log($"[MapEditor] Flushed {saved} pending tile-overlay zone(s) " +
                                  $"to outgoing world before slot switch.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor] Tile-overlay flush before slot switch failed: {ex.Message}");
            }
        }

        // Rebinds the tile editor's overlay persistence to whatever the
        // currently-active slot resolves to. Called immediately after
        // <see cref="MapEditorMapSlots.SetActive"/> so subsequent edits land
        // in the new slot's directory. Best-effort; failure logs a warning
        // but never blocks the slot transition.
        private void RebindTileEditorToActiveWorld()
        {
            try
            {
                var tileEditor = TileEditorManager.Instance != null
                    ? TileEditorManager.Instance
                    : FindObjectOfType<TileEditorManager>();
                tileEditor?.RebindToWorld(ResolveSlotStore().ActiveWorldId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor] Tile-editor rebind to active world failed: {ex.Message}");
            }
        }

        // ── Begin a fresh blank map ──────────────────────────────────────────────

        public bool BeginNewMap(string slotName)
        {
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) clean = MapEditorMapSlots.DEFAULT_SLOT;

            // Snapshot the OUTGOING active slot before we wipe live state.
            // If the user later changes their mind they can roll the slot
            // back from the backup browser without resetting their session.
            string outgoing = ResolveSlotStore().ActiveSlot;
            TryAutoSnapshot(outgoing, "Pre-new-map safety snapshot",
                            MapBackupSchema.KindAutoBeforeNew);

            BackupCurrentToActiveSlot();
            // Flush tile-overlay edits AND persist any pending Buildings-Editor
            // edits (placed/deleted/moved buildings, painted colliders) to the
            // OUTGOING slot's files BEFORE we flip the active-slot pointer.
            // Without these flushes the wipe below would silently destroy the
            // outgoing slot's data — the canonical "buildings disappeared from
            // default after creating a new map" bug.
            FlushTileOverlayEditsForOutgoingSlot();
            NotifyBuildingsEditorOfSlotChange();

            zoneManager?.ReplaceZones(Array.Empty<ZoneManager.ZoneDefinition>());
            if (_state != null)
            {
                _state.RestrictTileEditingToEditableZones = false;
                _state.NextZoneIndex = 1;
            }
            // Drop the outgoing slot's portals from the scene before flipping
            // the active-slot pointer; the new map starts portal-free.
            HydratePortalsFromPersistence(null);
            ResolveSlotStore().SetActive(clean);
            // Re-bind the tile editor's overlay persistence to the new slot's
            // world so any first edits land in the new directory.
            RebindTileEditorToActiveWorld();
            ResolveBuildingLoader()?.ClearGeneratedAbove(BIOME_INSTANCE_ID_BASE);
            // Wipe the tilemap so the user actually sees an empty canvas
            // instead of standing inside whatever was just removed from
            // ZoneManager. Override JSONs on disk are untouched — switching
            // back to a saved slot via LoadMapSlot will repaint them.
            if (worldGridBuilder != null)
                worldGridBuilder.ClearWorld();
            // Destroy every previously-spawned world object (buildings, NPCs,
            // lights, …) so the new map is genuinely empty. Without this the
            // user would teleport to (0,0) but still stand among the default
            // map's castle/houses/colosseum — see screenshot in task spec.
            ClearAllSpawnedWorldContent();
            // Teleport BEFORE the final PersistZonesToDisk so this slot's auto-
            // save captures the spawn position (0,0) immediately, giving the
            // new slot a deterministic "last known position" on first visit.
            TeleportPlayerToBlankMapOrigin();
            PersistZonesToDisk();
            OnMapSlotsChanged?.Invoke();
            return true;
        }

        // ── Auto-snapshot helper ─────────────────────────────────────────────────
        //
        // Centralised hook into the backup system. Wrapped in try/catch so a
        // failure (disk full, locked file, etc.) never prevents the user from
        // doing the actual destructive operation — the snapshot is a best-
        // effort safety net, not a precondition.
        private static MapBackupStore _sharedBackupStore;
        private static MapBackupStore BackupStore =>
            _sharedBackupStore ?? (_sharedBackupStore = new MapBackupStore());

        private static void TryAutoSnapshot(string slot, string label, string kind)
        {
            try
            {
                BackupStore.CreateSnapshot(slot, label, kind);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor] Auto-snapshot '{kind}' for '{slot}' failed: {ex.Message}");
            }
        }

        // ── Per-slot world-content swap ──────────────────────────────────────────
        //
        // The Map Editor owns ZONES. Other content systems (buildings, spawners,
        // lights, particles, drops, tile overrides) are loaded once at boot and
        // currently share a single hardcoded `WorldId.Base`. When the user
        // switches map slots, the SCENE state must at minimum visually mirror the
        // new map (no leftover buildings from the previous slot). Until the full
        // per-slot persistence routing lands across all subsystems, this helper
        // does the next-best thing: it destroys every spawned world object so a
        // newly-created map is genuinely empty, and lets LoadMapSlot trigger a
        // re-spawn from the on-disk data when the user switches back.
        //
        // See `.github/MAP_EDITOR_MULTIMAP_ROADMAP.md` (added in this commit) for
        // the full per-slot WorldId routing plan that closes the gap so each
        // slot owns its own buildings/lights/spawners/particles on disk too.

        private void ClearAllSpawnedWorldContent()
        {
            var bl = FindObjectOfType<BuildingLoader>();
            bl?.ClearSpawned();

            var sl = FindObjectOfType<SpawnerInstanceLoader>();
            sl?.ClearInstances();

            var wll = FindObjectOfType<WorldLightLoader>();
            if (wll != null)
            {
                var snapshot = new List<GameObject>(wll.ActiveLightObjects);
                foreach (var lightGo in snapshot)
                    wll.RemoveLight(lightGo);
            }
        }

        private void ReloadAllWorldContent()
        {
            FindObjectOfType<BuildingLoader>()?.LoadBuildings();
            FindObjectOfType<SpawnerInstanceLoader>()?.LoadInstances();
            // WorldLightLoader currently doesn't expose a public re-load — it
            // loads in Start() and exposes only RemoveLight. Re-loading here is
            // a no-op until the loader gains a `Reload()` API; the rest of the
            // pipeline is wired so adding it later is one more line.
        }

        private void TeleportPlayerToBlankMapOrigin()
            => TeleportPlayerToWorldPosition(Vector2.zero);

        /// <summary>
        /// Tell the Buildings runtime editor (F10) that the active map slot is
        /// about to change so it can flush pending edits to the OUTGOING slot's
        /// files and drop its cached collider stores. Must be called BEFORE
        /// <see cref="MapEditorMapSlots.SetActive"/> flips the slot pointer
        /// and BEFORE <see cref="ClearAllSpawnedWorldContent"/> wipes the
        /// scene — otherwise pending edits would be lost (empty scene
        /// serialised) or written to the wrong slot.
        ///
        /// Calls into the Buildings editor only when its singleton already
        /// exists; missing instance is a no-op so headless / pre-activation
        /// cases stay safe. Wrapped in try/catch so a failure inside the
        /// editor never blocks the slot transition itself.
        /// </summary>
        private static void NotifyBuildingsEditorOfSlotChange()
        {
            try
            {
                var instance = Valkur.Gameplay.Buildings.BuildingsRuntimeEditor.HasInstance
                    ? Valkur.Gameplay.Buildings.BuildingsRuntimeEditor.Instance
                    : null;
                instance?.NotifyActiveMapSlotChanged();
            }
            catch (Exception ex)
            {
                // Never let this throw — slot switching must keep working
                // even if the buildings editor fails to flush.
                Debug.LogWarning(
                    $"[MapEditor] BuildingsRuntimeEditor.NotifyActiveMapSlotChanged failed: {ex.Message}");
            }
        }

        private void TeleportPlayerToWorldPosition(Vector2 targetWorldPos)
        {
            var playerT = Valkur.Core.EntityRegistry.PlayerTransform;
            if (playerT == null) return;
            Vector3 oldPos = playerT.position;
            Vector3 newPos = new Vector3(targetWorldPos.x, targetWorldPos.y, oldPos.z);
            playerT.position = newPos;

            _cameraPan.Reset();
            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            if (camSetup != null)
            {
                camSetup.ReattachFollow();
                camSetup.SnapToFollowTarget(newPos - oldPos);
            }
        }

        // Reads the saved player position out of a parsed slot file. Returns
        // <see cref="Vector2.zero"/> when the slot has never been visited yet
        // (legacy file lacking the field, or fresh slot just created by
        // BeginNewMap).
        private static Vector2 GetSavedPlayerPosition(ZonePersistenceFile data)
        {
            if (data == null || !data.hasLastPlayerPosition) return Vector2.zero;
            return new Vector2(data.lastPlayerWorldX, data.lastPlayerWorldY);
        }

        // ── Delete + Rename ──────────────────────────────────────────────────────

        public bool DeleteMapSlot(string slotName)
        {
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) return false;
            // The "default" slot is the implicit baseline — never deletable.
            if (string.Equals(clean, MapEditorMapSlots.DEFAULT_SLOT,
                              StringComparison.OrdinalIgnoreCase))
                return false;
            // Snapshot before the destructive op so the user can recover the
            // slot from the backup browser if they regret the deletion.
            TryAutoSnapshot(clean, "Pre-delete safety snapshot",
                            MapBackupSchema.KindAutoBeforeDelete);
            bool ok = ResolveSlotStore().DeleteSlot(clean);
            if (ok) OnMapSlotsChanged?.Invoke();
            return ok;
        }

        public bool RenameMapSlot(string oldName, string newName)
        {
            string oldClean = MapEditorMapSlots.Sanitize(oldName);
            string newClean = MapEditorMapSlots.Sanitize(newName);
            if (string.IsNullOrEmpty(oldClean) || string.IsNullOrEmpty(newClean)) return false;
            // The "default" slot is the implicit baseline — never renamable.
            if (string.Equals(oldClean, MapEditorMapSlots.DEFAULT_SLOT,
                              StringComparison.OrdinalIgnoreCase))
                return false;
            // Renaming TO "default" would also collide with the protected slot.
            if (string.Equals(newClean, MapEditorMapSlots.DEFAULT_SLOT,
                              StringComparison.OrdinalIgnoreCase))
                return false;
            bool ok = ResolveSlotStore().RenameSlot(oldClean, newClean);
            if (ok) OnMapSlotsChanged?.Invoke();
            return ok;
        }

        // ── Internals ────────────────────────────────────────────────────────────

        private void BackupCurrentToActiveSlot()
        {
            var store = ResolveSlotStore();
            string active = store.ActiveSlot;
            if (string.IsNullOrEmpty(active)) return;
            PersistZonesToDisk();
            string json = ReadWorkingCopyJson();
            if (json != null)
                store.WriteSlot(active, json);
        }

        private string ReadWorkingCopyJson()
        {
            try
            {
                string raw = ResolveZonesRepository().ReadWithSidecarFallback(_persistenceWorldId, out _);
                return raw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor.Slots] Read working copy failed: {ex.Message}");
                return null;
            }
        }

        private void ApplySlotToZoneManager(ZonePersistenceFile data)
        {
            if (zoneManager == null || data == null) return;

            var defs = new List<ZoneManager.ZoneDefinition>(
                data.zones != null ? data.zones.Count : 0);
            if (data.zones != null)
            {
                for (int i = 0; i < data.zones.Count; i++)
                {
                    var entry = data.zones[i];
                    if (string.IsNullOrWhiteSpace(entry.zoneName)) continue;
                    defs.Add(new ZoneManager.ZoneDefinition
                    {
                        zoneName             = entry.zoneName,
                        gridOffset           = new Vector2Int(entry.gridOffsetX, entry.gridOffsetY),
                        zoneMusic            = null,
                        editableInTileEditor = entry.editableInTileEditor,
                    });
                }
            }
            zoneManager.ReplaceZones(defs);

            if (_state != null)
            {
                _state.RestrictTileEditingToEditableZones = data.restrictTileEditingToEditableZones;
                _state.NextZoneIndex = Mathf.Max(1, data.nextZoneIndex);
            }
        }
    }
}
