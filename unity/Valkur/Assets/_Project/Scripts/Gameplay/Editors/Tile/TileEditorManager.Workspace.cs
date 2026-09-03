using System;
using UnityEngine;
using Valkur.Core.Editors;
using Valkur.Gameplay.Editors.Workspace;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Tile Editor (F8) — what it remembers between sessions.
    ///
    /// Second adopter of <see cref="IProvidesWorkspaceState"/>, and the one that decides
    /// whether the layer holds: 78 files, 15k LOC, and the only editor whose state is big
    /// enough that "just persist everything" is the wrong answer.
    ///
    /// Three things are deliberately NOT persisted, and each is a decision rather than an
    /// omission — see the individual notes below: the collider / layer-jump paint modes,
    /// the clipboard, and the camera zoom.
    /// </summary>
    public partial class TileEditorManager : IProvidesWorkspaceState
    {
        // On-disk keys: string literals, never nameof(). Renaming a C# field must not
        // silently orphan an author's saved value.
        private const string WS_TOOL          = "tool";
        private const string WS_LAYER         = "layer";
        private const string WS_BRUSH_SIZE    = "brushSize";
        private const string WS_AUTO_BRUSH    = "autoBrush";
        private const string WS_CATEGORY      = "tileCategory";
        private const string WS_TILE_NAME     = "tileName";
        private const string WS_TERRAIN       = "terrain";
        private const string WS_SELECT_MODE   = "selectMode";
        private const string WS_COLLISION_TAG = "collisionTag";
        private const string WS_JUMP_TARGET   = "jumpTargetLayer";
        private const string WS_SHOW_GRID     = "showGridLines";
        private const string WS_SHOW_TILELAYER = "showTileLayer";
        private const string WS_SHOW_ZONEGRID = "showZoneGrid";
        private const string WS_SHOW_COLLIDERS = "showColliders";
        private const string WS_SHOW_JUMPS    = "showLayerJumps";

        /// <summary>The selection kind this editor writes. See <see cref="EditorSelectionRecord"/>.</summary>
        private const string WS_SELECTION_CELL = "cell";

        // ── IProvidesWorkspaceState ─────────────────────────────────────────────

        /// <summary>
        /// The UI canvas, not the object <c>SetVisible</c> toggles.
        ///
        /// This editor has no single visible root — <c>TileEditorUI.SetVisible</c> flips the
        /// menu bar and the layer indicator separately, and the eight panels are dropdowns
        /// underneath the canvas. Walking anything narrower would silently miss panels,
        /// which is the kind of failure that looks like "persistence is flaky" rather than
        /// like a bug.
        /// </summary>
        public Transform WorkspaceRoot => _ui != null ? _ui.CanvasRoot : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null || _state == null) return;

            ws.SetString(WS_TOOL,  _state.CurrentTool.ToString());
            ws.SetString(WS_LAYER, _state.CurrentLayer.ToString());
            ws.SetInt(WS_BRUSH_SIZE, _state.BrushSize);
            ws.SetBool(WS_AUTO_BRUSH, _state.AutoBrushMode);

            // A tile's stable identity is (category, name). Neither half alone is unique:
            // TileCategoryManifest generation reuses names across packs, and a category
            // holds thousands of cells. SelectedCatalogIndex is deliberately NOT stored —
            // it is a position in a list a re-import reorders, and it would fail silently
            // by selecting some other tile.
            ws.SetString(WS_CATEGORY, _state.SelectedCategory ?? string.Empty);
            ws.SetString(WS_TILE_NAME, ResolveSelectedTileName());

            ws.SetString(WS_TERRAIN, _state.SelectedTerrain ?? string.Empty);
            ws.SetString(WS_SELECT_MODE, _state.CurrentSelectMode.ToString());
            ws.SetString(WS_COLLISION_TAG, _state.ActiveCollisionTag ?? CollisionTagMap.Wildcard);
            ws.SetString(WS_JUMP_TARGET, _state.ActiveJumpTargetLayer ?? "0");

            ws.SetBool(WS_SHOW_GRID,      _state.ShowGridLines);
            ws.SetBool(WS_SHOW_TILELAYER, _state.ShowTileLayerOverlay);
            ws.SetBool(WS_SHOW_ZONEGRID,  _state.ShowZoneGrid);
            ws.SetBool(WS_SHOW_COLLIDERS, _state.ShowColliderOverlay);
            ws.SetBool(WS_SHOW_JUMPS,     _state.ShowLayerJumpsOverlay);

            CaptureSelectedCell(ws);

            // NOT captured, on purpose:
            //
            // • CurrentColliderMode / CurrentLayerJumpMode — HandleToggle resets both to
            //   None on every activate. That is a safety decision, not an oversight:
            //   opening F8 straight into a destructive paint mode is how an author paints
            //   collision over a map they only meant to look at. Restoring them would
            //   override a deliberate reset.
            // • Clipboard — TileEditorState documents it as OS-clipboard semantics, lost
            //   when the editor closes. Persisting it would also mean serializing tile
            //   references, which are assets, not data.
            // • Camera zoom — the shared EditorCameraZoomController keeps no level to read,
            //   and writing orthographicSize back collides with the pixel-snap ladder
            //   CLAUDE.md warns about: SnapOrthoSize keeps ortho on rungs where one art
            //   texel is a whole number of screen pixels, and a restored value between
            //   rungs makes every tile on screen crawl.
            // • Drag transients (IsDragging, BrushStrokeCells, Rect/Region drags) — state
            //   of a gesture that is over.
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null || _state == null) return;

            RestoreViewToggles(ws);
            RestoreBrush(ws);
            RestoreTileSelection(ws);   // may auto-switch the tool — must precede RestoreTool
            RestoreTool(ws);            // …which then puts the authored tool back
            RestoreSelectedCell(ws);
        }

        // ── Capture helpers ─────────────────────────────────────────────────────

        private string ResolveSelectedTileName()
        {
            if (_state.SelectedTile == null || tileCatalog == null) return string.Empty;

            var entries = tileCatalog.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].tile == _state.SelectedTile) return entries[i].tileName ?? string.Empty;

            return string.Empty;
        }

        private void CaptureSelectedCell(EditorWorkspace ws)
        {
            if (_state.SelectedCellPos.HasValue)
            {
                var c = _state.SelectedCellPos.Value;
                ws.selection.Set(WS_SELECTION_CELL,
                    $"{c.x},{c.y},{c.z}",
                    EditorWorkspaceContext.CurrentMapSlot,
                    EditorWorkspaceContext.CurrentZone);
            }
            else
            {
                ws.selection.Clear();
            }
        }

        // ── Restore helpers ─────────────────────────────────────────────────────

        private void RestoreViewToggles(EditorWorkspace ws)
        {
            _state.ShowGridLines        = ws.GetBool(WS_SHOW_GRID,      _state.ShowGridLines);
            _state.ShowTileLayerOverlay = ws.GetBool(WS_SHOW_TILELAYER, _state.ShowTileLayerOverlay);
            _state.ShowZoneGrid         = ws.GetBool(WS_SHOW_ZONEGRID,  _state.ShowZoneGrid);
            _state.ShowColliderOverlay  = ws.GetBool(WS_SHOW_COLLIDERS, _state.ShowColliderOverlay);
            _state.ShowLayerJumpsOverlay = ws.GetBool(WS_SHOW_JUMPS,    _state.ShowLayerJumpsOverlay);

            // The flags alone change nothing on screen — the overlays read them through
            // this one call, which is also what HandleToggle uses.
            ApplyViewOverlayVisibility();
            _ui?.RefreshViewToggles();
            _ui?.RefreshColliderToggles();
            _ui?.RefreshLayerJumpsToggles();
        }

        private void RestoreBrush(EditorWorkspace ws)
        {
            OnBrushSizeChanged(ws.GetInt(WS_BRUSH_SIZE, _state.BrushSize));
            _state.AutoBrushMode = ws.GetBool(WS_AUTO_BRUSH, _state.AutoBrushMode);

            if (Enum.TryParse(ws.GetString(WS_LAYER, null), out TilemapLayerSetup.TilemapLayer layer))
                OnLayerChanged(layer);

            if (Enum.TryParse(ws.GetString(WS_SELECT_MODE, null), out TileEditorState.SelectMode selectMode))
            {
                _state.CurrentSelectMode = selectMode;
                _ui?.RefreshSelectModeToggles();
            }

            // A tag outside the valid set would be stamped into CollisionTagMap and then
            // fail to resolve — validate against the registry, not against "is it empty".
            string tag = ws.GetString(WS_COLLISION_TAG, null);
            if (!string.IsNullOrEmpty(tag) && Array.IndexOf(CollisionTagMap.ValidTags, tag) >= 0)
            {
                _state.ActiveCollisionTag = tag;
                _ui?.RefreshCollisionTagPicker();
            }

            // A jump target is a visual layer index; anything else would send an entity to
            // a layer that does not exist.
            string jump = ws.GetString(WS_JUMP_TARGET, null);
            if (!string.IsNullOrEmpty(jump)
                && int.TryParse(jump, out int jumpIndex)
                && jumpIndex >= 0
                && jumpIndex <= (int)TilemapLayerSetup.TilemapLayer.ObjectsHigh)
            {
                _state.ActiveJumpTargetLayer = jump;
                _ui?.RefreshLayerJumpsPicker();
            }

            _state.SelectedTerrain = ws.GetString(WS_TERRAIN, _state.SelectedTerrain ?? string.Empty);
        }

        private void RestoreTileSelection(EditorWorkspace ws)
        {
            string category = ws.GetString(WS_CATEGORY, null);
            string tileName = ws.GetString(WS_TILE_NAME, null);
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(tileName)) return;
            if (tileCatalog == null) return;

            var entries = tileCatalog.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].category != category || entries[i].tileName != tileName) continue;
                OnTileSelected(entries[i]);
                return;
            }

            // The pack was re-imported, renamed or removed. Leaving nothing selected is
            // correct: TileEditorConstants.NoTileSelectedHint already tells the author what
            // to do, whereas picking "the nearest tile" would have them painting something
            // they never chose. Not a warning — a re-import between sessions is ordinary.
            _ui?.SetStatus($"El tile '{tileName}' de '{category}' ya no existe. Elige otro.");
        }

        private void RestoreTool(EditorWorkspace ws)
        {
            if (!Enum.TryParse(ws.GetString(WS_TOOL, null), out TileEditorState.Tool tool)) return;

            // OnToolChanged(Select) while Select is ALREADY active does not select the
            // tool — it toggles the SelectModes dropdown. HandleToggle forces Select on
            // every activate, so restoring Select through it would open a panel the author
            // had closed instead of restoring the tool. Nothing to do in that case anyway.
            if (tool == TileEditorState.Tool.Select && _state.CurrentTool == TileEditorState.Tool.Select)
                return;

            OnToolChanged(tool);
        }

        private void RestoreSelectedCell(EditorWorkspace ws)
        {
            var record = ws.selection;
            if (record == null || !record.HasValue) return;
            if (record.type != WS_SELECTION_CELL) return;

            // Discarded up front when the map slot or zone differs — the same cell
            // coordinate means a different place in another slot, so resolving it would
            // put the green outline somewhere the author never clicked.
            if (!record.AppliesTo(EditorWorkspaceContext.CurrentMapSlot,
                                  EditorWorkspaceContext.CurrentZone))
                return;

            var parts = record.id.Split(',');
            if (parts.Length != 3) return;
            if (!int.TryParse(parts[0], out int x)) return;
            if (!int.TryParse(parts[1], out int y)) return;
            if (!int.TryParse(parts[2], out int z)) return;

            _state.SelectedCellPos = new Vector3Int(x, y, z);
        }
    }
}
