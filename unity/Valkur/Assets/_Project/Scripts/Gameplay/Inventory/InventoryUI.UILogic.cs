using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.UIKit;
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
        private const float CHAR_BLOCK_H = SLOT_PX * 3 + SLOT_GAP * 2;   // matches 3×3 equipment grid
        private const float TABS_H       = 28f;
        private const float TITLE_H      = 22f;
        private const float TOOLTIP_H    = 38f;
        private const float FOOTER_H     = 32f;
        private const float PANEL_TOP_MARGIN   = 16f;
        private const float PANEL_RIGHT_MARGIN = 16f;

        private static readonly string[] TAB_LABELS =
            { "Equipo", "Materiales", "Consumibles", "Otros", "Quest" };

        private static readonly string[] CURRENCY_ITEM_IDS =
            { "gold", "coins", "coin", "gold_coin" };

        // ── Phase-2 visual refs ──
        private TextMeshProUGUI _hdrNameText;
        private TextMeshProUGUI _hdrLevelText;
        private Image           _portraitImg;
        private Image[]         _equipBgs;     // 9 slots (3×3)
        private Image[]         _equipIcons;
        private Image           _characterPreviewImg;
        private Image[]         _tabBgs;
        private TextMeshProUGUI[] _tabTexts;
        private TextMeshProUGUI _goldText;
        private int             _activeTabIndex = 0;

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
                              + CHAR_BLOCK_H + 8f
                              + TITLE_H + 4f
                              + TABS_H + 6f
                              + 1f + 6f
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
            BuildEquipmentAndCharacter(contentGo.transform, ref y);     y += 8f;

            // "Inventory" title
            var titleGo = CreateUIObject("InventoryTitle", contentGo.transform);
            PlaceTopAnchored(titleGo, y, 0f, TITLE_H);
            _titleText           = titleGo.AddComponent<TextMeshProUGUI>();
            _titleText.text      = "Inventory";
            _titleText.fontSize  = 14f;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.alignment = TextAlignmentOptions.Center;
            _titleText.color     = TileEditorTheme.HeaderTitle;
            y += TITLE_H + 4f;

            BuildTabsRow(contentGo.transform, ref y); y += 6f;

            var tabSepGo = CreateUIObject("TabsSep", contentGo.transform);
            PlaceTopAnchored(tabSepGo, y, 0f, 1f);
            tabSepGo.AddComponent<Image>().color = TileEditorTheme.Separator;
            y += 1f + 6f;

            _slotObjects     = new GameObject[totalSlots];
            _slotBackgrounds = new Image[totalSlots];
            _slotIcons       = new Image[totalSlots];
            _slotQuantities  = new TextMeshProUGUI[totalSlots];
            BuildMainGrid(contentGo.transform, ref y, totalSlots); y += 6f;

            // Tooltip
            var tooltipGo = CreateUIObject("Tooltip", contentGo.transform);
            PlaceTopAnchored(tooltipGo, y, 0f, TOOLTIP_H);
            _tooltipText           = tooltipGo.AddComponent<TextMeshProUGUI>();
            _tooltipText.text      = "Tab/I cerrar  |  Q soltar  |  doble-click usar  |  arrastrar mover";
            _tooltipText.fontSize  = 11f;
            _tooltipText.alignment = TextAlignmentOptions.Center;
            _tooltipText.color     = TEXT_MUTED;
            _tooltipText.enableWordWrapping = true;
            y += TOOLTIP_H + 6f;

            BuildGoldFooter(contentGo.transform, y);

            UpdateTabHighlights();
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
            var tray = Valkur.Gameplay.UIKit.MinimizedHUDTray.Instance;
            if (tray == null) return;
            var sprite = LoadHUDSprite("Assets/_Project/Art/UI/hud/inventory_hud_button.png");
            tray.Register("inventory", sprite, () => SetVisible(!_visible));
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

        private void BuildEquipmentAndCharacter(Transform parent, ref float y)
        {
            const int EQ_COLS = 3;
            const int EQ_ROWS = 3;
            float eqW = EQ_COLS * SLOT_PX + (EQ_COLS - 1) * SLOT_GAP;
            float eqH = EQ_ROWS * SLOT_PX + (EQ_ROWS - 1) * SLOT_GAP;
            float blockH = Mathf.Max(eqH, CHAR_BLOCK_H);

            var rowGo = CreateUIObject("EquipCharRow", parent);
            PlaceTopAnchored(rowGo, y, 0f, blockH);

            _equipBgs   = new Image[EquipmentView.SLOT_COUNT];
            _equipIcons = new Image[EquipmentView.SLOT_COUNT];
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

                var bg = slotGo.AddComponent<Image>();
                bg.color = SLOT_BG;
                var ol = slotGo.AddComponent<Outline>();
                ol.effectColor    = TileEditorTheme.Border;
                ol.effectDistance = new Vector2(1f, 1f);
                _equipBgs[i] = bg;

                var iconGo = CreateUIObject("Icon", slotGo.transform);
                var irt = iconGo.GetComponent<RectTransform>();
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = new Vector2(4f, 4f);
                irt.offsetMax = new Vector2(-4f, -4f);
                var img = iconGo.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget  = false;
                img.enabled        = false;
                _equipIcons[i] = img;
            }

            // Character preview
            var charGo = CreateUIObject("CharacterPreview", rowGo.transform);
            var crt = charGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot     = new Vector2(0f, 0.5f);
            crt.offsetMin = new Vector2(eqW + 12f, 0f);
            crt.offsetMax = new Vector2(0f, 0f);
            charGo.AddComponent<Image>().color = BG_SURFACE;
            var charOl = charGo.AddComponent<Outline>();
            charOl.effectColor    = TileEditorTheme.Border;
            charOl.effectDistance = new Vector2(1f, 1f);

            var bodyGo = CreateUIObject("Body", charGo.transform);
            var brt = bodyGo.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(6f, 6f);
            brt.offsetMax = new Vector2(-6f, -6f);
            _characterPreviewImg = bodyGo.AddComponent<Image>();
            _characterPreviewImg.preserveAspect = true;
            _characterPreviewImg.raycastTarget  = false;
            _characterPreviewImg.enabled        = false;

            y += blockH;
        }

        private void BuildTabsRow(Transform parent, ref float y)
        {
            var rowGo = CreateUIObject("TabsRow", parent);
            PlaceTopAnchored(rowGo, y, 0f, TABS_H);

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            int n = TAB_LABELS.Length;
            _tabBgs   = new Image[n];
            _tabTexts = new TextMeshProUGUI[n];

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var btnGo = CreateUIObject($"Tab_{TAB_LABELS[i]}", rowGo.transform);

                var img = btnGo.AddComponent<Image>();
                img.color = BTN_NORMAL;
                _tabBgs[i] = img;

                var btn = btnGo.AddComponent<Button>();
                var c = btn.colors;
                c.normalColor      = BTN_NORMAL;
                c.highlightedColor = BTN_HOVER;
                c.pressedColor     = BTN_ACTIVE;
                c.selectedColor    = BTN_NORMAL;
                btn.colors = c;
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActiveTab(idx));

                _tabTexts[i] = AddCenteredText(btnGo.transform, TAB_LABELS[i],
                    11f, FontStyles.Bold, TEXT_SECONDARY);
            }

            y += TABS_H;
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
        //  Tab handling
        // ─────────────────────────────────────────────────────────────────────

        private void SetActiveTab(int index)
        {
            if (_tabBgs == null || index < 0 || index >= _tabBgs.Length) return;
            _activeTabIndex = index;
            UpdateTabHighlights();
            RefreshSlots();
        }

        private void UpdateTabHighlights()
        {
            if (_tabBgs == null) return;
            for (int i = 0; i < _tabBgs.Length; i++)
            {
                bool active = i == _activeTabIndex;
                _tabBgs[i].color   = active ? ACCENT_BG : BTN_NORMAL;
                _tabTexts[i].color = active ? ACCENT    : TEXT_SECONDARY;
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

        private void RefreshSlots()
        {
            if (_slotObjects == null) return;

            int slotCount = _slotObjects.Length;
            int playerSlotCount = (_playerInventory != null) ? _playerInventory.Slots.Count : 0;
            var slots = _playerInventory != null ? _playerInventory.Slots : null;

            for (int i = 0; i < slotCount; i++)
            {
                bool show = false;
                if (slots != null && i < playerSlotCount && !slots[i].IsEmpty)
                {
                    var slot = slots[i];
                    if (slot.Item.MatchesTab(_activeTabIndex))
                    {
                        _slotIcons[i].enabled  = true;
                        _slotIcons[i].sprite   = slot.Item.icon ?? slot.Item.iconSmall;
                        _slotQuantities[i].text = slot.Quantity > 1 ? slot.Quantity.ToString() : "";
                        show = true;
                    }
                }
                if (!show)
                {
                    _slotIcons[i].enabled  = false;
                    _slotQuantities[i].text = "";
                }
            }

            if (_titleText != null && _playerInventory != null)
                _titleText.text = $"Inventory ({_playerInventory.UsedSlots}/{_playerInventory.Capacity})";
        }

        private void UpdateEquipmentView()
        {
            EquipmentView.Resolve(_playerInventory, _equipResolved);
            for (int i = 0; i < EquipmentView.SLOT_COUNT && i < _equipIcons.Length; i++)
            {
                var item = _equipResolved[i];
                if (item != null)
                {
                    _equipIcons[i].enabled = true;
                    _equipIcons[i].sprite  = item.icon ?? item.iconSmall;
                }
                else
                {
                    _equipIcons[i].enabled = false;
                    _equipIcons[i].sprite  = null;
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
            if (_characterPreviewImg != null)
            {
                _characterPreviewImg.sprite  = sp;
                _characterPreviewImg.enabled = sp != null;
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

            _tooltipText.text  = "Tab/I cerrar  |  Q soltar  |  doble-click usar  |  arrastrar mover";
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
            if (srcIndex < 0 || srcIndex >= _playerInventory.Slots.Count) return;
            var src = _playerInventory.Slots[srcIndex];
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

        public void EndSlotDrag(int srcIndex, PointerEventData ev)
        {
            DestroyDragGhost();
            if (_dragSourceIndex < 0) return;
            int src = _dragSourceIndex;
            _dragSourceIndex = -1;

            // 1) Dropped on another slot inside the panel?
            int dst = HitTestSlot(ev);
            if (dst >= 0 && dst != src)
            {
                if (!_playerInventory.TryMergeStacks(src, dst))
                    _playerInventory.SwapSlots(src, dst);
                SelectSlot(dst);
                return;
            }

            // 2) Dropped outside the panel → world drop.
            if (!IsPointerOverPanel(ev))
            {
                DropSlotToWorld(src);
            }
        }

        private int HitTestSlot(PointerEventData ev)
        {
            if (_slotObjects == null) return -1;
            for (int i = 0; i < _slotObjects.Length; i++)
            {
                var rt = _slotObjects[i].GetComponent<RectTransform>();
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, ev.position, ev.pressEventCamera))
                    return i;
            }
            return -1;
        }

        private bool IsPointerOverPanel(PointerEventData ev)
        {
            if (_panelRect == null) return true;
            return RectTransformUtility.RectangleContainsScreenPoint(_panelRect, ev.position, ev.pressEventCamera);
        }

        private void DropSlotToWorld(int srcIndex)
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
                Vector3 pos = player.transform.position +
                              (Vector3)(Random.insideUnitCircle.normalized * 1.5f);
                DropSystem.SpawnDrop(item, removed, pos);
            }

            _selectedSlot = -1;
            UpdateSlotHighlights();
            UpdateTooltip();
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
