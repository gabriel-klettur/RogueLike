using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Builds all UI panels for the Inventory Runtime Editor (F6) using the
    /// professional menu-bar + floating-panel architecture shared with the
    /// Tile (F8), Buildings (F10) and Items (F7) editors.
    ///
    /// Layout (mirrors Python inventory_editor sub-panels):
    ///   • 30 px menu bar — brand + Modes / Entities / Slots / Catalog dropdowns + ? + PERF
    ///   • Modes panel       (96 px,  top-left)     — View / Add / Del + Default/Active side + Save + Undo/Redo
    ///   • Entities panel    (280 px, next)         — Category tabs (Player/Monsters/Map) + search + scrollable list
    ///   • Slots panel       (360 px, top-right)    — Owner header + 5-col inventory grid
    ///   • Catalog panel     (320 px, bottom-right) — Default/Ground tabs + search + grid + qty stepper + Add
    ///   • Tutorial overlay  (top-right docked)
    /// </summary>
    public static class InventoryEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            ModesMenuBtnImg;     public TextMeshProUGUI ModesMenuBtnTmp;
            public Image            EntitiesMenuBtnImg;  public TextMeshProUGUI EntitiesMenuBtnTmp;
            public Image            SlotsMenuBtnImg;     public TextMeshProUGUI SlotsMenuBtnTmp;
            public Image            CatalogMenuBtnImg;   public TextMeshProUGUI CatalogMenuBtnTmp;
            public Image            PerfProbeMenuBtnImg; public TextMeshProUGUI PerfProbeMenuBtnTmp;

            // Panel roots + drag components
            public GameObject       ModesDropdown;     public DraggablePanel ModesPanelDrag;
            public GameObject       EntitiesDropdown;  public DraggablePanel EntitiesPanelDrag;
            public GameObject       SlotsDropdown;     public DraggablePanel SlotsPanelDrag;
            public GameObject       CatalogDropdown;   public DraggablePanel CatalogPanelDrag;

            // Modes panel
            public Image            ViewBtnImg;
            public Image            AddItemBtnImg;
            public Image            DeleteItemBtnImg;
            public Image            SideDefaultImg;
            public Image            SideActiveImg;

            // Entities panel
            public Image            PlayerTabImg;
            public Image            MonstersTabImg;
            public Image            MapTabImg;
            public TMP_InputField   EntitySearchBox;
            public RectTransform    EntityListContent;

            // Slots panel
            public TextMeshProUGUI  OwnerText;
            public RectTransform    SlotGridContent;
            public TextMeshProUGUI  StatusText;

            // Catalog panel
            public Image            CatDefaultImg;
            public Image            CatGroundImg;
            public TMP_InputField   CatalogSearchBox;
            public RectTransform    CatalogGridContent;
            public TMP_InputField   QtyInput;
        }

        // ── Panel sizes ───────────────────────────────────────────────────────────

        private const float MODES_W     = 96f;                       // wide enough for "Default" / "Active" labels
        private const float MODES_H     = 380f + PANEL_HDR_H;
        private const float ENTITIES_W  = 280f;
        private const float ENTITIES_H  = TILES_DROP_H;              // 564
        private const float SLOTS_W     = 360f;
        private const float SLOTS_H     = 460f + PANEL_HDR_H;
        private const float CATALOG_W   = 320f;
        private const float CATALOG_H   = 380f + PANEL_HDR_H;

        // Slot grid sizing (5 columns, like Python)
        private const int   SLOT_COLS   = 5;
        private const float SLOT_CELL   = 56f;
        private const float SLOT_SPACE  = 4f;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W    = 150f;
        private const float MODES_BTN_W    = 70f;
        private const float ENTITIES_BTN_W = 84f;
        private const float SLOTS_BTN_W    = 64f;
        private const float CATALOG_BTN_W  = 78f;
        private const float TUTORIAL_BTN_W = 40f;
        private const float PERF_BTN_W     = 46f;

        private const float BTN_H = 30f;

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,            Action onRedo,
            Action         onSave,
            Action         onShowDefault,     Action onShowActive,
            Action         onModeView,        Action onModeAddItem,    Action onModeDeleteItem,
            Action         onCatPlayer,       Action onCatMonsters,    Action onCatMap,
            Action<string> onEntitySearch,
            Action<string> onCatalogSearch,
            Action         onCatalogTabDefault, Action onCatalogTabGround,
            Action         onQtyMinus,        Action onQtyPlus,
            Action         onAddToInventory,
            Action         onToggleTutorial,
            Action         onPerfToggle = null)
        {
            // Reserve menu bar space so panels never occlude it.
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial, onPerfToggle);
            BuildModesPanel(canvasT, ref refs,
                onModeView, onModeAddItem, onModeDeleteItem,
                onShowDefault, onShowActive, onSave, onUndo, onRedo);
            BuildEntitiesPanel(canvasT, ref refs,
                onCatPlayer, onCatMonsters, onCatMap, onEntitySearch);
            BuildSlotsPanel(canvasT, ref refs);
            BuildCatalogPanel(canvasT, ref refs,
                onCatalogTabDefault, onCatalogTabGround,
                onCatalogSearch, onQtyMinus, onQtyPlus, onAddToInventory);
            return refs;
        }

        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT          : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial, Action onPerfToggle)
        {
            var go = CreateUI("InventoryMenuBar", canvasT);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0f, 1f);
            r.anchorMax        = new Vector2(1f, 1f);
            r.pivot            = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta        = new Vector2(0f, MENUBAR_HEIGHT);
            refs.MenuBar       = go;

            var bg = go.AddComponent<Image>();
            bg.color = MENUBAR_BG;
            bg.raycastTarget = true;

            var ol = go.AddComponent<Outline>();
            ol.effectColor    = BORDER;
            ol.effectDistance = new Vector2(0f, -1f);

            var chrome           = go.AddComponent<MenuBarChrome>();
            chrome.BgImage       = bg;
            chrome.BorderOutline = ol;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding                = new RectOffset((int)MENUBAR_PAD_H, (int)MENUBAR_PAD_H, 0, 0);
            hlg.spacing                = MENUBAR_SPACING;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var t = go.transform;

            // Brand
            var brand = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp              = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "INVENTORY EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ModesMenuBtnImg    = AddMenuBtn(t, "Modes v",    MODES_BTN_W,
                () => onToggle?.Invoke("modes"),    out refs.ModesMenuBtnTmp);
            refs.EntitiesMenuBtnImg = AddMenuBtn(t, "Entities v", ENTITIES_BTN_W,
                () => onToggle?.Invoke("entities"), out refs.EntitiesMenuBtnTmp);
            refs.SlotsMenuBtnImg    = AddMenuBtn(t, "Slots v",    SLOTS_BTN_W,
                () => onToggle?.Invoke("slots"),    out refs.SlotsMenuBtnTmp);
            refs.CatalogMenuBtnImg  = AddMenuBtn(t, "Catalog v",  CATALOG_BTN_W,
                () => onToggle?.Invoke("catalog"),  out refs.CatalogMenuBtnTmp);

            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
            AddMenuDivider(t);
            refs.PerfProbeMenuBtnImg = AddMenuBtn(t, "PERF", PERF_BTN_W,
                () => onPerfToggle?.Invoke(), out refs.PerfProbeMenuBtnTmp);
        }

        // ── Modes Panel (top-left, narrow) ────────────────────────────────────────

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onView, Action onAddItem, Action onDeleteItem,
            Action onShowDefault, Action onShowActive,
            Action onSave, Action onUndo, Action onRedo)
        {
            refs.ModesDropdown = MakeDrop("InventoryModesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "Modes", out var t, out refs.ModesPanelDrag);

            AddSectionLabel(t, "Mode");
            refs.ViewBtnImg       = AddTabBtn(t, "View",   BTN_H, onView);
            refs.AddItemBtnImg    = AddTabBtn(t, "Add",    BTN_H, onAddItem);
            refs.DeleteItemBtnImg = AddDangerTabBtn(t, "Del", BTN_H, onDeleteItem);

            AddInlineSeparator(t);
            AddSectionLabel(t, "Side");
            refs.SideDefaultImg = AddTabBtn(t, "Default", BTN_H, onShowDefault);
            refs.SideActiveImg  = AddTabBtn(t, "Active",  BTN_H, onShowActive);

            AddInlineSeparator(t);
            AddSectionLabel(t, "Actions");
            AddActionBtn(t, "Save", BTN_H, onSave);
            AddActionBtn(t, "Undo", 24f, onUndo);
            AddActionBtn(t, "Redo", 24f, onRedo);

            refs.ModesDropdown.SetActive(false);
        }

        // ── Entities Panel (next, 280 px) ────────────────────────────────────────

        private static void BuildEntitiesPanel(Transform canvasT, ref UIRefs refs,
            Action onCatPlayer, Action onCatMonsters, Action onCatMap,
            Action<string> onSearch)
        {
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.EntitiesDropdown = MakeDrop("InventoryEntitiesPanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET,
                ENTITIES_W, ENTITIES_H, "Entities", out var t, out refs.EntitiesPanelDrag);

            AddSectionLabel(t, "Category");
            var tabRow = CreateUI("CategoryRow", t);
            tabRow.AddComponent<LayoutElement>().preferredHeight = BTN_H;
            var tabHlg                       = tabRow.AddComponent<HorizontalLayoutGroup>();
            tabHlg.spacing                   = 4f;
            tabHlg.childForceExpandWidth     = true;
            tabHlg.childForceExpandHeight    = true;
            tabHlg.childControlWidth         = true;
            tabHlg.childControlHeight        = true;

            refs.PlayerTabImg   = AddInlineTabBtn(tabRow.transform, "Player",   onCatPlayer);
            refs.MonstersTabImg = AddInlineTabBtn(tabRow.transform, "Monsters", onCatMonsters);
            refs.MapTabImg      = AddInlineTabBtn(tabRow.transform, "Map",      onCatMap);

            AddInlineSeparator(t);

            refs.EntitySearchBox = SearchBox.Create(t, "Search entities\u2026",
                v => onSearch?.Invoke(v ?? ""));

            var (entScroll, entContent) = EditorUIHelpers.MakeScrollView(t, "EntityList");
            var entLE = entScroll.gameObject.AddComponent<LayoutElement>();
            entLE.flexibleHeight = 1f;
            entLE.minHeight      = 220f;
            EditorUIHelpers.AddVerticalScrollbar(entScroll);
            refs.EntityListContent = entContent;

            refs.EntitiesDropdown.SetActive(false);
        }

        // ── Slots Panel (top-right, 360 px) ──────────────────────────────────────

        private static void BuildSlotsPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.SlotsDropdown = MakeDrop("InventorySlotsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                SLOTS_W, SLOTS_H, "Slots", out var t, out refs.SlotsPanelDrag);

            // Owner header (rich-text)
            var ownerGo = CreateUI("OwnerHdr", t);
            ownerGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            refs.OwnerText                  = ownerGo.AddComponent<TextMeshProUGUI>();
            refs.OwnerText.text             = "(no entity selected)";
            refs.OwnerText.fontSize         = 11f;
            refs.OwnerText.fontStyle        = FontStyles.Normal;
            refs.OwnerText.alignment        = TextAlignmentOptions.MidlineLeft;
            refs.OwnerText.color            = TEXT_SECONDARY;
            refs.OwnerText.richText         = true;
            refs.OwnerText.enableWordWrapping = false;
            refs.OwnerText.overflowMode     = TextOverflowModes.Ellipsis;

            BuildSeparator(t);
            AddSectionLabel(t, "Inventory Grid");

            var (gridScroll, gridContent) = EditorUIHelpers.MakeGridPicker(
                t, "InvGrid", SLOT_COLS, SLOT_CELL, SLOT_SPACE);
            var gridLE = gridScroll.gameObject.AddComponent<LayoutElement>();
            gridLE.flexibleHeight = 1f;
            gridLE.minHeight      = 200f;
            EditorUIHelpers.AddVerticalScrollbar(gridScroll);
            refs.SlotGridContent = gridContent;

            BuildSeparator(t);
            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            refs.SlotsDropdown.SetActive(false);
        }

        // ── Catalog Panel (bottom-right, 320 px) ─────────────────────────────────

        private static void BuildCatalogPanel(Transform canvasT, ref UIRefs refs,
            Action onTabDefault, Action onTabGround,
            Action<string> onSearch,
            Action onQtyMinus, Action onQtyPlus,
            Action onAddToInventory)
        {
            refs.CatalogDropdown = MakeDrop("InventoryCatalogPanel", canvasT,
                PanelDock.BottomRight, PANEL_GAP, PANEL_GAP,
                CATALOG_W, CATALOG_H, "Catalog", out var t, out refs.CatalogPanelDrag);

            // Source tabs (Default / Ground)
            var tabRow = CreateUI("CatalogTabRow", t);
            tabRow.AddComponent<LayoutElement>().preferredHeight = BTN_H;
            var hlg                       = tabRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                   = 4f;
            hlg.childForceExpandWidth     = true;
            hlg.childForceExpandHeight    = true;
            hlg.childControlWidth         = true;
            hlg.childControlHeight        = true;
            refs.CatDefaultImg = AddInlineTabBtn(tabRow.transform, "Default", onTabDefault);
            refs.CatGroundImg  = AddInlineTabBtn(tabRow.transform, "Ground",  onTabGround);

            refs.CatalogSearchBox = SearchBox.Create(t, "Search items\u2026",
                v => onSearch?.Invoke(v ?? ""));

            var (catScroll, catContent) = EditorUIHelpers.MakeGridPicker(
                t, "CatalogGrid", 4, 56f, 4f);
            var catLE = catScroll.gameObject.AddComponent<LayoutElement>();
            catLE.flexibleHeight = 1f;
            catLE.minHeight      = 140f;
            EditorUIHelpers.AddVerticalScrollbar(catScroll);
            refs.CatalogGridContent = catContent;

            BuildSeparator(t);

            // Quantity stepper + Add to Inventory action
            var qtyRow = CreateUI("QtyRow", t);
            qtyRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var qHlg                      = qtyRow.AddComponent<HorizontalLayoutGroup>();
            qHlg.spacing                  = 4f;
            qHlg.childForceExpandWidth    = false;
            qHlg.childForceExpandHeight   = true;
            qHlg.childControlWidth        = true;
            qHlg.childControlHeight       = true;
            qHlg.childAlignment           = TextAnchor.MiddleLeft;

            var minusBtnGo = CreateUI("Minus", qtyRow.transform);
            minusBtnGo.AddComponent<LayoutElement>().preferredWidth = 30f;
            AddSimpleBtn(minusBtnGo, "-", onQtyMinus);

            var qtyGo = CreateUI("QtyInput", qtyRow.transform);
            qtyGo.AddComponent<LayoutElement>().preferredWidth = 60f;
            refs.QtyInput = MakeNumericInput(qtyGo);

            var plusBtnGo = CreateUI("Plus", qtyRow.transform);
            plusBtnGo.AddComponent<LayoutElement>().preferredWidth = 30f;
            AddSimpleBtn(plusBtnGo, "+", onQtyPlus);

            // Spacer
            CreateUI("QtySpacer", qtyRow.transform).AddComponent<LayoutElement>().flexibleWidth = 1f;

            var addBtnGo = CreateUI("AddBtn", qtyRow.transform);
            addBtnGo.AddComponent<LayoutElement>().preferredWidth = 140f;
            AddSimpleBtn(addBtnGo, "Add to Inventory", onAddToInventory);

            refs.CatalogDropdown.SetActive(false);
        }

        // ── Floating-panel chrome (mirrors ItemsEditorUIBuilder.MakeDrop) ────────

        private static GameObject MakeDrop(
            string name, Transform canvasT,
            PanelDock dock, float xOff, float yOff, float width, float height,
            string title, out Transform contentOut, out DraggablePanel dragOut)
        {
            var go = CreateUI(name, canvasT);
            var r  = go.GetComponent<RectTransform>();
            ApplyDock(r, dock, xOff, yOff, width, height);

            var img            = go.AddComponent<Image>();
            img.color          = TileEditorTheme.PanelBg;
            var ol             = go.AddComponent<Outline>();
            ol.effectColor     = TileEditorTheme.Border;
            ol.effectDistance  = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // Header
            var hdrGo                = CreateUI("PanelHeader", go.transform);
            var hdrRt                = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin          = new Vector2(0f, 1f);
            hdrRt.anchorMax          = new Vector2(1f, 1f);
            hdrRt.pivot              = new Vector2(0f, 1f);
            hdrRt.anchoredPosition   = Vector2.zero;
            hdrRt.sizeDelta          = new Vector2(0f, PANEL_HDR_H);

            var hdrImg               = hdrGo.AddComponent<Image>();
            hdrImg.color             = TileEditorTheme.HeaderBg;
            hdrImg.raycastTarget     = true;

            var hdrHlg                       = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.spacing                   = 0f;
            hdrHlg.childForceExpandWidth     = false;
            hdrHlg.childForceExpandHeight    = true;
            hdrHlg.childControlWidth         = true;
            hdrHlg.childControlHeight        = true;
            hdrHlg.childAlignment            = TextAnchor.MiddleLeft;
            hdrHlg.padding                   = new RectOffset(8, 8, 0, 0);

            var titleGo                       = CreateUI("Title", hdrGo.transform);
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var titleTmp                      = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text                     = title.ToUpper();
            titleTmp.fontSize                 = 10f;
            titleTmp.fontStyle                = FontStyles.Bold;
            titleTmp.color                    = TileEditorTheme.HeaderTitle;
            titleTmp.characterSpacing         = 1.5f;
            titleTmp.alignment                = TextAlignmentOptions.Left;
            titleTmp.enableWordWrapping       = false;
            titleTmp.overflowMode             = TextOverflowModes.Truncate;
            titleTmp.raycastTarget            = false;

            // Header/content separator
            var sepGo                = CreateUI("HdrSep", go.transform);
            var sepRt                = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin          = new Vector2(0f, 1f);
            sepRt.anchorMax          = new Vector2(1f, 1f);
            sepRt.pivot              = new Vector2(0f, 1f);
            sepRt.anchoredPosition   = new Vector2(0f, -PANEL_HDR_H);
            sepRt.sizeDelta          = new Vector2(0f, 1f);
            var sepImg               = sepGo.AddComponent<Image>();
            sepImg.color             = TileEditorTheme.Separator;

            // Content area
            var contentGo            = CreateUI("Content", go.transform);
            var contentRt            = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin      = new Vector2(0f, 0f);
            contentRt.anchorMax      = new Vector2(1f, 1f);
            contentRt.offsetMin      = new Vector2(0f, 0f);
            contentRt.offsetMax      = new Vector2(0f, -(PANEL_HDR_H + 1f));

            var layout                       = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding                   = new RectOffset(8, 8, 6, 6);
            layout.spacing                   = 4f;
            layout.childForceExpandWidth     = true;
            layout.childForceExpandHeight    = false;
            layout.childControlWidth         = true;
            layout.childControlHeight        = true;
            contentGo.AddComponent<CanvasGroup>();

            var drag         = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;
            go.AddComponent<CanvasGroup>();

            var chrome             = go.AddComponent<PanelChrome>();
            chrome.PanelBgImage    = img;
            chrome.PanelOutline    = ol;
            chrome.HeaderBgImage   = hdrImg;
            chrome.HeaderSeparator = sepImg;
            chrome.HeaderTitle     = titleTmp;

            contentOut = contentGo.transform;
            dragOut    = drag;
            return go;
        }

        private static void ApplyDock(RectTransform r, PanelDock dock,
            float xOff, float yOff, float width, float height)
        {
            switch (dock)
            {
                case PanelDock.TopLeft:
                    r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f);
                    r.pivot     = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
                case PanelDock.TopRight:
                    r.anchorMin = new Vector2(1f, 1f); r.anchorMax = new Vector2(1f, 1f);
                    r.pivot     = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-xOff, -yOff);
                    break;
                case PanelDock.BottomLeft:
                    r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 0f);
                    r.pivot     = new Vector2(0f, 0f);
                    r.anchoredPosition = new Vector2(xOff, yOff);
                    break;
                case PanelDock.BottomRight:
                    r.anchorMin = new Vector2(1f, 0f); r.anchorMax = new Vector2(1f, 0f);
                    r.pivot     = new Vector2(1f, 0f);
                    r.anchoredPosition = new Vector2(-xOff, yOff);
                    break;
            }
            r.sizeDelta = new Vector2(width, height);
        }

        // ── Local widget helpers ──────────────────────────────────────────────────

        private static void AddMenuDivider(Transform parent)
        {
            var go = CreateUI("Div", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 1f;
            go.AddComponent<Image>().color = BORDER;
        }

        private static Image AddMenuBtn(Transform parent, string label, float width,
            UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI tmp)
        {
            var go = CreateUI($"MenuBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;

            var img   = go.AddComponent<Image>();
            img.color = MENU_BTN_NORMAL;

            var btn   = go.AddComponent<Button>();
            var c     = btn.colors;
            c.normalColor      = MENU_BTN_NORMAL;
            c.highlightedColor = MENU_BTN_HOVER;
            c.pressedColor     = MENU_BTN_OPEN;
            c.selectedColor    = MENU_BTN_NORMAL;
            c.fadeDuration     = 0.08f;
            btn.colors        = c;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            tmp           = AddCenteredText(go.transform, label, 11f, FontStyles.Normal, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        private static Image AddTabBtn(Transform parent, string label, float height, Action onClick)
        {
            var go = CreateUI($"Tab_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn   = go.AddComponent<Button>();
            var c     = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp        = AddCenteredText(go.transform, label, 10f, FontStyles.Bold, TEXT_PRIMARY);
            tmp.alignment  = TextAlignmentOptions.Center;
            return img;
        }

        private static Image AddDangerTabBtn(Transform parent, string label, float height, Action onClick)
        {
            var img        = AddTabBtn(parent, label, height, onClick);
            var dangerBase = new Color(0.55f, 0.15f, 0.15f, 1f);
            img.color      = dangerBase;
            var btn        = img.GetComponent<Button>();
            var c          = btn.colors;
            c.normalColor      = dangerBase;
            c.highlightedColor = new Color(0.70f, 0.20f, 0.20f, 1f);
            c.pressedColor     = new Color(0.90f, 0.30f, 0.30f, 1f);
            btn.colors         = c;
            return img;
        }

        private static Image AddInlineTabBtn(Transform parent, string label, Action onClick)
        {
            var go    = CreateUI($"Tab_{label}", parent);
            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn   = go.AddComponent<Button>();
            var c     = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp        = AddCenteredText(go.transform, label, 10f, FontStyles.Bold, TEXT_PRIMARY);
            tmp.alignment  = TextAlignmentOptions.Center;
            return img;
        }

        private static void AddActionBtn(Transform parent, string label, float height, Action onClick)
        {
            var go = CreateUI($"Act_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn   = go.AddComponent<Button>();
            var c     = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp        = AddCenteredText(go.transform, label, 9f, FontStyles.Bold, TEXT_SECONDARY);
            tmp.alignment  = TextAlignmentOptions.Center;
        }

        private static void AddSimpleBtn(GameObject go, string label, Action onClick)
        {
            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn   = go.AddComponent<Button>();
            var c     = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp        = AddCenteredText(go.transform, label, 11f, FontStyles.Bold, TEXT_PRIMARY);
            tmp.alignment  = TextAlignmentOptions.Center;
        }

        private static void AddSectionLabel(Transform parent, string text)
        {
            var go = CreateUI("SectionLabel_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
            var tmp                 = go.AddComponent<TextMeshProUGUI>();
            tmp.text                = text.ToUpper();
            tmp.fontSize            = 9f;
            tmp.fontStyle           = FontStyles.Bold;
            tmp.alignment           = TextAlignmentOptions.Left;
            tmp.color               = ACCENT;
            tmp.characterSpacing    = 2f;
        }

        private static void AddInlineSeparator(Transform parent)
        {
            var go = CreateUI("Sep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = SEPARATOR;
        }

        private static TMP_InputField MakeNumericInput(GameObject host)
        {
            var bg   = host.AddComponent<Image>();
            bg.color = BG_SURFACE;

            var textArea = CreateUI("TextArea", host.transform);
            EditorUIHelpers.StretchFill(textArea);

            var phGo  = CreateUI("Placeholder", textArea.transform);
            EditorUIHelpers.StretchFill(phGo);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text       = "1";
            phTmp.fontSize   = 12f;
            phTmp.fontStyle  = FontStyles.Italic;
            phTmp.color      = TEXT_MUTED;
            phTmp.alignment  = TextAlignmentOptions.Center;

            var txtGo  = CreateUI("Text", textArea.transform);
            EditorUIHelpers.StretchFill(txtGo);
            var txtTmp = txtGo.AddComponent<TextMeshProUGUI>();
            txtTmp.fontSize  = 12f;
            txtTmp.color     = TEXT_PRIMARY;
            txtTmp.alignment = TextAlignmentOptions.Center;

            var input          = host.AddComponent<TMP_InputField>();
            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = txtTmp;
            input.placeholder   = phTmp;
            input.fontAsset     = txtTmp.font;
            input.contentType   = TMP_InputField.ContentType.IntegerNumber;
            input.text          = "1";
            return input;
        }
    }
}
