using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Persists tile-editor edits to disk on a per-zone basis and re-applies them on game load.
    /// <para>
    /// Each zone owns one overlay JSON file with the schema
    /// <c>{ "layers": { "Ground": [[...]], ... } }</c> (a matrix per layer).
    /// </para>
    /// <para>
    /// Storage is non-destructive: edits go to <c>Application.persistentDataPath/MapOverrides/</c>,
    /// not to the original <c>StreamingAssets/Maps</c> files. Use the editor menu
    /// <c>Valkur > Tile Editor > Bake Overrides into StreamingAssets</c> to promote them.
    /// </para>
    /// </summary>
    public partial class TileOverlayPersistence
    {
        private const string OVERRIDE_DIR_NAME = "MapOverrides";
        private const string OVERRIDE_EXTENSION = ".overlay.json";

        private readonly ZoneManager _zones;
        private readonly WorldGridBuilder _grid;
        private readonly HashSet<string> _dirtyZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Repository injection point. Tests pass an InMemoryTileOverrideRepository
        // through the third constructor overload; production code keeps using
        // the default-constructed instance, which falls back to the JSON-file
        // backend (byte-compatible with the legacy persistentDataPath/MapOverrides
        // layout). The static helpers in TileOverlayPersistence.Static.cs still
        // hit File.IO directly for back-compat with their many call-sites; those
        // will migrate when their consumers do.
        private readonly ITileOverrideRepository _repository;

        // Phase 1 per-world routing. Defaults to WorldId.Base so the legacy
        // single-world boot path is byte-compatible — overlays continue to
        // land directly under persistentDataPath/MapOverrides/. Multi-world
        // callers pass the active context's WorldId at construction time.
        private readonly WorldId _worldId;

        public WorldId WorldId => _worldId;

        /// <summary>
        /// Optional per-cell terrain layer. When non-null, <see cref="SaveZoneInternal"/>
        /// emits a <c>terrains</c> matrix alongside the layer matrices in the overlay
        /// JSON, and the loader can restore it via
        /// <see cref="OverlayLoader.ApplyTerrainsFromPath"/> for auto-tile auto-curation.
        /// Older overlays without the field load with an empty terrain map (legacy
        /// manual painting only).
        /// </summary>
        public TerrainMap TerrainMap { get; set; }

        /// <summary>
        /// Optional per-cell collision tag layer. When non-null,
        /// <see cref="SaveZoneInternal"/> emits a <c>collisionTags</c> matrix alongside
        /// the layer matrices in the overlay JSON, and the loader
        /// (<see cref="OverlayLoader.ApplyCollisionTagsFromPath"/>) can restore it.
        /// Legacy overlays without the field load with every cell defaulting to
        /// <see cref="CollisionTagMap.Wildcard"/> — preserves the pre-feature behaviour
        /// where a painted collider applies to every entity.
        /// </summary>
        public CollisionTagMap CollisionTagMap { get; set; }

        /// <summary>
        /// Optional per-cell layer-jumps map (M1.8). When non-null,
        /// <see cref="SaveZoneInternal"/> emits a <c>layerJumps</c> matrix alongside
        /// the layer / terrain / collisionTags matrices, and the loader
        /// (<see cref="OverlayLoader.ApplyLayerJumpsFromPath"/>) restores it. Legacy
        /// overlays without the field load with no jumps in the map → runtime
        /// <see cref="World.Layering.LayerJumpTriggerSystem"/> simply never fires.
        /// </summary>
        public World.Layering.LayerJumpMap LayerJumpMap { get; set; }

        public event Action OnDirtyChanged;
        public event Action<string> OnZoneSaved;
        public event Action<string, Exception> OnSaveFailed;

        public bool HasUnsavedChanges => _dirtyZones.Count > 0;
        public int DirtyZoneCount => _dirtyZones.Count;
        public IReadOnlyCollection<string> DirtyZones => _dirtyZones;

        public static string OverrideDirectory =>
            Path.Combine(Application.persistentDataPath, OVERRIDE_DIR_NAME);

        public TileOverlayPersistence(ZoneManager zones, WorldGridBuilder grid)
            : this(zones, grid, repository: null, worldId: WorldId.Base) { }

        public TileOverlayPersistence(ZoneManager zones, WorldGridBuilder grid,
                                      ITileOverrideRepository repository)
            : this(zones, grid, repository, worldId: WorldId.Base) { }

        public TileOverlayPersistence(ZoneManager zones, WorldGridBuilder grid,
                                      ITileOverrideRepository repository,
                                      WorldId worldId)
        {
            _zones = zones;
            _grid = grid;
            _repository = repository ?? new JsonFileTileOverrideRepository();
            _worldId = worldId;

            // Registers this instance so a hard quit / Play-mode exit can force
            // any pending deferred autosave to complete even though nothing
            // else holds a reference to it long enough to call Dispose — see
            // TileOverlayPersistence.Autosave.cs.
            RegisterForAutosaveLifecycleTracking();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Dirty tracking
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Mark the zone owning this cell as dirty.</summary>
        public void MarkCellDirty(Vector3Int cell)
        {
            if (_zones == null) return;
            if (_zones.TryGetZoneAtTile(new Vector2Int(cell.x, cell.y), out var zone))
            {
                if (_dirtyZones.Add(zone.zoneName))
                    OnDirtyChanged?.Invoke();
                // Arm/refresh the deferred autosave debounce — see
                // TileOverlayPersistence.Autosave.cs. No-op outside Play mode
                // (EditMode tests never touch it).
                ArmAutosaveTimer();
            }
        }

        public void MarkBatchDirty(IList<TileEdit> edits)
        {
            if (edits == null || edits.Count == 0 || _zones == null) return;
            int before = _dirtyZones.Count;
            for (int i = 0; i < edits.Count; i++)
            {
                if (_zones.TryGetZoneAtTile(new Vector2Int(edits[i].Position.x, edits[i].Position.y), out var zone))
                    _dirtyZones.Add(zone.zoneName);
            }
            if (_dirtyZones.Count != before)
                OnDirtyChanged?.Invoke();
            ArmAutosaveTimer();
        }

        public void ClearDirtyState()
        {
            if (_dirtyZones.Count == 0) return;
            _dirtyZones.Clear();
            OnDirtyChanged?.Invoke();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Save
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Write a snapshot of every dirty zone to disk. Returns the number of
        /// zones saved. Synchronous and complete on return, unconditionally —
        /// this is the "guaranteed immediate flush" half of the split described
        /// in TileOverlayPersistence.Autosave.cs, used by both the Save button /
        /// slot-switch / editor-close call sites AND every EditMode test that
        /// asserts the file exists right after this returns. Never made
        /// asynchronous; see the class-level remarks in Autosave.cs for why.
        /// </summary>
        public int SaveAllDirty()
        {
            // If a debounced background autosave is currently mid-flight, force
            // it to finish FIRST — otherwise its write could land after ours and
            // clobber it with stale data, or we could report zones already
            // claimed (and cleared from _dirtyZones) by that flush as unsaved.
            WaitForInFlightAutosave();

            if (_dirtyZones.Count == 0) return 0;

            EnsureDirectory();
            var snapshot = new List<string>(_dirtyZones);
            int saved = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (SaveZoneInternal(snapshot[i]))
                    saved++;
            }

            if (saved > 0)
            {
                _dirtyZones.Clear();
                OnDirtyChanged?.Invoke();
            }
            return saved;
        }

        public bool SaveZone(string zoneName)
        {
            WaitForInFlightAutosave();

            EnsureDirectory();
            bool ok = SaveZoneInternal(zoneName);
            if (ok && _dirtyZones.Remove(zoneName))
                OnDirtyChanged?.Invoke();
            return ok;
        }

        private bool SaveZoneInternal(string zoneName)
        {
            if (_zones == null || _grid == null || string.IsNullOrEmpty(zoneName)) return false;
            if (!_zones.TryGetZone(zoneName, out var zone)) return false;

            try
            {
                string json = BuildOverlayJson(zone);
                _repository.Write(_worldId, zoneName, json);
                OnZoneSaved?.Invoke(zoneName);
                Debug.Log($"[TileOverlayPersistence] Saved zone '{zoneName}' via repository.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TileOverlayPersistence] Failed to save zone '{zoneName}': {ex}");
                OnSaveFailed?.Invoke(zoneName, ex);
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Apply overrides on world load
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-apply every saved zone override on top of the freshly loaded world.
        /// Called once by <see cref="WorldLoader.LoadFullWorld"/> after the original overlays are painted.
        /// </summary>
    }
}