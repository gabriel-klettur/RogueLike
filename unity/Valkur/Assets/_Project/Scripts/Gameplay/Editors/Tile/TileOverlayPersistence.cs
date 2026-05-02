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
    /// Mirrors the Python tile→zone→map model: each zone owns one overlay JSON file with the
    /// same schema as <c>python/data/worlds/base/zones/overlays/{zone}.overlay.json</c>
    /// (a <c>{ "layers": { "Ground": [[...]], ... } }</c> matrix per layer).
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
        // through the second constructor overload; production code keeps using
        // the default-constructed instance, which falls back to the JSON-file
        // backend (byte-compatible with the legacy persistentDataPath/MapOverrides
        // layout). The static helpers in TileOverlayPersistence.Static.cs still
        // hit File.IO directly for back-compat with their many call-sites; those
        // will migrate when their consumers do.
        private readonly ITileOverrideRepository _repository;

        public event Action OnDirtyChanged;
        public event Action<string> OnZoneSaved;
        public event Action<string, Exception> OnSaveFailed;

        public bool HasUnsavedChanges => _dirtyZones.Count > 0;
        public int DirtyZoneCount => _dirtyZones.Count;
        public IReadOnlyCollection<string> DirtyZones => _dirtyZones;

        public static string OverrideDirectory =>
            Path.Combine(Application.persistentDataPath, OVERRIDE_DIR_NAME);

        public TileOverlayPersistence(ZoneManager zones, WorldGridBuilder grid)
            : this(zones, grid, repository: null) { }

        public TileOverlayPersistence(ZoneManager zones, WorldGridBuilder grid,
                                      ITileOverrideRepository repository)
        {
            _zones = zones;
            _grid = grid;
            _repository = repository ?? new JsonFileTileOverrideRepository();
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

        /// <summary>Write a snapshot of every dirty zone to disk. Returns the number of zones saved.</summary>
        public int SaveAllDirty()
        {
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
                _repository.Write(WorldId.Base, zoneName, json);
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