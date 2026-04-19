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
        private System.Action _onShowCollidersClicked;
        private System.Action _onDrawCollidersClicked;
        private System.Action _onEraseCollidersClicked;
        private System.Action _onPerfToggle;

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
            System.Action onSave = null,
            System.Action onShowCollidersClicked = null,
            System.Action onDrawCollidersClicked = null,
            System.Action onEraseCollidersClicked = null,
            System.Action onPerfToggle = null)
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
            _onShowCollidersClicked = onShowCollidersClicked;
            _onDrawCollidersClicked = onDrawCollidersClicked;
            _onEraseCollidersClicked = onEraseCollidersClicked;
            _onPerfToggle = onPerfToggle;
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
            foreach (var name in new[] { "tools", "tiles", "layers", "inspector", "colliders", "size" })
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
            RefreshBrushSizePresets();
        }

        /// <summary>
        /// Re-tint the 1x1..5x5 preset buttons in the Size dropdown so the active
        /// brush size pops in accent. State is owned by <see cref="TileEditorState"/>;
        /// this method just repaints to match.
        /// </summary>
        public void RefreshBrushSizePresets()
        {
            if (_refs.BrushSizePresetImgs == null) return;
            for (int i = 0; i < _refs.BrushSizePresetImgs.Count; i++)
            {
                bool active = (i + 1) == _state.BrushSize;
                if (_refs.BrushSizePresetImgs[i] != null)
                    _refs.BrushSizePresetImgs[i].color = active ? BTN_ACTIVE : BTN_NORMAL;
                if (i < _refs.BrushSizePresetLabels.Count && _refs.BrushSizePresetLabels[i] != null)
                    _refs.BrushSizePresetLabels[i].color = active ? ACCENT : TEXT_SECONDARY;
            }
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
            if (_refs.StatusText != null) _refs.StatusText.text = text;
        }

        /// <summary>Updates the PERF button highlight to reflect probe visibility.</summary>
        public void SetPerfProbeVisible(bool active)
        {
            if (_refs.PerfProbeMenuBtnImg != null)
                _refs.PerfProbeMenuBtnImg.color = active ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (_refs.PerfProbeMenuBtnTmp != null)
                _refs.PerfProbeMenuBtnTmp.color = active ? ACCENT : TEXT_PRIMARY;
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
