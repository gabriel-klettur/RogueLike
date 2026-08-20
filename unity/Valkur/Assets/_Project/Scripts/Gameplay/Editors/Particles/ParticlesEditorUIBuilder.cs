using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Builds the UI shell for the runtime Particles Editor (F1) — same chrome
    /// architecture as Buildings (F10), Entities (F5) and Tile (F8) editors:
    ///   • 30 px menu bar with brand + dropdown buttons + tutorial shortcut
    ///   • DraggablePanel + PanelChrome floating panels (Tools / Presets / Properties / Spells)
    ///
    /// Mirrors the Python <c>roguelike_editors/particles</c> panel layout:
    ///   particles_tool_bar_panel · particles_picker_panel · particles_properties_panel ·
    ///   particles_spells_list_panel · particles_add_remove_panel · particles_tutorial_panel.
    /// </summary>
    public static partial class ParticlesEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            ToolsMenuBtnImg;    public TextMeshProUGUI ToolsMenuBtnTmp;
            public Image            PresetsMenuBtnImg;  public TextMeshProUGUI PresetsMenuBtnTmp;
            public Image            PropsMenuBtnImg;    public TextMeshProUGUI PropsMenuBtnTmp;
            public Image            ViewMenuBtnImg;     public TextMeshProUGUI ViewMenuBtnTmp;
            public Image            SpellsMenuBtnImg;   public TextMeshProUGUI SpellsMenuBtnTmp;

            // Panel roots + drag components
            public GameObject       ToolsDropdown;     public DraggablePanel ToolsPanelDrag;
            public GameObject       PresetsDropdown;   public DraggablePanel PresetsPanelDrag;
            public GameObject       PropsDropdown;     public DraggablePanel PropsPanelDrag;
            public GameObject       ViewDropdown;      public DraggablePanel ViewPanelDrag;
            public GameObject       SpellsDropdown;    public DraggablePanel SpellsPanelDrag;

            // Tools panel
            public Image SelectBtnImg, PlaceBtnImg, DeleteBtnImg;
            public Image AddSystemBtnImg, RemoveSystemBtnImg;
            public Image UndoBtnImg, RedoBtnImg, SaveBtnImg, ReloadBtnImg;
            public TextMeshProUGUI UndoBtnLabel, RedoBtnLabel;

            // Presets panel — shared
            public TMP_InputField   SearchBox;
            /// <summary>Editable preset rows in the Properties panel. Rebuilt per selection.</summary>
            public PropertyForm     PresetPropsForm;
            /// <summary>Category filter strip above the search box. Content-less; filters only.</summary>
            public TabStrip         PresetsCategoryTabStrip;
            public TextMeshProUGUI  StatusText;
            public TabStrip         PresetsTabStrip;

            // Presets panel — Grid view
            public RectTransform    PickerContent;

            // Presets panel — Table view
            public ScrollRect       PresetsTableHeaderScroll;
            public RectTransform    PresetsTableHeaderContent;
            public ScrollRect       PresetsTableBodyScroll;
            public RectTransform    PresetsTableBodyContent;
            public Button           PresetsColumnsCfgBtn;
            public TextMeshProUGUI  PresetsColumnsCfgLabel;

            // Properties panel
            public TextMeshProUGUI  PresetPropsText;
            public Toggle           LoopsToggle;          // edits preset.vfx.loops in-memory
            public TextMeshProUGUI  InstancePropsText;
            public GameObject       DeleteInstanceBtnGo;  // shown only when an instance is selected
            public Image            DeleteInstanceBtnImg;

            // Tools panel — danger zone
            public Image            DeleteInZoneBtnImg;

            // View panel — live preview
            public RawImage         ViewRawImage;
            public TextMeshProUGUI  ViewPresetNameTmp;
            public TextMeshProUGUI  ViewStatusTmp;
            public Button           ViewPlayPauseBtn;
            public TextMeshProUGUI  ViewPlayPauseBtnLabel;
            public Button           ViewSpeed025Btn;
            public Image            ViewSpeed025BtnImg;
            public Button           ViewSpeed05Btn;
            public Image            ViewSpeed05BtnImg;
            public Button           ViewSpeed1Btn;
            public Image            ViewSpeed1BtnImg;
            public Button           ViewZoomInBtn;
            public Button           ViewZoomOutBtn;

            // Spells panel
            public TextMeshProUGUI  SpellsHeaderTmp;
            public RectTransform    SpellsContent;
        }

        // ── Panel sizes ───────────────────────────────────────────────────────────

        private const float TOOLS_W   = 200f;
        private const float TOOLS_H   = 360f + PANEL_HDR_H;

        private const float PRESETS_W = 280f;
        private const float PRESETS_H = 540f + PANEL_HDR_H;

        private const float PROPS_W   = 280f;
        private const float PROPS_H   = 460f + PANEL_HDR_H;

        private const float SPELLS_W  = 260f;
        private const float SPELLS_H  = 220f + PANEL_HDR_H;

        // ── Menu button widths ────────────────────────────────────────────────────

        private const float TITLE_BTN_W    = 140f;
        private const float TOOLS_BTN_W    = 70f;
        private const float PRESETS_BTN_W  = 80f;
        private const float PROPS_BTN_W    = 92f;
        private const float VIEW_BTN_W     = 60f;
        private const float SPELLS_BTN_W   = 76f;
        private const float TUTORIAL_BTN_W = 40f;

        private const float VIEW_W         = 420f;
        private const float VIEW_H         = 500f + PANEL_HDR_H;

        // ── BuildAll ──────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,              Action onRedo,
            Action         onSave,              Action onReload,
            Action         onModeSelect,        Action onModePlace, Action onModeDelete,
            Action         onAddSystem,         Action onRemoveSystem,
            Action<string> onSearchChanged,
            Action         onToggleSpells,
            Action         onToggleTutorial,
            Action         onDeleteInZone    = null,
            Action         onDeleteInstance  = null,
            UnityEngine.Events.UnityAction<bool> onLoopsToggled = null,
            Action<string> onCategoryChanged = null)
        {
            // Reserve space below the menu bar so draggable panels cannot occlude it.
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();

            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial);
            BuildToolsPanel(canvasT, ref refs,
                onModeSelect, onModePlace, onModeDelete,
                onAddSystem, onRemoveSystem,
                onUndo, onRedo, onSave, onReload,
                onDeleteInZone);
            BuildPresetsPanel(canvasT, ref refs, onSearchChanged, onCategoryChanged);
            BuildPropertiesPanel(canvasT, ref refs, onDeleteInstance, onLoopsToggled);
            BuildViewPanel(canvasT, ref refs);
            BuildSpellsPanel(canvasT, ref refs, onToggleSpells);

            return refs;
        }

        // ── Menu-button highlight helper ─────────────────────────────────────────

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
            var go = CreateUI("ParticlesMenuBar", canvasT);
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
            var brand = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "PARTICLES EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ToolsMenuBtnImg   = AddMenuBtn(t, "Tools v",      TOOLS_BTN_W,
                () => onToggle?.Invoke("tools"),   out refs.ToolsMenuBtnTmp);
            refs.PresetsMenuBtnImg = AddMenuBtn(t, "Presets v",    PRESETS_BTN_W,
                () => onToggle?.Invoke("presets"), out refs.PresetsMenuBtnTmp);
            refs.PropsMenuBtnImg   = AddMenuBtn(t, "Properties v", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),   out refs.PropsMenuBtnTmp);
            refs.ViewMenuBtnImg    = AddMenuBtn(t, "View v",       VIEW_BTN_W,
                () => onToggle?.Invoke("view"),    out refs.ViewMenuBtnTmp);
            refs.SpellsMenuBtnImg  = AddMenuBtn(t, "Spells v",     SPELLS_BTN_W,
                () => onToggle?.Invoke("spells"),  out refs.SpellsMenuBtnTmp);

            // Flexible spacer
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
        }

        // ── Menu helpers (shared with Panels partial) ────────────────────────────

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
