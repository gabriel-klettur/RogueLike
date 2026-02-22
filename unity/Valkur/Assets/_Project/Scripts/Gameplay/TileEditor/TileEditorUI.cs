using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Gameplay.Rendering;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds and manages the in-game tile editor UI.
    /// Provides: toolbar (tools), tile picker (scrollable palette), layer selector, brush size.
    /// Maps to Python's TileEditorView + TilePickerView + TileToolbarView.
    /// </summary>
    public class TileEditorUI : MonoBehaviour
    {
        // --- Style ---
        private static readonly Color PanelBg = new Color(0.1f, 0.1f, 0.14f, 0.92f);
        private static readonly Color PanelBorder = new Color(0.85f, 0.75f, 0.45f, 0.6f);
        private static readonly Color ButtonNormal = new Color(0.2f, 0.2f, 0.26f, 0.9f);
        private static readonly Color ButtonActive = new Color(0.85f, 0.75f, 0.45f, 0.7f);
        private static readonly Color ButtonHover = new Color(0.3f, 0.28f, 0.36f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color LabelColor = new Color(0.85f, 0.75f, 0.45f, 1f);
        private static readonly Color TileSlotBg = new Color(0.15f, 0.15f, 0.2f, 1f);
        private static readonly Color TileSlotSelected = new Color(0.85f, 0.75f, 0.45f, 0.8f);

        // --- References ---
        private TileEditorState _state;
        private TileCatalog _catalog;
        private System.Action<TileCatalog.TileEntry> _onTileSelected;
        private System.Action<TileEditorState.Tool> _onToolChanged;
        private System.Action<TilemapLayerSetup.TilemapLayer> _onLayerChanged;
        private System.Action<int> _onBrushSizeChanged;

        // --- UI Elements ---
        private Canvas _canvas;
        private GameObject _rootPanel;
        private CanvasGroup _canvasGroup;

        // Toolbar
        private readonly Dictionary<TileEditorState.Tool, Button> _toolButtons = new Dictionary<TileEditorState.Tool, Button>();
        private readonly Dictionary<TileEditorState.Tool, Image> _toolButtonImages = new Dictionary<TileEditorState.Tool, Image>();

        // Tile Picker
        private Transform _tileGridContent;
        private ScrollRect _tileScrollRect;
        private readonly List<GameObject> _tileSlots = new List<GameObject>();
        private int _selectedSlotIndex = -1;

        // Category tabs
        private Transform _categoryTabsContent;
        private readonly List<Button> _categoryButtons = new List<Button>();
        private string _currentCategory = "";

        // Layer selector
        private TextMeshProUGUI _layerLabel;

        // Brush size
        private TextMeshProUGUI _brushSizeLabel;

        // Status bar
        private TextMeshProUGUI _statusText;

        // Indicator (bottom center)
        private TextMeshProUGUI _layerIndicator;
        private GameObject _layerIndicatorPanel;

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

            BuildUI();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_rootPanel != null)
                _rootPanel.SetActive(visible);
            if (_layerIndicatorPanel != null)
                _layerIndicatorPanel.SetActive(visible);
        }

        public void RefreshToolHighlights()
        {
            foreach (var kvp in _toolButtonImages)
            {
                kvp.Value.color = kvp.Key == _state.CurrentTool ? ButtonActive : ButtonNormal;
            }
        }

        public void RefreshLayerLabel()
        {
            if (_layerLabel != null)
                _layerLabel.text = $"Layer: {_state.CurrentLayer}";
            if (_layerIndicator != null)
                _layerIndicator.text = $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}";
        }

        public void RefreshBrushSizeLabel()
        {
            if (_brushSizeLabel != null)
                _brushSizeLabel.text = $"{_state.BrushSize}x{_state.BrushSize}";
        }

        public void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.text = text;
        }

        public void RefreshTilePicker()
        {
            if (_catalog == null) return;
            PopulateTileGrid(_currentCategory);
        }

        // =====================================================================
        // UI CONSTRUCTION
        // =====================================================================

        private void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("TileEditorCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Root panel (left side, vertical strip)
            _rootPanel = CreateUI("TileEditorRoot", canvasGo.transform);
            var rootRect = _rootPanel.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 0.5f);
            rootRect.anchoredPosition = new Vector2(10f, 0f);
            rootRect.sizeDelta = new Vector2(280f, -20f);

            var rootImg = _rootPanel.AddComponent<Image>();
            rootImg.color = PanelBg;

            var rootOutline = _rootPanel.AddComponent<Outline>();
            rootOutline.effectColor = PanelBorder;
            rootOutline.effectDistance = new Vector2(1.5f, 1.5f);

            var rootLayout = _rootPanel.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(8, 8, 8, 8);
            rootLayout.spacing = 6f;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;

            _canvasGroup = _rootPanel.AddComponent<CanvasGroup>();

            // --- Title ---
            BuildTitle(_rootPanel.transform);

            // --- Toolbar ---
            BuildToolbar(_rootPanel.transform);

            // --- Layer selector ---
            BuildLayerSelector(_rootPanel.transform);

            // --- Brush size ---
            BuildBrushSize(_rootPanel.transform);

            // --- Category tabs ---
            BuildCategoryTabs(_rootPanel.transform);

            // --- Tile picker grid (scrollable) ---
            BuildTilePicker(_rootPanel.transform);

            // --- Status bar ---
            BuildStatusBar(_rootPanel.transform);

            // --- Layer indicator (bottom center, always visible) ---
            BuildLayerIndicator(canvasGo.transform);

            // Initial population
            if (_catalog != null)
            {
                var cats = _catalog.GetCategories();
                _currentCategory = cats.Count > 0 ? cats[0] : "";
                PopulateCategoryTabs();
                PopulateTileGrid(_currentCategory);
            }
        }

        private void BuildTitle(Transform parent)
        {
            var titleGo = CreateUI("Title", parent);
            var titleLayout = titleGo.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 30f;

            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text = "TILE EDITOR";
            titleText.fontSize = 20f;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = LabelColor;
        }

        private void BuildToolbar(Transform parent)
        {
            var toolbarGo = CreateUI("Toolbar", parent);
            var toolbarLayout = toolbarGo.AddComponent<LayoutElement>();
            toolbarLayout.preferredHeight = 40f;

            var hLayout = toolbarGo.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childForceExpandWidth = true;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            CreateToolButton(toolbarGo.transform, "B", TileEditorState.Tool.Brush, "Brush (B)");
            CreateToolButton(toolbarGo.transform, "E", TileEditorState.Tool.Eraser, "Eraser (E)");
            CreateToolButton(toolbarGo.transform, "F", TileEditorState.Tool.Fill, "Fill (F)");
            CreateToolButton(toolbarGo.transform, "I", TileEditorState.Tool.Eyedropper, "Eyedropper (I)");
            CreateToolButton(toolbarGo.transform, "S", TileEditorState.Tool.Select, "Select (S)");
        }

        private void CreateToolButton(Transform parent, string label, TileEditorState.Tool tool, string tooltip)
        {
            var btnGo = CreateUI($"Tool_{tool}", parent);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = ButtonNormal;

            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = ButtonNormal;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonActive;
            btn.colors = colors;
            btn.targetGraphic = btnImg;

            var capturedTool = tool;
            btn.onClick.AddListener(() => _onToolChanged?.Invoke(capturedTool));

            var textGo = CreateUI("Label", btnGo.transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 18f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = TextColor;

            _toolButtons[tool] = btn;
            _toolButtonImages[tool] = btnImg;
        }

        private void BuildLayerSelector(Transform parent)
        {
            var rowGo = CreateUI("LayerRow", parent);
            var rowLayout = rowGo.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 32f;

            var hLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            // Prev button
            var prevGo = CreateUI("PrevLayer", rowGo.transform);
            var prevLayout = prevGo.AddComponent<LayoutElement>();
            prevLayout.preferredWidth = 30f;
            var prevBtn = CreateSimpleButton(prevGo, "<", () =>
            {
                int val = (int)_state.CurrentLayer - 1;
                if (val < 0) val = 8;
                _onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)val);
            });

            // Label
            var labelGo = CreateUI("LayerLabel", rowGo.transform);
            var labelLayout = labelGo.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            _layerLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _layerLabel.text = $"Layer: {_state.CurrentLayer}";
            _layerLabel.fontSize = 16f;
            _layerLabel.alignment = TextAlignmentOptions.Center;
            _layerLabel.color = TextColor;

            // Next button
            var nextGo = CreateUI("NextLayer", rowGo.transform);
            var nextLayout = nextGo.AddComponent<LayoutElement>();
            nextLayout.preferredWidth = 30f;
            var nextBtn = CreateSimpleButton(nextGo, ">", () =>
            {
                int val = (int)_state.CurrentLayer + 1;
                if (val > 8) val = 0;
                _onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)val);
            });
        }

        private void BuildBrushSize(Transform parent)
        {
            var rowGo = CreateUI("BrushSizeRow", parent);
            var rowLayout = rowGo.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 28f;

            var hLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            // Label
            var labelGo = CreateUI("SizeLabel", rowGo.transform);
            var ll = labelGo.AddComponent<LayoutElement>();
            ll.preferredWidth = 80f;
            var sizeLabel = labelGo.AddComponent<TextMeshProUGUI>();
            sizeLabel.text = "Size:";
            sizeLabel.fontSize = 14f;
            sizeLabel.alignment = TextAlignmentOptions.Left;
            sizeLabel.color = TextColor;

            // Minus
            var minGo = CreateUI("SizeMinus", rowGo.transform);
            var minLayout = minGo.AddComponent<LayoutElement>();
            minLayout.preferredWidth = 28f;
            CreateSimpleButton(minGo, "-", () =>
            {
                int newSize = Mathf.Max(1, _state.BrushSize - 1);
                _onBrushSizeChanged?.Invoke(newSize);
            });

            // Value
            var valGo = CreateUI("SizeValue", rowGo.transform);
            var valLayout = valGo.AddComponent<LayoutElement>();
            valLayout.preferredWidth = 50f;
            _brushSizeLabel = valGo.AddComponent<TextMeshProUGUI>();
            _brushSizeLabel.text = $"{_state.BrushSize}x{_state.BrushSize}";
            _brushSizeLabel.fontSize = 14f;
            _brushSizeLabel.alignment = TextAlignmentOptions.Center;
            _brushSizeLabel.color = LabelColor;

            // Plus
            var plusGo = CreateUI("SizePlus", rowGo.transform);
            var plusLayout = plusGo.AddComponent<LayoutElement>();
            plusLayout.preferredWidth = 28f;
            CreateSimpleButton(plusGo, "+", () =>
            {
                int newSize = Mathf.Min(5, _state.BrushSize + 1);
                _onBrushSizeChanged?.Invoke(newSize);
            });
        }

        private void BuildCategoryTabs(Transform parent)
        {
            var scrollGo = CreateUI("CategoryScroll", parent);
            var scrollLayout = scrollGo.AddComponent<LayoutElement>();
            scrollLayout.preferredHeight = 30f;

            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = true;
            scrollRect.vertical = false;

            var viewport = CreateUI("Viewport", scrollGo.transform);
            var vpRect = viewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = Vector2.zero;
            var vpMask = viewport.AddComponent<Mask>();
            vpMask.showMaskGraphic = false;
            viewport.AddComponent<Image>().color = Color.clear;

            var content = CreateUI("Content", viewport.transform);
            _categoryTabsContent = content.transform;
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 0);
            contentRect.anchorMax = new Vector2(0, 1);
            contentRect.pivot = new Vector2(0, 0.5f);
            contentRect.sizeDelta = new Vector2(0, 0);

            var contentLayout = content.AddComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = 2f;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childForceExpandHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRect;
            scrollRect.viewport = vpRect;
        }

        private void BuildTilePicker(Transform parent)
        {
            var scrollGo = CreateUI("TilePickerScroll", parent);
            var scrollLayout = scrollGo.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.minHeight = 100f;

            _tileScrollRect = scrollGo.AddComponent<ScrollRect>();
            _tileScrollRect.horizontal = false;
            _tileScrollRect.vertical = true;

            var viewport = CreateUI("Viewport", scrollGo.transform);
            var vpRect = viewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = Vector2.zero;
            var vpMask = viewport.AddComponent<Mask>();
            vpMask.showMaskGraphic = false;
            viewport.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 1f);

            var content = CreateUI("Content", viewport.transform);
            _tileGridContent = content.transform;
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0, 1);
            contentRect.sizeDelta = new Vector2(0, 0);

            var gridLayout = content.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(48f, 48f);
            gridLayout.spacing = new Vector2(3f, 3f);
            gridLayout.padding = new RectOffset(4, 4, 4, 4);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 5;

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _tileScrollRect.content = contentRect;
            _tileScrollRect.viewport = vpRect;
        }

        private void BuildStatusBar(Transform parent)
        {
            var statusGo = CreateUI("StatusBar", parent);
            var statusLayout = statusGo.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 22f;

            _statusText = statusGo.AddComponent<TextMeshProUGUI>();
            _statusText.text = "F6: Toggle | B/E/F/I/S: Tools | Scroll: Layer";
            _statusText.fontSize = 11f;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.color = new Color(0.6f, 0.6f, 0.65f, 0.8f);
        }

        private void BuildLayerIndicator(Transform canvasTransform)
        {
            _layerIndicatorPanel = CreateUI("LayerIndicator", canvasTransform);
            var indRect = _layerIndicatorPanel.GetComponent<RectTransform>();
            indRect.anchorMin = new Vector2(0.5f, 0f);
            indRect.anchorMax = new Vector2(0.5f, 0f);
            indRect.pivot = new Vector2(0.5f, 0f);
            indRect.anchoredPosition = new Vector2(0f, 15f);
            indRect.sizeDelta = new Vector2(220f, 36f);

            var indBg = _layerIndicatorPanel.AddComponent<Image>();
            indBg.color = new Color(0.1f, 0.1f, 0.14f, 0.85f);

            var indOutline = _layerIndicatorPanel.AddComponent<Outline>();
            indOutline.effectColor = LabelColor;
            indOutline.effectDistance = new Vector2(1f, 1f);

            _layerIndicator = CreateUI("Text", _layerIndicatorPanel.transform).AddComponent<TextMeshProUGUI>();
            var textRect = _layerIndicator.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            _layerIndicator.text = $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}";
            _layerIndicator.fontSize = 18f;
            _layerIndicator.fontStyle = FontStyles.Bold;
            _layerIndicator.alignment = TextAlignmentOptions.Center;
            _layerIndicator.color = LabelColor;
        }

        // =====================================================================
        // TILE GRID POPULATION
        // =====================================================================

        private void PopulateCategoryTabs()
        {
            foreach (var btn in _categoryButtons)
                if (btn != null) Destroy(btn.gameObject);
            _categoryButtons.Clear();

            if (_catalog == null) return;
            var cats = _catalog.GetCategories();

            foreach (var cat in cats)
            {
                var btnGo = CreateUI($"Cat_{cat}", _categoryTabsContent);
                var btnLayout = btnGo.AddComponent<LayoutElement>();
                btnLayout.preferredWidth = Mathf.Max(60f, cat.Length * 9f);

                var btnImg = btnGo.AddComponent<Image>();
                btnImg.color = cat == _currentCategory ? ButtonActive : ButtonNormal;

                var btn = btnGo.AddComponent<Button>();
                var colors = btn.colors;
                colors.normalColor = ButtonNormal;
                colors.highlightedColor = ButtonHover;
                btn.colors = colors;
                btn.targetGraphic = btnImg;

                string capturedCat = cat;
                btn.onClick.AddListener(() => SelectCategory(capturedCat));

                var textGo = CreateUI("Text", btnGo.transform);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
                var text = textGo.AddComponent<TextMeshProUGUI>();
                text.text = cat;
                text.fontSize = 12f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = TextColor;

                _categoryButtons.Add(btn);
            }
        }

        private void SelectCategory(string category)
        {
            _currentCategory = category;
            PopulateTileGrid(category);

            // Update tab highlights
            if (_catalog != null)
            {
                var cats = _catalog.GetCategories();
                for (int i = 0; i < _categoryButtons.Count && i < cats.Count; i++)
                {
                    var img = _categoryButtons[i].GetComponent<Image>();
                    if (img != null)
                        img.color = cats[i] == category ? ButtonActive : ButtonNormal;
                }
            }
        }

        private void PopulateTileGrid(string category)
        {
            // Clear existing slots
            foreach (var slot in _tileSlots)
                if (slot != null) Destroy(slot);
            _tileSlots.Clear();
            _selectedSlotIndex = -1;

            if (_catalog == null) return;

            var tiles = string.IsNullOrEmpty(category)
                ? new List<TileCatalog.TileEntry>(_catalog.Entries)
                : _catalog.GetTilesForCategory(category);

            for (int i = 0; i < tiles.Count; i++)
            {
                var entry = tiles[i];
                var slotGo = CreateUI($"Slot_{i}", _tileGridContent);

                var slotImg = slotGo.AddComponent<Image>();
                slotImg.color = TileSlotBg;

                var btn = slotGo.AddComponent<Button>();
                var colors = btn.colors;
                colors.normalColor = TileSlotBg;
                colors.highlightedColor = ButtonHover;
                colors.selectedColor = TileSlotSelected;
                btn.colors = colors;
                btn.targetGraphic = slotImg;

                int capturedIndex = i;
                var capturedEntry = entry;
                btn.onClick.AddListener(() =>
                {
                    _selectedSlotIndex = capturedIndex;
                    _onTileSelected?.Invoke(capturedEntry);
                    HighlightSelectedSlot();
                });

                // Tile preview sprite
                Sprite preview = entry.preview;
                if (preview == null && entry.tile is Tile t)
                    preview = t.sprite;

                if (preview != null)
                {
                    var spriteGo = CreateUI("Preview", slotGo.transform);
                    var spriteRect = spriteGo.GetComponent<RectTransform>();
                    spriteRect.anchorMin = new Vector2(0.1f, 0.1f);
                    spriteRect.anchorMax = new Vector2(0.9f, 0.9f);
                    spriteRect.sizeDelta = Vector2.zero;
                    var spriteImg = spriteGo.AddComponent<Image>();
                    spriteImg.sprite = preview;
                    spriteImg.preserveAspect = true;
                    spriteImg.raycastTarget = false;
                }

                _tileSlots.Add(slotGo);
            }
        }

        private void HighlightSelectedSlot()
        {
            for (int i = 0; i < _tileSlots.Count; i++)
            {
                var img = _tileSlots[i].GetComponent<Image>();
                if (img != null)
                    img.color = i == _selectedSlotIndex ? TileSlotSelected : TileSlotBg;
            }
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private Button CreateSimpleButton(GameObject go, string label, UnityEngine.Events.UnityAction onClick)
        {
            var img = go.AddComponent<Image>();
            img.color = ButtonNormal;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = ButtonNormal;
            colors.highlightedColor = ButtonHover;
            btn.colors = colors;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var textGo = CreateUI("Text", go.transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = TextColor;

            return btn;
        }

        private static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
