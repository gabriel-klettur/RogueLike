using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Input;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Owns the <see cref="TileEditorState.Tool.AutoTileRegion"/> tool: rectangle
    /// drag → terrain fill → solver-driven variant resolution. Coexists with
    /// the manual brush/eraser/fill paths via the same <c>HandleMouseInput</c>
    /// dispatch — when the user activates the auto-tile tool the picker shows
    /// terrain chips instead of raw sprite tiles, and click+drag builds a
    /// rectangular region instead of painting per cell.
    /// </summary>
    public partial class TileEditorManager
    {
        private TerrainMap _terrainMap;

        /// <summary>
        /// Lazily-initialised terrain layer. The auto-tile tool stamps cells here,
        /// the solver reads from here. Lives parallel to the visual tilemap; survives
        /// tool switches but is reset whenever the editor closes (Fase 5 will persist
        /// it per-zone alongside the overlay JSON).
        /// </summary>
        public TerrainMap TerrainMap => _terrainMap ??= new TerrainMap();

        // Per-frame dispatch entrypoint — invoked from HandleMouseInput's switch.
        private void HandleAutoTileRegionInput(Tilemap tilemap, Vector3Int cellPos)
        {
            // Mouse press → begin drag.
            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                if (string.IsNullOrEmpty(_state.SelectedTerrain))
                {
                    _ui?.SetStatus("Pick a terrain chip in the Tiles panel first.");
                    return;
                }
                _state.RegionDragStart   = cellPos;
                _state.RegionDragCurrent = cellPos;
                _state.IsDragging        = true;
                ApplyRegionDragOverlay();
                return;
            }

            // Held → update live preview.
            if (MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.RegionDragCurrent = cellPos;
                ApplyRegionDragOverlay();
                return;
            }

            // Released → commit the rect.
            if (MouseInputManager.WasLeftMouseButtonReleasedThisFrame() && _state.IsDragging)
            {
                CommitAutoTileRegion(tilemap);
                _state.IsDragging        = false;
                _state.RegionDragStart   = null;
                _state.RegionDragCurrent = null;
                ApplyRegionDragOverlay();
                if (Application.isPlaying) _persistence?.SaveAllDirty();
            }
        }

        private void CommitAutoTileRegion(Tilemap tilemap)
        {
            if (!_state.RegionDragStart.HasValue || !_state.RegionDragCurrent.HasValue) return;
            var catalog = TerrainCatalogLoader.Load();
            if (catalog == null)
            {
                _ui?.SetStatus("No TerrainCatalog found in Resources/. Configure tilesets first.");
                return;
            }

            int xMin = Mathf.Min(_state.RegionDragStart.Value.x, _state.RegionDragCurrent.Value.x);
            int yMin = Mathf.Min(_state.RegionDragStart.Value.y, _state.RegionDragCurrent.Value.y);
            int xMax = Mathf.Max(_state.RegionDragStart.Value.x, _state.RegionDragCurrent.Value.x);
            int yMax = Mathf.Max(_state.RegionDragStart.Value.y, _state.RegionDragCurrent.Value.y);

            int w = (xMax - xMin) + 1;
            int h = (yMax - yMin) + 1;
            var rect = new BoundsInt(xMin, yMin, 0, w, h, 1);

            _undo.StartStroke(tilemap);
            var edits = TerrainPainter.PaintRegion(
                tilemap, rect, _state.SelectedTerrain, catalog, TerrainMap, CanEditCell);
            _undo.RecordEdits(edits);
            _undo.EndStroke();
            _persistence?.MarkBatchDirty(edits);

            _ui?.SetStatus(edits.Count > 0
                ? $"Auto-tile painted {edits.Count} cell(s) of '{_state.SelectedTerrain}' in {w}×{h} region."
                : "No cells changed (terrain has no ruleset, or rect is outside editable area).");
        }

        /// <summary>
        /// Mirrors <see cref="ApplySelectionOverlay"/>: pushes the live region drag
        /// rect to the GL overlay so the yellow preview rectangle re-renders even
        /// when the cursor sits over UI.
        /// </summary>
        private void ApplyRegionDragOverlay()
        {
            if (_gridOverlay == null) return;
            _gridOverlay.SetRectDragPreview(_state.RegionDragStart, _state.RegionDragCurrent);
        }

        // ── Terrain selection (called by the picker chips) ───────────────────

        public void SelectTerrain(string terrain)
        {
            _state.SelectedTerrain = terrain ?? string.Empty;
            _ui?.SetStatus(string.IsNullOrEmpty(_state.SelectedTerrain)
                ? "No terrain selected."
                : $"Terrain selected: {_state.SelectedTerrain}");
        }

        // ── Disk → memory: terrain matrix load + auto-curation ───────────────

        /// <summary>
        /// Scan every override file under <c>persistentDataPath/MapOverrides</c>
        /// (for the active world) and stream the optional <c>terrains</c> matrix
        /// into the in-memory <see cref="TerrainMap"/>. Then re-resolve auto-tile
        /// variants on the visual tilemap so cells whose ruleset changed since the
        /// last save get the up-to-date sprite (auto-curation).
        ///
        /// Safe to call multiple times — terrain entries are idempotent. No-op when
        /// persistence isn't ready or the override directory is missing.
        /// </summary>
        public int LoadAllTerrainsFromDisk()
        {
            if (_persistence == null || worldGridBuilder == null) return 0;
            var zoneManager = FindObjectOfType<Valkur.Gameplay.World.ZoneManager>();
            if (zoneManager == null) return 0;

            string dir = TileOverlayPersistence.OverrideDirectoryForWorld(_persistence.WorldId);
            if (!System.IO.Directory.Exists(dir)) return 0;

            var files = System.IO.Directory.GetFiles(dir, "*.overlay.json");
            if (files.Length == 0) return 0;

            var terrainMap = TerrainMap;
            var tagSink = CollisionTags;
            var jumpSink = LayerJumps;
            var catalog = TerrainCatalogLoader.Load();
            int totalLoaded = 0;
            int totalCured = 0;
            int totalTags = 0;
            int totalJumps = 0;

            for (int i = 0; i < files.Length; i++)
            {
                string zoneName = System.IO.Path.GetFileNameWithoutExtension(files[i]);
                if (zoneName.EndsWith(".overlay", System.StringComparison.OrdinalIgnoreCase))
                    zoneName = zoneName.Substring(0, zoneName.Length - ".overlay".Length);
                if (!zoneManager.TryGetZone(zoneName, out var zone)) continue;

                int written = Valkur.Gameplay.World.OverlayLoader.ApplyTerrainsFromPath(
                    files[i], terrainMap, zone.gridOffset.x, zone.gridOffset.y);
                // Tags + jumps live next to the terrain matrix — load them in the
                // same sweep so an F8-open that arrived after WorldLoader's
                // ApplyAllOverrides still seeds the maps. Idempotent: re-applying
                // the same matrix just overwrites identical entries.
                totalTags += Valkur.Gameplay.World.OverlayLoader.ApplyCollisionTagsFromPath(
                    files[i], tagSink, zone.gridOffset.x, zone.gridOffset.y);
                totalJumps += Valkur.Gameplay.World.OverlayLoader.ApplyLayerJumpsFromPath(
                    files[i], jumpSink, zone.gridOffset.x, zone.gridOffset.y);

                if (written == 0) continue;
                totalLoaded += written;

                if (catalog == null) continue;
                int w = zoneManager.ZoneWidthTiles;
                int h = zoneManager.ZoneHeightTiles;
                var groundTilemap = worldGridBuilder.GetTilemap(Valkur.Gameplay.World.TilemapLayerSetup.TilemapLayer.Ground);
                if (groundTilemap == null) continue;

                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var cell = new Vector3Int(zone.gridOffset.x + x, zone.gridOffset.y + y, 0);
                    var edit = TerrainPainter.Resolve(groundTilemap, cell, catalog, terrainMap);
                    if (edit.HasValue) totalCured++;
                }
            }

            if (totalLoaded > 0)
                Debug.Log($"[TileEditor] Loaded {totalLoaded} terrain cells from disk; " +
                          $"auto-cured {totalCured} variant(s).");
            if (totalTags > 0 || totalJumps > 0)
                Debug.Log($"[TileEditor] Loaded {totalTags} collision tag(s) and " +
                          $"{totalJumps} layer-jump(s) from disk.");

            // M2.1: rebake sub-tilemap composites so per-layer physics reflects the
            // freshly streamed tag map. Inexpensive when nothing was loaded.
            if (totalTags > 0 && Application.isPlaying)
                Valkur.Gameplay.World.Layering.WorldCollisionBaker.EnsureExists().ScheduleRebake();
            return totalLoaded;
        }
    }
}
