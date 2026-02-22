using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Gameplay.Rendering;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Tile editor UI: public API, state management, tile grid population, layer visibility.
    /// Construction delegated to TileEditorUIBuilder. Design tokens in TileEditorUIHelpers.
    /// </summary>
    public class TileEditorUI : MonoBehaviour
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
            if (_refs.LeftPanel != null) _refs.LeftPanel.SetActive(visible);
            if (_refs.ViewPanel != null) _refs.ViewPanel.SetActive(visible);
            if (_refs.LayersPanel != null) _refs.LayersPanel.SetActive(visible);
            if (_refs.LayerIndicatorPanel != null) _refs.LayerIndicatorPanel.SetActive(visible);
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

        private void BuildUI()
        {
            var canvasGo = new GameObject("TileEditorCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _refs = TileEditorUIBuilder.BuildAll(canvasGo.transform, _state,
                _onToolChanged, _onLayerChanged, _onBrushSizeChanged);

            WireLayerVisibilityButtons();

            if (_catalog != null)
            {
                _currentCategory = "";
                PopulateCategoryTabs();
                PopulateTileGrid(_currentCategory);
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
        }

        public bool IsLayerVisible(int layerIdx)
        {
            return layerIdx >= 0 && layerIdx < 9 && _layerVisibility[layerIdx];
        }

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
            PopulateCategoryTabs();
        }

        private void PopulateTileGrid(string category)
        {
            foreach (var slot in _tileSlots) if (slot != null) Destroy(slot);
            _tileSlots.Clear();
            _selectedSlotIndex = -1;
            if (_catalog == null) return;

            var tiles = string.IsNullOrEmpty(category)
                ? new List<TileCatalog.TileEntry>(_catalog.Entries)
                : _catalog.GetTilesForCategory(category);

            for (int i = 0; i < tiles.Count; i++)
            {
                var entry = tiles[i];
                var go = CreateUI($"Slot_{i}", _refs.TileGridContent);
                var slotImg = go.AddComponent<Image>();
                slotImg.color = SLOT_BG;
                var btn = go.AddComponent<Button>();
                var bc = btn.colors;
                bc.normalColor = SLOT_BG;
                bc.highlightedColor = SLOT_HOVER;
                bc.selectedColor = SLOT_SELECTED;
                btn.colors = bc;
                btn.targetGraphic = slotImg;
                int ci = i; var ce = entry;
                btn.onClick.AddListener(() => { _selectedSlotIndex = ci; _onTileSelected?.Invoke(ce); HighlightSelectedSlot(); });

                Sprite preview = entry.preview;
                if (preview == null && entry.tile is Tile t) preview = t.sprite;
                if (preview != null)
                {
                    var sgo = CreateUI("Prev", go.transform);
                    var sr = sgo.GetComponent<RectTransform>();
                    sr.anchorMin = new Vector2(0.06f, 0.06f); sr.anchorMax = new Vector2(0.94f, 0.94f);
                    sr.sizeDelta = Vector2.zero;
                    var si = sgo.AddComponent<Image>();
                    si.sprite = preview; si.preserveAspect = true; si.raycastTarget = false;
                }

                var nameGo = CreateUI("TName", go.transform);
                var nr = nameGo.GetComponent<RectTransform>();
                nr.anchorMin = new Vector2(0f, 0f); nr.anchorMax = new Vector2(1f, 0.22f);
                nr.sizeDelta = Vector2.zero;
                var nbg = nameGo.AddComponent<Image>();
                nbg.color = new Color(0f, 0f, 0f, 0.7f); nbg.raycastTarget = false;
                var nt = TileEditorUIHelpers.AddCenteredText(nameGo.transform, entry.tileName, 7f, FontStyles.Normal, TEXT_PRIMARY);
                nt.raycastTarget = false;
                nt.overflowMode = TextOverflowModes.Ellipsis;
                nt.enableWordWrapping = false;

                _tileSlots.Add(go);
            }

            if (_refs.TileCountText != null)
                _refs.TileCountText.text = $"{tiles.Count} tiles" + (string.IsNullOrEmpty(category) ? "" : $" in {category}");
        }

        private void HighlightSelectedSlot()
        {
            for (int i = 0; i < _tileSlots.Count; i++)
            {
                var img = _tileSlots[i].GetComponent<Image>();
                if (img != null) img.color = i == _selectedSlotIndex ? SLOT_SELECTED : SLOT_BG;
            }
        }
    }
}
