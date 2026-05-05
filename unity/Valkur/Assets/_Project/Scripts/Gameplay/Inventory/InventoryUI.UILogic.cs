using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Inventory UI builder + runtime behaviour (Phase 2: full functionality).
    /// Mirrors Python's InventoryUISystem: header (portrait/name/level),
    /// 3×3 equipment preview, character body avatar, 5×5 grid with tab filter,
    /// gold pill footer, drag-and-drop (slot↔slot swap/merge, drop-to-world),
    /// double-click to use, single-click to select.
    /// </summary>
    public partial class InventoryUI
    {
        // ── Layout constants (mirror Python ui_constants where relevant) ──
        private const int   GRID_COLS_PY = 5;
        private const int   GRID_ROWS_PY = 5;
        private const float SLOT_PX      = 52f;
        private const float SLOT_GAP     = 4f;
        private const float PANEL_PAD_X  = 12f;
        private const float PANEL_PAD_Y  = 10f;
        private const float HDR_BAR_H    = 48f;                          // taller so 48-px portrait fits
        private const float EQUIP_BLOCK_H = SLOT_PX * 3 + SLOT_GAP * 2;  // 3×3 grid for equipped items
        private const float TOOLTIP_H    = 38f;
        private const float FOOTER_H     = 32f;
        private const float PANEL_TOP_MARGIN   = 16f;
        private const float PANEL_RIGHT_MARGIN = 16f;

        // Labels for the 9 equipment slots, row-major. Layout follows a
        // humanoid paper-doll: hands on the sides, helmet centered up top,
        // body in the middle, legs/jewelry at the bottom. Slots with no
        // backing item render the label as placeholder text instead.
        private static readonly string[] EQUIP_SLOT_LABELS =
        {
            "L. Hand", "Helmet", "R. Hand",
            "Arms",    "Chest",  "Gloves",
            "Pants",   "Boots",  "Jewelry",
        };

        private static readonly string[] CURRENCY_ITEM_IDS =
            { "gold", "coins", "coin", "gold_coin" };

        // ── Phase-2 visual refs ──
        private TextMeshProUGUI _hdrNameText;
        private TextMeshProUGUI _hdrLevelText;
        private Image           _portraitImg;
        private TextMeshProUGUI _goldText;
        private GameObject[]       _equipObjects;        // 9 cells (3×3 paper doll)
        private Image[]            _equipIcons;
        private TextMeshProUGUI[]  _equipQtyTexts;
        private TextMeshProUGUI[]  _equipLabels;         // placeholder labels shown when slot empty
        private Outline[]          _equipOutlines;       // recolored yellow when targeted by a deposit drag

        // Drag state
        private GameObject _dragGhost;
        private Image      _dragGhostImg;
        private int        _dragSourceIndex = -1;
        private RectTransform _dragGhostRt;

        // ─────────────────────────────────────────────────────────────────────
        //  BuildUI — full visual rebuild
        // ─────────────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("InventoryCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(1600, 800);
            scaler.matchWidthOrHeight   = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Panel sizing
            int totalSlots = GRID_COLS_PY * GRID_ROWS_PY;
            float gridW = GRID_COLS_PY * SLOT_PX + (GRID_COLS_PY - 1) * SLOT_GAP;
            float gridH = GRID_ROWS_PY * SLOT_PX + (GRID_ROWS_PY - 1) * SLOT_GAP;
            float panelWidth  = gridW + PANEL_PAD_X * 2;
            float panelHeight = PANEL_HDR_H
                              + PANEL_PAD_Y
                              + HDR_BAR_H + 6f
                              + EQUIP_BLOCK_H + 8f
                              + gridH + 6f
                              + TOOLTIP_H + 6f
                              + FOOTER_H
                              + PANEL_PAD_Y;

            _panelGo   = CreateUIObject("InventoryPanel", _canvas.transform);
            _panelRect = _panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(1f, 1f);
            _panelRect.anchorMax = new Vector2(1f, 1f);
            _panelRect.pivot     = new Vector2(1f, 1f);
            _panelRect.anchoredPosition = new Vector2(-PANEL_RIGHT_MARGIN, -PANEL_TOP_MARGIN);
            _panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            var panelBg = _panelGo.AddComponent<Image>();
            panelBg.color = TileEditorTheme.PanelBg;

            var panelOl = _panelGo.AddComponent<Outline>();
            panelOl.effectColor    = TileEditorTheme.Border;
            panelOl.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            _panelGroup = _panelGo.AddComponent<CanvasGroup>();

            // Chrome header
            var hdrGo = CreateUIObject("ChromeHeader", _panelGo.transform);
            var hdrRt = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0f, 1f);
            hdrRt.anchorMax = new Vector2(1f, 1f);
            hdrRt.pivot     = new Vector2(0.5f, 1f);
            hdrRt.anchoredPosition = Vector2.zero;
            hdrRt.sizeDelta = new Vector2(0f, PANEL_HDR_H);
            hdrGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            // Window drag: clicking-and-dragging the header moves the panel.
            var dragger = hdrGo.AddComponent<WindowDragHandler>();
            dragger.Target = _panelRect;

            var hdrTitleGo = CreateUIObject("Title", hdrGo.transform);
            var hdrTitleRt = hdrTitleGo.GetComponent<RectTransform>();
            hdrTitleRt.anchorMin = Vector2.zero;
            hdrTitleRt.anchorMax = Vector2.one;
            hdrTitleRt.offsetMin = new Vector2(10f, 0f);
            hdrTitleRt.offsetMax = new Vector2(-(PANEL_HDR_BTN_W * 2f + 6f), 0f);
            var hdrTitleTmp = hdrTitleGo.AddComponent<TextMeshProUGUI>();
            hdrTitleTmp.text             = "INVENTORY";
            hdrTitleTmp.fontSize         = 11f;
            hdrTitleTmp.fontStyle        = FontStyles.Bold;
            hdrTitleTmp.alignment        = TextAlignmentOptions.Left;
            hdrTitleTmp.color            = TileEditorTheme.HeaderTitle;
            hdrTitleTmp.characterSpacing = 2f;

            BuildHeaderCloseBtn(hdrGo.transform);
            BuildHeaderMinimizeBtn(hdrGo.transform);

            // Separator
            var sepGo = CreateUIObject("HdrSep", _panelGo.transform);
            var sepRt = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot     = new Vector2(0.5f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -PANEL_HDR_H);
            sepRt.sizeDelta = new Vector2(0f, 1f);
            sepGo.AddComponent<Image>().color = TileEditorTheme.Separator;

            // Content area
            var contentGo = CreateUIObject("Content", _panelGo.transform);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(PANEL_PAD_X, PANEL_PAD_Y);
            contentRt.offsetMax = new Vector2(-PANEL_PAD_X, -(PANEL_HDR_H + 1f + PANEL_PAD_Y));

            float y = 0f;
            BuildHeaderRow(contentGo.transform, ref y);                 y += 6f;
            BuildEquipmentGrid(contentGo.transform, ref y);             y += 8f;

            _slotObjects     = new GameObject[totalSlots];
            _slotBackgrounds = new Image[totalSlots];
            _slotOutlines    = new Outline[totalSlots];
            _slotIcons       = new Image[totalSlots];
            _slotQuantities  = new TextMeshProUGUI[totalSlots];
            BuildMainGrid(contentGo.transform, ref y, totalSlots); y += 6f;

            // Tooltip
            var tooltipGo = CreateUIObject("Tooltip", contentGo.transform);
            PlaceTopAnchored(tooltipGo, y, 0f, TOOLTIP_H);
            _tooltipText           = tooltipGo.AddComponent<TextMeshProUGUI>();
            _tooltipText.text      = "Tab/I close  |  Q drop  |  double-click use  |  drag to move";
            _tooltipText.fontSize  = 11f;
            _tooltipText.alignment = TextAlignmentOptions.Center;
            _tooltipText.color     = TEXT_MUTED;
            _tooltipText.enableWordWrapping = true;
            y += TOOLTIP_H + 6f;

            BuildGoldFooter(contentGo.transform, y);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Sub-builders
        // ─────────────────────────────────────────────────────────────────────

        private void BuildHeaderCloseBtn(Transform hdrParent)
        {
            var go = CreateUIObject("CloseBtn", hdrParent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-2f, 0f);
            rt.sizeDelta = new Vector2(PANEL_HDR_BTN_W, 0f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.72f, 0.10f, 0.10f, 0.85f);

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = new Color(0.72f, 0.10f, 0.10f, 0.85f);
            c.highlightedColor = PANEL_HDR_CLOSE_HOVER;
            c.pressedColor     = new Color(0.55f, 0.05f, 0.05f, 1f);
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SetVisible(false));

            AddCenteredText(go.transform, "X", 12f, FontStyles.Bold, TEXT_PRIMARY);
        }

        private void BuildHeaderMinimizeBtn(Transform hdrParent)
        {
            var go = CreateUIObject("MinimizeBtn", hdrParent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 0.5f);
            // Sit immediately to the left of the close (X) button.
            rt.anchoredPosition = new Vector2(-(PANEL_HDR_BTN_W + 4f), 0f);
            rt.sizeDelta = new Vector2(PANEL_HDR_BTN_W, 0f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.18f, 0.22f, 0.85f);

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = new Color(0.18f, 0.18f, 0.22f, 0.85f);
            c.highlightedColor = new Color(0.30f, 0.30f, 0.36f, 1f);
            c.pressedColor     = new Color(0.10f, 0.10f, 0.12f, 1f);
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(MinimizeToTray);

            AddCenteredText(go.transform, "_", 14f, FontStyles.Bold, TEXT_PRIMARY);
        }

        private void MinimizeToTray()
        {
            SetVisible(false);
        }

        internal void RegisterTrayButton()
        {
            var bar = Valkur.UIKit.HUDIconBar.Instance;
            if (bar == null) return;
            var sprite = LoadHUDSprite("Assets/_Project/Art/UI/hud/inventory_hud_button.png");
            bar.Register("inventory", sprite, () => SetVisible(!_visible), order: 0);
        }

        private static Sprite LoadHUDSprite(string assetPath)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
#else
            return null;
#endif
        }

        private void BuildHeaderRow(Transform parent, ref float y)
        {
            var rowGo = CreateUIObject("HeaderRow", parent);
            PlaceTopAnchored(rowGo, y, 0f, HDR_BAR_H);

            // Portrait (left) — square HDR_BAR_H x HDR_BAR_H
            var portraitGo = CreateUIObject("Portrait", rowGo.transform);
            var portRt = portraitGo.GetComponent<RectTransform>();
            portRt.anchorMin = new Vector2(0f, 0f);
            portRt.anchorMax = new Vector2(0f, 1f);
            portRt.pivot     = new Vector2(0f, 0.5f);
            portRt.anchoredPosition = new Vector2(0f, 0f);
            portRt.sizeDelta = new Vector2(HDR_BAR_H, 0f);
            portraitGo.AddComponent<Image>().color = BG_SURFACE;
            var portOl = portraitGo.AddComponent<Outline>();
            portOl.effectColor    = TileEditorTheme.Border;
            portOl.effectDistance = new Vector2(1f, 1f);

            var portIconGo = CreateUIObject("Icon", portraitGo.transform);
            var picRt = portIconGo.GetComponent<RectTransform>();
            picRt.anchorMin = Vector2.zero;
            picRt.anchorMax = Vector2.one;
            picRt.offsetMin = new Vector2(2f, 2f);
            picRt.offsetMax = new Vector2(-2f, -2f);
            _portraitImg = portIconGo.AddComponent<Image>();
            _portraitImg.preserveAspect = true;
            _portraitImg.enabled = false;

            // Name (centered vertically, beside portrait)
            var nameGo = CreateUIObject("Name", rowGo.transform);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot     = new Vector2(0f, 0.5f);
            nameRt.offsetMin = new Vector2(HDR_BAR_H + 8f, 0f);
            nameRt.offsetMax = new Vector2(-130f, 0f);
            _hdrNameText           = nameGo.AddComponent<TextMeshProUGUI>();
            _hdrNameText.text      = "Hero";
            _hdrNameText.fontSize  = 16f;
            _hdrNameText.fontStyle = FontStyles.Bold;
            _hdrNameText.alignment = TextAlignmentOptions.MidlineLeft;
            _hdrNameText.color     = TEXT_PRIMARY;
            _hdrNameText.enableWordWrapping = false;
            _hdrNameText.overflowMode = TextOverflowModes.Ellipsis;

            // Level (right)
            var lvlGo = CreateUIObject("Level", rowGo.transform);
            var lvlRt = lvlGo.GetComponent<RectTransform>();
            lvlRt.anchorMin = new Vector2(1f, 0f);
            lvlRt.anchorMax = new Vector2(1f, 1f);
            lvlRt.pivot     = new Vector2(1f, 0.5f);
            lvlRt.anchoredPosition = new Vector2(0f, 0f);
            lvlRt.sizeDelta = new Vector2(128f, 0f);
            _hdrLevelText           = lvlGo.AddComponent<TextMeshProUGUI>();
            _hdrLevelText.text      = "Lvl 1 (0%)";
            _hdrLevelText.fontSize  = 13f;
            _hdrLevelText.fontStyle = FontStyles.Bold;
            _hdrLevelText.alignment = TextAlignmentOptions.MidlineRight;
            _hdrLevelText.color     = ACCENT;

            y += HDR_BAR_H;
        }

        private void BuildEquipmentGrid(Transform parent, ref float y)
        {
            const int EQ_COLS = 3;
            float blockW = EQ_COLS * SLOT_PX + (EQ_COLS - 1) * SLOT_GAP;

            // Centered horizontally inside the panel content area.
            var rowGo = CreateUIObject("EquipGrid", parent);
            var rrt = rowGo.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0.5f, 1f);
            rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot     = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -y);
            rrt.sizeDelta = new Vector2(blockW, EQUIP_BLOCK_H);

            _equipObjects  = new GameObject[EquipmentView.SLOT_COUNT];
            _equipIcons    = new Image[EquipmentView.SLOT_COUNT];
            _equipQtyTexts = new TextMeshProUGUI[EquipmentView.SLOT_COUNT];
            _equipLabels   = new TextMeshProUGUI[EquipmentView.SLOT_COUNT];
            _equipOutlines = new Outline[EquipmentView.SLOT_COUNT];

            // Equipment slots use the unified index space so a single drag
            // handler can route bag↔equipment swaps without an extra "kind"
            // parameter. Bag = [0, DefaultBagCapacity), equipment = above.
            int equipBase = Inventory.DefaultBagCapacity;

            for (int i = 0; i < EquipmentView.SLOT_COUNT; i++)
            {
                int r = i / EQ_COLS;
                int c = i % EQ_COLS;
                float sx = c * (SLOT_PX + SLOT_GAP);
                float sy = -r * (SLOT_PX + SLOT_GAP);

                var slotGo = CreateUIObject($"Equip_{i}", rowGo.transform);
                var srt = slotGo.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, 1f);
                srt.anchorMax = new Vector2(0f, 1f);
                srt.pivot     = new Vector2(0f, 1f);
                srt.anchoredPosition = new Vector2(sx, sy);
                srt.sizeDelta = new Vector2(SLOT_PX, SLOT_PX);

                slotGo.AddComponent<Image>().color = SLOT_BG;
                var ol = slotGo.AddComponent<Outline>();
                ol.effectColor    = TileEditorTheme.Border;
                ol.effectDistance = new Vector2(1f, 1f);
                _equipOutlines[i] = ol;

                // Click / drag handler — bound to the unified index so the
                // EndSlotDrag router knows this is an equipment cell.
                var handler = slotGo.AddComponent<InventorySlotDragHandler>();
                handler.Bind(this, equipBase + i);

                // Placeholder label (shown only when the slot is empty).
                var labelGo = CreateUIObject("Label", slotGo.transform);
                var lrt = labelGo.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(2f, 2f);
                lrt.offsetMax = new Vector2(-2f, -2f);
                var lbl = labelGo.AddComponent<TextMeshProUGUI>();
                lbl.text          = EQUIP_SLOT_LABELS[i];
                lbl.fontSize      = 9f;
                lbl.alignment     = TextAlignmentOptions.Center;
                lbl.color         = TEXT_MUTED;
                lbl.raycastTarget = false;
                lbl.enableWordWrapping = true;
                _equipLabels[i] = lbl;

                // Item icon (shown when this equipment slot holds an item).
                var iconGo = CreateUIObject("Icon", slotGo.transform);
                var irt = iconGo.GetComponent<RectTransform>();
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = new Vector2(4f, 4f);
                irt.offsetMax = new Vector2(-4f, -4f);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.preserveAspect = true;
                iconImg.raycastTarget  = false;
                iconImg.enabled        = false;
                _equipIcons[i] = iconImg;

                // Stack quantity text (only relevant if a stackable item ends
                // up equipped, e.g. a stack of throwing knives in the off-hand).
                var qtyGo = CreateUIObject("Qty", slotGo.transform);
                var qrt = qtyGo.GetComponent<RectTransform>();
                qrt.anchorMin = new Vector2(1f, 0f);
                qrt.anchorMax = new Vector2(1f, 0f);
                qrt.pivot     = new Vector2(1f, 0f);
                qrt.anchoredPosition = new Vector2(-3f, 2f);
                qrt.sizeDelta = new Vector2(40f, 16f);
                var qtyText = qtyGo.AddComponent<TextMeshProUGUI>();
                qtyText.text          = "";
                qtyText.fontSize      = 11f;
                qtyText.fontStyle     = FontStyles.Bold;
                qtyText.alignment     = TextAlignmentOptions.BottomRight;
                qtyText.color         = ACCENT;
                qtyText.raycastTarget = false;
                _equipQtyTexts[i] = qtyText;

                _equipObjects[i] = slotGo;
            }

            y += EQUIP_BLOCK_H;
        }

        private void BuildMainGrid(Transform parent, ref float y, int totalSlots)
        {
            float gridW = GRID_COLS_PY * SLOT_PX + (GRID_COLS_PY - 1) * SLOT_GAP;
            float gridH = GRID_ROWS_PY * SLOT_PX + (GRID_ROWS_PY - 1) * SLOT_GAP;

            var gridGo = CreateUIObject("Grid", parent);
            var grt = gridGo.GetComponent<RectTransform>();
            grt.anchorMin = new Vector2(0.5f, 1f);
            grt.anchorMax = new Vector2(0.5f, 1f);
            grt.pivot     = new Vector2(0.5f, 1f);
            grt.anchoredPosition = new Vector2(0f, -y);
            grt.sizeDelta = new Vector2(gridW, gridH);

            for (int i = 0; i < totalSlots; i++)
            {
                int col = i % GRID_COLS_PY;
                int row = i / GRID_COLS_PY;
                float sx = col * (SLOT_PX + SLOT_GAP);
                float sy = -row * (SLOT_PX + SLOT_GAP);

                var slotGo = CreateUIObject($"Slot_{i}", gridGo.transform);
                var srt = slotGo.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, 1f);
                srt.anchorMax = new Vector2(0f, 1f);
                srt.pivot     = new Vector2(0f, 1f);
                srt.anchoredPosition = new Vector2(sx, sy);
                srt.sizeDelta = new Vector2(SLOT_PX, SLOT_PX);

                var bg = slotGo.AddComponent<Image>();
                bg.color = SLOT_BG;
                var ol = slotGo.AddComponent<Outline>();
                ol.effectColor    = TileEditorTheme.Border;
                ol.effectDistance = new Vector2(1f, 1f);

                // Drag/click handler
                var handler = slotGo.AddComponent<InventorySlotDragHandler>();
                handler.Bind(this, i);

                var iconGo = CreateUIObject("Icon", slotGo.transform);
                var irt = iconGo.GetComponent<RectTransform>();
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = new Vector2(4f, 4f);
                irt.offsetMax = new Vector2(-4f, -4f);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.preserveAspect = true;
                iconImg.raycastTarget  = false;
                iconImg.enabled        = false;

                var qtyGo = CreateUIObject("Qty", slotGo.transform);
                var qrt = qtyGo.GetComponent<RectTransform>();
                qrt.anchorMin = new Vector2(1f, 0f);
                qrt.anchorMax = new Vector2(1f, 0f);
                qrt.pivot     = new Vector2(1f, 0f);
                qrt.anchoredPosition = new Vector2(-3f, 2f);
                qrt.sizeDelta = new Vector2(40f, 16f);
                var qtyText = qtyGo.AddComponent<TextMeshProUGUI>();
                qtyText.text          = "";
                qtyText.fontSize      = 11f;
                qtyText.fontStyle     = FontStyles.Bold;
                qtyText.alignment     = TextAlignmentOptions.BottomRight;
                qtyText.color         = ACCENT;
                qtyText.raycastTarget = false;

                _slotObjects[i]     = slotGo;
                _slotBackgrounds[i] = bg;
                _slotOutlines[i]    = ol;
                _slotIcons[i]       = iconImg;
                _slotQuantities[i]  = qtyText;
            }

            y += gridH;
        }

        private void BuildGoldFooter(Transform parent, float y)
        {
            var pillGo = CreateUIObject("GoldPill", parent);
            var prt = pillGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 1f);
            prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot     = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -y);
            prt.sizeDelta = new Vector2(140f, FOOTER_H);

            pillGo.AddComponent<Image>().color = BG_SURFACE;
            var ol = pillGo.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(1f, 1f);

            var dotGo = CreateUIObject("Coin", pillGo.transform);
            var drt = dotGo.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0f, 0.5f);
            drt.anchorMax = new Vector2(0f, 0.5f);
            drt.pivot     = new Vector2(0f, 0.5f);
            drt.anchoredPosition = new Vector2(10f, 0f);
            drt.sizeDelta = new Vector2(16f, 16f);
            dotGo.AddComponent<Image>().color = ACCENT;

            var goldGo = CreateUIObject("GoldText", pillGo.transform);
            var grt = goldGo.GetComponent<RectTransform>();
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = new Vector2(32f, 0f);
            grt.offsetMax = new Vector2(-10f, 0f);
            _goldText           = goldGo.AddComponent<TextMeshProUGUI>();
            _goldText.text      = "0";
            _goldText.fontSize  = 13f;
            _goldText.fontStyle = FontStyles.Bold;
            _goldText.alignment = TextAlignmentOptions.MidlineLeft;
            _goldText.color     = ACCENT;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Layout helper
        // ─────────────────────────────────────────────────────────────────────

        private static void PlaceTopAnchored(GameObject go, float yFromTop, float width, float height)
        {
            var rt = go.GetComponent<RectTransform>();
            if (width > 0f)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -yFromTop);
                rt.sizeDelta = new Vector2(width, height);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -yFromTop);
                rt.sizeDelta = new Vector2(0f, height);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Refresh
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshAll()
        {
            UpdateHeaderInfo();
            UpdateEquipmentView();
            RefreshSlots();
            UpdateGold();
            UpdateSlotHighlights();
            UpdateTooltip();
        }

        // Reads directly from the player's equipment storage (no auto-mirror
        // from the bag). Empty cells keep their placeholder label visible so
        // the user can see what each slot is for.
        private void UpdateEquipmentView()
        {
            if (_equipIcons == null) return;

            var slots = _playerInventory != null ? _playerInventory.EquipmentSlots : null;
            for (int i = 0; i < EquipmentView.SLOT_COUNT && i < _equipIcons.Length; i++)
            {
                var slot = (slots != null && i < slots.Count) ? slots[i] : default;
                bool hasItem = !slot.IsEmpty;
                _equipIcons[i].enabled  = hasItem;
                _equipIcons[i].sprite   = hasItem ? (slot.Item.icon ?? slot.Item.iconSmall) : null;
                if (_equipQtyTexts[i] != null)
                    _equipQtyTexts[i].text = (hasItem && slot.Quantity > 1) ? slot.Quantity.ToString() : "";
                if (_equipLabels[i] != null)
                    _equipLabels[i].enabled = !hasItem;
            }
        }

        private void RefreshSlots()
        {
            if (_slotObjects == null) return;

            int slotCount = _slotObjects.Length;
            var slots = _playerInventory != null ? _playerInventory.Slots : null;
            int playerSlotCount = slots != null ? slots.Count : 0;

            for (int i = 0; i < slotCount; i++)
            {
                if (slots != null && i < playerSlotCount && !slots[i].IsEmpty)
                {
                    var slot = slots[i];
                    _slotIcons[i].enabled    = true;
                    _slotIcons[i].sprite     = slot.Item.icon ?? slot.Item.iconSmall;
                    _slotQuantities[i].text  = slot.Quantity > 1 ? slot.Quantity.ToString() : "";
                }
                else
                {
                    _slotIcons[i].enabled   = false;
                    _slotQuantities[i].text = "";
                }
            }
        }

        private void UpdateHeaderInfo()
        {
            // Name (class display name → fallback to playerKey or "Hero")
            if (_hdrNameText != null)
            {
                string name = _playerDef != null && !string.IsNullOrEmpty(_playerDef.displayName)
                    ? _playerDef.displayName
                    : (PlayerSelectionState.SelectedPlayerKey ?? "Hero");
                if (!string.IsNullOrEmpty(name))
                    name = char.ToUpperInvariant(name[0]) + name.Substring(1);
                _hdrNameText.text = name;
            }

            // Level + xp%
            if (_hdrLevelText != null)
            {
                int lvl = _playerXp != null ? Mathf.Max(1, _playerXp.Level) : 1;
                int pct = _playerXp != null ? Mathf.RoundToInt(_playerXp.NormalizedProgress * 100f) : 0;
                _hdrLevelText.text = $"Lvl {lvl} ({pct}%)";
            }

            // Portrait + body avatar — mirror the player sprite (same as Python)
            Sprite sp = _playerSprite != null ? _playerSprite.sprite : null;
            if (_portraitImg != null)
            {
                _portraitImg.sprite  = sp;
                _portraitImg.enabled = sp != null;
            }
        }

        private void UpdateGold()
        {
            if (_goldText == null) return;

            int total = 0;
            if (_playerWallet != null) total += _playerWallet.Coins;

            // Also sum currency item-id stacks in the inventory (Python parity).
            if (_playerInventory != null)
            {
                var slots = _playerInventory.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].IsEmpty) continue;
                    string id = slots[i].Item.itemId;
                    for (int k = 0; k < CURRENCY_ITEM_IDS.Length; k++)
                    {
                        if (string.Equals(id, CURRENCY_ITEM_IDS[k], System.StringComparison.OrdinalIgnoreCase))
                        {
                            total += slots[i].Quantity;
                            break;
                        }
                    }
                }
            }
            _goldText.text = total.ToString();
        }

        private void UpdateSlotHighlights()
        {
            if (_slotBackgrounds == null) return;
            for (int i = 0; i < _slotBackgrounds.Length; i++)
                _slotBackgrounds[i].color = (i == _selectedSlot) ? SLOT_SELECTED : SLOT_BG;
        }

        private void UpdateTooltip()
        {
            if (_tooltipText == null) return;

            if (_playerInventory != null && _selectedSlot >= 0 &&
                _selectedSlot < _playerInventory.Slots.Count)
            {
                var slot = _playerInventory.Slots[_selectedSlot];
                if (!slot.IsEmpty)
                {
                    string desc = !string.IsNullOrEmpty(slot.Item.description)
                        ? slot.Item.description
                        : "Sin descripcion";
                    _tooltipText.text  = $"<b>{slot.Item.displayName}</b> x{slot.Quantity}\n{desc}";
                    _tooltipText.color = TEXT_PRIMARY;
                    return;
                }
            }

            _tooltipText.text  = "Tab/I close  |  Q drop  |  double-click use  |  drag to move";
            _tooltipText.color = TEXT_MUTED;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Slot interactions (called from InventorySlotDragHandler)
        // ─────────────────────────────────────────────────────────────────────

        public void UseSlot(int slotIndex)
        {
            if (_playerInventory == null) return;
            if (slotIndex < 0 || slotIndex >= _playerInventory.Slots.Count) return;
            var slot = _playerInventory.Slots[slotIndex];
            if (slot.IsEmpty) return;

            if (slot.Item.GetCategory() == ItemCategory.Consumable && _playerConsumer != null)
            {
                _playerConsumer.TryConsume(slot.Item);
                _selectedSlot = -1;
                UpdateSlotHighlights();
                UpdateTooltip();
            }
            else
            {
                // Non-consumable: just keep selection.
                SelectSlot(slotIndex);
            }
        }

        public void BeginSlotDrag(int srcIndex, PointerEventData ev)
        {
            if (_playerInventory == null) return;
            var src = _playerInventory.GetSlotByIndex(srcIndex);
            if (src.IsEmpty) return;

            _dragSourceIndex = srcIndex;
            CreateDragGhost(src.Item);
            UpdateSlotDrag(ev);
        }

        public void UpdateSlotDrag(PointerEventData ev)
        {
            if (_dragGhostRt == null) return;
            _dragGhostRt.position = ev.position;
        }

        // Drag end-routing across the unified slot space:
        //   • src ↔ dst within the bag           → existing merge / swap.
        //   • bag ↔ equipment, or eq ↔ eq        → MoveSlotByIndex (swap or
        //     stack-merge depending on item compatibility).
        //   • dst outside any slot but inside    → no-op (cancels the drag).
        //     the panel
        //   • dst outside the panel altogether   → world drop at cursor.
        public void EndSlotDrag(int srcIndex, PointerEventData ev)
        {
            DestroyDragGhost();
            if (_dragSourceIndex < 0) return;
            int src = _dragSourceIndex;
            _dragSourceIndex = -1;
            if (_playerInventory == null) return;

            int dst = HitTestSlot(ev);
            if (dst >= 0 && dst != src)
            {
                if (_playerInventory.IsEquipmentIndex(src) || _playerInventory.IsEquipmentIndex(dst))
                {
                    _playerInventory.MoveSlotByIndex(src, dst);
                }
                else if (!_playerInventory.TryMergeStacks(src, dst))
                {
                    _playerInventory.SwapSlots(src, dst);
                }
                SelectSlot(dst);
                return;
            }

            if (!IsPointerOverPanel(ev))
            {
                DropSlotToWorld(src, ResolveWorldDropPosition(ev));
            }
        }

        // Tests both grids (bag first, then equipment) and returns the unified
        // index — caller routes by Inventory.IsEquipmentIndex.
        private int HitTestSlot(PointerEventData ev)
        {
            if (_slotObjects != null)
            {
                for (int i = 0; i < _slotObjects.Length; i++)
                {
                    var rt = _slotObjects[i].GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, ev.position, ev.pressEventCamera))
                        return i;
                }
            }
            if (_equipObjects != null)
            {
                for (int i = 0; i < _equipObjects.Length; i++)
                {
                    var go = _equipObjects[i];
                    if (go == null) continue;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, ev.position, ev.pressEventCamera))
                        return Inventory.DefaultBagCapacity + i;
                }
            }
            return -1;
        }

        private bool IsPointerOverPanel(PointerEventData ev)
        {
            if (_panelRect == null) return true;
            return RectTransformUtility.RectangleContainsScreenPoint(_panelRect, ev.position, ev.pressEventCamera);
        }

        /// <summary>
        /// True when the inventory window is open AND the given screen-space point
        /// falls inside the panel. Used by world-drop drag systems to detect a
        /// drop-into-inventory gesture without going through PointerEventData.
        /// Canvas is ScreenSpaceOverlay so the camera arg is null.
        /// </summary>
        public bool IsScreenPointOverPanel(Vector2 screenPos)
        {
            if (!_visible || _panelRect == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_panelRect, screenPos, null);
        }

        /// <summary>
        /// Returns the index of the slot whose rect contains <paramref name="screenPos"/>,
        /// or -1 if no slot is hit (or the panel isn't visible). Used by
        /// <c>WorldDropInteractor</c> to honour "deposit in the cell I want".
        /// </summary>
        public int HitTestSlotByScreenPos(Vector2 screenPos)
        {
            if (!_visible) return -1;
            if (_slotObjects != null)
            {
                for (int i = 0; i < _slotObjects.Length; i++)
                {
                    var go = _slotObjects[i];
                    if (go == null) continue;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                        return i;
                }
            }
            if (_equipObjects != null)
            {
                for (int i = 0; i < _equipObjects.Length; i++)
                {
                    var go = _equipObjects[i];
                    if (go == null) continue;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                        return Inventory.DefaultBagCapacity + i;
                }
            }
            return -1;
        }

        // Yellow border drawn on the slot that AddItem would deposit into,
        // refreshed every frame by WorldDropInteractor while a world drag is
        // active. Reuses each slot's existing Outline component to avoid extra
        // GameObjects — only color/distance get swapped.
        private int _depositTargetSlot = -1;
        private static readonly Color s_depositTargetColor    = new Color(1.00f, 0.86f, 0.20f, 1f);
        private static readonly Vector2 s_depositTargetOffset = new Vector2(3f, 3f);

        /// <summary>
        /// Tag a slot as the current deposit target so it stands out with a
        /// yellow border. Pass -1 to clear. Slot indices outside the grid are
        /// ignored. Idempotent and per-frame safe.
        /// </summary>
        public void SetDepositTargetSlot(int slotIndex)
        {
            if (slotIndex == _depositTargetSlot) return;

            ResetSlotOutline(_depositTargetSlot);
            _depositTargetSlot = slotIndex;
            ApplyDepositOutline(_depositTargetSlot);
        }

        private Outline GetOutlineByIndex(int unifiedIndex)
        {
            if (unifiedIndex < 0) return null;
            if (unifiedIndex < Inventory.DefaultBagCapacity)
                return (_slotOutlines != null && unifiedIndex < _slotOutlines.Length)
                    ? _slotOutlines[unifiedIndex] : null;
            int eq = unifiedIndex - Inventory.DefaultBagCapacity;
            return (_equipOutlines != null && eq >= 0 && eq < _equipOutlines.Length)
                ? _equipOutlines[eq] : null;
        }

        private void ResetSlotOutline(int unifiedIndex)
        {
            var ol = GetOutlineByIndex(unifiedIndex);
            if (ol == null) return;
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(1f, 1f);
        }

        private void ApplyDepositOutline(int unifiedIndex)
        {
            var ol = GetOutlineByIndex(unifiedIndex);
            if (ol == null) return;
            ol.effectColor    = s_depositTargetColor;
            ol.effectDistance = s_depositTargetOffset;
        }

        private void DropSlotToWorld(int srcIndex, Vector3? worldDropPos = null)
        {
            if (_playerInventory == null) return;
            if (srcIndex < 0 || srcIndex >= _playerInventory.Slots.Count) return;
            var slot = _playerInventory.Slots[srcIndex];
            if (slot.IsEmpty) return;

            var item = slot.Item;
            int qty  = slot.Quantity;
            int removed = _playerInventory.RemoveItem(item, qty);
            if (removed <= 0) return;

            var player = EntityRegistry.Player;
            if (player != null)
            {
                // Drag-from-inventory passes the (clamped) cursor world position;
                // the Q-key path passes null and falls back to a small random
                // offset around the player so the drop doesn't stack on the foot.
                Vector3 pos = worldDropPos
                              ?? player.transform.position
                                 + (Vector3)(Random.insideUnitCircle.normalized * 1.5f);
                DropSystem.SpawnDrop(item, removed, pos);
            }

            _selectedSlot = -1;
            UpdateSlotHighlights();
            UpdateTooltip();
        }

        // Convert the pointer release position to a clamped world-space drop
        // location. Uses the player's WorldDropInteractor to enforce the same
        // interaction range that bounds drag-from-ground, so the player can
        // always reach back to whatever they just placed.
        private Vector3 ResolveWorldDropPosition(PointerEventData ev)
        {
            var player = EntityRegistry.Player;
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

            var cam = ev.pressEventCamera != null ? ev.pressEventCamera : Camera.main;
            if (cam == null) return playerPos;

            Vector3 sp = new Vector3(ev.position.x, ev.position.y, -cam.transform.position.z);
            Vector3 worldCursor = cam.ScreenToWorldPoint(sp);
            worldCursor.z = 0f;

            if (player != null)
            {
                var interactor = player.GetComponent<WorldDropInteractor>();
                if (interactor != null) return interactor.ClampToReach(worldCursor);
            }
            return worldCursor;
        }

        private void CreateDragGhost(ItemDefinition item)
        {
            if (item == null || _canvas == null) return;
            DestroyDragGhost();

            _dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup));
            _dragGhost.transform.SetParent(_canvas.transform, false);
            _dragGhostRt = _dragGhost.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(SLOT_PX, SLOT_PX);

            var cg = _dragGhost.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.alpha = 0.85f;

            _dragGhostImg = _dragGhost.AddComponent<Image>();
            _dragGhostImg.sprite         = item.icon ?? item.iconSmall;
            _dragGhostImg.preserveAspect = true;
            _dragGhostImg.raycastTarget  = false;
        }

        private void DestroyDragGhost()
        {
            if (_dragGhost != null) Destroy(_dragGhost);
            _dragGhost    = null;
            _dragGhostImg = null;
            _dragGhostRt  = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Drop selected (Q key)
        // ─────────────────────────────────────────────────────────────────────

        private void DropSelectedItem()
        {
            DropSlotToWorld(_selectedSlot);
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private void OnDisable()
        {
            _toggleAction?.Disable();
            _dropAction?.Disable();
        }

        protected override void OnDestroy()
        {
            UnsubscribePlayer();
            DestroyDragGhost();
            _toggleAction?.Dispose();
            _dropAction?.Dispose();
            base.OnDestroy();
        }
    }
}
