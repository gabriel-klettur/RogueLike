using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Inventory
{
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

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
