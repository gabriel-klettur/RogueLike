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
                _onUndo, _onRedo, _onSave,
                _onShowCollidersClicked, _onDrawCollidersClicked, _onEraseCollidersClicked,
                _onPerfToggle);

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
            var layer = (TilemapLayerSetup.TilemapLayer)layerIdx;
            _onLayerVisibilityChanged?.Invoke(layer, _layerVisibility[layerIdx]);
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
