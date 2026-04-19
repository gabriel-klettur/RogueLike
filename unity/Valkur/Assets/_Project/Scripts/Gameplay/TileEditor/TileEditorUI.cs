using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Tile editor UI: public API, state management, tile grid population, layer visibility.
    /// Construction delegated to TileEditorUIBuilder. Design tokens in TileEditorUIHelpers.
    /// Menu bar + dropdown panel architecture: only a slim 30px bar is always visible;
    /// Tools, Tiles, Layers, Inspector toggle as floating dropdown panels.
    /// </summary>
    public partial class TileEditorUI : MonoBehaviour
    {
        // ── Callbacks ──
        private TileEditorState _state;
        private TileCatalog _catalog;
        private System.Action<TileCatalog.TileEntry> _onTileSelected;
        private System.Action<TileEditorState.Tool> _onToolChanged;
        private System.Action<TilemapLayerSetup.TilemapLayer> _onLayerChanged;
        private System.Action<int> _onBrushSizeChanged;
        private System.Action<TilemapLayerSetup.TilemapLayer, bool> _onLayerVisibilityChanged;
        private System.Action _onUndo;
        private System.Action _onRedo;
        private System.Action _onSave;

        // ── UI refs from builder ──
        private TileEditorUIBuilder.UIRefs _refs;
        private readonly List<Button> _categoryButtons = new List<Button>();
        private string _currentCategory = "";
        private readonly List<GameObject> _tileSlots = new List<GameObject>();
        private int _selectedSlotIndex = -1;
        private readonly bool[] _layerVisibility = new bool[9];

        // ── Dropdown state ──
        // Each panel opens / closes independently; we keep a set of open panel keys
        // so the four menu buttons act as independent toggles.
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        public void Initialize(TileEditorState state, TileCatalog catalog,
            System.Action<TileCatalog.TileEntry> onTileSelected,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged,
            System.Action<TilemapLayerSetup.TilemapLayer, bool> onLayerVisibilityChanged = null,
            System.Action onUndo = null,
            System.Action onRedo = null,
            System.Action onSave = null)
        {
            _state = state;
            _catalog = catalog;
            _onTileSelected = onTileSelected;
            _onToolChanged = onToolChanged;
            _onLayerChanged = onLayerChanged;
            _onBrushSizeChanged = onBrushSizeChanged;
            _onLayerVisibilityChanged = onLayerVisibilityChanged;
            _onUndo = onUndo;
            _onRedo = onRedo;
            _onSave = onSave;
            for (int i = 0; i < 9; i++) _layerVisibility[i] = true;

            BuildUI();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_refs.MenuBar != null) _refs.MenuBar.SetActive(visible);
            if (_refs.LayerIndicatorPanel != null) _refs.LayerIndicatorPanel.SetActive(visible);
            if (!visible) CloseAllDropdowns();
        }

        public void ToggleDropdown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_openDropdowns.Contains(name))
            {
                SetDropdownOpen(name, false);
                _openDropdowns.Remove(name);
            }
            else
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
            }
            RefreshMenuBtnHighlights();
        }

        public void CloseAllDropdowns()
        {
            foreach (var name in _openDropdowns)
                SetDropdownOpen(name, false);
            _openDropdowns.Clear();
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            switch (name)
            {
                case "tools":
                    if (_refs.ToolsDropdown != null) _refs.ToolsDropdown.SetActive(open);
                    break;
                case "tiles":
                    if (_refs.TilesDropdown != null) _refs.TilesDropdown.SetActive(open);
                    break;
                case "layers":
                    if (_refs.LayersDropdown != null) _refs.LayersDropdown.SetActive(open);
                    break;
                case "inspector":
                    if (_refs.InspectorDropdown != null) _refs.InspectorDropdown.SetActive(open);
                    break;
            }
        }

        private void RefreshMenuBtnHighlights()
        {
            if (_refs.ToolsMenuBtnImg != null)
                _refs.ToolsMenuBtnImg.color = _openDropdowns.Contains("tools") ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.TilesMenuBtnImg != null)
                _refs.TilesMenuBtnImg.color = _openDropdowns.Contains("tiles") ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.LayersMenuBtnImg != null)
                _refs.LayersMenuBtnImg.color = _openDropdowns.Contains("layers") ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.InspectorMenuBtnImg != null)
                _refs.InspectorMenuBtnImg.color = _openDropdowns.Contains("inspector") ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
        }

        public void RefreshToolHighlights()
        {
            foreach (var kvp in _refs.ToolButtonImages)
                kvp.Value.color = kvp.Key == _state.CurrentTool ? BTN_ACTIVE : BTN_NORMAL;
            foreach (var kvp in _refs.ToolButtonTexts)
                kvp.Value.color = kvp.Key == _state.CurrentTool ? ACCENT : TEXT_SECONDARY;
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
        }

        public void RefreshBrushSizeLabel()
        {
            if (_refs.BrushSizeLabel != null)
                _refs.BrushSizeLabel.text = $"{_state.BrushSize}x{_state.BrushSize}";
        }

        public void SetStatus(string text)
        {
            if (_refs.StatusText != null) _refs.StatusText.text = text;
        }

        /// <summary>
        /// Reflect the persistence dirty-state in the Save button colour and the counter beneath it.
        /// Bright accent + zone count when there are unsaved changes; muted when clean.
        /// </summary>
        public void SetDirtyState(bool isDirty, int dirtyZoneCount)
        {
            if (_refs.SaveButtonImg != null)
                _refs.SaveButtonImg.color = isDirty ? ACCENT_BG : BTN_NORMAL;
            if (_refs.SaveButtonLabel != null)
                _refs.SaveButtonLabel.color = isDirty ? ACCENT : TEXT_SECONDARY;
            if (_refs.DirtyIndicatorText != null)
                _refs.DirtyIndicatorText.text = isDirty
                    ? (dirtyZoneCount == 1 ? "1 zone *" : $"{dirtyZoneCount} zones *")
                    : string.Empty;
        }

        public void RefreshTilePicker()
        {
            if (_catalog == null) return;
            PopulateTileGrid(_currentCategory);
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
