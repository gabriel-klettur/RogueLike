using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorUI
    {
        // =====================================================================
        // UI CONSTRUCTION
        // =====================================================================

        private partial void BuildUI()
        {
            var canvasGo = new GameObject("TileEditorCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _refs = TileEditorUIBuilder.BuildAll(canvasGo.transform, _state,
                _onToolChanged, _onLayerChanged, _onBrushSizeChanged, ToggleDropdown,
                _onUndo, _onRedo,
                _onShowCollidersClicked, _onDrawCollidersClicked, _onEraseCollidersClicked,
                _onPerfToggle, ToggleAllPanels,
                _onShowGridLinesClicked, _onShowZoneGridClicked,
                _onSelectModeChanged, _onCopyClicked, _onCutClicked,
                _onPasteClicked, _onClearSelectionClicked,
                _onMoveToLayerClicked,
                _onCollisionTagChanged,
                _onShowLayerJumpsClicked, _onDrawLayerJumpsClicked,
                _onEraseLayerJumpsClicked, _onLayerJumpsTargetChanged);

            // Slider value-changed → refresh the "Target: {idx}: {Layer}" label
            // so the user sees the live destination while dragging. The actual
            // commit fires on slider release via MoveLayerSliderRelay (wired
            // inside the builder).
            if (_refs.MoveToLayerSlider != null)
                _refs.MoveToLayerSlider.onValueChanged.AddListener(v =>
                    RefreshMoveToLayerLabel(Mathf.RoundToInt(v)));

            // Wire close callbacks: clicking the ✕ on any panel header closes it cleanly
            // (updates menu-bar button highlights via ToggleDropdown).
            if (_refs.ToolsPanelDrag      != null) _refs.ToolsPanelDrag.OnClose      = () => ToggleDropdown("tools");
            if (_refs.TilesPanelDrag      != null) _refs.TilesPanelDrag.OnClose      = () => ToggleDropdown("tiles");
            if (_refs.LayersPanelDrag     != null) _refs.LayersPanelDrag.OnClose     = () => ToggleDropdown("layers");
            if (_refs.InspectorPanelDrag  != null) _refs.InspectorPanelDrag.OnClose  = () => ToggleDropdown("inspector");
            if (_refs.CollidersPanelDrag  != null) _refs.CollidersPanelDrag.OnClose  = () => ToggleDropdown("colliders");
            if (_refs.SizePanelDrag       != null) _refs.SizePanelDrag.OnClose       = () => ToggleDropdown("size");
            if (_refs.ViewPanelDrag       != null) _refs.ViewPanelDrag.OnClose       = () => ToggleDropdown("view");
            if (_refs.UxPanelDrag         != null) _refs.UxPanelDrag.OnClose         = () => ToggleDropdown("ux");
            // SelectModes panel closes silently on [x] — visibility resumes when the
            // user picks Select again from the Tools panel (RefreshToolHighlights re-shows it).
            if (_refs.SelectModesPanelDrag != null) _refs.SelectModesPanelDrag.OnClose =
                () => { if (_refs.SelectModesDropdown != null) _refs.SelectModesDropdown.SetActive(false);
                        _openDropdowns.Remove("selectmodes"); };
            // Colliders-Layer diagnostic: closes silently on [x]. Visibility is re-
            // computed by the manager's ApplyColliderOverlayVisibility — toggling
            // ShowColliderOverlay off+on brings it back. Not part of _openDropdowns
            // because it isn't user-toggled, only conditionally auto-shown.
            if (_refs.CollidersLayerPanelDrag != null) _refs.CollidersLayerPanelDrag.OnClose =
                () => { if (_refs.CollidersLayerDropdown != null) _refs.CollidersLayerDropdown.SetActive(false); };
            // Layer Jumps dropdown: closes via menu-bar toggle path so the
            // "Jumps" menu-bar button highlight stays in sync (clicking it again
            // re-opens; OnClose just removes from _openDropdowns).
            if (_refs.LayerJumpsPanelDrag != null) _refs.LayerJumpsPanelDrag.OnClose =
                () => ToggleDropdown("layerjumps");

            WireLayerVisibilityButtons();
            WireConfiguratorButton();
            WireTilesetControls();

            if (_catalog != null)
            {
                _currentCategory = "";
                PopulateCategoryTabs();
                PopulateTileGrid(_currentCategory);
                _currentPickerContent = PickerContentKind.Tiles;
                RefreshConfiguratorButtonState();
            }
        }

        private void WireLayerVisibilityButtons()
        {
            for (int i = 0; i < _refs.LayerVisIcons.Count; i++)
            {
                int capIdx = i;
                var visGo = _refs.LayerVisIcons[i].gameObject;
                var btn = visGo.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => ToggleLayerVisibility(capIdx));
            }
        }

        // =====================================================================
        // LAYER VISIBILITY
        // =====================================================================

        private void ToggleLayerVisibility(int layerIdx)
        {
            _layerVisibility[layerIdx] = !_layerVisibility[layerIdx];
            if (layerIdx < _refs.LayerVisIcons.Count)
                _refs.LayerVisIcons[layerIdx].color = _layerVisibility[layerIdx] ? VIS_ON : VIS_OFF;
            var layer = (TilemapLayerSetup.TilemapLayer)layerIdx;
            _onLayerVisibilityChanged?.Invoke(layer, _layerVisibility[layerIdx]);
        }

        public bool IsLayerVisible(int layerIdx)
        {
            return layerIdx >= 0 && layerIdx < 9 && _layerVisibility[layerIdx];
        }

        /// <summary>
        /// Expose the "COLLIDERS LAYER" diagnostic panel GameObject so the manager
        /// can flip its active state in response to editor / Show-Colliders toggles
        /// without reaching into the private <c>_refs</c> struct.
        /// </summary>
        public GameObject GetCollidersLayerPanel() => _refs.CollidersLayerDropdown;

        private void RefreshLayersPanel()
        {
            for (int i = 0; i < _refs.LayerRowBgs.Count && i < 9; i++)
            {
                bool active = i == (int)_state.CurrentLayer;
                _refs.LayerRowBgs[i].color = active ? LAYER_ACTIVE_BG : Color.clear;
                if (i < _refs.LayerRowLabels.Count)
                    _refs.LayerRowLabels[i].color = active ? TEXT_PRIMARY : TEXT_SECONDARY;
            }
        }

        // =====================================================================
        // TILE GRID POPULATION
        // =====================================================================

        private void PopulateCategoryTabs()
        {
            foreach (var btn in _categoryButtons) if (btn != null) Destroy(btn.gameObject);
            _categoryButtons.Clear();
            if (_catalog == null) return;

            AddCategoryTab("All", "");
            foreach (var cat in _catalog.GetCategories())
                AddCategoryTab(cat, cat);
        }

        private void AddCategoryTab(string displayName, string categoryKey)
        {
            var go = CreateUI($"Cat_{displayName}", _refs.CategoryTabsContent);
            var img = go.AddComponent<Image>();
            bool isActive = categoryKey == _currentCategory;
            img.color = isActive ? BTN_ACTIVE : BTN_NORMAL;

            if (isActive)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = ACCENT;
                outline.effectDistance = new Vector2(1f, 1f);
            }

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            string cap = categoryKey;
            btn.onClick.AddListener(() => SelectCategory(cap));

            var tmp = TileEditorUIHelpers.AddCenteredText(go.transform, displayName, 10f, FontStyles.Normal,
                isActive ? ACCENT : TEXT_SECONDARY);
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.margin = new Vector4(6f, 0f, 0f, 0f);

            _categoryButtons.Add(btn);
        }

        private void SelectCategory(string category)
        {
            _currentCategory = category;
            PopulateTileGrid(category);
            _currentPickerContent = PickerContentKind.Tiles;
            PopulateCategoryTabs();
            RefreshConfiguratorButtonState();
        }

        private void PopulateTileGrid(string category)
        {
            foreach (var slot in _tileSlots) if (slot != null) Destroy(slot);
            _tileSlots.Clear();
            _selectedSlotIndex = -1;
            // Old indices reference now-destroyed slots — clear so the next
            // HighlightSelectedSlot doesn't try to "deselect" a stale index
            // that may collide with a different tile in the new grid.
            _highlightedSlotIndex = -1;

            // Reset the unified picker selection state — applies equally to
            // tilesheet (manifest-driven) and legacy categories so their
            // Single/Rect/Multi semantics behave identically. The (r,c) keys
            // we accumulate during a session no longer point at the same
            // tile once a different category is on screen.
            ResetPickerSelectionState();

            if (_catalog == null) return;

            var tiles = string.IsNullOrEmpty(category)
                ? new List<TileCatalog.TileEntry>(_catalog.Entries)
                : _catalog.GetTilesForCategory(category);

            bool isTilesheet = tiles.Count > 0 && tiles[0].gridR >= 0;
            // Cache the flag here so IsCurrentCategoryTilesheet stays O(1) for
            // the rest of the session (zoom slider, dedup toggle, etc.).
            _currentCategoryIsTilesheet = isTilesheet;

            ApplyGridLayoutForCategory(tiles, isTilesheet);
            if (_refs.TilesetControlsRow != null)
                _refs.TilesetControlsRow.SetActive(isTilesheet);

            // Deactivate the grid container while we mass-add children. UGUI
            // would otherwise queue per-child Rebuild marks against the
            // GridLayoutGroup + ContentSizeFitter and re-measure inside
            // CanvasUpdateRegistry at end-of-frame; with the container
            // inactive the layout system skips every queued rebuild and we
            // pay one batched layout pass on SetActive(true). For
            // castle_pandora (~2,688 cells) this turns a multi-frame stall
            // into a single end-of-frame layout. Skipped entirely when the
            // container is missing (BuildUI called before this UI exists).
            var gridGo = _refs.TileGridContent != null ? _refs.TileGridContent.gameObject : null;
            bool wasGridActive = gridGo != null && gridGo.activeSelf;
            if (wasGridActive) gridGo.SetActive(false);

            int slotCount = isTilesheet
                ? PopulateTilesheetSlots(tiles)
                : PopulateLegacySlots(tiles);

            if (wasGridActive) gridGo.SetActive(true);

            if (_refs.TileCountText != null)
                _refs.TileCountText.text = $"{slotCount} tiles" + (string.IsNullOrEmpty(category) ? "" : $" in {category}");
        }

        private void ApplyGridLayoutForCategory(List<TileCatalog.TileEntry> tiles, bool isTilesheet)
        {
            if (_refs.TileGridContent == null) return;
            var gl = _refs.TileGridContent.GetComponent<GridLayoutGroup>();
            if (gl == null) return;
            if (isTilesheet)
            {
                int maxC = 0;
                for (int i = 0; i < tiles.Count; i++)
                    if (tiles[i].gridC > maxC) maxC = tiles[i].gridC;
                gl.constraintCount = maxC + 1;
                gl.cellSize = new Vector2(_tilesetZoom, _tilesetZoom);
                gl.spacing = new Vector2(1f, 1f);
            }
            else
            {
                gl.constraintCount = TILES_GRID_COLS;
                gl.cellSize = new Vector2(TILES_CELL_SIZE, TILES_CELL_SIZE);
                gl.spacing = new Vector2(TILES_GRID_SPACING, TILES_GRID_SPACING);
            }
        }

        private int PopulateLegacySlots(List<TileCatalog.TileEntry> tiles)
        {
            // Synthesise (R, C) for each slot from its index in the picker grid.
            // Legacy categories don't have a manifest; we lay out their tiles in
            // the standard 4-column grid (TILES_GRID_COLS, from
            // TileEditorUIHelpers via `using static`) and treat each slot's
            // visible position as its selection identifier — so the same
            // Single/Rect/Multi handlers in TileEditorUI.TilesetView can drive
            // legacy categories with zero special-casing.
            int cols = TILES_GRID_COLS;

            for (int i = 0; i < tiles.Count; i++)
            {
                var entry = tiles[i];
                int row = i / cols;
                int col = i % cols;

                var go = CreateUI($"Slot_{i}", _refs.TileGridContent);
                var slotImg = go.AddComponent<Image>();
                slotImg.color = SLOT_BG;

                Sprite preview = entry.preview;
                if (preview == null && entry.tile is Tile t) preview = t.sprite;

                // Sprite preview fills the entire cell (no name label — names shown in the SELECTED preview row).
                if (preview != null)
                {
                    var sgo = CreateUI("Prev", go.transform);
                    var sr = sgo.GetComponent<RectTransform>();
                    sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 1f);
                    sr.offsetMin = new Vector2(2f, 2f);
                    sr.offsetMax = new Vector2(-2f, -2f);
                    var si = sgo.AddComponent<Image>();
                    si.sprite = preview; si.preserveAspect = true; si.raycastTarget = false;
                }

                // Selection-highlight overlay (initially hidden). Mirrors the
                // tilesheet view's DragHL pattern so the same RefreshTilesetSelectionVisuals
                // can paint Rect-preview gold + persistent-selection green here too.
                var hgo = CreateUI("DragHL", go.transform);
                var hrt = hgo.GetComponent<RectTransform>();
                hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(1f, 1f);
                hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
                var hImg = hgo.AddComponent<Image>();
                hImg.raycastTarget = false;
                hgo.SetActive(false);

                RegisterPickerSlot(go, row, col, entry, hgo);
                AttachPickerSlotHandlers(go, row, col, i, entry);

                _tileSlots.Add(go);
            }
            return tiles.Count;
        }

        // Slot index whose background is currently painted with SLOT_SELECTED.
        // Tracked so HighlightSelectedSlot only repaints the two slots that
        // actually changed colour (de-select old, select new) — was iterating
        // all 2,688 slots on every click for castle_pandora, calling
        // GetComponent<Image>() per slot.
        private int _highlightedSlotIndex = -1;

        private void HighlightSelectedSlot()
        {
            if (_highlightedSlotIndex == _selectedSlotIndex) return;

            if (_highlightedSlotIndex >= 0 && _highlightedSlotIndex < _tileSlots.Count)
            {
                var prev = _tileSlots[_highlightedSlotIndex];
                if (prev != null)
                {
                    var img = prev.GetComponent<Image>();
                    if (img != null) img.color = SLOT_BG;
                }
            }

            if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _tileSlots.Count)
            {
                var cur = _tileSlots[_selectedSlotIndex];
                if (cur != null)
                {
                    var img = cur.GetComponent<Image>();
                    if (img != null) img.color = SLOT_SELECTED;
                }
            }

            _highlightedSlotIndex = _selectedSlotIndex;
        }
    }
}
