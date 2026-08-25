using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorUI : MonoBehaviour
    {

        public void RefreshToolHighlights()
        {
            foreach (var kvp in _refs.ToolButtonImages)
                kvp.Value.color = kvp.Key == _state.CurrentTool ? BTN_ACTIVE : BTN_NORMAL;
            foreach (var kvp in _refs.ToolButtonTexts)
                kvp.Value.color = kvp.Key == _state.CurrentTool ? ACCENT : TEXT_SECONDARY;

            // SelectModes panel is opt-in. Visibility = (Select active) AND (user opted in).
            // The opt-in flag lives in <c>_openDropdowns</c> via the existing toggle path:
            //   • ToggleDropdown("selectmodes") flips it.
            //   • [x] header close removes it.
            // Leaving it untouched when the tool changes keeps the user's choice sticky:
            // Brush ↔ Select transitions don't reset their preference.
            bool selectActive = _state.CurrentTool == TileEditorState.Tool.Select;
            bool userWants    = _openDropdowns.Contains("selectmodes");
            if (_refs.SelectModesDropdown != null)
                _refs.SelectModesDropdown.SetActive(selectActive && userWants);
        }

        public void RefreshLayerLabel()
        {
            if (_refs.LayerLabel != null)
                _refs.LayerLabel.text = _state.CurrentLayer.ToString();
            if (_refs.LayerIndicator != null)
                _refs.LayerIndicator.text = $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}";
            RefreshLayersPanel();
            if (_refs.ViewLayerSelectedText != null)
                _refs.ViewLayerSelectedText.text = $"  {(int)_state.CurrentLayer}: {_state.CurrentLayer}";
            // Active-layer flip changes the Move-To-Layer no-op guard, so re-evaluate
            // the Move button's enable state.
            RefreshClipboardButtons();
        }

        public void RefreshBrushSizeLabel()
        {
            if (_refs.BrushSizeLabel != null)
                _refs.BrushSizeLabel.text = $"{_state.BrushSize}x{_state.BrushSize}";

            // Sync the slider when the brush size changed from somewhere else
            // (menu-bar −/+, hotkeys). SetValueWithoutNotify avoids re-firing the
            // onValueChanged callback that would re-enter OnBrushSizeChanged.
            if (_refs.BrushSizeSlider != null)
                _refs.BrushSizeSlider.SetValueWithoutNotify(_state.BrushSize);
        }

        /// <summary>
        /// Refresh the visual state (background tint, dot color, ON/OFF label) of the
        /// three Colliders panel toggles. Called by <see cref="TileEditorManager"/> after
        /// any state mutation (Show overlay flip, Draw/Erase mode change). The state is
        /// owned by the manager — this method merely repaints to match.
        /// </summary>
        public void RefreshColliderToggles()
        {
            if (_state == null) return;

            ApplyColliderToggleVisual(_refs.ShowCollidersToggleImg, _refs.ShowCollidersToggleLabel,
                _state.ShowColliderOverlay);
            ApplyColliderToggleVisual(_refs.DrawCollidersToggleImg, _refs.DrawCollidersToggleLabel,
                _state.CurrentColliderMode == TileEditorState.ColliderMode.Draw);
            ApplyColliderToggleVisual(_refs.EraseCollidersToggleImg, _refs.EraseCollidersToggleLabel,
                _state.CurrentColliderMode == TileEditorState.ColliderMode.Erase);

            // The View panel hosts a duplicate "Show Colliders" row that must stay in sync.
            RefreshViewToggles();
        }

        /// <summary>
        /// Repaints the three radio rows of the SelectModes panel to reflect the current
        /// <see cref="TileEditorState.CurrentSelectMode"/>. Mode rows are mutually
        /// exclusive — only one is ON at a time.
        /// </summary>
        public void RefreshSelectModeToggles()
        {
            if (_state == null) return;

            ApplyColliderToggleVisual(_refs.ModeSingleToggleImg, _refs.ModeSingleToggleLabel,
                _state.CurrentSelectMode == TileEditorState.SelectMode.Single);
            ApplyColliderToggleVisual(_refs.ModeRectToggleImg, _refs.ModeRectToggleLabel,
                _state.CurrentSelectMode == TileEditorState.SelectMode.Rect);
            ApplyColliderToggleVisual(_refs.ModeMultiToggleImg, _refs.ModeMultiToggleLabel,
                _state.CurrentSelectMode == TileEditorState.SelectMode.Multi);
        }

        /// <summary>
        /// Refresh the enabled state of the Copy / Cut / Paste / Clear buttons.
        /// Paste is the only clipboard control gated on persistent state (clipboard
        /// contents); the rest are interactable when there is a map selection.
        ///
        /// Note: the Move-To-Layer slider has no companion button to disable —
        /// the slider stays interactable so users can preview the destination
        /// label; the manager's <c>OnMoveToLayerClicked</c> shows a status
        /// message and bails harmlessly when there is no selection or the target
        /// equals the active layer.
        /// </summary>
        public void RefreshClipboardButtons()
        {
            if (_state == null) return;

            bool hasClipboard = _state.Clipboard != null && !_state.Clipboard.IsEmpty;
            bool hasSelection = _state.SelectedCells.Count > 0;

            if (_refs.PasteButton != null) _refs.PasteButton.interactable = hasClipboard;
            if (_refs.CopyButton  != null) _refs.CopyButton.interactable  = hasSelection;
            if (_refs.CutButton   != null) _refs.CutButton.interactable   = hasSelection;
            if (_refs.ClearSelectionButton != null) _refs.ClearSelectionButton.interactable = hasSelection;
        }

        /// <summary>
        /// Refresh the two readouts of the "PLAYER LAYER" diagnostic panel
        /// (bottom-right, visible while ShowPlayerLayer is ON). Cheap string-
        /// builds: called every frame by the manager while the panel is
        /// visible, so allocations are kept to two TextMeshPro.text assignments
        /// and any underlying string formatting Unity does internally.
        /// </summary>
        public void RefreshPlayerLayerPanel(int logicalLayer, string logicalLayerName,
                                            bool[] underfoot, UnityEngine.Vector2Int? cell)
        {
            if (_refs.PlayerLayerLogicalLabel != null)
            {
                _refs.PlayerLayerLogicalLabel.text = logicalLayer < 0
                    ? "Layer: (no player)"
                    : $"Layer: {logicalLayer} — {logicalLayerName}";
            }

            if (_refs.PlayerLayerUnderfootLabel != null)
            {
                if (underfoot == null || underfoot.Length == 0)
                {
                    _refs.PlayerLayerUnderfootLabel.text = "Underfoot: —";
                }
                else
                {
                    var sb = new System.Text.StringBuilder(48);
                    sb.Append("Underfoot: ");
                    bool any = false;
                    for (int i = 0; i < underfoot.Length; i++)
                    {
                        if (!underfoot[i]) continue;
                        if (any) sb.Append(", ");
                        sb.Append(i);
                        any = true;
                    }
                    if (!any) sb.Append("(none)");
                    _refs.PlayerLayerUnderfootLabel.text = sb.ToString();
                }
            }

            if (_refs.PlayerLayerCellLabel != null)
            {
                _refs.PlayerLayerCellLabel.text = cell.HasValue
                    ? $"Cell: ({cell.Value.x}, {cell.Value.y})"
                    : "Cell: —";
            }
        }

        /// <summary>
        /// Repaint the Apply-To-Layer button row in the Colliders panel + the value
        /// label below the header to reflect <see cref="TileEditorState.ActiveCollisionTag"/>.
        /// The active button uses the same red-accent tint as the Show/Draw/Erase toggles
        /// to feel native to the panel; the value label echoes the tag textually so the
        /// user can confirm at a glance which tag the next collider paint will stamp.
        /// </summary>
        public void RefreshCollisionTagPicker()
        {
            if (_state == null) return;

            // The active label shows the canonical CSV — "*" for full mask, "" for
            // empty mask (no layers selected), "0,2,5" for a multi-layer subset.
            string label = string.IsNullOrEmpty(_state.ActiveCollisionTag)
                ? "(none)"
                : _state.ActiveCollisionTag;
            if (_refs.CollisionTagActiveLabel != null)
                _refs.CollisionTagActiveLabel.text = $"Active: {label}";

            if (_refs.CollisionTagButtonImgs == null || _refs.CollisionTagButtonLabels == null) return;

            // Multi-tag picker (M1.10): each digit button highlights independently
            // based on its bit in the active layer mask. The "*" button highlights
            // when the mask is FULL (canonical wildcard); it acts as an all/clear
            // shortcut, not a mutually-exclusive option.
            int mask = TileEditor.CollisionTagMap.LayerMaskFromTag(_state.ActiveCollisionTag);
            // Special case: empty string means "no layers", not the legacy wildcard
            // fallback that LayerMaskFromTag normally returns for empty input.
            if (string.IsNullOrEmpty(_state.ActiveCollisionTag)) mask = 0;

            for (int i = 0; i < _refs.CollisionTagButtonImgs.Length; i++)
            {
                string tag = TileEditor.CollisionTagMap.ValidTags[i];
                bool active;
                if (tag == TileEditor.CollisionTagMap.Wildcard)
                    active = mask == TileEditor.CollisionTagMap.FullLayerMask;
                else if (tag.Length == 1 && tag[0] >= '0' && tag[0] <= '8')
                    active = (mask & (1 << (tag[0] - '0'))) != 0;
                else
                    active = false;

                if (_refs.CollisionTagButtonImgs[i] != null)
                    _refs.CollisionTagButtonImgs[i].color = active
                        ? new Color(COLLIDER_BORDER.r, COLLIDER_BORDER.g, COLLIDER_BORDER.b, 0.30f)
                        : BTN_NORMAL;
                if (_refs.CollisionTagButtonLabels[i] != null)
                    _refs.CollisionTagButtonLabels[i].color = active ? RED_ACCENT : TEXT_PRIMARY;
            }
        }

        /// <summary>
        /// Sync the "Target: {idx}: {Layer}" label below the Move-To-Layer slider.
        /// Called whenever the slider changes or the active layer flips (so the
        /// "differs from active" hint stays truthful).
        /// </summary>
        public void RefreshMoveToLayerLabel(int sliderValue)
        {
            if (_refs.MoveToLayerValueLabel == null) return;
            _refs.MoveToLayerValueLabel.text = TileEditorUIBuilder.FormatMoveToLayerLabel(sliderValue);
        }

        /// <summary>
        /// Repaints the three rows of the View dropdown (Tiles Grid, Zone Grid, Show Colliders)
        /// to match <see cref="TileEditorState"/>. Called whenever any of the three flags is
        /// flipped from either the View panel or — in the case of Show Colliders — the
        /// Colliders panel.
        /// </summary>
        public void RefreshViewToggles()
        {
            if (_state == null) return;

            ApplyColliderToggleVisual(_refs.ShowGridLinesToggleImg, _refs.ShowGridLinesToggleLabel,
                _state.ShowGridLines);
            ApplyColliderToggleVisual(_refs.ShowZoneGridToggleImg, _refs.ShowZoneGridToggleLabel,
                _state.ShowZoneGrid);
            ApplyColliderToggleVisual(_refs.ViewShowCollidersToggleImg, _refs.ViewShowCollidersToggleLabel,
                _state.ShowColliderOverlay);
            ApplyColliderToggleVisual(_refs.ViewShowLayerJumpsToggleImg, _refs.ViewShowLayerJumpsToggleLabel,
                _state.ShowLayerJumpsOverlay);
            ApplyColliderToggleVisual(_refs.ViewShowTileLayerToggleImg, _refs.ViewShowTileLayerToggleLabel,
                _state.ShowTileLayerOverlay);
        }

        // ── Layer Jumps panel (M1.8) ─────────────────────────────────────

        /// <summary>
        /// Repaint the three toggle rows in the LAYER JUMPS panel to mirror the
        /// current <see cref="TileEditorState.ShowLayerJumpsOverlay"/> and
        /// <see cref="TileEditorState.CurrentLayerJumpMode"/>. Also keeps the
        /// duplicate "Show Layer Jumps" row in the View panel in sync.
        /// </summary>
        public void RefreshLayerJumpsToggles()
        {
            if (_state == null) return;

            ApplyColliderToggleVisual(_refs.ShowLayerJumpsToggleImg, _refs.ShowLayerJumpsToggleLabel,
                _state.ShowLayerJumpsOverlay);
            ApplyColliderToggleVisual(_refs.DrawLayerJumpsToggleImg, _refs.DrawLayerJumpsToggleLabel,
                _state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Draw);
            ApplyColliderToggleVisual(_refs.EraseLayerJumpsToggleImg, _refs.EraseLayerJumpsToggleLabel,
                _state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Erase);

            // The View panel hosts a duplicate "Show Layer Jumps" row.
            RefreshViewToggles();
        }

        /// <summary>
        /// Repaint the 9-button TARGET LAYER picker row + the active-value label
        /// to reflect the current <see cref="TileEditorState.ActiveJumpTargetLayer"/>.
        /// </summary>
        public void RefreshLayerJumpsPicker()
        {
            if (_state == null) return;

            if (_refs.LayerJumpsActiveLabel != null)
                _refs.LayerJumpsActiveLabel.text = $"Active: {_state.ActiveJumpTargetLayer}";

            if (_refs.LayerJumpsTargetImgs == null || _refs.LayerJumpsTargetLabels == null) return;
            for (int i = 0; i < _refs.LayerJumpsTargetImgs.Length; i++)
            {
                string target = i.ToString();
                bool active = target == _state.ActiveJumpTargetLayer;
                if (_refs.LayerJumpsTargetImgs[i] != null)
                    _refs.LayerJumpsTargetImgs[i].color = active
                        ? new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.30f)
                        : BTN_NORMAL;
                if (_refs.LayerJumpsTargetLabels[i] != null)
                    _refs.LayerJumpsTargetLabels[i].color = active ? ACCENT : TEXT_PRIMARY;
            }
        }

        private static void ApplyColliderToggleVisual(UnityEngine.UI.Image bg, TMPro.TextMeshProUGUI label, bool on)
        {
            if (bg != null)
            {
                var onColor = new Color(COLLIDER_BORDER.r, COLLIDER_BORDER.g, COLLIDER_BORDER.b, 0.30f);
                bg.color = on ? onColor : BTN_NORMAL;
            }
            if (label != null)
                label.color = on ? RED_ACCENT : TEXT_PRIMARY;

            // Update children: dot color (index 0) and ON/OFF state label (last child).
            // The row layout from BuildColliderToggleRow places: [Dot][Lbl][State].
            if (bg != null)
            {
                var rowT = bg.transform;
                if (rowT.childCount >= 1)
                {
                    var dot = rowT.GetChild(0).GetComponent<UnityEngine.UI.Image>();
                    if (dot != null)
                        dot.color = on ? COLLIDER_BORDER : new Color(0.4f, 0.4f, 0.45f, 1f);
                }
                if (rowT.childCount >= 3)
                {
                    var stateTmp = rowT.GetChild(2).GetComponent<TMPro.TextMeshProUGUI>();
                    if (stateTmp != null)
                    {
                        stateTmp.text = on ? "ON" : "OFF";
                        stateTmp.color = on ? RED_ACCENT : TEXT_MUTED;
                    }
                }
            }
        }

        public void SetStatus(string text)
        {
            if (_refs.StatusText != null)
                _refs.StatusText.text = text;
            else
                Debug.Log($"[TileEditor] {text}");
        }

        /// <summary>Updates the PERF button highlight to reflect probe visibility.</summary>
        public void SetPerfProbeVisible(bool active)
        {
            if (_refs.PerfProbeMenuBtnImg != null)
                _refs.PerfProbeMenuBtnImg.color = active ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.PerfProbeMenuBtnTmp != null)
                _refs.PerfProbeMenuBtnTmp.color = active ? ACCENT : TEXT_PRIMARY;
        }

        // Tracks which content kind currently lives in the picker grid so
        // RefreshTilePicker can skip the wholesale rebuild when the active tool
        // changes without crossing the AutoTileRegion ↔ regular-tools boundary
        // (Brush ↔ Eraser ↔ Fill ↔ Eyedropper ↔ Select all share the same
        // tile-grid content). Rebuilding castle_pandora's 2,688 slots on every
        // tool flip caused a per-frame freeze when the user picked a tile from
        // the map (Eyedropper auto-switches to Brush → fired RefreshTilePicker).
        private enum PickerContentKind { None, Tiles, TerrainChips }
        private PickerContentKind _currentPickerContent = PickerContentKind.None;

        public void RefreshTilePicker()
        {
            bool wantTerrain = _state != null && _state.CurrentTool == TileEditorState.Tool.AutoTileRegion;

            if (wantTerrain)
            {
                if (_currentPickerContent == PickerContentKind.TerrainChips) return;
                PopulateTerrainChips();
                _currentPickerContent = PickerContentKind.TerrainChips;
                return;
            }

            if (_catalog == null) return;
            if (_currentPickerContent == PickerContentKind.Tiles) return;
            PopulateTileGrid(_currentCategory);
            _currentPickerContent = PickerContentKind.Tiles;
        }

        /// <summary>
        /// Force-invalidates the cached picker-content kind so the next
        /// <see cref="RefreshTilePicker"/> rebuilds even if the active tool's
        /// kind hasn't changed. Called by paths that genuinely need a fresh
        /// grid (category change, dedup toggle, F8 activation) — those already
        /// call <see cref="PopulateTileGrid"/> directly, so they merely need
        /// to keep the cache honest.
        /// </summary>
        internal void InvalidatePickerContentCache()
        {
            _currentPickerContent = PickerContentKind.None;
        }

        public void UpdateSelectedTilePreview(Sprite sprite, string tileName)
        {
            if (_refs.SelectedTilePreviewImg != null)
            {
                _refs.SelectedTilePreviewImg.sprite = sprite;
                _refs.SelectedTilePreviewImg.color = sprite != null ? Color.white : SLOT_BG;
            }
            if (_refs.SelectedTileNameText != null)
                _refs.SelectedTileNameText.text = tileName ?? "(none)";
            if (_refs.ViewChoiceImg != null)
            {
                _refs.ViewChoiceImg.sprite = sprite;
                _refs.ViewChoiceImg.color = sprite != null ? Color.white : SLOT_BG;
            }
            if (_refs.ViewChoiceLabel != null)
                _refs.ViewChoiceLabel.text = tileName ?? "";
        }

        public void UpdateViewPanelHovered(Sprite sprite, string name, string layerName)
        {
            if (_refs.ViewHoveredImg != null)
            {
                _refs.ViewHoveredImg.sprite = sprite;
                _refs.ViewHoveredImg.color = sprite != null ? Color.white : SLOT_BG;
            }
            if (_refs.ViewHoveredLabel != null) _refs.ViewHoveredLabel.text = name ?? "";
            if (_refs.ViewLayerHoveredText != null) _refs.ViewLayerHoveredText.text = $"  {layerName}";
        }

        public void UpdateViewPanelSelected(Sprite sprite, string name)
        {
            if (_refs.ViewSelectedImg != null)
            {
                _refs.ViewSelectedImg.sprite = sprite;
                _refs.ViewSelectedImg.color = sprite != null ? Color.white : SLOT_BG;
            }
            if (_refs.ViewSelectedLabel != null) _refs.ViewSelectedLabel.text = name ?? "";
        }

        // =====================================================================
        // UI CONSTRUCTION (delegates to builder)
        // =====================================================================

        private partial void BuildUI();
    }
}