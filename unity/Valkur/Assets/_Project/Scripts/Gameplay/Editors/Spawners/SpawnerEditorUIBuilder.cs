using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Builds all UI panels for the Spawner Runtime Editor (F3) using the same
    /// professional menu-bar + draggable-panel architecture as the Buildings,
    /// Entities, FSM and Tile editors. Pure UI: every callback comes from the
    /// runtime editor (<see cref="SpawnerEditorManager"/>).
    ///
    /// Mirrors the Python <c>roguelike_editors/spawners</c> package layout:
    ///   • Tools panel       ≡ <c>spawner_tool_bar_panel</c>      (undo / redo / save / reload)
    ///   • Picker panel      ≡ <c>spawner_picker_panel</c>        (search + template list)
    ///   • Modes panel       ≡ <c>spawner_add_remove_panel</c>    (Select / Place / Delete)
    ///   • Properties panel  ≡ <c>spawner_properties_panel</c>    (form on the selected instance)
    ///   • Tutorial overlay  ≡ <c>spawner_tutorial_panel</c>
    ///
    /// Reuses <see cref="EditorUIHelpers.MakeDropPanel"/>, <see cref="EditorUIHelpers.AddActionBtn"/>,
    /// <see cref="EditorUIHelpers.AddModeBtn"/>, <see cref="EditorUIHelpers.AddMenuBtn"/> so chrome
    /// and theme stay identical to the rest of the editor suite.
    /// </summary>
    public static partial class SpawnerEditorUIBuilder
    {
        // ── UIRefs ──────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject       MenuBar;
            public Image            ToolsMenuBtnImg;   public TextMeshProUGUI ToolsMenuBtnTmp;
            public Image            PickerMenuBtnImg;  public TextMeshProUGUI PickerMenuBtnTmp;
            public Image            PropsMenuBtnImg;   public TextMeshProUGUI PropsMenuBtnTmp;

            // Panels (roots + drag components)
            public GameObject       ToolsDropdown;     public DraggablePanel ToolsPanelDrag;
            public GameObject       PickerDropdown;    public DraggablePanel PickerPanelDrag;
            public GameObject       PropsDropdown;     public DraggablePanel PropsPanelDrag;

            // Tools panel
            public Image            UndoBtnImg, RedoBtnImg, SaveBtnImg, ReloadBtnImg;

            // Picker panel
            public TMP_InputField   SearchBox;
            public RectTransform    PickerContent;
            public TextMeshProUGUI  StatusText;

            // Properties panel — a scrollable form of rows (read-only labels for
            // identity/runtime info, committed TMP_InputFields for the numeric template
            // fields that actually drive behaviour), rebuilt on every selection change by
            // RefreshPropertiesPanel via EntitiesEditorUIBuilder.AddPropertyRow/AddEditableRow.
            public RectTransform    PropsFormRoot;
            public Image            DeleteFromPropsBtnImg;
            public GameObject       DeleteFromPropsBtnGo;   // hidden when no spawner selected
            public TextMeshProUGUI  DeleteFromPropsBtnTmp;
        }

        // ── Panel sizes (compatible visual budget with Entities / Particles) ────

        private const float TOOLS_W   = TOOLS_DROP_W;             // 60 (narrow)
        private const float TOOLS_H   = 220f + PANEL_HDR_H;       // Undo/Redo/Save/Reload

        private const float PICKER_W  = 256f;
        private const float PICKER_H  = 540f + PANEL_HDR_H;

        private const float PROPS_W   = 320f;
        private const float PROPS_H   = 540f + PANEL_HDR_H;

        // ── Menu-bar button widths ─────────────────────────────────────────────

        private const float TITLE_BTN_W   = 130f;
        private const float TOOLS_BTN_W   = 60f;
        private const float PICKER_BTN_W  = 70f;
        private const float PROPS_BTN_W   = 96f;
        private const float TUTORIAL_BTN_W = 40f;

        // ── BuildAll ────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action onUndo, Action onRedo, Action onSave, Action onReload,
            Action<string> onSearchChanged,
            Action onDeleteSelected,
            Action onToggleTutorial)
        {
            // Reserve space below the menu bar so draggable panels cannot occlude it.
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();

            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial);
            BuildToolsPanel(canvasT, ref refs, onUndo, onRedo, onSave, onReload);
            BuildPickerPanel(canvasT, ref refs, onSearchChanged);
            BuildPropertiesPanel(canvasT, ref refs, onDeleteSelected);

            return refs;
        }

        // ── Menu Bar ────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial)
        {
            var go = EditorUIHelpers.CreateUI("SpawnerMenuBar", canvasT);
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
            hlg.padding                = new RectOffset((int)MENUBAR_PAD_H, (int)MENUBAR_PAD_H, 0, 0);
            hlg.spacing                = MENUBAR_SPACING;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var t = go.transform;

            // Brand
            var brand = EditorUIHelpers.CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp                = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text               = "SPAWNER EDITOR";
            brandTmp.fontSize           = 11f;
            brandTmp.fontStyle          = FontStyles.Bold;
            brandTmp.alignment          = TextAlignmentOptions.Left;
            brandTmp.color              = UITheme.ACCENT;
            brandTmp.characterSpacing   = 2f;

            EditorUIHelpers.AddMenuDivider(t);

            refs.ToolsMenuBtnImg  = EditorUIHelpers.AddMenuBtn(t, "Tools v",      TOOLS_BTN_W,
                () => onToggle?.Invoke("tools"),  out refs.ToolsMenuBtnTmp);
            refs.PickerMenuBtnImg = EditorUIHelpers.AddMenuBtn(t, "Picker v",     PICKER_BTN_W,
                () => onToggle?.Invoke("picker"), out refs.PickerMenuBtnTmp);
            refs.PropsMenuBtnImg  = EditorUIHelpers.AddMenuBtn(t, "Properties v", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),  out refs.PropsMenuBtnTmp);

            EditorUIHelpers.CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            EditorUIHelpers.AddMenuDivider(t);
            EditorUIHelpers.AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
        }
    }
}
