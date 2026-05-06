using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Builds all UI panels for the Entities Runtime Editor (F5) using the same
    /// professional menu-bar + draggable-panel architecture as the Buildings (F10),
    /// FSM (F12) and Tile (F8) editors.
    ///
    /// Mirrors the Python <c>roguelike_editors/entities</c> package layout:
    ///   • Tools panel       ≡ <c>entities_tool_bar_panel</c> (undo / redo / save / reload)
    ///   • Categories panel  ≡ <c>entities_picker_panel</c> tabs
    ///                         (Hostiles / Neutrals / Specials / Players)
    ///   • Picker panel      ≡ <c>entities_picker_panel</c> grid (search + thumbnails)
    ///   • Add/Remove panel  ≡ <c>entities_add_remove_panel</c>
    ///                         (Add / Remove / Add-on-System / Confirm)
    ///   • Properties panel  ≡ <c>entities_properties_panel</c> (structured form)
    ///   • Tutorial overlay  ≡ <c>entities_tutorial_panel</c>
    ///
    /// This builder is purely UI/UX — it wires no gameplay logic. The runtime
    /// editor (<see cref="EntitiesRuntimeEditor"/>) supplies callbacks.
    /// </summary>
    public static partial class EntitiesEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            ToolsMenuBtnImg;       public TextMeshProUGUI ToolsMenuBtnTmp;
            public Image            CategoriesMenuBtnImg;  public TextMeshProUGUI CategoriesMenuBtnTmp;
            public Image            PickerMenuBtnImg;      public TextMeshProUGUI PickerMenuBtnTmp;
            public Image            AddRemoveMenuBtnImg;   public TextMeshProUGUI AddRemoveMenuBtnTmp;
            public Image            PropsMenuBtnImg;       public TextMeshProUGUI PropsMenuBtnTmp;

            // Panel roots + drag components
            public GameObject       ToolsDropdown;       public DraggablePanel ToolsPanelDrag;
            public GameObject       CategoriesDropdown;  public DraggablePanel CategoriesPanelDrag;
            public GameObject       PickerDropdown;      public DraggablePanel PickerPanelDrag;
            public GameObject       AddRemoveDropdown;   public DraggablePanel AddRemovePanelDrag;
            public GameObject       PropsDropdown;       public DraggablePanel PropsPanelDrag;

            // Tools panel
            public Image UndoBtnImg, RedoBtnImg, SaveBtnImg, ReloadBtnImg;

            // Categories panel (4 tabs)
            public Image            HostilesTabImg;      public TextMeshProUGUI HostilesTabTmp;
            public Image            NeutralsTabImg;      public TextMeshProUGUI NeutralsTabTmp;
            public Image            SpecialsTabImg;      public TextMeshProUGUI SpecialsTabTmp;
            public Image            PlayersTabImg;       public TextMeshProUGUI PlayersTabTmp;

            // Picker panel
            public TMP_InputField   SearchBox;
            public RectTransform    PickerContent;
            public TextMeshProUGUI  StatusText;

            // Add/Remove panel
            public Image            AddBtnImg;           public TextMeshProUGUI AddBtnTmp;
            public Image            RemoveBtnImg;        public TextMeshProUGUI RemoveBtnTmp;
            public Image            AddOnSystemBtnImg;   public TextMeshProUGUI AddOnSystemBtnTmp;
            public Image            ConfirmBtnImg;       public TextMeshProUGUI ConfirmBtnTmp;
            public TextMeshProUGUI  AddRemoveHintText;

            // Properties panel — structured (Identity, Stats, AI, Spawn, Auto-Cast, Assets)
            public TextMeshProUGUI  PropsHintText;
            public RectTransform    PropsFormRoot;
            public RectTransform    PropsIdentitySection;
            public RectTransform    PropsStatsSection;
            public RectTransform    PropsAISection;
            public RectTransform    PropsSpawnSection;
            public RectTransform    PropsAutoCastSection;
            public RectTransform    PropsAssetsSection;

            // Boss Editor handoff button (shown only when selected entity is a boss).
            public GameObject       BossHandoffBtnGo;
        }

        // ── Panel sizes ───────────────────────────────────────────────────────────

        private const float TOOLS_W      = TOOLS_DROP_W;          // 60 px (narrow)
        private const float TOOLS_H      = 220f + PANEL_HDR_H;    // Undo/Redo/Save/Reload

        private const float CATEGORIES_W = 160f;
        private const float CATEGORIES_H = 200f + PANEL_HDR_H;

        private const float PICKER_W     = TILES_DROP_W;          // 256 px
        private const float PICKER_H     = TILES_DROP_H;          // 564 px

        private const float ADDREM_W     = 180f;
        private const float ADDREM_H     = 240f + PANEL_HDR_H;

        private const float PROPS_W      = 320f;
        private const float PROPS_H      = 540f + PANEL_HDR_H;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W      = 130f;
        private const float TOOLS_BTN_W      = 60f;
        private const float CATEGORIES_BTN_W = 92f;
        private const float PICKER_BTN_W     = 70f;
        private const float ADDREM_BTN_W     = 110f;
        private const float PROPS_BTN_W      = 96f;
        private const float TUTORIAL_BTN_W   = 40f;

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,        Action onRedo,
            Action         onSave,        Action onReload,
            Action         onCatHostiles, Action onCatNeutrals,
            Action         onCatSpecials, Action onCatPlayers,
            Action<string> onSearchChanged,
            Action         onAdd,         Action onRemove,
            Action         onAddOnSystem, Action onConfirm,
            Action         onToggleTutorial)
        {
            // Reserve space below the menu bar so draggable panels cannot occlude it.
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();

            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial);
            BuildToolsPanel(canvasT, ref refs, onUndo, onRedo, onSave, onReload);
            BuildCategoriesPanel(canvasT, ref refs,
                onCatHostiles, onCatNeutrals, onCatSpecials, onCatPlayers);
            BuildPickerPanel(canvasT, ref refs, onSearchChanged);
            BuildAddRemovePanel(canvasT, ref refs,
                onAdd, onRemove, onAddOnSystem, onConfirm);
            BuildPropertiesPanel(canvasT, ref refs);

            return refs;
        }

        // ── Menu-button highlight helper ──────────────────────────────────────────

        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT      : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial)
        {
            var go = CreateUI("EntitiesMenuBar", canvasT);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0f, 1f);
            r.anchorMax        = new Vector2(1f, 1f);
            r.pivot            = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta        = new Vector2(0f, MENUBAR_HEIGHT);
            refs.MenuBar       = go;

            var bg           = go.AddComponent<Image>();
            bg.color         = MENUBAR_BG;
            bg.raycastTarget = true;

            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = BORDER;
            ol.effectDistance = new Vector2(0f, -1f);

            var chrome           = go.AddComponent<MenuBarChrome>();
            chrome.BgImage       = bg;
            chrome.BorderOutline = ol;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding             = new RectOffset((int)MENUBAR_PAD_H, (int)MENUBAR_PAD_H, 0, 0);
            hlg.spacing             = MENUBAR_SPACING;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var t = go.transform;

            // Brand
            var brand           = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp        = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "ENTITIES EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ToolsMenuBtnImg      = AddMenuBtn(t, "Tools v",       TOOLS_BTN_W,
                () => onToggle?.Invoke("tools"),       out refs.ToolsMenuBtnTmp);
            refs.CategoriesMenuBtnImg = AddMenuBtn(t, "Categories v",  CATEGORIES_BTN_W,
                () => onToggle?.Invoke("categories"),  out refs.CategoriesMenuBtnTmp);
            refs.PickerMenuBtnImg     = AddMenuBtn(t, "Picker v",      PICKER_BTN_W,
                () => onToggle?.Invoke("picker"),      out refs.PickerMenuBtnTmp);
            refs.AddRemoveMenuBtnImg  = AddMenuBtn(t, "Add/Remove v",  ADDREM_BTN_W,
                () => onToggle?.Invoke("addremove"),   out refs.AddRemoveMenuBtnTmp);
            refs.PropsMenuBtnImg      = AddMenuBtn(t, "Properties v",  PROPS_BTN_W,
                () => onToggle?.Invoke("props"),       out refs.PropsMenuBtnTmp);

            // Flexible spacer
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
        }

        // ── Menu helpers (shared with Panels partial) ─────────────────────────────

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

            var img = go.AddComponent<Image>();
            img.color = MENU_BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
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
    }
}
