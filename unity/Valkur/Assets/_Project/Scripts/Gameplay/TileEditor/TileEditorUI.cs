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

        // ── UI refs from builder ──
        private TileEditorUIBuilder.UIRefs _refs;
        private readonly List<Button> _categoryButtons = new List<Button>();
        private string _currentCategory = "";
        private readonly List<GameObject> _tileSlots = new List<GameObject>();
        private int _selectedSlotIndex = -1;
        private readonly bool[] _layerVisibility = new bool[9];

        // ── Dropdown state ──
        private string _openDropdown;

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        public void Initialize(TileEditorState state, TileCatalog catalog,
            System.Action<TileCatalog.TileEntry> onTileSelected,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged)
        {
            _state = state;
            _catalog = catalog;
            _onTileSelected = onTileSelected;
            _onToolChanged = onToolChanged;
            _onLayerChanged = onLayerChanged;
            _onBrushSizeChanged = onBrushSizeChanged;
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
            if (_openDropdown == name)
            {
                CloseAllDropdowns();
                return;
            }
            CloseAllDropdowns();
            _openDropdown = name;
            switch (name)
            {
                case "tools":
                    if (_refs.ToolsDropdown != null) _refs.ToolsDropdown.SetActive(true);
                    break;
                case "tiles":
                    if (_refs.TilesDropdown != null) _refs.TilesDropdown.SetActive(true);
                    break;
                case "layers":
                    if (_refs.LayersDropdown != null) _refs.LayersDropdown.SetActive(true);
                    break;
                case "inspector":
                    if (_refs.InspectorDropdown != null) _refs.InspectorDropdown.SetActive(true);
                    break;
            }
            RefreshMenuBtnHighlights();
        }

        public void CloseAllDropdowns()
        {
            _openDropdown = null;
            if (_refs.ToolsDropdown != null) _refs.ToolsDropdown.SetActive(false);
            if (_refs.TilesDropdown != null) _refs.TilesDropdown.SetActive(false);
            if (_refs.LayersDropdown != null) _refs.LayersDropdown.SetActive(false);
            if (_refs.InspectorDropdown != null) _refs.InspectorDropdown.SetActive(false);
            RefreshMenuBtnHighlights();
        }

        private void RefreshMenuBtnHighlights()
        {
            if (_refs.ToolsMenuBtnImg != null)
                _refs.ToolsMenuBtnImg.color = _openDropdown == "tools" ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.TilesMenuBtnImg != null)
                _refs.TilesMenuBtnImg.color = _openDropdown == "tiles" ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.LayersMenuBtnImg != null)
                _refs.LayersMenuBtnImg.color = _openDropdown == "layers" ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.InspectorMenuBtnImg != null)
                _refs.InspectorMenuBtnImg.color = _openDropdown == "inspector" ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
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
