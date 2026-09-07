using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Core.Input;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {
        // ── Toggle ──

        private partial void HandleToggle()
        {
            _state.Active = !_state.Active;
            _ui.SetVisible(_state.Active);

            if (_state.Active)
            {
                _state.CurrentTool = TileEditorState.Tool.Select;
                _state.BrushStrokeCells.Clear();
                _state.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
                _state.CurrentColliderMode = TileEditorState.ColliderMode.None;
                _state.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.None;
                _ui.RefreshToolHighlights();
                _ui.RefreshLayerLabel();
                _ui.RefreshBrushSizeLabel();
                _ui.RefreshTilePicker();
                _ui.RefreshColliderToggles();
                _ui.RefreshLayerJumpsToggles();
                _ui.SetStatus("Tile Editor active. F8 to close. Player movement enabled.");
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(true);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(true);
                if (_gridOverlayGo != null) _gridOverlayGo.SetActive(true);
                // Sync the Map Editor's zone-border overlay with the View panel's
                // "Zone Grid" toggle (default: hidden). Authors enable it explicitly
                // from View → Zone Grid when they want to see zone boundaries.
                ApplyViewOverlayVisibility();
                UpdateBorderToolLabel();
                // Pull persisted terrain data + auto-cure variants. Safe to call here
                // even if no overlays exist on disk — it short-circuits.
                LoadAllTerrainsFromDisk();
                // Camera stays attached so the player can walk and test tile colliders.
                // Middle-mouse pan is still available via HandleCameraPan() → DetachFollow.

                // ── Force uncapped FPS while editing so we can see real perf ──
                _savedTargetFrameRate = Application.targetFrameRate;
                _savedVSyncCount = QualitySettings.vSyncCount;
                Application.targetFrameRate = 120;
                QualitySettings.vSyncCount = 0;
                Debug.Log($"[TileEditor] FPS cap override: target=120 vSync=0 (was {_savedTargetFrameRate}/{_savedVSyncCount})");

                Debug.Log("[TileEditor] Activated (F8)");
            }
            else
            {
                _undo.EndStroke();
                // Flush any unsaved tile edits so changes aren't lost when the editor is closed.
                if (Application.isPlaying) _persistence?.SaveAllDirty();
                _state.SelectedCellPos = null;
                _lastMapCursorCell = null;
                _state.BrushStrokeCells.Clear();
                // Clear the map-side clipboard outline on deactivate so it doesn't
                // linger as a ghost on the next editor session.
                ClearCopiedMapCells();
                // Clear the picker-side copy-highlight (yellow CopyHL overlays) too.
                _ui?.ClearTilesetCopyHighlight();
                HideBrushPreview();
                if (_borderOverlayGo != null) _borderOverlayGo.SetActive(false);
                if (_gridCursor != null) _gridCursor.gameObject.SetActive(false);
                if (_gridOverlayGo != null) _gridOverlayGo.SetActive(false);
                // The PLAYER LAYER panel hides automatically via SetVisible(false)
                // → CloseAllDropdowns. No explicit handling needed here.
                // Release the zone-border overlay request so it hides unless
                // the Map Editor itself is still active.
                if (Valkur.Gameplay.MapEditor.MapEditorManager.HasInstance)
                    Valkur.Gameplay.MapEditor.MapEditorManager.Instance.SetExternalOverlayRequest(false);
                // Keep perf probe state — re-enabled on activate via CreatePerfProbe defaults
                _cameraPan.Reset();
                _doubleClick.Reset();
                // Re-attach in case middle-mouse pan had detached the camera during editing.
                if (Valkur.Gameplay.CameraSetup.Instance != null)
                    Valkur.Gameplay.CameraSetup.Instance.ReattachFollow();

                // Restore previous FPS cap
                Application.targetFrameRate = _savedTargetFrameRate;
                QualitySettings.vSyncCount = _savedVSyncCount;
                Debug.Log($"[TileEditor] FPS cap restored: target={_savedTargetFrameRate} vSync={_savedVSyncCount}");

                Debug.Log("[TileEditor] Deactivated (F8)");
            }
        }

        // ── Input dispatch ──

        private partial void HandleToolShortcuts()
        {
            var tool = _input.PollToolShortcut();
            if (tool.HasValue) OnToolChanged(tool.Value);
        }

        // Shared with Buildings/Map Editor: EditorCameraZoomController.Tick() does
        // the pointer-over-UI gate + wheel poll itself, then feeds
        // CameraSetup.ComputeEditorZoomNext (hybrid PPU-aligned N-step inside the
        // snap range, pure multiplicative above it) into CameraSetup.SetEditorZoom
        // — which is the only path that runs SnapOrthoSize, so the ortho stays on
        // the integer-texel-per-screen-pixel ladder. A bespoke multiplicative-only
        // zoomFactor (the old implementation here) cannot cross the N=2→N=1
        // boundary and gets stuck; see CameraSetup.ComputeEditorZoomNext's doc
        // comment for the regression this replaces.
        private readonly Valkur.Gameplay.Editors.EditorCameraZoomController _cameraZoom
            = new Valkur.Gameplay.Editors.EditorCameraZoomController();

        private partial void HandleCameraZoom()
        {
            _cameraZoom.Tick();
        }

        private partial void HandleUndoRedo()
        {
            int action = _input.PollUndoRedo();
            if (action == 1)
            {
                // BUG 1 fix: close any in-flight stroke first. Without this, pressing
                // Ctrl+Z while still dragging the brush would (a) skip the open batch
                // (it never gets pushed to the undo stack), and (b) leak it into the
                // next stroke when EndStroke fires on mouse-up — producing phantom
                // undo entries that don't match anything visible.
                _undo?.EndStroke();
                var batch = _undo?.Undo();
                if (batch != null) { _persistence?.MarkBatchDirty(batch.Edits); _ui?.SetStatus("Undo"); RegenerateColliderIfNeeded(batch); }
            }
            else if (action == 2)
            {
                _undo?.EndStroke();
                var batch = _undo?.Redo();
                if (batch != null) { _persistence?.MarkBatchDirty(batch.Edits); _ui?.SetStatus("Redo"); RegenerateColliderIfNeeded(batch); }
            }

            // (Ctrl+S removed — every edit path marks its zones dirty, and the debounced
            // autosave flushes them off-thread shortly after the last edit. Closing the
            // editor, switching map slot and leaving Play mode all force that flush to
            // complete first, so a manual save key would be redundant.)

            // ── Select tool: clipboard hotkeys (Ctrl+C/X/V) and Esc to clear ──
            // Gated on CurrentTool so they don't shadow other editors' shortcuts.
            if (_state.CurrentTool == TileEditorState.Tool.Select)
            {
                bool ctrl = Valkur.Core.Input.KeyboardInputManager.IsCtrlHeld();
                if (ctrl && EditorInput.Tool(InputActionCatalog.MapTileEditor, "Copy"))
                    OnCopyClicked();
                else if (ctrl && EditorInput.Tool(InputActionCatalog.MapTileEditor, "Cut"))
                    OnCutClicked();
                else if (ctrl && EditorInput.Tool(InputActionCatalog.MapTileEditor, "Paste"))
                    OnPasteClicked();
                else if (EditorInput.ClosePressed())
                    ClearSelection();
            }
        }

        // ── Mouse input ──

        private partial void HandleMouseInput()
        {
            if (IsPointerOverUiCached())
            {
                // If the user released LMB while the pointer was over UI (e.g. the
                // TILES PICKER), any in-flight drag on the map must be cancelled here —
                // the tool handlers never see the release event because this guard
                // returns early, leaving _state.IsDragging = true indefinitely and
                // preventing the picker from registering subsequent clicks correctly.
                if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame()
                    && _state.IsDragging)
                {
                    // Commit any pending Rect selection (uses the last known drag end).
                    if (_state.CurrentTool == TileEditorState.Tool.Select
                        && _state.CurrentSelectMode == TileEditorState.SelectMode.Rect)
                    {
                        CommitRectSelection();
                        var releaseTilemap = GetCurrentTilemap();
                        if (releaseTilemap != null) UpdateSelectionStatusForUI(releaseTilemap);
                        _ui?.RefreshClipboardButtons();
                        ApplySelectionOverlay();
                    }
                    _state.IsDragging      = false;
                    _state.RectDragStart   = null;
                    _state.RectDragCurrent = null;
                    // Close any brush/eraser stroke that was released over UI too.
                    _undo?.EndStroke();
                    // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.
                }
                return;
            }

            // Collider edit modes (Draw / Erase) take priority over the regular tool
            // dispatch — they always target the Collision tilemap and ignore SelectedTile.
            if (IsColliderEditModeActive())
            {
                HandleColliderInput();
                return;
            }

            // M1.8: Layer-Jumps edit mode is mutually exclusive with Colliders
            // (the toggle handlers enforce it), so this branch only fires when
            // Colliders is OFF and Draw/Erase Jumps is ON.
            if (IsLayerJumpsEditModeActive())
            {
                HandleLayerJumpsInput();
                return;
            }

            var tilemap = GetCurrentTilemap();
            if (tilemap == null) return;

            Vector3Int cellPos = GetCellUnderMouse(tilemap);

            switch (_state.CurrentTool)
            {
                case TileEditorState.Tool.Brush:          HandleBrushInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Eraser:         HandleEraserInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Fill:           HandleFillInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Eyedropper:     HandleEyedropperInput(tilemap, cellPos); break;
                case TileEditorState.Tool.Select:         HandleSelectInput(tilemap, cellPos); break;
                case TileEditorState.Tool.AutoTileRegion: HandleAutoTileRegionInput(tilemap, cellPos); break;
            }
        }

        private bool _brushDiagLogged;

        private void HandleBrushInput(Tilemap tilemap, Vector3Int cellPos)
        {
            // A tool that silently does nothing reads as a broken editor. Say why —
            // but only on an actual click, since this guard runs every frame the
            // Brush is selected and a per-frame message would spam the status line.
            if (_state.SelectedTile == null)
            {
                if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
                    _ui?.SetStatus(TileEditorConstants.NoTileSelectedHint);
                return;
            }

            // AUTO modifier: paint the selected tile's pack TERRAIN instead of its
            // sprite and let the solver pick every cell's variant. Fully separate
            // branch — see HandleAutoBrushInput for the stroke lifecycle.
            if (_state.AutoBrushMode)
            {
                HandleAutoBrushInput(tilemap, cellPos);
                return;
            }

            // Use MouseInputManager so the legacy backend kicks in if the new
            // InputSystem package is dropping OS events.
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
                _state.IsDragging = true;

                if (!_brushDiagLogged)
                {
                    _brushDiagLogged = true;
                    TileEditorDiagnostics.LogBrushDiagnostics(this, tilemap, cellPos, _state.SelectedTile);
                }
            }
            else if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
            }
            else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
                // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.
            }
        }

        /// <summary>Mark all cells in the brush footprint anchored at <paramref name="anchor"/> (cursor cell = top-left, footprint extends right + down).</summary>
        private void AddCellsToBrushStroke(Vector3Int anchor)
        {
            int size = _state.ActiveBrushSize;
            for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    _state.BrushStrokeCells.Add(new Vector3Int(anchor.x + dx, anchor.y - dy, 0));
        }

        // ── AUTO brush modifier ─────────────────────────────────────────────
        // Freehand sibling of TileEditorManager.AutoTileHandlers.cs's
        // AutoTileRegion tool: same TerrainPainter/TerrainMap/TerrainCatalog
        // wiring, driven by a brush drag instead of a click-drag rectangle.

        /// <summary>
        /// Stroke lifecycle for the AUTO-modified Brush: mirrors
        /// <see cref="HandleBrushInput"/>'s press/drag/release shape, but every
        /// paint call goes through <see cref="PaintAutoBrushFootprint"/> (terrain +
        /// solver) instead of <see cref="TileBrush.Paint"/> (raw sprite stamp).
        /// The terrain is re-resolved from <see cref="TileEditorState.SelectedCategory"/>
        /// on every call rather than cached at press time, matching how the plain
        /// brush re-reads <see cref="TileEditorState.SelectedTile"/> every frame —
        /// if resolution fails mid-drag the frame is skipped silently (the initial
        /// press already explained why, and re-spamming the same reason every
        /// frame would flood the status line for no new information).
        /// </summary>
        private void HandleAutoBrushInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                var (ruleset, terrain, reason) = ResolveAutoBrushTerrain();
                if (string.IsNullOrEmpty(terrain))
                {
                    _ui?.SetStatus(reason);
                    return;
                }

                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(tilemap);
                PaintAutoBrushFootprint(tilemap, cellPos, terrain, ruleset, ResolveAutoBrushSheet(ruleset));
                AddCellsToBrushStroke(cellPos);
                _state.IsDragging = true;
            }
            else if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                var (ruleset, terrain, _) = ResolveAutoBrushTerrain();
                if (!string.IsNullOrEmpty(terrain))
                {
                    _state.SelectedCellPos = cellPos;
                    PaintAutoBrushFootprint(tilemap, cellPos, terrain, ruleset, ResolveAutoBrushSheet(ruleset));
                    AddCellsToBrushStroke(cellPos);
                }
            }
            else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
                // Autosave is DEBOUNCED — see the identical comment on HandleBrushInput's
                // release branch. MarkCellDirty/MarkBatchDirty below already armed it.
            }
        }

        /// <summary>
        /// Stamps <paramref name="terrain"/> onto the brush-size×brush-size
        /// footprint anchored at <paramref name="cursorCell"/> (top-left corner,
        /// extends right + down — identical convention to <see cref="TileBrush.Paint"/>)
        /// via <see cref="TerrainPainter.PaintRegion"/>, which also re-resolves the
        /// auto-tile variant for the one-cell ring around the footprint so neighbours
        /// whose corner/cardinal reading just changed get repainted too. Both the
        /// visual <see cref="TileEdit"/>s and the parallel terrain <see cref="MetadataEdit"/>s
        /// are folded into the SAME open undo batch and marked dirty for autosave —
        /// a stroke that recorded tiles without terrain (or vice versa) would leave
        /// <c>TerrainMap</c> and the visual tilemap disagreeing the moment Ctrl+Z fires,
        /// and that mismatch is exactly what gets written to the .overlay.json next.
        /// </summary>
        private void PaintAutoBrushFootprint(
            Tilemap tilemap, Vector3Int cursorCell, string terrain, TilesetRuleset ruleset,
            AutoTileSheetFilter filter)
        {
            var catalog = TerrainCatalogLoader.Load();
            if (catalog == null) return; // already explained on press; nothing to do mid-drag

            int size = _state.AutoBrushSize;
            var rect = new BoundsInt(cursorCell.x, cursorCell.y - (size - 1), 0, size, size, 1);

            // AUTO ANALYSES rather than stamps: it reads the tiles already under the
            // footprint, recovers what terrain each of their corners stands for, and
            // re-resolves so the seam between them is drawn. The selected tile's terrain
            // only breaks ties, which is how the author steers which way a boundary leans.
            var (edits, metadataEdits) = TerrainPainter.ConnectRegion(
                tilemap, rect, ruleset, tileCatalog, TerrainMap, terrain, CanEditCell, filter);

            _undo.RecordEdits(edits);
            _undo.RecordMetadataEdits(metadataEdits);
            _persistence?.MarkBatchDirty(edits);
            // A cell whose terrain changed but whose resolved sprite didn't (or
            // couldn't be resolved at all) produces a MetadataEdit with no matching
            // TileEdit — MarkBatchDirty alone would miss it, so mark those cells
            // dirty individually too (mirrors TileEditorManager.LayerJumps.cs's
            // StampLayerJumpsFootprint, which has the same metadata-only shape).
            if (_persistence != null)
                for (int i = 0; i < metadataEdits.Count; i++)
                    _persistence.MarkCellDirty(metadataEdits[i].Position);
        }

        /// <summary>
        /// Resolves the terrain to paint when AUTO is active, from the pack of the
        /// currently selected tile (<see cref="TileEditorState.SelectedCategory"/>).
        /// Returns the terrain on success, or a null terrain + an explanatory
        /// <see cref="TileEditorConstants"/> hint on failure — no tile selected, no
        /// <c>TerrainCatalog</c> in Resources/, or the pack's ruleset can't actually
        /// be resolved by <see cref="TerrainCatalog.FindBaseRuleset"/> (the same gate
        /// <see cref="TerrainPainter.PaintRegion"/> uses internally — checked here too
        /// so a "success" toast at toggle-time can never be followed by a silent
        /// no-op stroke: a Corner16 pack is BY DEFINITION a transition ruleset, so a
        /// pack whose primary terrain has no separate base ruleset registered would
        /// otherwise look fine here and then paint nothing on every single stroke).
        /// </summary>
        /// <summary>
        /// The sheet every cell of an AUTO stroke resolves from: the picker category of the
        /// tile the author picked. A pack merged from several sheets otherwise scatters all
        /// of them across one stroke — measured on grass_rock, seven different arts cell by
        /// cell. Null when the tile belongs to no category, which restores the unfiltered
        /// behaviour rather than refusing the stroke.
        /// </summary>
        internal AutoTileSheetFilter ResolveAutoBrushSheet(TilesetRuleset pack = null)
        {
            var sprite = (_state.SelectedTile as Tile)?.sprite;
            if (ReferenceEquals(sprite, _autoCacheSprite) && ReferenceEquals(pack, _autoCachePack))
                return _autoCacheFilter;

            _autoCacheFilter = AutoTileSheetFilter.ForSprite(tileCatalog, sprite, pack);
            _autoCacheSprite = sprite;
            _autoCachePack = pack;
            return _autoCacheFilter;
        }

        // ── One-entry memo for the two AUTO lookups ─────────────────────────
        //
        // Both walk the picker catalog, which is 3,765 entries on the shipped project, and
        // both are called on mouse-down AND on every frame of a drag — measured at 1.1 ms a
        // call, so a stroke was paying over 2 ms per frame to re-derive an answer that cannot
        // change while the author holds the button. A stroke uses ONE tile, so a single-entry
        // memo removes all of it. Keyed on the sprite and the pack by REFERENCE, and the
        // terrain memo also on the catalog instance, so a ruleset re-import (which hands back
        // a different TerrainCatalog through TerrainCatalogLoader) is a miss rather than a
        // stale answer.
        private Sprite _autoCacheSprite;
        private TilesetRuleset _autoCachePack;
        private AutoTileSheetFilter _autoCacheFilter;

        private Sprite _autoTerrainCacheSprite;
        private TerrainCatalog _autoTerrainCacheCatalog;
        private (TilesetRuleset Ruleset, string Terrain, string Reason) _autoTerrainCache;
        private bool _autoTerrainCacheValid;

        private (TilesetRuleset Ruleset, string Terrain, string Reason) ResolveAutoBrushTerrain()
        {
            var catalog = TerrainCatalogLoader.Load();
            if (catalog == null)
                return (null, null, "No TerrainCatalog found in Resources/. Configure tilesets first.");

            var sprite = (_state.SelectedTile as Tile)?.sprite;
            if (sprite == null)
                return (null, null, TileEditorConstants.NoTileSelectedHint);

            if (_autoTerrainCacheValid &&
                ReferenceEquals(sprite, _autoTerrainCacheSprite) &&
                ReferenceEquals(catalog, _autoTerrainCacheCatalog))
                return _autoTerrainCache;

            var (ruleset, slot) = AutoBrushPackLookup.FindPackAndSlot(catalog, sprite, tileCatalog);
            string terrain = ruleset == null ? null : AutoBrushPackLookup.TerrainForSlot(ruleset, slot);
            var answer = string.IsNullOrEmpty(terrain)
                ? (null, null, TileEditorConstants.NoRulesetForCategoryHint)
                : (ruleset, terrain, (string)null);

            _autoTerrainCache = answer;
            _autoTerrainCacheSprite = sprite;
            _autoTerrainCacheCatalog = catalog;
            _autoTerrainCacheValid = true;
            return answer;
        }

    }
}