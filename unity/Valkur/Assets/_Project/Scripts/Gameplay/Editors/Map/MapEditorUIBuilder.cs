using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.TileEditor;
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
            MapSlotCallbacks       mapSlotCallbacks)
        {
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle);
            BuildZonesPanel(canvasT, ref refs);
            BuildActionsPanel(canvasT, ref refs,
                onBeginAddZoneFlow, onDuplicateSelectedZone,
                onRequestDeleteSelectedZone);
            BuildPropertiesPanel(canvasT, ref refs, onRenameSelectedZone,
                onToggleSelectedZoneEditable, onRestrictEditChanged);
            BuildBiomesPanel(canvasT, ref refs, onConfirmGenerateBiomes);
            BuildMapsPanel(canvasT, ref refs, mapSlotCallbacks);
            BuildAddZoneDialog(canvasT, ref refs, onConfirmAddZone, onCancelAddZoneFlow);
            BuildDeleteZoneDialog(canvasT, ref refs, onConfirmDeleteSelectedZone, onCancelDeleteZone);
            return refs;
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
            Action onRequestDelete)
        {
            float x = PANEL_GAP + ZONES_W + PANEL_GAP;
            refs.ActionsDropdown = MakeDrop("ActionsPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                ACTIONS_W, ACTIONS_H, "ACTIONS", out var t, out refs.ActionsPanelDrag);

            BuildSectionLabel(t, "Operations");

            var addZoneBtn = AddActionBtn(t, "Add Zone",  BTN_H, onBeginAdd);
            refs.AddZoneBtnImage   = addZoneBtn.GetComponent<Image>();
            var addOutline         = addZoneBtn.gameObject.AddComponent<Outline>();
            addOutline.effectColor    = new Color(0f, 0f, 0f, 0f);   // invisible until mode active
            addOutline.effectDistance = new Vector2(2f, 2f);
            refs.AddZoneBtnOutline = addOutline;

            AddActionBtn(t, "Duplicate", BTN_H, onDuplicate);
            AddActionBtn(t, "Delete",    BTN_H, onRequestDelete, danger: true);

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

        /// <summary>Creates a two-column label + value row inside a panel VLG.</summary>
        private static TextMeshProUGUI BuildPropRow(Transform parent, string label)
        {
            var row = CreateUI($"PropRow_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 22f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Lbl", row.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 66f;
            var lbl           = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text          = $"{label}:";
            lbl.fontSize      = 10f;
            lbl.color         = TEXT_SECONDARY;
            lbl.alignment     = TextAlignmentOptions.MidlineLeft;
            lbl.raycastTarget = false;

            var valGo = CreateUI("Val", row.transform);
            valGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var val                  = valGo.AddComponent<TextMeshProUGUI>();
            val.text                 = "\u2014";  // em-dash placeholder
            val.fontSize             = 10f;
            val.color                = TEXT_PRIMARY;
            val.fontStyle            = FontStyles.Bold;
            val.alignment            = TextAlignmentOptions.MidlineLeft;
            val.enableWordWrapping   = false;
            val.raycastTarget        = false;
            return val;
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

        // ── Private Helpers ──────────────────────────────────────────────────────

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

        /// <summary>Creates a floating dropdown panel shell with header + content area + DraggablePanel.</summary>
        private static GameObject MakeDrop(
            string name, Transform canvasT,
            PanelDock dock, float xOff, float yOff, float width, float height,
            string title, out Transform contentOut, out DraggablePanel dragOut)
        {
            var go = CreateUI(name, canvasT);
            var r  = go.GetComponent<RectTransform>();
            ApplyDock(r, dock, xOff, yOff, width, height);

            var img           = go.AddComponent<Image>();
            img.color         = TileEditorTheme.PanelBg;
            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // Header
            var hdrGo          = CreateUI("PanelHeader", go.transform);
            var hdrRt          = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin        = new Vector2(0f, 1f);
            hdrRt.anchorMax        = new Vector2(1f, 1f);
            hdrRt.pivot            = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = Vector2.zero;
            hdrRt.sizeDelta        = new Vector2(0f, PANEL_HDR_H);

            var hdrImg          = hdrGo.AddComponent<Image>();
            hdrImg.color        = TileEditorTheme.HeaderBg;
            hdrImg.raycastTarget = true;

            var hdrHlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.padding             = new RectOffset(8, 8, 0, 0);
            hdrHlg.spacing             = 0f;
            hdrHlg.childForceExpandWidth  = false;
            hdrHlg.childForceExpandHeight = true;
            hdrHlg.childControlWidth      = true;
            hdrHlg.childControlHeight     = true;
            hdrHlg.childAlignment         = TextAnchor.MiddleLeft;

            var titleGo               = CreateUI("Title", hdrGo.transform);
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var titleTmp              = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text             = title;
            titleTmp.fontSize         = 10f;
            titleTmp.fontStyle        = FontStyles.Bold;
            titleTmp.color            = TileEditorTheme.HeaderTitle;
            titleTmp.characterSpacing = 1.5f;
            titleTmp.alignment        = TextAlignmentOptions.Left;
            titleTmp.enableWordWrapping = false;
            titleTmp.overflowMode     = TextOverflowModes.Truncate;
            titleTmp.raycastTarget    = false;

            // Separator
            var sepGo              = CreateUI("HdrSep", go.transform);
            var sepRt              = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin        = new Vector2(0f, 1f);
            sepRt.anchorMax        = new Vector2(1f, 1f);
            sepRt.pivot            = new Vector2(0f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -PANEL_HDR_H);
            sepRt.sizeDelta        = new Vector2(0f, 1f);
            var sepImg             = sepGo.AddComponent<Image>();
            sepImg.color           = TileEditorTheme.Separator;

            // Content area
            var contentGo     = CreateUI("Content", go.transform);
            var contentRt     = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(0f, 0f);
            contentRt.offsetMax = new Vector2(0f, -(PANEL_HDR_H + 1f));

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding             = new RectOffset(8, 8, 6, 6);
            layout.spacing             = 4f;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            contentGo.AddComponent<CanvasGroup>();

            // DraggablePanel
            var drag         = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;
            go.AddComponent<CanvasGroup>();

            // PanelChrome
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
                    r.anchorMin        = new Vector2(0f, 1f);
                    r.anchorMax        = new Vector2(0f, 1f);
                    r.pivot            = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
                case PanelDock.TopRight:
                    r.anchorMin        = new Vector2(1f, 1f);
                    r.anchorMax        = new Vector2(1f, 1f);
                    r.pivot            = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-xOff, -yOff);
                    break;
                default:
                    r.anchorMin        = new Vector2(0f, 1f);
                    r.anchorMax        = new Vector2(0f, 1f);
                    r.pivot            = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
            }
            r.sizeDelta = new Vector2(width, height);
        }

        private static Button AddActionBtn(Transform parent, string label, float height,
            Action onClick, bool danger = false)
        {
            var go  = CreateUI($"Btn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img = go.AddComponent<Image>();
            img.color = danger ? new Color(0.55f, 0.15f, 0.15f, 1f) : BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = danger ? new Color(0.55f, 0.15f, 0.15f, 1f) : BTN_NORMAL;
            c.highlightedColor = danger ? new Color(0.70f, 0.20f, 0.20f, 1f) : BTN_HOVER;
            c.pressedColor     = danger ? RED_ACCENT                           : BTN_ACTIVE;
            c.selectedColor    = c.normalColor;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            AddCenteredText(go.transform, label, 12f, FontStyles.Bold, TEXT_PRIMARY);
            return btn;
        }

        private static void AddArrowBtn(Transform parent, string arrow, Action onClick)
        {
            var go  = CreateUI($"ArrowBtn_{arrow}", parent);
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            AddCenteredText(go.transform, arrow, 16f, FontStyles.Bold, TEXT_PRIMARY);
        }

        private static GameObject MakeRow(string name, Transform parent, float height)
        {
            var go = CreateUI(name, parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            return go;
        }

        // Width of the scrollbar track pinned to the right edge of the zones list.
        // Matches TILES_SCROLLBAR_W from TileEditorUIHelpers for visual consistency.
        private const float ZONES_SCROLLBAR_W = 12f;

        /// <summary>
        /// Builds a scroll view with a permanent thin-gold vertical scrollbar that
        /// matches the style used in Tile Editor and Buildings Editor.
        /// </summary>
        private static GameObject MakeScrollView(string name, Transform parent,
            out RectTransform content, float minHeight = 200f)
        {
            var root   = CreateUI(name, parent);
            var rootImg = root.AddComponent<Image>();
            rootImg.color = new Color(0.06f, 0.06f, 0.08f, 1f);

            // Viewport leaves a gutter on the right for the scrollbar track.
            var viewport = CreateUI("Viewport", root.transform);
            var vpRt     = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = new Vector2(-ZONES_SCROLLBAR_W, 0f);
            viewport.AddComponent<RectMask2D>();

            // Content grows downward from the top of the viewport.
            var contentGo = CreateUI("Content", viewport.transform);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin        = new Vector2(0f, 1f);
            contentRt.anchorMax        = new Vector2(1f, 1f);
            contentRt.pivot            = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;
            content = contentRt;

            var vLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            vLayout.padding                = new RectOffset(4, 4, 4, 4);
            vLayout.spacing                = 3f;
            vLayout.childControlWidth      = true;
            vLayout.childControlHeight     = false;
            vLayout.childForceExpandWidth  = true;
            vLayout.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // ScrollRect — same sensitivity as TileEditor tile picker.
            var scroll             = root.AddComponent<ScrollRect>();
            scroll.viewport        = vpRt;
            scroll.content         = contentRt;
            scroll.horizontal      = false;
            scroll.vertical        = true;
            scroll.scrollSensitivity = 24f;
            scroll.movementType    = ScrollRect.MovementType.Clamped;

            // Gold scrollbar widget — same look as Tile / Buildings editors.
            AddZonesScrollbar(root.transform, scroll);

            return root;
        }

        /// <summary>
        /// Builds the thin permanent vertical scrollbar pinned to the right edge
        /// of the zone-list scroll container. Gold handle, dark track.
        /// </summary>
        private static void AddZonesScrollbar(Transform scrollRoot, ScrollRect scrollRect)
        {
            var sbGo = CreateUI("VScrollbar", scrollRoot);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin        = new Vector2(1f, 0f);
            sbRt.anchorMax        = new Vector2(1f, 1f);
            sbRt.pivot            = new Vector2(1f, 1f);
            sbRt.sizeDelta        = new Vector2(ZONES_SCROLLBAR_W, 0f);
            sbRt.anchoredPosition = Vector2.zero;
            sbGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.85f);

            var scrollbar       = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingArea = CreateUI("SlidingArea", sbGo.transform);
            var saRt        = slidingArea.GetComponent<RectTransform>();
            saRt.anchorMin  = Vector2.zero;
            saRt.anchorMax  = Vector2.one;
            saRt.offsetMin  = new Vector2(2f,  2f);
            saRt.offsetMax  = new Vector2(-2f, -2f);

            var handleGo    = CreateUI("Handle", slidingArea.transform);
            var hRt         = handleGo.GetComponent<RectTransform>();
            hRt.anchorMin   = Vector2.zero;
            hRt.anchorMax   = Vector2.one;
            hRt.offsetMin   = Vector2.zero;
            hRt.offsetMax   = Vector2.zero;
            var hImg        = handleGo.AddComponent<Image>();
            hImg.color      = new Color(0.55f, 0.45f, 0.22f, 0.85f);

            scrollbar.targetGraphic = hImg;
            scrollbar.handleRect    = hRt;

            var sbColors              = scrollbar.colors;
            sbColors.normalColor      = new Color(0.55f, 0.45f, 0.22f, 0.85f);
            sbColors.highlightedColor = new Color(0.75f, 0.62f, 0.30f, 0.95f);
            sbColors.pressedColor     = new Color(0.90f, 0.76f, 0.38f, 1f);
            scrollbar.colors          = sbColors;

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private static TMP_InputField MakeTmpInput(GameObject host, string placeholder)
        {
            var bg    = host.AddComponent<Image>();
            bg.color  = new Color(0.13f, 0.14f, 0.18f, 1f);

            var viewport = CreateUI("Viewport", host.transform);
            var vpRt     = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(8f, 4f);
            vpRt.offsetMax = new Vector2(-8f, -4f);
            viewport.AddComponent<RectMask2D>();

            var textGo  = CreateUI("Text", viewport.transform);
            var textRt  = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize  = 13f;
            textTmp.color     = TEXT_PRIMARY;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var phGo  = CreateUI("Placeholder", viewport.transform);
            var phRt  = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.sizeDelta = Vector2.zero;
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text      = placeholder;
            phTmp.fontSize  = 13f;
            phTmp.color     = new Color(0.55f, 0.58f, 0.65f, 0.75f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var input = host.AddComponent<TMP_InputField>();
            input.textViewport    = vpRt;
            input.textComponent   = textTmp;
            input.placeholder     = phTmp;
            input.lineType        = TMP_InputField.LineType.SingleLine;
            input.characterLimit  = 64;
            return input;
        }

        private static Toggle MakeToggle(Transform parent)
        {
            var root = CreateUI("Toggle", parent);
            root.AddComponent<LayoutElement>().preferredWidth = 28f;
            var rRt = root.GetComponent<RectTransform>();
            rRt.sizeDelta = new Vector2(28f, 28f);

            var bg    = CreateUI("Background", root.transform);
            var bgRt  = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.1f, 0.1f);
            bgRt.anchorMax = new Vector2(0.9f, 0.9f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.13f, 0.14f, 0.18f, 1f);

            var check   = CreateUI("Checkmark", bg.transform);
            var checkRt = check.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.2f, 0.2f);
            checkRt.anchorMax = new Vector2(0.8f, 0.8f);
            checkRt.offsetMin = Vector2.zero;
            checkRt.offsetMax = Vector2.zero;
            var checkImg = check.AddComponent<Image>();
            checkImg.color = new Color(0.40f, 0.88f, 0.40f, 1f);

            var toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic       = checkImg;
            return toggle;
        }
    }
}
