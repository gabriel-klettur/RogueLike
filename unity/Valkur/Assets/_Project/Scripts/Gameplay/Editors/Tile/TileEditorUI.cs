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
        private System.Action _onShowCollidersClicked;
        private System.Action _onDrawCollidersClicked;
        private System.Action _onEraseCollidersClicked;
        private System.Action _onPerfToggle;
        private System.Action _onShowGridLinesClicked;
        private System.Action _onShowZoneGridClicked;
        private System.Action<TileEditorState.SelectMode> _onSelectModeChanged;
        private System.Action _onCopyClicked;
        private System.Action _onCutClicked;
        private System.Action _onPasteClicked;
        private System.Action _onClearSelectionClicked;
        private System.Action<int> _onMoveToLayerClicked;
        private System.Action<string> _onCollisionTagChanged;
        // M1.8 Layer Jumps
        private System.Action _onShowLayerJumpsClicked;
        private System.Action _onDrawLayerJumpsClicked;
        private System.Action _onEraseLayerJumpsClicked;
        private System.Action<string> _onLayerJumpsTargetChanged;
        // M1.8b Show Player Layer
        private System.Action _onShowPlayerLayerClicked;

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
            System.Action onShowCollidersClicked = null,
            System.Action onDrawCollidersClicked = null,
            System.Action onEraseCollidersClicked = null,
            System.Action onPerfToggle = null,
            System.Action onShowGridLinesClicked = null,
            System.Action onShowZoneGridClicked = null,
            System.Action<TileEditorState.SelectMode> onSelectModeChanged = null,
            System.Action onCopyClicked = null,
            System.Action onCutClicked = null,
            System.Action onPasteClicked = null,
            System.Action onClearSelectionClicked = null,
            System.Action<int> onMoveToLayerClicked = null,
            System.Action<string> onCollisionTagChanged = null,
            // M1.8 Layer Jumps
            System.Action onShowLayerJumpsClicked = null,
            System.Action onDrawLayerJumpsClicked = null,
            System.Action onEraseLayerJumpsClicked = null,
            System.Action<string> onLayerJumpsTargetChanged = null,
            // M1.8b Show Player Layer
            System.Action onShowPlayerLayerClicked = null)
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
            _onShowCollidersClicked = onShowCollidersClicked;
            _onDrawCollidersClicked = onDrawCollidersClicked;
            _onEraseCollidersClicked = onEraseCollidersClicked;
            _onPerfToggle = onPerfToggle;
            _onShowGridLinesClicked = onShowGridLinesClicked;
            _onShowZoneGridClicked = onShowZoneGridClicked;
            _onSelectModeChanged   = onSelectModeChanged;
            _onCopyClicked         = onCopyClicked;
            _onCutClicked          = onCutClicked;
            _onPasteClicked        = onPasteClicked;
            _onClearSelectionClicked = onClearSelectionClicked;
            _onMoveToLayerClicked    = onMoveToLayerClicked;
            _onCollisionTagChanged   = onCollisionTagChanged;
            _onShowLayerJumpsClicked  = onShowLayerJumpsClicked;
            _onDrawLayerJumpsClicked  = onDrawLayerJumpsClicked;
            _onEraseLayerJumpsClicked = onEraseLayerJumpsClicked;
            _onLayerJumpsTargetChanged = onLayerJumpsTargetChanged;
            _onShowPlayerLayerClicked = onShowPlayerLayerClicked;
            for (int i = 0; i < 9; i++) _layerVisibility[i] = true;

            BuildUI();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_refs.MenuBar != null) _refs.MenuBar.SetActive(visible);
            if (_refs.LayerIndicatorPanel != null) _refs.LayerIndicatorPanel.SetActive(visible);
            if (!visible)
                CloseAllDropdowns();
            else
                OpenAllDropdowns();
        }

        private void OpenAllDropdowns()
        {
            foreach (var name in new[] { "tools", "tiles", "layers", "inspector", "colliders", "size", "view" })
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
            }
            RefreshMenuBtnHighlights();
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

        public void ToggleAllPanels()
        {
            var mainPanels = new[] { "tools", "tiles", "layers", "inspector", "colliders", "size", "view" };
            bool allOpen = System.Array.TrueForAll(mainPanels, n => _openDropdowns.Contains(n));
            if (allOpen)
            {
                foreach (var name in mainPanels)
                {
                    SetDropdownOpen(name, false);
                    _openDropdowns.Remove(name);
                }
            }
            else
            {
                foreach (var name in mainPanels)
                {
                    SetDropdownOpen(name, true);
                    _openDropdowns.Add(name);
                }
            }
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
                case "colliders":
                    if (_refs.CollidersDropdown != null) _refs.CollidersDropdown.SetActive(open);
                    break;
                case "size":
                    if (_refs.SizeDropdown != null) _refs.SizeDropdown.SetActive(open);
                    break;
                case "view":
                    if (_refs.ViewDropdown != null) _refs.ViewDropdown.SetActive(open);
                    break;
                case "selectmodes":
                    if (_refs.SelectModesDropdown != null) _refs.SelectModesDropdown.SetActive(open);
                    break;
                case "ux":
                    if (_refs.UxDropdown != null) _refs.UxDropdown.SetActive(open);
                    break;
                case "layerjumps":
                    if (_refs.LayerJumpsDropdown != null) _refs.LayerJumpsDropdown.SetActive(open);
                    break;
            }
        }

        private void RefreshMenuBtnHighlights()
        {
            ApplyMenuBtnStyle(_refs.ToolsMenuBtnImg,     _refs.ToolsMenuBtnTmp,     _openDropdowns.Contains("tools"));
            ApplyMenuBtnStyle(_refs.TilesMenuBtnImg,     _refs.TilesMenuBtnTmp,     _openDropdowns.Contains("tiles"));
            ApplyMenuBtnStyle(_refs.LayersMenuBtnImg,    _refs.LayersMenuBtnTmp,    _openDropdowns.Contains("layers"));
            ApplyMenuBtnStyle(_refs.InspectorMenuBtnImg, _refs.InspectorMenuBtnTmp, _openDropdowns.Contains("inspector"));
            ApplyMenuBtnStyle(_refs.CollidersMenuBtnImg, _refs.CollidersMenuBtnTmp, _openDropdowns.Contains("colliders"));
            ApplyMenuBtnStyle(_refs.SizeMenuBtnImg,      _refs.SizeMenuBtnTmp,      _openDropdowns.Contains("size"));
            ApplyMenuBtnStyle(_refs.ViewMenuBtnImg,      _refs.ViewMenuBtnTmp,      _openDropdowns.Contains("view"));
            ApplyMenuBtnStyle(_refs.UxMenuBtnImg,        _refs.UxMenuBtnTmp,        _openDropdowns.Contains("ux"));
            ApplyMenuBtnStyle(_refs.JumpsMenuBtnImg,     _refs.JumpsMenuBtnTmp,     _openDropdowns.Contains("layerjumps"));
            bool allMainOpen = _openDropdowns.Contains("tools")     && _openDropdowns.Contains("tiles") &&
                               _openDropdowns.Contains("layers")    && _openDropdowns.Contains("inspector") &&
                               _openDropdowns.Contains("colliders") && _openDropdowns.Contains("size") &&
                               _openDropdowns.Contains("view");
            ApplyMenuBtnStyle(_refs.PanelsToggleBtnImg, _refs.PanelsToggleBtnTmp, allMainOpen);
        }

        private static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

    }
}