using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Gameplay.Rendering;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Professional in-game tile editor UI.
    /// Layout: LEFT toolbar/picker panel, RIGHT info/layers sidebar, BOTTOM layer indicator.
    /// Toggle with F6. Dark theme with amber accent.
    /// </summary>
    public class TileEditorUI : MonoBehaviour
    {
        // ── Design Tokens ──
        private static readonly Color BG_PANEL       = new Color(0.09f, 0.09f, 0.12f, 0.94f);
        private static readonly Color BG_SURFACE     = new Color(0.13f, 0.13f, 0.17f, 1f);
        private static readonly Color BG_ELEVATED    = new Color(0.17f, 0.17f, 0.22f, 1f);
        private static readonly Color ACCENT         = new Color(0.90f, 0.76f, 0.38f, 1f);
        private static readonly Color ACCENT_DIM     = new Color(0.90f, 0.76f, 0.38f, 0.45f);
        private static readonly Color ACCENT_BG      = new Color(0.90f, 0.76f, 0.38f, 0.15f);
        private static readonly Color TEXT_PRIMARY    = new Color(0.93f, 0.93f, 0.96f, 1f);
        private static readonly Color TEXT_SECONDARY  = new Color(0.60f, 0.62f, 0.68f, 1f);
        private static readonly Color TEXT_MUTED      = new Color(0.42f, 0.44f, 0.50f, 1f);
        private static readonly Color BTN_NORMAL      = new Color(0.16f, 0.16f, 0.21f, 1f);
        private static readonly Color BTN_HOVER       = new Color(0.22f, 0.22f, 0.28f, 1f);
        private static readonly Color BTN_ACTIVE      = new Color(0.90f, 0.76f, 0.38f, 0.55f);
        private static readonly Color SLOT_BG         = new Color(0.13f, 0.13f, 0.17f, 1f);
        private static readonly Color SLOT_HOVER      = new Color(0.22f, 0.22f, 0.28f, 1f);
        private static readonly Color SLOT_SELECTED   = new Color(0.90f, 0.76f, 0.38f, 0.65f);
        private static readonly Color LAYER_ACTIVE_BG = new Color(0.90f, 0.76f, 0.38f, 0.12f);
        private static readonly Color VIS_ON          = new Color(0.40f, 0.88f, 0.40f, 1f);
        private static readonly Color VIS_OFF         = new Color(0.50f, 0.50f, 0.50f, 0.45f);
        private static readonly Color BORDER          = new Color(0.90f, 0.76f, 0.38f, 0.35f);
        private static readonly Color SEPARATOR       = new Color(0.25f, 0.25f, 0.30f, 0.6f);
        private static readonly Color CYAN_ACCENT     = new Color(0.30f, 0.85f, 0.90f, 1f);
        private static readonly Color GREEN_ACCENT    = new Color(0.30f, 0.90f, 0.45f, 1f);

        private const float LEFT_WIDTH = 300f;
        private const float RIGHT_WIDTH = 230f;
        private const float PANEL_PAD = 10f;
        private const float SECTION_SPACING = 6f;
        private const float INNER_PAD = 10f;

        // ── Callbacks ──
        private TileEditorState _state;
        private TileCatalog _catalog;
        private System.Action<TileCatalog.TileEntry> _onTileSelected;
        private System.Action<TileEditorState.Tool> _onToolChanged;
        private System.Action<TilemapLayerSetup.TilemapLayer> _onLayerChanged;
        private System.Action<int> _onBrushSizeChanged;

        // ── Canvas ──
        private Canvas _canvas;

        // ── Left Panel ──
        private GameObject _leftPanel;
        private readonly Dictionary<TileEditorState.Tool, Image> _toolButtonImages = new Dictionary<TileEditorState.Tool, Image>();
        private readonly Dictionary<TileEditorState.Tool, TextMeshProUGUI> _toolButtonTexts = new Dictionary<TileEditorState.Tool, TextMeshProUGUI>();
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

        // ── Right: View Panel ──
        private GameObject _viewPanel;
        private Image _viewHoveredImg;
        private TextMeshProUGUI _viewHoveredLabel;
        private Image _viewSelectedImg;
        private TextMeshProUGUI _viewSelectedLabel;
        private Image _viewChoiceImg;
        private TextMeshProUGUI _viewChoiceLabel;
        private TextMeshProUGUI _viewLayerHoveredText;
        private TextMeshProUGUI _viewLayerSelectedText;

        // ── Right: Layers Panel ──
        private GameObject _layersPanel;
        private readonly List<Image> _layerRowBgs = new List<Image>();
        private readonly List<TextMeshProUGUI> _layerRowLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> _layerVisIcons = new List<Image>();
        private readonly bool[] _layerVisibility = new bool[9];

        // ── Bottom: Layer Indicator ──
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
                kvp.Value.color = kvp.Key == _state.CurrentTool ? BTN_ACTIVE : BTN_NORMAL;
            foreach (var kvp in _toolButtonTexts)
                kvp.Value.color = kvp.Key == _state.CurrentTool ? ACCENT : TEXT_SECONDARY;
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
                _selectedTilePreviewImg.color = sprite != null ? Color.white : SLOT_BG;
            }
            if (_selectedTileNameText != null)
                _selectedTileNameText.text = tileName ?? "(none)";
            if (_viewChoiceImg != null)
            {
                _viewChoiceImg.sprite = sprite;
                _viewChoiceImg.color = sprite != null ? Color.white : SLOT_BG;
            }
            if (_viewChoiceLabel != null)
                _viewChoiceLabel.text = tileName ?? "";
        }

        public void UpdateViewPanelHovered(Sprite sprite, string name, string layerName)
        {
            if (_viewHoveredImg != null)
            {
                _viewHoveredImg.sprite = sprite;
                _viewHoveredImg.color = sprite != null ? Color.white : SLOT_BG;
            }
            if (_viewHoveredLabel != null) _viewHoveredLabel.text = name ?? "";
            if (_viewLayerHoveredText != null) _viewLayerHoveredText.text = $"  {layerName}";
        }

        public void UpdateViewPanelSelected(Sprite sprite, string name)
        {
            if (_viewSelectedImg != null)
            {
                _viewSelectedImg.sprite = sprite;
                _viewSelectedImg.color = sprite != null ? Color.white : SLOT_BG;
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
            BuildRightSidebar(canvasGo.transform);
            BuildLayerIndicator(canvasGo.transform);

            if (_catalog != null)
            {
                _currentCategory = "";
                PopulateCategoryTabs();
                PopulateTileGrid(_currentCategory);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LEFT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void BuildLeftPanel(Transform canvasT)
        {
            _leftPanel = MakePanel("LeftPanel", canvasT,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(PANEL_PAD, 0f), new Vector2(LEFT_WIDTH, -16f));

            var layout = _leftPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 8);
            layout.spacing = SECTION_SPACING;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            _leftPanel.AddComponent<CanvasGroup>();

            // Title
            BuildSectionHeader(_leftPanel.transform, "TILE EDITOR", 20f);
            BuildSeparator(_leftPanel.transform);

            // Toolbar with shortcut labels
            BuildToolbar(_leftPanel.transform);
            BuildSeparator(_leftPanel.transform);

            // Layer + Brush row (compact)
            BuildLayerAndBrushRow(_leftPanel.transform);
            BuildSeparator(_leftPanel.transform);

            // Selected tile preview
            BuildSelectedTilePreview(_leftPanel.transform);
            BuildSeparator(_leftPanel.transform);

            // Category tabs
            BuildSectionLabel(_leftPanel.transform, "CATEGORIES");
            BuildCategorySelector(_leftPanel.transform);

            // Tile picker grid
            BuildSectionLabel(_leftPanel.transform, "TILES");
            BuildTilePicker(_leftPanel.transform);
            BuildTileCountRow(_leftPanel.transform);

            // Status bar
            BuildSeparator(_leftPanel.transform);
            BuildStatusBar(_leftPanel.transform);
        }

        private void BuildSectionHeader(Transform parent, string text, float fontSize)
        {
            var go = CreateUI("Header_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 6f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = ACCENT;
            tmp.characterSpacing = 4f;
        }

        private void BuildSectionLabel(Transform parent, string text)
        {
            var go = CreateUI("Label_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 11f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = TEXT_SECONDARY;
            tmp.characterSpacing = 2f;
        }

        private void BuildSeparator(Transform parent)
        {
            var go = CreateUI("Sep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = SEPARATOR;
        }

        private void BuildToolbar(Transform parent)
        {
            var go = CreateUI("Toolbar", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 44f;
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            CreateToolBtn(go.transform, "Brush", "B", TileEditorState.Tool.Brush);
            CreateToolBtn(go.transform, "Erase", "E", TileEditorState.Tool.Eraser);
            CreateToolBtn(go.transform, "Fill", "F", TileEditorState.Tool.Fill);
            CreateToolBtn(go.transform, "Pick", "I", TileEditorState.Tool.Eyedropper);
            CreateToolBtn(go.transform, "Select", "S", TileEditorState.Tool.Select);
        }

        private void CreateToolBtn(Transform parent, string label, string shortcut, TileEditorState.Tool tool)
        {
            var go = CreateUI($"Tool_{tool}", parent);
            var img = go.AddComponent<Image>();
            bool active = tool == _state.CurrentTool;
            img.color = active ? BTN_ACTIVE : BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            var cap = tool;
            btn.onClick.AddListener(() => _onToolChanged?.Invoke(cap));

            // Vertical layout: label + shortcut
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.spacing = -2f;

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 11f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color = active ? ACCENT : TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = shortcut;
            keyTmp.fontSize = 9f;
            keyTmp.alignment = TextAlignmentOptions.Center;
            keyTmp.color = TEXT_MUTED;

            _toolButtonImages[tool] = img;
            _toolButtonTexts[tool] = lblTmp;
        }

        private void BuildLayerAndBrushRow(Transform parent)
        {
            // Layer selector
            var layerRow = CreateUI("LayerRow", parent);
            layerRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var lh = layerRow.AddComponent<HorizontalLayoutGroup>();
            lh.spacing = 4f; lh.childForceExpandWidth = false; lh.childForceExpandHeight = true;
            lh.childControlWidth = true; lh.childControlHeight = true;

            // Layer label
            var layerLbl = CreateUI("LLbl", layerRow.transform);
            layerLbl.AddComponent<LayoutElement>().preferredWidth = 44f;
            var lt = layerLbl.AddComponent<TextMeshProUGUI>();
            lt.text = "Layer"; lt.fontSize = 11f; lt.alignment = TextAlignmentOptions.Left; lt.color = TEXT_SECONDARY;

            var prev = CreateUI("Prev", layerRow.transform);
            prev.AddComponent<LayoutElement>().preferredWidth = 26f;
            MakeBtn(prev, "\u25C0", () => { int v = (int)_state.CurrentLayer - 1; if (v < 0) v = 8; _onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v); }, 10f);

            var lbl = CreateUI("LayerVal", layerRow.transform);
            lbl.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _layerLabel = lbl.AddComponent<TextMeshProUGUI>();
            _layerLabel.text = _state.CurrentLayer.ToString();
            _layerLabel.fontSize = 13f;
            _layerLabel.fontStyle = FontStyles.Bold;
            _layerLabel.alignment = TextAlignmentOptions.Center;
            _layerLabel.color = ACCENT;

            var next = CreateUI("Next", layerRow.transform);
            next.AddComponent<LayoutElement>().preferredWidth = 26f;
            MakeBtn(next, "\u25B6", () => { int v = (int)_state.CurrentLayer + 1; if (v > 8) v = 0; _onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v); }, 10f);

            // Spacer
            var spacer = CreateUI("Spacer", layerRow.transform);
            spacer.AddComponent<LayoutElement>().preferredWidth = 8f;

            // Brush size
            var brushLbl = CreateUI("BLbl", layerRow.transform);
            brushLbl.AddComponent<LayoutElement>().preferredWidth = 34f;
            var bt = brushLbl.AddComponent<TextMeshProUGUI>();
            bt.text = "Size"; bt.fontSize = 11f; bt.alignment = TextAlignmentOptions.Left; bt.color = TEXT_SECONDARY;

            var minus = CreateUI("Minus", layerRow.transform);
            minus.AddComponent<LayoutElement>().preferredWidth = 22f;
            MakeBtn(minus, "-", () => _onBrushSizeChanged?.Invoke(Mathf.Max(1, _state.BrushSize - 1)), 12f);

            var val = CreateUI("Val", layerRow.transform);
            val.AddComponent<LayoutElement>().preferredWidth = 36f;
            _brushSizeLabel = val.AddComponent<TextMeshProUGUI>();
            _brushSizeLabel.text = $"{_state.BrushSize}x{_state.BrushSize}";
            _brushSizeLabel.fontSize = 12f;
            _brushSizeLabel.fontStyle = FontStyles.Bold;
            _brushSizeLabel.alignment = TextAlignmentOptions.Center;
            _brushSizeLabel.color = TEXT_PRIMARY;

            var plus = CreateUI("Plus", layerRow.transform);
            plus.AddComponent<LayoutElement>().preferredWidth = 22f;
            MakeBtn(plus, "+", () => _onBrushSizeChanged?.Invoke(Mathf.Min(5, _state.BrushSize + 1)), 12f);
        }

        private void BuildSelectedTilePreview(Transform parent)
        {
            var row = CreateUI("SelectedPreview", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 56f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 4, 4);

            // Preview image with accent border
            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 48f;
            _selectedTilePreviewImg = imgGo.AddComponent<Image>();
            _selectedTilePreviewImg.color = SLOT_BG;
            _selectedTilePreviewImg.preserveAspect = true;
            var outline = imgGo.AddComponent<Outline>();
            outline.effectColor = ACCENT;
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            // Name + label
            var infoGo = CreateUI("Info", row.transform);
            infoGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = infoGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2f; vl.childForceExpandHeight = false; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childControlWidth = true;

            var labelGo = CreateUI("Lbl", infoGo.transform);
            labelGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = "SELECTED"; labelTmp.fontSize = 9f; labelTmp.color = TEXT_MUTED;
            labelTmp.characterSpacing = 2f;

            var nameGo = CreateUI("Name", infoGo.transform);
            nameGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _selectedTileNameText = nameGo.AddComponent<TextMeshProUGUI>();
            _selectedTileNameText.text = "(none)";
            _selectedTileNameText.fontSize = 13f;
            _selectedTileNameText.alignment = TextAlignmentOptions.Left;
            _selectedTileNameText.color = TEXT_PRIMARY;
            _selectedTileNameText.enableWordWrapping = true;
        }

        private void BuildCategorySelector(Transform parent)
        {
            var scrollGo = CreateUI("CatScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.preferredHeight = 56f;
            le.minHeight = 32f;
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = BG_SURFACE;

            var content = CreateUI("Content", vp.transform);
            _categoryTabsContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1); cr.sizeDelta = Vector2.zero;

            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(130f, 24f);
            gl.spacing = new Vector2(3f, 3f);
            gl.padding = new RectOffset(3, 3, 3, 3);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 2;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = cr; sr.viewport = vp.GetComponent<RectTransform>();
        }

        private void BuildTilePicker(Transform parent)
        {
            var scrollGo = CreateUI("TileScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f; le.minHeight = 140f;
            _tileScrollRect = scrollGo.AddComponent<ScrollRect>();
            _tileScrollRect.horizontal = false; _tileScrollRect.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = BG_SURFACE;

            var content = CreateUI("Content", vp.transform);
            _tileGridContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1); cr.sizeDelta = Vector2.zero;
            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(52f, 52f);
            gl.spacing = new Vector2(4f, 4f);
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
            go.AddComponent<LayoutElement>().preferredHeight = 16f;
            _tileCountText = go.AddComponent<TextMeshProUGUI>();
            _tileCountText.text = "";
            _tileCountText.fontSize = 10f;
            _tileCountText.alignment = TextAlignmentOptions.Right;
            _tileCountText.color = TEXT_MUTED;
        }

        private void BuildStatusBar(Transform parent)
        {
            var go = CreateUI("Status", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            _statusText = go.AddComponent<TextMeshProUGUI>();
            _statusText.text = "F6 Toggle  |  B E F I S Tools  |  Scroll Layer  |  Ctrl+Z Undo";
            _statusText.fontSize = 9f;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.color = TEXT_MUTED;
        }

        // ═══════════════════════════════════════════════════════════════
        //  RIGHT SIDEBAR (View + Layers merged)
        // ═══════════════════════════════════════════════════════════════

        private void BuildRightSidebar(Transform canvasT)
        {
            // View panel (top-right)
            _viewPanel = MakePanel("ViewPanel", canvasT,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-PANEL_PAD, -8f), new Vector2(RIGHT_WIDTH, 240f));

            var vLayout = _viewPanel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(10, 10, 8, 8);
            vLayout.spacing = 4f;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;

            BuildSectionLabel(_viewPanel.transform, "INSPECTOR");
            BuildViewRow(_viewPanel.transform, "Hovered", CYAN_ACCENT, out _viewHoveredImg, out _viewHoveredLabel);
            BuildViewRow(_viewPanel.transform, "Selected", GREEN_ACCENT, out _viewSelectedImg, out _viewSelectedLabel);
            BuildViewRow(_viewPanel.transform, "Brush", ACCENT, out _viewChoiceImg, out _viewChoiceLabel);
            BuildSeparator(_viewPanel.transform);

            // Layer info
            var lhGo = CreateUI("LayerHov", _viewPanel.transform);
            lhGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lhTmp = lhGo.AddComponent<TextMeshProUGUI>();
            lhTmp.text = "Hover Layer"; lhTmp.fontSize = 10f; lhTmp.color = TEXT_MUTED;
            var lhVal = CreateUI("LHVal", _viewPanel.transform);
            lhVal.AddComponent<LayoutElement>().preferredHeight = 18f;
            _viewLayerHoveredText = lhVal.AddComponent<TextMeshProUGUI>();
            _viewLayerHoveredText.text = ""; _viewLayerHoveredText.fontSize = 12f;
            _viewLayerHoveredText.fontStyle = FontStyles.Bold; _viewLayerHoveredText.color = ACCENT;

            var lsGo = CreateUI("LayerSel", _viewPanel.transform);
            lsGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lsTmp = lsGo.AddComponent<TextMeshProUGUI>();
            lsTmp.text = "Active Layer"; lsTmp.fontSize = 10f; lsTmp.color = TEXT_MUTED;
            var lsVal = CreateUI("LSVal", _viewPanel.transform);
            lsVal.AddComponent<LayoutElement>().preferredHeight = 18f;
            _viewLayerSelectedText = lsVal.AddComponent<TextMeshProUGUI>();
            _viewLayerSelectedText.text = $"  {(int)_state.CurrentLayer}: {_state.CurrentLayer}";
            _viewLayerSelectedText.fontSize = 12f;
            _viewLayerSelectedText.fontStyle = FontStyles.Bold; _viewLayerSelectedText.color = ACCENT;

            // Layers panel (below view panel)
            _layersPanel = MakePanel("LayersPanel", canvasT,
                new Vector2(1f, 0f), new Vector2(1f, 0.52f), new Vector2(1f, 0f),
                new Vector2(-PANEL_PAD, 8f), new Vector2(RIGHT_WIDTH, 0f));

            var lLayout = _layersPanel.AddComponent<VerticalLayoutGroup>();
            lLayout.padding = new RectOffset(8, 8, 6, 6);
            lLayout.spacing = 2f;
            lLayout.childForceExpandWidth = true;
            lLayout.childForceExpandHeight = false;
            lLayout.childControlWidth = true;
            lLayout.childControlHeight = true;

            BuildSectionLabel(_layersPanel.transform, "LAYERS");

            var layers = System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));
            foreach (TilemapLayerSetup.TilemapLayer layer in layers)
                BuildLayerRow(_layersPanel.transform, layer);
        }

        private void BuildViewRow(Transform parent, string label, Color accentColor,
            out Image tileImg, out TextMeshProUGUI nameText)
        {
            var row = CreateUI($"View_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 38f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;
            h.padding = new RectOffset(2, 2, 2, 2);

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 34f;
            tileImg = imgGo.AddComponent<Image>();
            tileImg.color = SLOT_BG;
            tileImg.preserveAspect = true;
            var ol = imgGo.AddComponent<Outline>();
            ol.effectColor = accentColor;
            ol.effectDistance = new Vector2(1.5f, 1.5f);

            var txtGo = CreateUI("Txt", row.transform);
            txtGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = txtGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0; vl.childForceExpandHeight = true; vl.childControlHeight = true;

            var lblGo = CreateUI("Lbl", txtGo.transform);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label; lblTmp.fontSize = 9f; lblTmp.color = accentColor;

            var valGo = CreateUI("Val", txtGo.transform);
            nameText = valGo.AddComponent<TextMeshProUGUI>();
            nameText.text = ""; nameText.fontSize = 12f; nameText.color = TEXT_PRIMARY;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
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
            bg.color = layer == _state.CurrentLayer ? LAYER_ACTIVE_BG : Color.clear;
            _layerRowBgs.Add(bg);

            // Visibility toggle
            var visGo = CreateUI("Vis", row.transform);
            visGo.AddComponent<LayoutElement>().preferredWidth = 16f;
            var visImg = visGo.AddComponent<Image>();
            visImg.color = VIS_ON;
            _layerVisIcons.Add(visImg);
            var visBtn = visGo.AddComponent<Button>();
            visBtn.targetGraphic = visImg;
            int capIdx = idx;
            visBtn.onClick.AddListener(() => ToggleLayerVisibility(capIdx));

            // Index
            var idxGo = CreateUI("Idx", row.transform);
            idxGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var idxTmp = idxGo.AddComponent<TextMeshProUGUI>();
            idxTmp.text = idx.ToString(); idxTmp.fontSize = 11f;
            idxTmp.alignment = TextAlignmentOptions.Center; idxTmp.color = ACCENT_DIM;

            // Name
            var nameGo = CreateUI("Name", row.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = layer.ToString(); nameTmp.fontSize = 11f;
            nameTmp.alignment = TextAlignmentOptions.Left;
            nameTmp.color = layer == _state.CurrentLayer ? TEXT_PRIMARY : TEXT_SECONDARY;
            _layerRowLabels.Add(nameTmp);

            // Click to select
            var rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = bg;
            var colors = rowBtn.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = BTN_HOVER;
            rowBtn.colors = colors;
            var capLayer = layer;
            rowBtn.onClick.AddListener(() => _onLayerChanged?.Invoke(capLayer));
        }

        private void ToggleLayerVisibility(int layerIdx)
        {
            _layerVisibility[layerIdx] = !_layerVisibility[layerIdx];
            if (layerIdx < _layerVisIcons.Count)
                _layerVisIcons[layerIdx].color = _layerVisibility[layerIdx] ? VIS_ON : VIS_OFF;
        }

        public bool IsLayerVisible(int layerIdx)
        {
            return layerIdx >= 0 && layerIdx < 9 && _layerVisibility[layerIdx];
        }

        private void RefreshLayersPanel()
        {
            for (int i = 0; i < _layerRowBgs.Count && i < 9; i++)
            {
                bool active = i == (int)_state.CurrentLayer;
                _layerRowBgs[i].color = active ? LAYER_ACTIVE_BG : Color.clear;
                if (i < _layerRowLabels.Count)
                    _layerRowLabels[i].color = active ? TEXT_PRIMARY : TEXT_SECONDARY;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  BOTTOM LAYER INDICATOR
        // ═══════════════════════════════════════════════════════════════

        private void BuildLayerIndicator(Transform canvasT)
        {
            _layerIndicatorPanel = CreateUI("LayerIndicator", canvasT);
            var r = _layerIndicatorPanel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0f); r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(0f, 12f);
            r.sizeDelta = new Vector2(240f, 34f);
            var bg = _layerIndicatorPanel.AddComponent<Image>();
            bg.color = BG_PANEL;
            var ol = _layerIndicatorPanel.AddComponent<Outline>();
            ol.effectColor = ACCENT_DIM;
            ol.effectDistance = new Vector2(1f, 1f);

            _layerIndicator = AddCenteredText(_layerIndicatorPanel.transform,
                $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}", 16f, FontStyles.Bold, ACCENT);
        }

        // ═══════════════════════════════════════════════════════════════
        //  TILE GRID POPULATION
        // ═══════════════════════════════════════════════════════════════

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
            var go = CreateUI($"Cat_{displayName}", _categoryTabsContent);
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

            var tmp = AddCenteredText(go.transform, displayName, 10f, FontStyles.Normal,
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
                var go = CreateUI($"Slot_{i}", _tileGridContent);
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

                // Name label at bottom
                var nameGo = CreateUI("TName", go.transform);
                var nr = nameGo.GetComponent<RectTransform>();
                nr.anchorMin = new Vector2(0f, 0f); nr.anchorMax = new Vector2(1f, 0.22f);
                nr.sizeDelta = Vector2.zero;
                var nbg = nameGo.AddComponent<Image>();
                nbg.color = new Color(0f, 0f, 0f, 0.7f); nbg.raycastTarget = false;
                var nt = AddCenteredText(nameGo.transform, entry.tileName, 7f, FontStyles.Normal, TEXT_PRIMARY);
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
                if (img != null) img.color = i == _selectedSlotIndex ? SLOT_SELECTED : SLOT_BG;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private GameObject MakePanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreateUI(name, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot;
            r.anchoredPosition = anchoredPos; r.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = BG_PANEL;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = BORDER; ol.effectDistance = new Vector2(1f, 1f);
            return go;
        }

        private void MakeBtn(GameObject go, string label, UnityEngine.Events.UnityAction onClick, float fontSize = 13f)
        {
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            AddCenteredText(go.transform, label, fontSize, FontStyles.Bold, TEXT_PRIMARY);
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
