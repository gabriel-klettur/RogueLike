using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Builds all UI panels for the Map Editor (F11) using the same
    /// professional menu-bar + floating-panel architecture as the Tile Editor (F8)
    /// and Buildings Editor (F10).
    ///
    /// Layout:
    ///   • 30 px menu bar at top — brand + "Zones v", "Actions v", "Props v" buttons
    ///   • Zones panel    (280 px)  — zone list
    ///   • Actions panel  (230 px)  — CRUD ops + move arrows
    ///   • Properties panel (260 px) — zone name / offset / size / editable + restrict toggle
    ///   • AddZone dialog  (modal overlay)
    ///   • DeleteZone dialog (modal overlay)
    /// Widget helpers (MakeDrop, MakeScrollView, AddActionBtn, etc.) live in
    /// MapEditorUIBuilder.Widgets.cs.
    /// </summary>
    public static partial class MapEditorUIBuilder
    {
        // ── UIRefs ──────────────────────────────────────────────────────────────

        public partial struct UIRefs
        {
            // Menu bar
            public GameObject      MenuBar;
            public Image           ZonesMenuBtnImg;    public TextMeshProUGUI ZonesMenuBtnTmp;
            public Image           ActionsMenuBtnImg;  public TextMeshProUGUI ActionsMenuBtnTmp;
            public TextMeshProUGUI StatusBarText;

            // Floating panel roots + drag handles
            public GameObject    ZonesDropdown;    public DraggablePanel ZonesPanelDrag;
            public GameObject    ActionsDropdown;  public DraggablePanel ActionsPanelDrag;

            // Zones panel
            public ScrollRect      ZonesScrollRect;
            public RectTransform   ZonesListContent;

            // Actions panel (no longer holds NameInput — moved to Properties)

            // AddZone dialog
            public GameObject      AddZoneDialog;
            public TMP_InputField  AddZoneNameInput;
            public TextMeshProUGUI AddZoneSourceText;
            public TextMeshProUGUI AddZoneTargetText;
            public Toggle          AddUseTemplateToggle;
            public Toggle          AddEditableToggle;

            // DeleteZone dialog
            public GameObject      DeleteZoneDialog;
            public TextMeshProUGUI DeleteZonePrompt;

            // Properties panel
            public Image           PropsMenuBtnImg;  public TextMeshProUGUI PropsMenuBtnTmp;
            public GameObject      PropsDropdown;    public DraggablePanel  PropsPanelDrag;
            public TextMeshProUGUI PropsHintText;
            public TMP_InputField  NameInput;         // zone-name input (live rename on EndEdit)
            public TextMeshProUGUI PropsOffsetText;
            public TextMeshProUGUI PropsDimText;
            public TextMeshProUGUI PropsEditableText; // clickable row (toggle on click)
            public Toggle          RestrictToggle;    // restrict tile editor to editable zones

            // Add Zone mode visual indicators
            public Image   AddZoneBtnImage;
            public Outline AddZoneBtnOutline;
        }

        // ── Panel sizes (mirror Buildings Editor constants) ──────────────────────

        private const float ZONES_W    = 280f;
        private const float ZONES_H    = 420f + PANEL_HDR_H;   // 444 px

        private const float ACTIONS_W  = 230f;
        private const float ACTIONS_H  = 112f + PANEL_HDR_H;   // 136 px

        private const float BTN_H      = 32f;                   // action button height

        private const float PROPS_W        = 260f;
        private const float PROPS_H        = 237f + PANEL_HDR_H;   // +46 px for restrict toggle section

        // ── Menu button widths ───────────────────────────────────────────────────

        private const float BRAND_BTN_W    = 126f;
        private const float ZONES_BTN_W    = 74f;
        private const float ACTIONS_BTN_W  = 82f;
        private const float PROPS_BTN_W    = 68f;
        private const float BIOMES_BTN_W   = 80f;
        private const float MAPS_BTN_W     = 70f;
        private const float STAMP_BTN_W    = 70f;

        // ── BuildAll ─────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform              canvasT,
            Action<string>         onDropdownToggle,
            Action<string, bool, bool> onConfirmAddZone,
            Action                 onCancelAddZoneFlow,
            Action                 onBeginAddZoneFlow,
            Action                 onDuplicateSelectedZone,
            Action                 onRequestDeleteSelectedZone,
            Action                 onConfirmDeleteSelectedZone,
            Action                 onCancelDeleteZone,
            Action<string>         onRenameSelectedZone,
            Action                 onToggleSelectedZoneEditable,
            Action<bool>           onRestrictEditChanged,
            Action<BiomeDialogResult> onConfirmGenerateBiomes,
            MapSlotCallbacks       mapSlotCallbacks,
            PortalCallbacks        portalCallbacks,
            StampCallbacks         stampCallbacks)
        {
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle);
            BuildZonesPanel(canvasT, ref refs);
            BuildActionsPanel(canvasT, ref refs,
                onBeginAddZoneFlow, onDuplicateSelectedZone,
                onRequestDeleteSelectedZone, portalCallbacks);
            BuildPropertiesPanel(canvasT, ref refs, onRenameSelectedZone,
                onToggleSelectedZoneEditable, onRestrictEditChanged);
            BuildBiomesPanel(canvasT, ref refs, onConfirmGenerateBiomes);
            BuildMapsPanel(canvasT, ref refs, mapSlotCallbacks);
            BuildStampPanel(canvasT, ref refs,
                stampCallbacks.DiscoverStamps,
                stampCallbacks.OnPlaceStamp,
                stampCallbacks.OnCancelStamp);
            BuildAddZoneDialog(canvasT, ref refs, onConfirmAddZone, onCancelAddZoneFlow);
            BuildDeleteZoneDialog(canvasT, ref refs, onConfirmDeleteSelectedZone, onCancelDeleteZone);
            BuildPlacePortalDialog(canvasT, ref refs, portalCallbacks);
            return refs;
        }

        /// <summary>
        /// Bundle for the Map Editor Stamp panel callbacks. Mirrors
        /// <see cref="MapSlotCallbacks"/> / <see cref="PortalCallbacks"/> so the
        /// BuildAll signature stays manageable.
        /// </summary>
        public struct StampCallbacks
        {
            public Func<List<StampDescriptor>> DiscoverStamps;
            public Action<string, TilemapLayerSetup.TilemapLayer> OnPlaceStamp;
            public Action OnCancelStamp;
        }

        /// <summary>
        /// Snapshot of biome-dialog choices forwarded to the manager when the user
        /// clicks Generate. Plain primitives so the UIBuilder doesn't need to
        /// reference <see cref="MapEditorManager.BiomeGenerationRequest"/>.
        /// </summary>
        public struct BiomeDialogResult
        {
            public Valkur.Data.Biomes.BiomeKind biome;
            public bool randomPerZone;
            public bool selectedZoneOnly;
            public int seed;
        }

        // ── Public helper: menu-button highlight ─────────────────────────────────

        /// <summary>
        /// Mirrors <c>TileEditorUI.ApplyMenuBtnStyle</c> —
        /// updates colour + bold state of a menu-bar button to reflect open/closed.
        /// </summary>
        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null) img.color = isOpen ? MENU_BTN_OPEN : MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? ACCENT     : TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ── Menu Bar ─────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle)
        {
            var go = CreateUI("MapEditorMenuBar", canvasT);
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

            // Brand label
            var brand           = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = BRAND_BTN_W;
            var brandTmp        = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "MAP EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ZonesMenuBtnImg    = AddMenuBtn(t, "Zones v",    ZONES_BTN_W,
                () => onToggle?.Invoke("zones"),    out refs.ZonesMenuBtnTmp);
            refs.ActionsMenuBtnImg  = AddMenuBtn(t, "Actions v",  ACTIONS_BTN_W,
                () => onToggle?.Invoke("actions"),  out refs.ActionsMenuBtnTmp);
            refs.PropsMenuBtnImg    = AddMenuBtn(t, "Props v",    PROPS_BTN_W,
                () => onToggle?.Invoke("props"),    out refs.PropsMenuBtnTmp);
            refs.BiomesMenuBtnImg   = AddMenuBtn(t, "Biomes v",   BIOMES_BTN_W,
                () => onToggle?.Invoke("biomes"),   out refs.BiomesMenuBtnTmp);
            refs.MapsMenuBtnImg     = AddMenuBtn(t, "Maps v",     MAPS_BTN_W,
                () => onToggle?.Invoke("maps"),     out refs.MapsMenuBtnTmp);
            refs.StampMenuBtnImg    = AddMenuBtn(t, "Stamp v",    STAMP_BTN_W,
                () => onToggle?.Invoke("stamp"),    out refs.StampMenuBtnTmp);

            // Flexible spacer pushes status text to the right
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);

            // Status text (right side of menu bar)
            var statusGo  = CreateUI("StatusText", t);
            statusGo.AddComponent<LayoutElement>().preferredWidth = 320f;
            refs.StatusBarText        = statusGo.AddComponent<TextMeshProUGUI>();
            refs.StatusBarText.text   = "F11 to close";
            refs.StatusBarText.fontSize     = 10f;
            refs.StatusBarText.color        = TEXT_SECONDARY;
            refs.StatusBarText.alignment    = TextAlignmentOptions.Right;
            refs.StatusBarText.raycastTarget = false;
        }

        // ── Zones Panel ──────────────────────────────────────────────────────────

        private static void BuildZonesPanel(Transform canvasT, ref UIRefs refs)
        {
            float x = PANEL_GAP;
            refs.ZonesDropdown = MakeDrop("ZonesPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                ZONES_W, ZONES_H, "ZONES", out var t, out refs.ZonesPanelDrag);

            // Zone list scroll view (selection info moved to Properties panel)
            var scrollGo = MakeScrollView("ZonesList", t, out var content, 380f);
            var scrollLE = scrollGo.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;
            scrollLE.minHeight      = 200f;
            refs.ZonesScrollRect    = scrollGo.GetComponent<ScrollRect>();
            refs.ZonesListContent   = content;

            refs.ZonesDropdown.SetActive(false);
        }

        // ── Actions Panel ────────────────────────────────────────────────────────

        private static void BuildActionsPanel(Transform canvasT, ref UIRefs refs,
            Action onBeginAdd,
            Action onDuplicate,
            Action onRequestDelete,
            PortalCallbacks portalCallbacks)
        {
            float x = PANEL_GAP + ZONES_W + PANEL_GAP;
            refs.ActionsDropdown = MakeDrop("ActionsPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                ACTIONS_W, ACTIONS_H + BTN_H + 12f /* room for Portal button */,
                "ACTIONS", out var t, out refs.ActionsPanelDrag);

            BuildSectionLabel(t, "Operations");

            var addZoneBtn = AddActionBtn(t, "Add Zone",  BTN_H, onBeginAdd);
            refs.AddZoneBtnImage   = addZoneBtn.GetComponent<Image>();
            var addOutline         = addZoneBtn.gameObject.AddComponent<Outline>();
            addOutline.effectColor    = new Color(0f, 0f, 0f, 0f);   // invisible until mode active
            addOutline.effectDistance = new Vector2(2f, 2f);
            refs.AddZoneBtnOutline = addOutline;

            AddActionBtn(t, "Duplicate", BTN_H, onDuplicate);
            AddActionBtn(t, "Delete",    BTN_H, onRequestDelete, danger: true);

            // Portal placement: armed by this button, finalised in the
            // BuildPlacePortalDialog modal. Outline pulses while armed so
            // the user has the same visual cue as Add Zone.
            var placePortalBtn = AddActionBtn(t, "Place Portal", BTN_H,
                () => portalCallbacks.OnBeginPlace?.Invoke());
            refs.PlacePortalBtnImage   = placePortalBtn.GetComponent<Image>();
            var placeOutline           = placePortalBtn.gameObject.AddComponent<Outline>();
            placeOutline.effectColor    = new Color(0f, 0f, 0f, 0f);
            placeOutline.effectDistance = new Vector2(2f, 2f);
            refs.PlacePortalBtnOutline = placeOutline;

            refs.ActionsDropdown.SetActive(false);
        }

        // ── Properties Panel ─────────────────────────────────────────────────────

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onRename, Action onToggleEditable, Action<bool> onRestrictChanged)
        {
            refs.PropsDropdown = MakeDrop("PropertiesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "PROPERTIES", out var t, out refs.PropsPanelDrag);

            // Hint text — shown when no zone is selected
            var hintGo = CreateUI("PropsHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 34f;
            refs.PropsHintText                    = hintGo.AddComponent<TextMeshProUGUI>();
            refs.PropsHintText.text               = "Select a zone to\nview its properties.";
            refs.PropsHintText.fontSize           = 11f;
            refs.PropsHintText.color              = TEXT_SECONDARY;
            refs.PropsHintText.alignment          = TextAlignmentOptions.TopLeft;
            refs.PropsHintText.enableWordWrapping = true;

            BuildSeparator(t);

            // ── Zone name (live rename on EndEdit) ───────────────────────────
            BuildSectionLabel(t, "Zone name");
            var nameHost = CreateUI("NameHost", t);
            nameHost.AddComponent<LayoutElement>().preferredHeight = 34f;
            refs.NameInput = MakeTmpInput(nameHost, "zone_name");
            // Rename fires automatically when the user presses Enter or leaves the field.
            refs.NameInput.onEndEdit.AddListener(name => onRename?.Invoke(name));

            BuildSeparator(t);

            // ── Read-only info rows ───────────────────────────────────────────
            refs.PropsOffsetText = BuildPropRow(t, "Offset");
            refs.PropsDimText    = BuildPropRow(t, "Size");

            BuildSeparator(t);

            // ── Editable state (click anywhere on row to toggle) ──────────────
            refs.PropsEditableText = BuildPropRow(t, "Editable");
            // Add a transparent button over the whole row so clicking it anywhere toggles editable.
            var editRow = refs.PropsEditableText.transform.parent.gameObject;
            var editImg = editRow.AddComponent<Image>();
            editImg.color = Color.clear;
            var editBtn = editRow.AddComponent<Button>();
            var ec = editBtn.colors;
            ec.normalColor      = Color.clear;
            ec.highlightedColor = new Color(1f, 1f, 1f, 0.07f);
            ec.pressedColor     = new Color(1f, 1f, 1f, 0.15f);
            ec.selectedColor    = Color.clear;
            ec.colorMultiplier  = 1f;
            editBtn.colors      = ec;
            editBtn.targetGraphic = editImg;
            editBtn.onClick.AddListener(() => onToggleEditable?.Invoke());
            // Make label & value TMP non-raycast so clicks pass through to the button.
            refs.PropsEditableText.raycastTarget = true;  // already false in BuildPropRow; button covers row

            BuildSeparator(t);

            // ── Tile editor constraint (was Settings panel) ────────────────────
            BuildSectionLabel(t, "Tile editor constraint");
            var toggleRow = MakeRow("RestrictRow", t, 28f);
            var restrictLabel            = CreateUI("RestrictLabel", toggleRow.transform);
            restrictLabel.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var rlTmp                    = restrictLabel.AddComponent<TextMeshProUGUI>();
            rlTmp.text                   = "Restrict editing to editable zones";
            rlTmp.fontSize               = 10f;
            rlTmp.color                  = TEXT_SECONDARY;
            rlTmp.alignment              = TextAlignmentOptions.MidlineLeft;
            rlTmp.enableWordWrapping     = true;
            refs.RestrictToggle = MakeToggle(toggleRow.transform);
            refs.RestrictToggle.onValueChanged.AddListener(v => onRestrictChanged?.Invoke(v));

            refs.PropsDropdown.SetActive(false);
        }

        // ── AddZone Dialog ───────────────────────────────────────────────────────
        // Uses the same MakeDrop floating-panel shell as Zones / Actions / Settings
        // for full UI/UX consistency (same header, outline, chrome, drag handle).

        private const float ADD_DIALOG_W   = 430f;
        private const float ADD_DIALOG_H   = 285f + PANEL_HDR_H;   // shell adds header
        private const float DEL_DIALOG_W   = 430f;
        private const float DEL_DIALOG_H   = 130f + PANEL_HDR_H;

        private static void BuildAddZoneDialog(Transform canvasT, ref UIRefs refs,
            Action<string, bool, bool> onConfirm, Action onCancel)
        {
            float x = PANEL_GAP + ZONES_W + PANEL_GAP;
            var go = MakeDrop("AddZoneDialog", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                ADD_DIALOG_W, ADD_DIALOG_H, "ADD ZONE", out var t, out _);
            refs.AddZoneDialog = go;

            // Source + Target info
            var srcGo = CreateUI("Source", t);
            srcGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.AddZoneSourceText = srcGo.AddComponent<TextMeshProUGUI>();
            refs.AddZoneSourceText.text      = "Source: (none)";
            refs.AddZoneSourceText.fontSize  = 11f;
            refs.AddZoneSourceText.color     = TEXT_SECONDARY;
            refs.AddZoneSourceText.alignment = TextAlignmentOptions.Left;

            var tgtGo = CreateUI("Target", t);
            tgtGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.AddZoneTargetText = tgtGo.AddComponent<TextMeshProUGUI>();
            refs.AddZoneTargetText.text      = "Target: click map to mark (50x50)";
            refs.AddZoneTargetText.fontSize  = 11f;
            refs.AddZoneTargetText.color     = TEXT_SECONDARY;
            refs.AddZoneTargetText.alignment = TextAlignmentOptions.Left;

            BuildSeparator(t);
            BuildSectionLabel(t, "Zone name");

            var nameHost = CreateUI("NameHost", t);
            nameHost.AddComponent<LayoutElement>().preferredHeight = 34f;
            refs.AddZoneNameInput = MakeTmpInput(nameHost, "new_zone_name");

            BuildSectionLabel(t, "Options");

            var tplRow   = MakeRow("TplRow", t, 28f);
            var tplLabel = CreateUI("TplLabel", tplRow.transform);
            tplLabel.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var tplTmp   = tplLabel.AddComponent<TextMeshProUGUI>();
            tplTmp.text      = "Use selected as template";
            tplTmp.fontSize  = 11f;
            tplTmp.color     = TEXT_PRIMARY;
            tplTmp.alignment = TextAlignmentOptions.MidlineLeft;
            refs.AddUseTemplateToggle = MakeToggle(tplRow.transform);

            var editRow   = MakeRow("EditRow", t, 28f);
            var editLabel = CreateUI("EditLabel", editRow.transform);
            editLabel.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var editTmp   = editLabel.AddComponent<TextMeshProUGUI>();
            editTmp.text      = "Editable in tile editor";
            editTmp.fontSize  = 11f;
            editTmp.color     = TEXT_PRIMARY;
            editTmp.alignment = TextAlignmentOptions.MidlineLeft;
            refs.AddEditableToggle = MakeToggle(editRow.transform);

            BuildSeparator(t);

            // Capture ref-param fields as locals — C# forbids capturing ref params in lambdas (CS1628)
            var localAddNameInput      = refs.AddZoneNameInput;
            var localUseTemplateToggle = refs.AddUseTemplateToggle;
            var localAddEditableToggle = refs.AddEditableToggle;

            var btnRow = MakeRow("AddDialogBtns", t, BTN_H);
            AddActionBtn(btnRow.transform, "Confirm Add", BTN_H,
                () => onConfirm?.Invoke(
                    localAddNameInput?.text ?? "",
                    localUseTemplateToggle != null && localUseTemplateToggle.isOn,
                    localAddEditableToggle != null && localAddEditableToggle.isOn));
            AddActionBtn(btnRow.transform, "Cancel", BTN_H,
                () => { onCancel?.Invoke(); go.SetActive(false); });

            go.SetActive(false);
        }

        // ── DeleteZone Dialog ────────────────────────────────────────────────────

        private static void BuildDeleteZoneDialog(Transform canvasT, ref UIRefs refs,
            Action onConfirm, Action onCancel)
        {
            float x = PANEL_GAP + ZONES_W + PANEL_GAP;
            var go = MakeDrop("DeleteZoneDialog", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET + ADD_DIALOG_H + PANEL_GAP,
                DEL_DIALOG_W, DEL_DIALOG_H, "CONFIRM DELETE", out var t, out _);
            refs.DeleteZoneDialog = go;

            // Re-skin the shell with a danger tint on outline only — keep header style consistent
            var ol = go.GetComponent<Outline>();
            if (ol != null) ol.effectColor = new Color(1f, 0.32f, 0.36f, 0.6f);

            var promptGo = CreateUI("Prompt", t);
            promptGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            refs.DeleteZonePrompt = promptGo.AddComponent<TextMeshProUGUI>();
            refs.DeleteZonePrompt.text       = "Delete zone?";
            refs.DeleteZonePrompt.fontSize   = 13f;
            refs.DeleteZonePrompt.color      = TEXT_PRIMARY;
            refs.DeleteZonePrompt.alignment  = TextAlignmentOptions.Left;

            BuildSeparator(t);

            var btnRow = MakeRow("DeleteDialogBtns", t, BTN_H);
            AddActionBtn(btnRow.transform, "Delete", BTN_H,
                () => { onConfirm?.Invoke(); go.SetActive(false); },
                danger: true);
            AddActionBtn(btnRow.transform, "Cancel", BTN_H,
                () => { onCancel?.Invoke(); go.SetActive(false); });

            go.SetActive(false);
        }
    }
}
