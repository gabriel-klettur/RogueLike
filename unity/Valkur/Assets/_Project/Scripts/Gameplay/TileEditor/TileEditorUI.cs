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
    /// Replicates all Python tile editor panels:
    ///   LEFT:  Title, Toolbar, Layer selector, Brush size, Selected tile preview,
    ///          Category tabs, Tile picker grid (scrollable), Status bar
    ///   RIGHT: View Panel (hovered/selected/choice info), Layers Panel (9 layers + visibility)
    ///   BOTTOM CENTER: Layer indicator
    /// </summary>
    public class TileEditorUI : MonoBehaviour
    {
        // --- Style constants (dark theme matching Python editor) ---
        private static readonly Color PanelBg = new Color(0.1f, 0.1f, 0.14f, 0.92f);
        private static readonly Color PanelBorder = new Color(0.85f, 0.75f, 0.45f, 0.6f);
        private static readonly Color ButtonNormal = new Color(0.2f, 0.2f, 0.26f, 0.9f);
        private static readonly Color ButtonActive = new Color(0.85f, 0.75f, 0.45f, 0.7f);
        private static readonly Color ButtonHover = new Color(0.3f, 0.28f, 0.36f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color LabelColor = new Color(0.85f, 0.75f, 0.45f, 1f);
        private static readonly Color TileSlotBg = new Color(0.15f, 0.15f, 0.2f, 1f);
        private static readonly Color TileSlotSelected = new Color(0.85f, 0.75f, 0.45f, 0.8f);
        private static readonly Color SectionHeaderBg = new Color(0.14f, 0.14f, 0.18f, 1f);
        private static readonly Color LayerActiveBg = new Color(0.25f, 0.22f, 0.12f, 0.9f);
        private static readonly Color ToggleOnColor = new Color(0.4f, 0.85f, 0.4f, 1f);
        private static readonly Color ToggleOffColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

        // --- Callbacks ---
        private TileEditorState _state;
        private TileCatalog _catalog;
        private System.Action<TileCatalog.TileEntry> _onTileSelected;
        private System.Action<TileEditorState.Tool> _onToolChanged;
        private System.Action<TilemapLayerSetup.TilemapLayer> _onLayerChanged;
        private System.Action<int> _onBrushSizeChanged;

        // --- Canvas ---
        private Canvas _canvas;

        // --- LEFT PANEL ---
        private GameObject _leftPanel;
        private readonly Dictionary<TileEditorState.Tool, Image> _toolButtonImages = new Dictionary<TileEditorState.Tool, Image>();
        private TextMeshProUGUI _layerLabel;
        private TextMeshProUGUI _brushSizeLabel;
        private Image _selectedTilePreviewImg;
        private TextMeshProUGUI _selectedTileNameText;
        private Transform _categoryTabsContent;
        private readonly List<Button> _categoryButtons = new List<Button>();
        private string _currentCategory = "";
        private Transform _tileGridContent;
        private ScrollRect _tileScrollRect;
        private readonly List<GameObject> _tileSlots = new List<GameObject>();
        private int _selectedSlotIndex = -1;
        private TextMeshProUGUI _tileCountText;
        private TextMeshProUGUI _statusText;

        // --- RIGHT: VIEW PANEL (Python's TilesViewPanel) ---
        private GameObject _viewPanel;
        private Image _viewHoveredImg;
        private TextMeshProUGUI _viewHoveredLabel;
        private Image _viewSelectedImg;
        private TextMeshProUGUI _viewSelectedLabel;
        private Image _viewChoiceImg;
        private TextMeshProUGUI _viewChoiceLabel;
        private TextMeshProUGUI _viewLayerHoveredText;
        private TextMeshProUGUI _viewLayerSelectedText;

        // --- RIGHT: LAYERS PANEL (Python's LayersPanel) ---
        private GameObject _layersPanel;
        private readonly List<Image> _layerRowBgs = new List<Image>();
        private readonly List<TextMeshProUGUI> _layerRowLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> _layerVisIcons = new List<Image>();
        private readonly bool[] _layerVisibility = new bool[9];

        // --- BOTTOM: LAYER INDICATOR ---
        private GameObject _layerIndicatorPanel;
        private TextMeshProUGUI _layerIndicator;

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
            if (_leftPanel != null) _leftPanel.SetActive(visible);
            if (_viewPanel != null) _viewPanel.SetActive(visible);
            if (_layersPanel != null) _layersPanel.SetActive(visible);
            if (_layerIndicatorPanel != null) _layerIndicatorPanel.SetActive(visible);
        }

        public void RefreshToolHighlights()
        {
            foreach (var kvp in _toolButtonImages)
                kvp.Value.color = kvp.Key == _state.CurrentTool ? ButtonActive : ButtonNormal;
        }

        public void RefreshLayerLabel()
        {
            if (_layerLabel != null)
                _layerLabel.text = _state.CurrentLayer.ToString();
            if (_layerIndicator != null)
                _layerIndicator.text = $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}";
            RefreshLayersPanel();
            if (_viewLayerSelectedText != null)
                _viewLayerSelectedText.text = $"  {(int)_state.CurrentLayer}: {_state.CurrentLayer}";
        }

        public void RefreshBrushSizeLabel()
        {
            if (_brushSizeLabel != null)
                _brushSizeLabel.text = $"{_state.BrushSize}x{_state.BrushSize}";
        }

        public void SetStatus(string text)
        {
            if (_statusText != null) _statusText.text = text;
        }

        public void RefreshTilePicker()
        {
            if (_catalog == null) return;
            PopulateTileGrid(_currentCategory);
        }

        public void UpdateSelectedTilePreview(Sprite sprite, string tileName)
        {
            if (_selectedTilePreviewImg != null)
            {
                _selectedTilePreviewImg.sprite = sprite;
                _selectedTilePreviewImg.color = sprite != null ? Color.white : new Color(0.15f, 0.15f, 0.2f, 1f);
            }
            if (_selectedTileNameText != null)
                _selectedTileNameText.text = tileName ?? "(none)";
            if (_viewChoiceImg != null)
            {
                _viewChoiceImg.sprite = sprite;
                _viewChoiceImg.color = sprite != null ? Color.white : TileSlotBg;
            }
            if (_viewChoiceLabel != null)
                _viewChoiceLabel.text = tileName ?? "";
        }

        public void UpdateViewPanelHovered(Sprite sprite, string name, string layerName)
        {
            if (_viewHoveredImg != null)
            {
                _viewHoveredImg.sprite = sprite;
                _viewHoveredImg.color = sprite != null ? Color.white : TileSlotBg;
            }
            if (_viewHoveredLabel != null) _viewHoveredLabel.text = name ?? "";
            if (_viewLayerHoveredText != null) _viewLayerHoveredText.text = $"  {layerName}";
        }

        public void UpdateViewPanelSelected(Sprite sprite, string name)
        {
            if (_viewSelectedImg != null)
            {
                _viewSelectedImg.sprite = sprite;
                _viewSelectedImg.color = sprite != null ? Color.white : TileSlotBg;
            }
            if (_viewSelectedLabel != null) _viewSelectedLabel.text = name ?? "";
        }

        // =====================================================================
        // UI CONSTRUCTION
        // =====================================================================

        private void BuildUI()
        {
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

            BuildLeftPanel(canvasGo.transform);
            BuildViewPanel(canvasGo.transform);
            BuildLayersPanel(canvasGo.transform);
            BuildLayerIndicator(canvasGo.transform);

            if (_catalog != null)
            {
                // Default to "All" so all tiles are visible on open
                _currentCategory = "";
                PopulateCategoryTabs();
                PopulateTileGrid(_currentCategory);
            }
        }

        // =========================== LEFT PANEL ==============================

        private void BuildLeftPanel(Transform canvasT)
        {
            _leftPanel = CreatePanel("LeftPanel", canvasT,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(280f, -20f));

            var layout = _leftPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 5f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            _leftPanel.AddComponent<CanvasGroup>();

            BuildSectionHeader(_leftPanel.transform, "TILE EDITOR", 26f);
            BuildToolbar(_leftPanel.transform);
            BuildLayerSelector(_leftPanel.transform);
            BuildBrushSize(_leftPanel.transform);
            BuildSelectedTilePreview(_leftPanel.transform);
            BuildSectionHeader(_leftPanel.transform, "TILE PICKER", 16f);
            BuildCategorySelector(_leftPanel.transform);
            BuildTilePicker(_leftPanel.transform);
            BuildTileCountRow(_leftPanel.transform);
            BuildStatusBar(_leftPanel.transform);
        }

        private void BuildSectionHeader(Transform parent, string text, float fontSize)
        {
            var go = CreateUI("Header_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 8f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = LabelColor;
        }

        private void BuildToolbar(Transform parent)
        {
            var go = CreateUI("Toolbar", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 38f;
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 3f;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            CreateToolBtn(go.transform, "B", TileEditorState.Tool.Brush);
            CreateToolBtn(go.transform, "E", TileEditorState.Tool.Eraser);
            CreateToolBtn(go.transform, "F", TileEditorState.Tool.Fill);
            CreateToolBtn(go.transform, "I", TileEditorState.Tool.Eyedropper);
            CreateToolBtn(go.transform, "S", TileEditorState.Tool.Select);
        }

        private void CreateToolBtn(Transform parent, string label, TileEditorState.Tool tool)
        {
            var go = CreateUI($"Tool_{tool}", parent);
            var img = go.AddComponent<Image>();
            img.color = ButtonNormal;
            var btn = go.AddComponent<Button>();
            var c = btn.colors; c.normalColor = ButtonNormal; c.highlightedColor = ButtonHover; c.pressedColor = ButtonActive; btn.colors = c;
            btn.targetGraphic = img;
            var cap = tool;
            btn.onClick.AddListener(() => _onToolChanged?.Invoke(cap));
            AddCenteredText(go.transform, label, 16f, FontStyles.Bold, TextColor);
            _toolButtonImages[tool] = img;
        }

        private void BuildLayerSelector(Transform parent)
        {
            var row = CreateUI("LayerRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 30f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            var prev = CreateUI("Prev", row.transform);
            prev.AddComponent<LayoutElement>().preferredWidth = 28f;
            MakeBtn(prev, "<", () => { int v = (int)_state.CurrentLayer - 1; if (v < 0) v = 8; _onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v); });

            var lbl = CreateUI("LayerLbl", row.transform);
            lbl.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _layerLabel = lbl.AddComponent<TextMeshProUGUI>();
            _layerLabel.text = _state.CurrentLayer.ToString();
            _layerLabel.fontSize = 14f;
            _layerLabel.alignment = TextAlignmentOptions.Center;
            _layerLabel.color = TextColor;

            var next = CreateUI("Next", row.transform);
            next.AddComponent<LayoutElement>().preferredWidth = 28f;
            MakeBtn(next, ">", () => { int v = (int)_state.CurrentLayer + 1; if (v > 8) v = 0; _onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v); });
        }

        private void BuildBrushSize(Transform parent)
        {
            var row = CreateUI("BrushRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            var lbl = CreateUI("Lbl", row.transform);
            lbl.AddComponent<LayoutElement>().preferredWidth = 50f;
            var t = lbl.AddComponent<TextMeshProUGUI>();
            t.text = "Size:"; t.fontSize = 13f; t.alignment = TextAlignmentOptions.Left; t.color = TextColor;

            var minus = CreateUI("Minus", row.transform);
            minus.AddComponent<LayoutElement>().preferredWidth = 26f;
            MakeBtn(minus, "-", () => _onBrushSizeChanged?.Invoke(Mathf.Max(1, _state.BrushSize - 1)));

            var val = CreateUI("Val", row.transform);
            val.AddComponent<LayoutElement>().preferredWidth = 46f;
            _brushSizeLabel = val.AddComponent<TextMeshProUGUI>();
            _brushSizeLabel.text = $"{_state.BrushSize}x{_state.BrushSize}";
            _brushSizeLabel.fontSize = 13f; _brushSizeLabel.alignment = TextAlignmentOptions.Center; _brushSizeLabel.color = LabelColor;

            var plus = CreateUI("Plus", row.transform);
            plus.AddComponent<LayoutElement>().preferredWidth = 26f;
            MakeBtn(plus, "+", () => _onBrushSizeChanged?.Invoke(Mathf.Min(5, _state.BrushSize + 1)));

            // Visual brush size buttons (1-5)
            for (int s = 1; s <= 5; s++)
            {
                var sb = CreateUI($"S{s}", row.transform);
                sb.AddComponent<LayoutElement>().preferredWidth = 22f;
                int cap = s;
                MakeBtn(sb, $"{s}", () => _onBrushSizeChanged?.Invoke(cap), 11f);
            }
        }

        private void BuildSelectedTilePreview(Transform parent)
        {
            var row = CreateUI("SelectedPreview", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 52f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 2, 2);

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 48f;
            _selectedTilePreviewImg = imgGo.AddComponent<Image>();
            _selectedTilePreviewImg.color = TileSlotBg;
            _selectedTilePreviewImg.preserveAspect = true;
            var outline = imgGo.AddComponent<Outline>();
            outline.effectColor = LabelColor;
            outline.effectDistance = new Vector2(1f, 1f);

            var nameGo = CreateUI("Name", row.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _selectedTileNameText = nameGo.AddComponent<TextMeshProUGUI>();
            _selectedTileNameText.text = "(none)";
            _selectedTileNameText.fontSize = 13f;
            _selectedTileNameText.alignment = TextAlignmentOptions.Left;
            _selectedTileNameText.color = TextColor;
            _selectedTileNameText.enableWordWrapping = true;
        }

        private void BuildCategorySelector(Transform parent)
        {
            // Vertical scrollable category list with wrapping grid
            var scrollGo = CreateUI("CatScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.preferredHeight = 60f;
            le.minHeight = 40f;
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.5f);

            var content = CreateUI("Content", vp.transform);
            _categoryTabsContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1); cr.sizeDelta = Vector2.zero;

            // Use GridLayout so categories wrap into rows
            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(124f, 22f);
            gl.spacing = new Vector2(3f, 2f);
            gl.padding = new RectOffset(2, 2, 2, 2);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 2;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = cr; sr.viewport = vp.GetComponent<RectTransform>();
        }

        private void BuildTilePicker(Transform parent)
        {
            var scrollGo = CreateUI("TileScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f; le.minHeight = 120f;
            _tileScrollRect = scrollGo.AddComponent<ScrollRect>();
            _tileScrollRect.horizontal = false; _tileScrollRect.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 1f);

            var content = CreateUI("Content", vp.transform);
            _tileGridContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1); cr.sizeDelta = Vector2.zero;
            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(48f, 48f);
            gl.spacing = new Vector2(3f, 3f);
            gl.padding = new RectOffset(4, 4, 4, 4);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 5;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _tileScrollRect.content = cr;
            _tileScrollRect.viewport = vp.GetComponent<RectTransform>();
        }

        private void BuildTileCountRow(Transform parent)
        {
            var go = CreateUI("TileCount", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            _tileCountText = go.AddComponent<TextMeshProUGUI>();
            _tileCountText.text = "";
            _tileCountText.fontSize = 11f;
            _tileCountText.alignment = TextAlignmentOptions.Right;
            _tileCountText.color = new Color(0.5f, 0.5f, 0.55f, 0.7f);
        }

        private void BuildStatusBar(Transform parent)
        {
            var go = CreateUI("Status", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            _statusText = go.AddComponent<TextMeshProUGUI>();
            _statusText.text = "F6: Toggle | B/E/F/I/S: Tools | Scroll: Layer";
            _statusText.fontSize = 10f;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.color = new Color(0.55f, 0.55f, 0.6f, 0.7f);
        }

        // ========================= RIGHT: VIEW PANEL =========================
        // Maps to Python's TilesViewPanelView: hovered, selected, choice sprites + layer info

        private void BuildViewPanel(Transform canvasT)
        {
            _viewPanel = CreatePanel("ViewPanel", canvasT,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-10f, -10f), new Vector2(220f, 260f));

            var layout = _viewPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            BuildSectionHeader(_viewPanel.transform, "VIEW", 15f);

            // Hovered tile row
            BuildViewRow(_viewPanel.transform, "Hovered", new Color(0f, 0.9f, 0.9f, 1f),
                out _viewHoveredImg, out _viewHoveredLabel);

            // Selected tile row
            BuildViewRow(_viewPanel.transform, "Selected", new Color(0f, 1f, 0f, 1f),
                out _viewSelectedImg, out _viewSelectedLabel);

            // Choice tile row
            BuildViewRow(_viewPanel.transform, "Choice", new Color(1f, 0.85f, 0f, 1f),
                out _viewChoiceImg, out _viewChoiceLabel);

            // Separator
            var sep = CreateUI("Sep", _viewPanel.transform);
            sep.AddComponent<LayoutElement>().preferredHeight = 1f;
            sep.AddComponent<Image>().color = PanelBorder;

            // Layer Hovered
            var lhGo = CreateUI("LayerHov", _viewPanel.transform);
            lhGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lhLabel = lhGo.AddComponent<TextMeshProUGUI>();
            lhLabel.text = "Layer Hovered:"; lhLabel.fontSize = 11f; lhLabel.color = TextColor;
            var lhVal = CreateUI("LHVal", _viewPanel.transform);
            lhVal.AddComponent<LayoutElement>().preferredHeight = 18f;
            _viewLayerHoveredText = lhVal.AddComponent<TextMeshProUGUI>();
            _viewLayerHoveredText.text = ""; _viewLayerHoveredText.fontSize = 13f;
            _viewLayerHoveredText.fontStyle = FontStyles.Bold; _viewLayerHoveredText.color = LabelColor;

            // Layer Selected
            var lsGo = CreateUI("LayerSel", _viewPanel.transform);
            lsGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lsLabel = lsGo.AddComponent<TextMeshProUGUI>();
            lsLabel.text = "Layer Selected:"; lsLabel.fontSize = 11f; lsLabel.color = TextColor;
            var lsVal = CreateUI("LSVal", _viewPanel.transform);
            lsVal.AddComponent<LayoutElement>().preferredHeight = 18f;
            _viewLayerSelectedText = lsVal.AddComponent<TextMeshProUGUI>();
            _viewLayerSelectedText.text = $"  {(int)_state.CurrentLayer}: {_state.CurrentLayer}";
            _viewLayerSelectedText.fontSize = 13f;
            _viewLayerSelectedText.fontStyle = FontStyles.Bold; _viewLayerSelectedText.color = LabelColor;
        }

        private void BuildViewRow(Transform parent, string label, Color outlineColor,
            out Image tileImg, out TextMeshProUGUI nameText)
        {
            var row = CreateUI($"View_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 36f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 32f;
            tileImg = imgGo.AddComponent<Image>();
            tileImg.color = TileSlotBg;
            tileImg.preserveAspect = true;
            var ol = imgGo.AddComponent<Outline>();
            ol.effectColor = outlineColor;
            ol.effectDistance = new Vector2(1.5f, 1.5f);

            var txtGo = CreateUI("Txt", row.transform);
            txtGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = txtGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0; vl.childForceExpandHeight = true; vl.childControlHeight = true;

            var lblGo = CreateUI("Lbl", txtGo.transform);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label; lblTmp.fontSize = 10f; lblTmp.color = outlineColor;

            var valGo = CreateUI("Val", txtGo.transform);
            nameText = valGo.AddComponent<TextMeshProUGUI>();
            nameText.text = ""; nameText.fontSize = 12f; nameText.color = TextColor;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ======================== RIGHT: LAYERS PANEL ========================
        // Maps to Python's LayersPanelView: 9 layers with visibility toggles

        private void BuildLayersPanel(Transform canvasT)
        {
            _layersPanel = CreatePanel("LayersPanel", canvasT,
                new Vector2(1f, 0f), new Vector2(1f, 0.55f), new Vector2(1f, 0f),
                new Vector2(-10f, 10f), new Vector2(220f, 0f));

            var layout = _layersPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 2f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            BuildSectionHeader(_layersPanel.transform, "LAYERS", 14f);

            var layers = System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));
            foreach (TilemapLayerSetup.TilemapLayer layer in layers)
            {
                BuildLayerRow(_layersPanel.transform, layer);
            }
        }

        private void BuildLayerRow(Transform parent, TilemapLayerSetup.TilemapLayer layer)
        {
            int idx = (int)layer;
            var row = CreateUI($"Layer_{layer}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 1, 1);

            var bg = row.AddComponent<Image>();
            bg.color = layer == _state.CurrentLayer ? LayerActiveBg : Color.clear;
            _layerRowBgs.Add(bg);

            // Visibility toggle
            var visGo = CreateUI("Vis", row.transform);
            visGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var visImg = visGo.AddComponent<Image>();
            visImg.color = ToggleOnColor;
            _layerVisIcons.Add(visImg);
            var visBtn = visGo.AddComponent<Button>();
            visBtn.targetGraphic = visImg;
            int capIdx = idx;
            visBtn.onClick.AddListener(() => ToggleLayerVisibility(capIdx));

            // Index label
            var idxGo = CreateUI("Idx", row.transform);
            idxGo.AddComponent<LayoutElement>().preferredWidth = 20f;
            var idxTmp = idxGo.AddComponent<TextMeshProUGUI>();
            idxTmp.text = idx.ToString(); idxTmp.fontSize = 12f;
            idxTmp.alignment = TextAlignmentOptions.Center; idxTmp.color = LabelColor;

            // Name label
            var nameGo = CreateUI("Name", row.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = layer.ToString(); nameTmp.fontSize = 12f;
            nameTmp.alignment = TextAlignmentOptions.Left; nameTmp.color = TextColor;
            _layerRowLabels.Add(nameTmp);

            // Click to select layer
            var rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = bg;
            var colors = rowBtn.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = new Color(0.3f, 0.28f, 0.2f, 0.4f);
            rowBtn.colors = colors;
            var capLayer = layer;
            rowBtn.onClick.AddListener(() => _onLayerChanged?.Invoke(capLayer));
        }

        private void ToggleLayerVisibility(int layerIdx)
        {
            _layerVisibility[layerIdx] = !_layerVisibility[layerIdx];
            if (layerIdx < _layerVisIcons.Count)
                _layerVisIcons[layerIdx].color = _layerVisibility[layerIdx] ? ToggleOnColor : ToggleOffColor;
        }

        public bool IsLayerVisible(int layerIdx)
        {
            return layerIdx >= 0 && layerIdx < 9 && _layerVisibility[layerIdx];
        }

        private void RefreshLayersPanel()
        {
            for (int i = 0; i < _layerRowBgs.Count && i < 9; i++)
                _layerRowBgs[i].color = i == (int)_state.CurrentLayer ? LayerActiveBg : Color.clear;
        }

        // ======================== BOTTOM: LAYER INDICATOR ====================

        private void BuildLayerIndicator(Transform canvasT)
        {
            _layerIndicatorPanel = CreateUI("LayerIndicator", canvasT);
            var r = _layerIndicatorPanel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0f); r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(0f, 15f);
            r.sizeDelta = new Vector2(220f, 36f);
            var bg = _layerIndicatorPanel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.14f, 0.85f);
            _layerIndicatorPanel.AddComponent<Outline>().effectColor = LabelColor;

            _layerIndicator = AddCenteredText(_layerIndicatorPanel.transform,
                $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}", 18f, FontStyles.Bold, LabelColor);
        }

        // =====================================================================
        // TILE GRID POPULATION
        // =====================================================================

        private void PopulateCategoryTabs()
        {
            foreach (var btn in _categoryButtons) if (btn != null) Destroy(btn.gameObject);
            _categoryButtons.Clear();
            if (_catalog == null) return;

            // "All" tab
            AddCategoryTab("All", "");

            foreach (var cat in _catalog.GetCategories())
                AddCategoryTab(cat, cat);
        }

        private void AddCategoryTab(string displayName, string categoryKey)
        {
            var go = CreateUI($"Cat_{displayName}", _categoryTabsContent);
            var img = go.AddComponent<Image>();
            bool isActive = categoryKey == _currentCategory;
            img.color = isActive ? ButtonActive : ButtonNormal;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = isActive ? LabelColor : Color.clear;
            outline.effectDistance = new Vector2(1f, 1f);

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = ButtonNormal;
            c.highlightedColor = ButtonHover;
            c.pressedColor = ButtonActive;
            btn.colors = c;
            btn.targetGraphic = img;
            string cap = categoryKey;
            btn.onClick.AddListener(() => SelectCategory(cap));

            var tmp = AddCenteredText(go.transform, displayName, 11f, FontStyles.Normal,
                isActive ? LabelColor : TextColor);
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.margin = new Vector4(6f, 0f, 0f, 0f);

            _categoryButtons.Add(btn);
        }

        private void SelectCategory(string category)
        {
            _currentCategory = category;
            PopulateTileGrid(category);

            // Rebuild tabs to update highlights (simpler than tracking all sub-elements)
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
                var go = CreateUI($"Slot_{i}", _tileGridContent);
                var slotImg = go.AddComponent<Image>();
                slotImg.color = TileSlotBg;
                var btn = go.AddComponent<Button>();
                var c = btn.colors;
                c.normalColor = TileSlotBg; c.highlightedColor = ButtonHover; c.selectedColor = TileSlotSelected;
                btn.colors = c; btn.targetGraphic = slotImg;
                int ci = i; var ce = entry;
                btn.onClick.AddListener(() => { _selectedSlotIndex = ci; _onTileSelected?.Invoke(ce); HighlightSelectedSlot(); });

                Sprite preview = entry.preview;
                if (preview == null && entry.tile is Tile t) preview = t.sprite;
                if (preview != null)
                {
                    var sgo = CreateUI("Prev", go.transform);
                    var sr = sgo.GetComponent<RectTransform>();
                    sr.anchorMin = new Vector2(0.08f, 0.08f); sr.anchorMax = new Vector2(0.92f, 0.92f);
                    sr.sizeDelta = Vector2.zero;
                    var si = sgo.AddComponent<Image>();
                    si.sprite = preview; si.preserveAspect = true; si.raycastTarget = false;
                }

                // Tile name tooltip at bottom
                var nameGo = CreateUI("TName", go.transform);
                var nr = nameGo.GetComponent<RectTransform>();
                nr.anchorMin = new Vector2(0f, 0f); nr.anchorMax = new Vector2(1f, 0.25f);
                nr.sizeDelta = Vector2.zero;
                var nbg = nameGo.AddComponent<Image>();
                nbg.color = new Color(0f, 0f, 0f, 0.6f); nbg.raycastTarget = false;
                var nt = AddCenteredText(nameGo.transform, entry.tileName, 7f, FontStyles.Normal, TextColor);
                nt.raycastTarget = false;
                nt.overflowMode = TextOverflowModes.Ellipsis;
                nt.enableWordWrapping = false;

                _tileSlots.Add(go);
            }

            if (_tileCountText != null)
                _tileCountText.text = $"{tiles.Count} tiles" + (string.IsNullOrEmpty(category) ? "" : $" in {category}");
        }

        private void HighlightSelectedSlot()
        {
            for (int i = 0; i < _tileSlots.Count; i++)
            {
                var img = _tileSlots[i].GetComponent<Image>();
                if (img != null) img.color = i == _selectedSlotIndex ? TileSlotSelected : TileSlotBg;
            }
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private GameObject CreatePanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreateUI(name, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot;
            r.anchoredPosition = anchoredPos; r.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = PanelBg;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = PanelBorder; ol.effectDistance = new Vector2(1.5f, 1.5f);
            return go;
        }

        private void MakeBtn(GameObject go, string label, UnityEngine.Events.UnityAction onClick, float fontSize = 14f)
        {
            var img = go.AddComponent<Image>();
            img.color = ButtonNormal;
            var btn = go.AddComponent<Button>();
            var c = btn.colors; c.normalColor = ButtonNormal; c.highlightedColor = ButtonHover; btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            AddCenteredText(go.transform, label, fontSize, FontStyles.Bold, TextColor);
        }

        private TextMeshProUGUI AddCenteredText(Transform parent, string text, float size, FontStyles style, Color color)
        {
            var go = CreateUI("Txt", parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = color;
            return tmp;
        }

        private static void StretchFill(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
        }

        private static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
