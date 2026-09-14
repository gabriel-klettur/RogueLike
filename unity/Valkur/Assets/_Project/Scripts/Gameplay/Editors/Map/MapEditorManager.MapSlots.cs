using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
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

        // True while Start() is mid-flight and the active-slot sync is still
        // running. Prevents PersistZonesToDisk's MirrorWorkingCopyToActiveSlot
        // from clobbering the slot file with a half-loaded DB+working-copy
        // state — the canonical regression that ate slot data on every
        // launch with a custom slot active.
        private bool _isBootSyncInProgress;
        public bool IsBootSyncInProgress => _isBootSyncInProgress;

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

        // ── Boot-time sync with active slot ──────────────────────────────────────

        /// <summary>
        /// Boot-time alignment between <c>_active.txt</c> and the live scene.
        /// LoadZonesFromDisk reads the working copy keyed off <c>WorldId.Base</c>;
        /// if the user shut down with a custom slot active, that working copy
        /// reflects the previous edit but the scene's DB zones are the default
        /// world. Without this sync the result is a Frankenstein layout and
        /// the next PersistZonesToDisk mirrors that layout into the custom
        /// slot file, silently overwriting the user's saved zones.
        ///
        /// Solution: when the active slot is custom, treat the slot file as
        /// authoritative — replace zones, hydrate portals + biome buildings,
        /// rebind tile-overlay routing, and reload world content. No
        /// PersistZonesToDisk fires during this pass (the in-progress flag
        /// disables the slot mirror), so the slot file stays untouched.
        /// </summary>
        private void BootSyncWithActiveSlotIfNeeded()
        {
            var store = ResolveSlotStore();
            string active = store.ActiveSlot;
            if (string.IsNullOrEmpty(active)) return;
            if (string.Equals(active, MapEditorMapSlots.DEFAULT_SLOT,
                              StringComparison.OrdinalIgnoreCase))
                return;

            string json = store.ReadSlot(active);
            if (json == null)
            {
                Debug.LogWarning($"[MapEditor] Active slot '{active}' has no on-disk file " +
                                 $"— falling back to default zones. Use F11 -> Maps to repick a slot.");
                return;
            }

            ZonePersistenceFile data;
            try { data = JsonUtility.FromJson<ZonePersistenceFile>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Boot-sync parse failed for slot '{active}': {ex.Message}");
                return;
            }
            if (data == null) return;
            MapZonesMigrations.Migrate(data);

            // The caller (Start()) holds _isBootSyncInProgress for the entire
            // boot window — assert defensively so a future caller invoking
            // this method outside boot doesn't silently corrupt the slot
            // file via the mirror call inside PersistZonesToDisk.
            if (!_isBootSyncInProgress)
            {
                Debug.LogError("[MapEditor] BootSyncWithActiveSlotIfNeeded called outside the " +
                               "boot window — aborting to avoid mirroring half-loaded state into the slot file.");
                return;
            }

            Debug.Log($"[MapEditor] Boot-sync: active slot is '{active}' — " +
                      $"replacing default zones with slot snapshot ({data.zones?.Count ?? 0} zone(s)).");

            // Slot file is authoritative — replace, don't merge with DB.
            ApplySlotToZoneManager(data);
            // From here the live zones ARE the slot's, so a persist writes the slot file.
            _liveZonesOwnerPin = null;
            // Adopt (don't re-apply) the overlay: the renamed names are
            // already baked into data.zones, but the (originalName →
            // currentName) pairs must travel with subsequent persists so
            // chained renames stay coherent and a future revert can drop
            // the right entry.
            AdoptDatabaseRenamesFromSlot(data);

            // Tile editor's persistence layer must point at the active
            // slot's WorldId so any subsequent paint lands in the right
            // directory. WorldLoader's ApplyAllOverrides already used the
            // correct WorldId, but the editor's internal _persistence
            // instance was created with whatever it last cached.
            RebindTileEditorToActiveWorld();

            // Re-paint the tilemap for the now-correct zone set. Without
            // this the visual stays on the old DB-default tiles.
            RefreshTilemapForActiveSlot();

            // Slot-specific runtime objects.
            HydratePortalsFromPersistence(data);
            HydrateBiomeBuildingsFromPersistence(data);

            // Buildings: drop everything spawned during boot from the
            // default and reload from the slot's BuildingsDir.
            ResolveBuildingLoader()?.ClearGeneratedAbove(BIOME_INSTANCE_ID_BASE);
            ClearAllSpawnedWorldContent();
            ReloadAllWorldContent();

            // Restore last-known player position. Ignored when the slot
            // hasn't been visited yet.
            Vector2 spawnPos = GetSavedPlayerPosition(data);
            TeleportPlayerToWorldPosition(spawnPos);

            // Notify UI listeners so any "active map" indicator refreshes
            // its highlight without waiting for the user to open F11.
            OnMapSlotsChanged?.Invoke();
        }

        // ── Load a named slot into the live ZoneManager ──────────────────────────

        public bool LoadMapSlot(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName)) return false;
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) return false;

            var store = ResolveSlotStore();

            // PEPITORIA IS NOT A SLOT FILE. The base world is the zone database plus the base working
            // copy, merged the way boot merges them; Maps/default.zones.json is only a mirror of it,
            // and loading that mirror instead replaced the whole base world with whatever the file
            // held — a fixture's single "Alpha" zone, measured on this machine the day it mattered.
            // Every other map needs its own file, and it is parsed BEFORE anything is written, so an
            // unreadable slot refuses the switch instead of abandoning a half-saved world.
            bool isDefault = IsBaseSlot(clean);
            ZonePersistenceFile data = null;
            if (!isDefault)
            {
                string json = store.ReadSlot(clean);
                if (json == null) return false;
                try { data = JsonUtility.FromJson<ZonePersistenceFile>(json); }
                catch (Exception ex)
                {
                    Debug.LogError($"[MapEditor] Slot '{clean}' parse failed: {ex.Message}");
                    return false;
                }
                if (data == null) return false;
                MapZonesMigrations.Migrate(data);
            }

            // Snapshot the OUTGOING slot's player state to disk before
            // anything mutates the world. Without this, switching maps would
            // discard whatever the user did since the last autosave tick:
            // position, HP, mana, inventory, learned skills. Mid-switch
            // crashes are also covered — the save lands BEFORE we wipe the
            // grid. Skipped during boot-sync to avoid mirroring a half-loaded
            // state into the slot we're about to abandon.
            if (!_isBootSyncInProgress) FlushPlayerStateBeforeSlotChange("LoadMapSlot");

            // Persist the OUTGOING map into its own file first so the user doesn't silently lose
            // unsaved edits — except when reloading Pepitoria onto itself, which is a request to
            // read it back from disk. The persist records where the player stood, which is how a
            // return to a map lands them where they left it.
            bool reloadingBaseOntoItself = isDefault && IsBaseSlot(LiveZonesOwner);
            if (!reloadingBaseOntoItself) PersistZonesToDisk();
            // Flush tile-overlay edits AND persist Buildings-Editor edits to
            // the OUTGOING slot before any wipe / flip. Without these the
            // pending edits would either be discarded (overlays) or silently
            // re-written into the new slot (buildings) — see BeginNewMap.
            FlushTileOverlayEditsForOutgoingSlot();
            NotifyBuildingsEditorOfSlotChange();
            FlushLightEditsForOutgoingSlot();
            FlushItemDropsForOutgoingSlot();

            Vector2 spawnPos;
            if (isDefault)
            {
                // The pointer flips FIRST here: the rebuild reads the base world's files and the
                // repaint below resolves the base overrides through the active slot.
                store.SetActive(MapEditorMapSlots.DEFAULT_SLOT);
                _liveZonesOwnerPin = null;
                spawnPos = RebuildBaseWorldZones();
            }
            else
            {
                ApplySlotToZoneManager(data);
                // Adopt the slot's database-rename overlay. ApplySlotToZoneManager
                // already replayed the renamed names into ZoneManager, so this
                // is "remember the pairs for future persists", not "re-apply".
                AdoptDatabaseRenamesFromSlot(data);
                // Respawn portal objects from the new slot's record.
                HydratePortalsFromPersistence(data);
                // Re-create biome-generated buildings so a previously-run
                // biome reproduces instead of disappearing on slot switch.
                HydrateBiomeBuildingsFromPersistence(data);
                spawnPos = GetSavedPlayerPosition(data);
                // Flip the active-slot pointer so the visual repaint below
                // resolves the new slot's WorldId. Tile-overlay routing keys off
                // the live store value via RebindTileEditorToActiveWorld().
                store.SetActive(clean);
                _liveZonesOwnerPin = null;
            }

            // Clear undo history so a Ctrl+Z after a slot switch can't
            // resurrect a zone from the previous slot — the captured Do/Undo
            // closures reference the outgoing ZoneManager state.
            ClearUndoHistory();
            // Re-bind the in-game tile editor's persistence layer to the new
            // world id BEFORE repainting so any subsequent dirty-tracking lands
            // in the new slot's directory.
            RebindTileEditorToActiveWorld();

            // Visual swap: drop any tiles painted for the previous slot, then
            // repaint the ones that belong to this slot. Without this the new
            // ZoneManager state is correct but the user still sees the
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

        // Clears the live tilemap and repaints the active slot's ground. Each slot
        // owns its own override directory under <c>persistentDataPath/MapOverrides/</c>
        // (the implicit "default" slot keeps the legacy flat root for byte-compat),
        // so the visual swap between maps is fully isolated — no interference even
        // when two slots share a zone name.
        //
        // Pepitoria's ground is NOT only its overrides: it is the shipped overlays and
        // collision grids plus the author's overrides on top, which is the WorldLoader
        // recipe boot and a return from an interior both use. Repainting just the
        // overrides left every zone nobody had edited on this machine without a tile.
        private void RefreshTilemapForActiveSlot()
        {
            if (worldGridBuilder == null) return;
            WorldId worldId = ResolveSlotStore().ActiveWorldId;
            var world = worldId.IsBase ? FindObjectOfType<WorldLoader>() : null;
            if (world != null)
            {
                TerrainCatalogLoader.InvalidateCache();
                worldGridBuilder.ClearWorld();
                world.LoadFullWorld();
                if (Valkur.Gameplay.World.Layering.WorldCollisionBaker.HasInstance)
                    Valkur.Gameplay.World.Layering.WorldCollisionBaker.Instance.RebuildAll();
                return;
            }
            worldGridBuilder.ClearWorld();
            if (zoneManager == null) return;
            Valkur.Gameplay.TileEditor.TileOverlayPersistence
                .ApplyAllOverrides(worldGridBuilder, zoneManager, worldId);
        }

        /// <summary>The base world's hub (Pepitoria) — where a return lands when nothing recorded a position.</summary>
        internal const string BASE_WORLD_HUB_ZONE = "Lobby";

        /// <summary>
        /// Pepitoria's zones, rebuilt exactly the way boot builds them: the zone database first, then
        /// the base working copy merged on top (renames, user zones, shelving, portals, biome
        /// buildings). Returns where to put the player: the position the working copy recorded the
        /// last time the player stood in Pepitoria — i.e. the spot they left it from — or the hub's
        /// centre when it never recorded one.
        /// </summary>
        private Vector2 RebuildBaseWorldZones()
        {
            // Nothing the outgoing map spawned or renamed belongs to Pepitoria.
            HydratePortalsFromPersistence(null);
            HydrateBiomeBuildingsFromPersistence(null);
            ClearDatabaseRenames();

            var database = FindObjectOfType<ZoneDatabaseLoader>();
            if (database != null) database.LoadDatabase();
            else zoneManager?.ReplaceZones(Array.Empty<ZoneManager.ZoneDefinition>());

            MergePersistedZonesIntoLive(reapplyOverrides: false);

            if (TryReadPersistenceFile(out var working) != null && working.hasLastPlayerPosition)
                return new Vector2(working.lastPlayerWorldX, working.lastPlayerWorldY);
            return BaseWorldHubCentre();
        }

        private Vector2 BaseWorldHubCentre()
        {
            if (zoneManager == null || !zoneManager.TryGetZone(BASE_WORLD_HUB_ZONE, out var hub))
                return Vector2.zero;
            return new Vector2(hub.gridOffset.x + zoneManager.ZoneWidthTiles * 0.5f,
                               hub.gridOffset.y + zoneManager.ZoneHeightTiles * 0.5f);
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
            // A blank map is a NEW map. Blanking the base world would end in a persist of zero zones
            // over Pepitoria's working copy, so the implicit default is refused rather than emptied.
            if (IsBaseSlot(clean))
            {
                Debug.LogWarning("[MapEditor] BeginNewMap refused: the base world cannot be blanked. Give the new map a name.");
                return false;
            }

            // Persist the player's current run state before BeginNewMap wipes
            // the live grid. Same rationale as the LoadMapSlot guard.
            FlushPlayerStateBeforeSlotChange("BeginNewMap");

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
            FlushLightEditsForOutgoingSlot();
            FlushItemDropsForOutgoingSlot();

            zoneManager?.ReplaceZones(Array.Empty<ZoneManager.ZoneDefinition>());
            if (_state != null)
            {
                _state.RestrictTileEditingToEditableZones = false;
                _state.NextZoneIndex = 1;
            }
            // Drop the outgoing slot's portals from the scene before flipping
            // the active-slot pointer; the new map starts portal-free.
            HydratePortalsFromPersistence(null);
            // Same for biome-buildings — the new map is clean of any
            // previously-generated biome content.
            HydrateBiomeBuildingsFromPersistence(null);
            // Same for database-rename overlay — a fresh blank map has no
            // catalog zones to rename, so the outgoing slot's pairs are
            // irrelevant here.
            ClearDatabaseRenames();
            ResolveSlotStore().SetActive(clean);
            _liveZonesOwnerPin = null;
            ClearUndoHistory();
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
        /// <summary>
        /// The backup store caches file handles and paths for the active slot. Held
        /// across a Play boundary it would keep serving the previous session's slot.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedBackupStoreOnPlayModeEnter()
        {
            _sharedBackupStore = null;
        }

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

        public void ClearAllSpawnedWorldContent()
        {
            var bl = FindObjectOfType<BuildingLoader>();
            bl?.ClearSpawned();

            var sl = FindObjectOfType<SpawnerInstanceLoader>();
            sl?.ClearInstances();

            // The spawners are gone; the monsters they made are not unless someone removes them,
            // and a persistent vendor is never removed by distance. See DespawnAllForWorldSwap.
            var monsters = FindObjectOfType<Valkur.Gameplay.MonsterSpawner>();
            if (monsters != null) monsters.DespawnAllForWorldSwap();

            FindObjectOfType<WorldLightLoader>()?.ClearSpawnedLights();

            FindObjectOfType<Valkur.Gameplay.VFX.ParticleInstancesLoader>()?.ClearAll();

            // Entities placed through F5 are the fifth kind of world content, and until the
            // Entities editor grew a repository they were the only one with nothing to
            // clear — so a monster placed on map A survived the swap and floated over map B.
            // ClearSpawned flushes the pending autosave first, against the OUTGOING slot,
            // because the active-slot pointer has not flipped yet at this point. It lives on
            // the runtime service, not the editor: the editor is absent from a release build.
            Valkur.Gameplay.Entities.PlacedEntityService.Instance?.ClearSpawned();
        }

        public void ReloadAllWorldContent()
        {
            // Every loader below resolves its file through the ACTIVE slot at
            // call time (MapEditorActiveSlot / WorldStreamingFileRepositoryBase),
            // so calling them after the slot pointer has flipped re-spawns the
            // incoming map's content — never the outgoing one's.
            FindObjectOfType<BuildingLoader>()?.LoadBuildings();
            FindObjectOfType<SpawnerInstanceLoader>()?.LoadInstances();
            FindObjectOfType<WorldLightLoader>()?.Reload();
            FindObjectOfType<Valkur.Gameplay.VFX.ParticleInstancesLoader>()?.Reload();
            ServiceLocator.Get<Valkur.Gameplay.WorldDrops.ItemDropService>()?.ReloadForActiveSlot();
            Valkur.Gameplay.Entities.PlacedEntityService.Instance?.Reload();
        }

        // Persists the live light set to the OUTGOING slot's file before the
        // active-slot pointer flips. The Lighting editor (Ctrl+F3) saves only
        // on an explicit Save click, so without this a user who places lamps
        // and then switches maps loses them.
        //
        // Guarded on a non-empty light set: WorldLightLoader bails out of
        // LoadInstances when URP 2D or the preset catalog is missing, leaving
        // zero active lights. Flushing that state would serialise an empty
        // array over a perfectly good light_instances.json.
        // Persists authored item drops to the OUTGOING slot's file before the
        // active-slot pointer flips, mirroring the buildings / lights flushes.
        private static void FlushItemDropsForOutgoingSlot()
        {
            try
            {
                ServiceLocator.Get<Valkur.Gameplay.WorldDrops.ItemDropService>()?.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor] Item-drop flush before slot switch failed: {ex.Message}");
            }
        }

        private static void FlushLightEditsForOutgoingSlot()
        {
            try
            {
                var loader = WorldLightLoader.Instance != null
                    ? WorldLightLoader.Instance
                    : FindObjectOfType<WorldLightLoader>();
                // PersistentLightCount, not ActiveLightCount: the latter counts the lights
                // derived from lamp-post buildings, which SaveAll never writes. A world whose
                // authored lights failed to spawn but whose fixtures did would otherwise pass
                // this guard with a non-zero count and then flush an empty array.
                if (loader == null || loader.PersistentLightCount == 0) return;
                int written = loader.SaveAll();
                if (written > 0)
                    Debug.Log($"[MapEditor] Flushed {written} light instance(s) " +
                              "to outgoing slot before slot switch.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor] Light flush before slot switch failed: {ex.Message}");
            }
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

        /// <summary>
        /// Force-flush the player's run state (HP, mana, inventory, position,
        /// zone) to the active save run AND drop a position checkpoint just
        /// before a destructive slot operation (LoadMapSlot / BeginNewMap).
        /// Without this, switching maps would discard whatever the player
        /// did since the last autosave tick — the autosave timer is at
        /// minute granularity, the user expects "I just clicked a slot" to
        /// be safe.
        /// </summary>
        private void FlushPlayerStateBeforeSlotChange(string trigger)
        {
            var saveService = Valkur.Gameplay.SaveService.Instance;
            if (saveService == null) return;
            try
            {
                saveService.SavePositionCheckpoint();
                saveService.SaveImmediately($"map slot change ({trigger})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor] Pre-slot-change flush failed: {ex.Message}");
            }
        }

        // Persists the live zones into the file of the map they belong to. It used to also copy
        // the base working copy into the ACTIVE slot's file — correct only while those were the
        // same map, and the copy that stamped Pepitoria's catalog zones into generated worlds.
        private void BackupCurrentToActiveSlot() => PersistZonesToDisk();

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
