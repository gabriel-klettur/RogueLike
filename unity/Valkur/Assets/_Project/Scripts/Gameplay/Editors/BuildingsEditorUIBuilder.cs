using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Builds all UI panels for the Buildings Runtime Editor using the same
    /// professional menu-bar + floating-panel architecture as the Tile Editor (F8).
    ///
    /// Layout mirrors TileEditor exactly:
    ///   • 30 px menu bar at top   — brand + dropdown buttons + tutorial shortcut
    ///   • Modes panel  (60 px, top-left)  ≡ TileEditor "Tools" panel
    ///   • Buildings panel (256 px, next)  ≡ TileEditor "Tiles" panel
    ///   • Properties panel (250 px, right) ≡ TileEditor "Inspector" panel
    ///
    /// All floating panels use DraggablePanel + PanelChrome so they participate
    /// in the shared TileEditorTheme live-repaint system.
    /// </summary>
    public static class BuildingsEditorUIBuilder
    {
        // ── UIRefs ────────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject      MenuBar;
            public Image           ModesMenuBtnImg;     public TextMeshProUGUI ModesMenuBtnTmp;
            public Image           BuildingsMenuBtnImg; public TextMeshProUGUI BuildingsMenuBtnTmp;
            public Image           CollidersMenuBtnImg; public TextMeshProUGUI CollidersMenuBtnTmp;
            public Image           PropsMenuBtnImg;     public TextMeshProUGUI PropsMenuBtnTmp;

            // Panel roots + drag components
            public GameObject    ModesDropdown;     public DraggablePanel ModesPanelDrag;
            public GameObject    BuildingsDropdown; public DraggablePanel BuildingsPanelDrag;
            public GameObject    CollidersDropdown; public DraggablePanel CollidersPanelDrag;
            public GameObject    PropsDropdown;     public DraggablePanel PropsPanelDrag;

            // Modes panel refs
            public Image SelectBtnImg, PlaceBtnImg, ResizeBtnImg, DeleteBtnImg;
            public Image AddBtnImg, RemoveBtnImg;

            // Buildings panel refs
            public TMP_InputField  SearchBox;
            public RectTransform   PickerContent;
            public TextMeshProUGUI StatusText;

            // Properties panel refs
            public TextMeshProUGUI PropsText;       // hint when idle OR rich-text building info
            public GameObject      InspectorRoot;   // hidden until a building is selected
            public Slider          SplitSlider;
            public TextMeshProUGUI ZBottomVal, ZTopVal;
            public Image           ScopeBtnImg;
            public TextMeshProUGUI ScopeBtnLabel;

            // Colliders panel refs (redesigned: ON/OFF toggle + Paint/Erase action + scope + size).
            public Image           CollVisibilityBtnImg;   public TextMeshProUGUI CollVisibilityBtnLabel;
            public Image           CollScopeBtnImg;        public TextMeshProUGUI CollScopeBtnLabel;
            public Image           CollBrushToggleImg;     public TextMeshProUGUI CollBrushToggleLabel;
            public Image           CollPaintBtnImg;        // # action button
            public Image           CollEraseBtnImg;        // . action button
            public Slider          CollBrushSizeSlider;
            public TextMeshProUGUI CollBrushSizeVal;
            public TextMeshProUGUI CollTargetText;         // "ID 142 | Scope CG\nimage:..."
            public TextMeshProUGUI CollStateText;          // "Grid 8x6 | Solids 12 | Dirty | ON #"
            public TextMeshProUGUI CollHintText;
        }

        // ── Panel sizes (mirrors TileEditor constants) ────────────────────────────

        private const float MODES_W     = TOOLS_DROP_W;          // 60 px
        private const float MODES_H     = TOOLS_DROP_H;          // 484 px
        private const float BUILDINGS_W = TILES_DROP_W;          // 256 px
        private const float BUILDINGS_H = TILES_DROP_H;          // 564 px
        private const float COLLIDERS_W = 220f;                  // narrower than props
        private const float COLLIDERS_H = 470f + PANEL_HDR_H;
        private const float PROPS_W     = INSPECTOR_DROP_W;      // 250 px
        private const float PROPS_H     = 400f + PANEL_HDR_H;    // 424 px

        // ── Menu button widths ─────────────────────────────────────────────────

        private const float TITLE_BTN_W     = 145f;
        private const float MODES_BTN_W     = 70f;
        private const float BUILDINGS_BTN_W = 92f;
        private const float COLLIDERS_BTN_W = 92f;
        private const float PROPS_BTN_W     = 98f;
        private const float TUTORIAL_BTN_W  = 40f;

        private const float BTN_H = 44f;   // mode/tool button height (same as TileEditor)

        // ── BuildAll ─────────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,         Action onRedo,
            Action         onSave,         Action onReload,
            Action         onModeSelect,   Action onModePlace,
            Action         onModeResize,   Action onModeDelete,
            Action         onAddBuilding,  Action onRemoveBuilding, Action onAddOnSystem,
            Action         onToggleTutorial,
            Action<string> onSearchChanged,
            Action<float>  onSplitChanged,
            Action         onZBottomMinus, Action onZBottomPlus,
            Action         onZTopMinus,    Action onZTopPlus,
            Action         onColliderScope,
            Action         onPaintSolid,   Action onPaintWalk, Action onSaveCU,
            Action         onDeleteBuilding,
            Action         onResetBuilding,
            // Colliders panel callbacks (redesigned)
            Action         onToggleCollidersVisible,
            Action         onCollScopeToggle,
            Action         onBrushToggle,                 // B → toggle brush ON/OFF
            Action         onBrushPaint,                  // # → action = Paint
            Action         onBrushErase,                  // . → action = Erase
            Action<float>  onCollBrushSizeChanged,
            Action         onCollSave)
        {
            // Reserve space below the menu bar so draggable panels cannot occlude it
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();
            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial);
            BuildModesPanel(canvasT, ref refs,
                onModeSelect, onModePlace, onModeResize, onModeDelete,
                onAddBuilding, onRemoveBuilding, onAddOnSystem,
                onUndo, onRedo, onSave, onReload);
            BuildBuildingsPanel(canvasT, ref refs, onSearchChanged);
            BuildCollidersPanel(canvasT, ref refs,
                onToggleCollidersVisible,
                onCollScopeToggle,
                onBrushToggle, onBrushPaint, onBrushErase,
                onCollBrushSizeChanged, onCollSave);
            BuildPropertiesPanel(canvasT, ref refs, onSplitChanged,
                onZBottomMinus, onZBottomPlus, onZTopMinus, onZTopPlus,
                onColliderScope, onPaintSolid, onPaintWalk, onSaveCU, onDeleteBuilding, onResetBuilding);
            return refs;
        }

        // ── Public helper: menu-button highlight ──────────────────────────────────

        /// <summary>
        /// Called by BuildingsRuntimeEditor to reflect open/closed dropdown state
        /// on the corresponding menu bar button (mirrors TileEditorUI.ApplyMenuBtnStyle).
        /// </summary>
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
            var go = CreateUI("BuildingsMenuBar", canvasT);
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
            brandTmp.text             = "BUILDINGS EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ModesMenuBtnImg     = AddMenuBtn(t, "Modes \u25be",      MODES_BTN_W,
                () => onToggle?.Invoke("modes"),     out refs.ModesMenuBtnTmp);
            refs.BuildingsMenuBtnImg = AddMenuBtn(t, "Buildings \u25be",  BUILDINGS_BTN_W,
                () => onToggle?.Invoke("buildings"), out refs.BuildingsMenuBtnTmp);
            refs.CollidersMenuBtnImg = AddMenuBtn(t, "Colliders \u25be",  COLLIDERS_BTN_W,
                () => onToggle?.Invoke("colliders"), out refs.CollidersMenuBtnTmp);
            refs.PropsMenuBtnImg     = AddMenuBtn(t, "Properties \u25be", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),     out refs.PropsMenuBtnTmp);

            // Flexible spacer
            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
        }

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

        // ── Modes Panel ───────────────────────────────────────────────────────────
        // 60 px wide (same as TileEditor Tools). Narrow icon-style buttons.
        // Contains: mode selection, add/remove, undo/redo, save/reload.

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onSelect, Action onPlace, Action onResize, Action onDelete,
            Action onAdd,    Action onRemove, Action onAddSystem,
            Action onUndo,   Action onRedo,
            Action onSave,   Action onReload)
        {
            refs.ModesDropdown = MakeDrop("ModesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "", out var t, out refs.ModesPanelDrag, narrowPanel: true);

            refs.SelectBtnImg = AddToolBtn(t, "Sel", "S", BTN_H, onSelect);
            refs.PlaceBtnImg  = AddToolBtn(t, "Plc", "P", BTN_H, onPlace);
            refs.ResizeBtnImg = AddToolBtn(t, "Siz", "R", BTN_H, onResize);
            refs.DeleteBtnImg = AddDangerToolBtn(t, "Del", "D", BTN_H, onDelete);
            BuildSeparator(t);

            refs.AddBtnImg    = AddToolBtn(t, "+",       "Add", BTN_H, onAdd);
            refs.RemoveBtnImg = AddDangerToolBtn(t, "\u2212", "Rem", BTN_H, onRemove);
            AddToolBtn(t, "+S", "Sys", BTN_H, onAddSystem);
            BuildSeparator(t);

            AddActionBtn(t, "Undo", BTN_H, onUndo);
            AddActionBtn(t, "Redo", BTN_H, onRedo);
            BuildSeparator(t);

            AddActionBtn(t, "Save", BTN_H, onSave);
            AddActionBtn(t, "Rld",  BTN_H, onReload);

            refs.ModesDropdown.SetActive(false);
        }

        // icon-style tool button (same pattern as TileEditor CreateToolBtn)
        private static Image AddToolBtn(Transform parent, string label, string sub,
            float height, Action onClick)
        {
            var go = CreateUI($"ToolBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

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

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment         = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth      = true;
            vl.childControlHeight     = true;
            vl.spacing                = 0f;
            vl.padding                = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var lblTmp       = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text      = label;
            lblTmp.fontSize  = 10f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color     = TEXT_PRIMARY;

            if (!string.IsNullOrEmpty(sub))
            {
                var subGo = CreateUI("Sub", go.transform);
                subGo.AddComponent<LayoutElement>().preferredHeight = 10f;
                var subTmp       = subGo.AddComponent<TextMeshProUGUI>();
                subTmp.text      = sub;
                subTmp.fontSize  = 8f;
                subTmp.alignment = TextAlignmentOptions.Center;
                subTmp.color     = TEXT_MUTED;
            }
            return img;
        }

        private static Image AddDangerToolBtn(Transform parent, string label, string sub,
            float height, Action onClick)
        {
            var img        = AddToolBtn(parent, label, sub, height, onClick);
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

        private static void AddActionBtn(Transform parent, string label, float height, Action onClick)
        {
            var go = CreateUI($"Act_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

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

            var tmp       = AddCenteredText(go.transform, label, 9f, FontStyles.Bold, TEXT_SECONDARY);
            tmp.alignment = TextAlignmentOptions.Center;
        }

        // ── Buildings Panel ───────────────────────────────────────────────────────
        // 256 px wide (same as TileEditor Tiles). Search + 3-column grid picker.

        private static void BuildBuildingsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float buildX = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.BuildingsDropdown = MakeDrop("BuildingsPanel", canvasT,
                PanelDock.TopLeft, buildX, PANEL_TOP_OFFSET,
                BUILDINGS_W, BUILDINGS_H, "Buildings", out var t, out refs.BuildingsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search buildings\u2026",
                v => onSearchChanged?.Invoke(v ?? ""));

            var (_, pickerContent) = EditorUIHelpers.MakeGridPicker(t, "BuildingGrid", 3, 80f, 4f);
            refs.PickerContent     = pickerContent;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            refs.BuildingsDropdown.SetActive(false);
        }

        // ── Colliders Panel ───────────────────────────────────────────────────────
        // Sits between Buildings and Properties. Provides:
        //   • Visibility toggle for the per-building collider overlay (red shapes).
        //   • Scope toggle (CG = shared by image / CU = unique to this instance).
        //   • Brush ON/OFF + Action (# Paint / . Erase) + Size slider [1..8].
        //   • Status (target id, scope, grid size, dirty flag, brush state).
        //   • Save Colliders.
        // Keyboard shortcuts (handled in BuildingsRuntimeEditor while panel is open):
        //   B  → toggle brush ON/OFF
        //   #  → set action = Paint
        //   .  → set action = Erase
        //   [  → brush size −1     ]  → brush size +1
        //   Tab→ toggle scope CG ↔ CU

        private static void BuildCollidersPanel(Transform canvasT, ref UIRefs refs,
            Action onToggleVisible,
            Action onScopeToggle,
            Action onBrushToggle, Action onBrushPaint, Action onBrushErase,
            Action<float> onBrushSizeChanged,
            Action onSave)
        {
            float collX = PANEL_GAP + MODES_W + PANEL_GAP + BUILDINGS_W + PANEL_GAP;
            refs.CollidersDropdown = MakeDrop("CollidersPanel", canvasT,
                PanelDock.TopLeft, collX, PANEL_TOP_OFFSET,
                COLLIDERS_W, COLLIDERS_H, "Colliders", out var t, out refs.CollidersPanelDrag);

            // ── Visibility toggle ──
            BuildSeparator(t);
            AddSectionLabel(t, "Visibility");
            (refs.CollVisibilityBtnImg, refs.CollVisibilityBtnLabel) =
                AddFullWidthBtn(t, "Show Colliders", 30f, onToggleVisible);

            // ── Scope toggle (CG / CU) ──
            BuildSeparator(t);
            AddSectionLabel(t, "Scope (Tab)");
            (refs.CollScopeBtnImg, refs.CollScopeBtnLabel) =
                AddFullWidthBtn(t, "Scope: --", 30f, onScopeToggle);

            // ── Brush ON / OFF ──
            BuildSeparator(t);
            AddSectionLabel(t, "Brush (B)");
            (refs.CollBrushToggleImg, refs.CollBrushToggleLabel) =
                AddFullWidthBtn(t, "Brush: OFF", 30f, onBrushToggle);

            // ── Action: # Paint / . Erase ──
            AddSectionLabel(t, "Action");
            var actionRow = CreateUI("ActionRow", t);
            actionRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var ahlg = actionRow.AddComponent<HorizontalLayoutGroup>();
            ahlg.spacing                = 4f;
            ahlg.childForceExpandWidth  = true;
            ahlg.childForceExpandHeight = true;
            ahlg.childControlWidth      = true;
            ahlg.childControlHeight     = true;

            refs.CollPaintBtnImg = AddBrushActionBtn(actionRow.transform, "# Paint", onBrushPaint);
            refs.CollEraseBtnImg = AddBrushActionBtn(actionRow.transform, ". Erase", onBrushErase);

            // ── Brush size slider [1..8] ──
            BuildSeparator(t);
            var sizeRow = CreateUI("SizeRow", t);
            sizeRow.AddComponent<LayoutElement>().preferredHeight = 18f;
            var srhlg = sizeRow.AddComponent<HorizontalLayoutGroup>();
            srhlg.spacing                = 4f;
            srhlg.childForceExpandWidth  = false;
            srhlg.childForceExpandHeight = true;
            srhlg.childControlWidth      = true;
            srhlg.childControlHeight     = true;

            var sizeLblGo = CreateUI("Lbl", sizeRow.transform);
            sizeLblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var sizeLblTmp = sizeLblGo.AddComponent<TextMeshProUGUI>();
            sizeLblTmp.text      = "Brush size [ / ]";
            sizeLblTmp.fontSize  = 10f;
            sizeLblTmp.color     = TEXT_SECONDARY;
            sizeLblTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var sizeValGo = CreateUI("Val", sizeRow.transform);
            sizeValGo.AddComponent<LayoutElement>().preferredWidth = 30f;
            refs.CollBrushSizeVal           = sizeValGo.AddComponent<TextMeshProUGUI>();
            refs.CollBrushSizeVal.text      = "1";
            refs.CollBrushSizeVal.fontSize  = 11f;
            refs.CollBrushSizeVal.alignment = TextAlignmentOptions.MidlineRight;
            refs.CollBrushSizeVal.color     = TEXT_PRIMARY;

            var sliderGo = CreateUI("BrushSizeSlider", t);
            sliderGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            refs.CollBrushSizeSlider = sliderGo.AddComponent<Slider>();
            var sBg = CreateUI("Bg", sliderGo.transform);
            StretchFill(sBg);
            sBg.AddComponent<Image>().color = BG_SURFACE;
            var sFillArea = CreateUI("FillArea", sliderGo.transform);
            var sFaRt     = sFillArea.GetComponent<RectTransform>();
            sFaRt.anchorMin = new Vector2(0f, 0.25f);
            sFaRt.anchorMax = new Vector2(1f, 0.75f);
            sFaRt.offsetMin = new Vector2(6f, 0f);
            sFaRt.offsetMax = new Vector2(-6f, 0f);
            var sFillGo = CreateUI("Fill", sFillArea.transform);
            StretchFill(sFillGo);
            sFillGo.AddComponent<Image>().color = ACCENT;
            refs.CollBrushSizeSlider.fillRect     = sFillGo.GetComponent<RectTransform>();
            refs.CollBrushSizeSlider.minValue     = 1f;
            refs.CollBrushSizeSlider.maxValue     = 8f;
            refs.CollBrushSizeSlider.wholeNumbers = true;
            refs.CollBrushSizeSlider.value        = 1f;
            if (onBrushSizeChanged != null)
                refs.CollBrushSizeSlider.onValueChanged.AddListener(v => onBrushSizeChanged(v));

            // ── Status texts ──
            BuildSeparator(t);
            var targetGo = CreateUI("CollTarget", t);
            targetGo.AddComponent<LayoutElement>().preferredHeight = 28f;
            refs.CollTargetText                     = targetGo.AddComponent<TextMeshProUGUI>();
            refs.CollTargetText.text                = "No building selected.";
            refs.CollTargetText.fontSize            = 10f;
            refs.CollTargetText.color               = TEXT_PRIMARY;
            refs.CollTargetText.alignment           = TextAlignmentOptions.TopLeft;
            refs.CollTargetText.enableWordWrapping  = true;

            var stateGo = CreateUI("CollState", t);
            stateGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            refs.CollStateText                      = stateGo.AddComponent<TextMeshProUGUI>();
            refs.CollStateText.text                 = "Grid: -- | Brush OFF";
            refs.CollStateText.fontSize             = 9f;
            refs.CollStateText.color                = TEXT_MUTED;
            refs.CollStateText.alignment            = TextAlignmentOptions.TopLeft;
            refs.CollStateText.enableWordWrapping   = true;

            // ── Save action ──
            BuildSeparator(t);
            EditorUIHelpers.MakeButton(t, "Save Colliders", () => onSave?.Invoke(), 30f, 11f);

            // ── Hint text ──
            var hintGo = CreateUI("Hint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 64f;
            refs.CollHintText                     = hintGo.AddComponent<TextMeshProUGUI>();
            refs.CollHintText.text                =
                "B brush · # paint · . erase · [ ] size · Tab scope. LMB on the building to apply.";
            refs.CollHintText.fontSize            = 9f;
            refs.CollHintText.color               = TEXT_MUTED;
            refs.CollHintText.alignment           = TextAlignmentOptions.TopLeft;
            refs.CollHintText.enableWordWrapping  = true;

            refs.CollidersDropdown.SetActive(false);
        }

        private static void AddSectionLabel(Transform parent, string text)
        {
            var go = CreateUI($"Lbl_{text}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 16f;
            var tmp       = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 10f;
            tmp.color     = TEXT_SECONDARY;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static (Image img, TextMeshProUGUI label) AddFullWidthBtn(
            Transform parent, string label, float height, Action onClick)
        {
            var go = CreateUI($"Btn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor = BTN_NORMAL; c.highlightedColor = BTN_HOVER; c.pressedColor = BTN_ACTIVE;
            btn.colors = c; btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());
            var tmp = AddCenteredText(go.transform, label, 11f, FontStyles.Bold, TEXT_PRIMARY);
            return (img, tmp);
        }

        private static Image AddBrushActionBtn(Transform parent, string label, Action onClick)
        {
            var go = CreateUI($"ActionBtn_{label}", parent);
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor = BTN_NORMAL; c.highlightedColor = BTN_HOVER; c.pressedColor = BTN_ACTIVE;
            btn.colors = c; btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());
            var tmp       = AddCenteredText(go.transform, label, 10f, FontStyles.Bold, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        // ── Properties Panel ──────────────────────────────────────────────────────
        // 250 px wide (same as TileEditor Inspector). Building info + inspector controls.

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs,
            Action<float> onSplitChanged,
            Action onZBottomMinus, Action onZBottomPlus,
            Action onZTopMinus,    Action onZTopPlus,
            Action onScope,
            Action onPaintSolid, Action onPaintWalk, Action onSaveCU,
            Action onDelete, Action onReset)
        {
            refs.PropsDropdown = MakeDrop("PropertiesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties", out var t, out refs.PropsPanelDrag);

            // Hint / rich-text (dual-purpose: hint when idle, building info when active)
            var propsGo = CreateUI("PropsText", t);
            propsGo.AddComponent<LayoutElement>().preferredHeight = 100f;
            refs.PropsText                 = propsGo.AddComponent<TextMeshProUGUI>();
            refs.PropsText.text            = "Select a building\nto view properties.";
            refs.PropsText.fontSize        = 11f;
            refs.PropsText.color           = TEXT_SECONDARY;
            refs.PropsText.alignment       = TextAlignmentOptions.TopLeft;
            refs.PropsText.enableWordWrapping = true;

            // Inspector controls root (hidden until a building is selected)
            refs.InspectorRoot = CreateUI("InspectorRoot", t);
            var inspVlg = refs.InspectorRoot.AddComponent<VerticalLayoutGroup>();
            inspVlg.childForceExpandWidth  = true;
            inspVlg.childForceExpandHeight = false;
            inspVlg.childControlWidth      = true;
            inspVlg.childControlHeight     = true;
            inspVlg.spacing                = 4f;
            refs.InspectorRoot.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            BuildInspectorControls(refs.InspectorRoot.transform, ref refs,
                onSplitChanged,
                onZBottomMinus, onZBottomPlus, onZTopMinus, onZTopPlus,
                onScope, onPaintSolid, onPaintWalk, onSaveCU, onDelete, onReset);

            refs.InspectorRoot.SetActive(false);
            refs.PropsDropdown.SetActive(false);
        }

        private static void BuildInspectorControls(Transform parent, ref UIRefs refs,
            Action<float> onSplitChanged,
            Action onZBottomMinus, Action onZBottomPlus,
            Action onZTopMinus,    Action onZTopPlus,
            Action onScope,
            Action onPaintSolid, Action onPaintWalk, Action onSaveCU,
            Action onDelete, Action onReset)
        {
            BuildSeparator(parent);

            // Split ratio label
            var splitLbl       = CreateUI("SplitLbl", parent);
            splitLbl.AddComponent<LayoutElement>().preferredHeight = 18f;
            var splitLblTmp    = splitLbl.AddComponent<TextMeshProUGUI>();
            splitLblTmp.text   = "Split ratio";
            splitLblTmp.fontSize = 10f;
            splitLblTmp.color  = TEXT_SECONDARY;

            // Split slider
            var sliderGo = CreateUI("SplitSlider", parent);
            sliderGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            refs.SplitSlider    = sliderGo.AddComponent<Slider>();
            var bg              = CreateUI("Bg", sliderGo.transform);
            StretchFill(bg);
            bg.AddComponent<Image>().color = BG_SURFACE;
            var fillArea        = CreateUI("FillArea", sliderGo.transform);
            var faRt            = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin      = new Vector2(0f, 0.25f);
            faRt.anchorMax      = new Vector2(1f, 0.75f);
            faRt.offsetMin      = new Vector2(6f, 0f);
            faRt.offsetMax      = new Vector2(-6f, 0f);
            var fillGo          = CreateUI("Fill", fillArea.transform);
            StretchFill(fillGo);
            fillGo.AddComponent<Image>().color = ACCENT;
            refs.SplitSlider.fillRect  = fillGo.GetComponent<RectTransform>();
            refs.SplitSlider.minValue  = 0.05f;
            refs.SplitSlider.maxValue  = 0.95f;
            refs.SplitSlider.value     = 0.5f;
            if (onSplitChanged != null)
                refs.SplitSlider.onValueChanged.AddListener(v => onSplitChanged(v));

            // Z rows
            BuildZRow(parent, "Z-Bottom", onZBottomMinus, onZBottomPlus, out refs.ZBottomVal);
            BuildZRow(parent, "Z-Top",    onZTopMinus,    onZTopPlus,    out refs.ZTopVal);

            // Collider scope
            BuildSeparator(parent);
            var scopeRow = CreateUI("ScopeRow", parent);
            scopeRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var srhlg = scopeRow.AddComponent<HorizontalLayoutGroup>();
            srhlg.spacing             = 4f;
            srhlg.childForceExpandWidth  = false;
            srhlg.childForceExpandHeight = true;
            srhlg.childControlWidth      = true;
            srhlg.childControlHeight     = true;

            var scopeLblGo    = CreateUI("ScopeLbl", scopeRow.transform);
            scopeLblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var scopeLblTmp       = scopeLblGo.AddComponent<TextMeshProUGUI>();
            scopeLblTmp.text      = "Collider scope";
            scopeLblTmp.fontSize  = 10f;
            scopeLblTmp.color     = TEXT_SECONDARY;
            scopeLblTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var scopeBtn           = CreateUI("ScopeBtn", scopeRow.transform);
            scopeBtn.AddComponent<LayoutElement>().preferredWidth = 44f;
            refs.ScopeBtnImg       = scopeBtn.AddComponent<Image>();
            refs.ScopeBtnImg.color = BTN_NORMAL;
            var sbtn               = scopeBtn.AddComponent<Button>();
            var sc                 = sbtn.colors;
            sc.normalColor = BTN_NORMAL; sc.highlightedColor = BTN_HOVER; sc.pressedColor = BTN_ACTIVE;
            sbtn.colors = sc; sbtn.targetGraphic = refs.ScopeBtnImg;
            if (onScope != null) sbtn.onClick.AddListener(() => onScope.Invoke());
            refs.ScopeBtnLabel = AddCenteredText(scopeBtn.transform, "CG", 10f, FontStyles.Bold, TEXT_PRIMARY);

            // Colliders paint (Phase 2 placeholder)
            BuildSeparator(parent);
            var paintLbl       = CreateUI("PaintLbl", parent);
            paintLbl.AddComponent<LayoutElement>().preferredHeight = 16f;
            var paintLblTmp    = paintLbl.AddComponent<TextMeshProUGUI>();
            paintLblTmp.text   = "Colliders paint (Phase 2)";
            paintLblTmp.fontSize = 9f;
            paintLblTmp.color  = TEXT_MUTED;

            var paintRow = CreateUI("PaintRow", parent);
            paintRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var phlg = paintRow.AddComponent<HorizontalLayoutGroup>();
            phlg.spacing = 4f; phlg.childForceExpandWidth = true; phlg.childForceExpandHeight = false;
            EditorUIHelpers.MakeButton(paintRow.transform, "# Solid", () => onPaintSolid?.Invoke(), 26f, 9f);
            EditorUIHelpers.MakeButton(paintRow.transform, ". Walk",  () => onPaintWalk?.Invoke(),  26f, 9f);
            EditorUIHelpers.MakeButton(paintRow.transform, "Save CU", () => onSaveCU?.Invoke(),     26f, 9f);

            // Delete building (danger) + Reset building
            BuildSeparator(parent);
            var actionRow = CreateUI("DeleteResetRow", parent);
            actionRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            var arhlg = actionRow.AddComponent<HorizontalLayoutGroup>();
            arhlg.spacing = 4f; arhlg.childForceExpandWidth = true; arhlg.childForceExpandHeight = false;
            EditorUIHelpers.MakeDangerButton(actionRow.transform, "Delete Building",
                () => onDelete?.Invoke(), 32f);
            EditorUIHelpers.MakeButton(actionRow.transform, "Reset",
                () => onReset?.Invoke(), 32f, 10f);
        }

        private static void BuildZRow(Transform parent, string label,
            Action onMinus, Action onPlus, out TextMeshProUGUI outVal)
        {
            var row = CreateUI($"{label}Row", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lbl          = CreateUI("Lbl", row.transform);
            lbl.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var lblTmp       = lbl.AddComponent<TextMeshProUGUI>();
            lblTmp.text      = label; lblTmp.fontSize = 10f; lblTmp.color = TEXT_SECONDARY;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var minGo = CreateUI("Minus", row.transform);
            minGo.AddComponent<LayoutElement>().preferredWidth = 24f;
            AddSmallBtn(minGo, "\u2212", onMinus);

            var valGo        = CreateUI("Val", row.transform);
            valGo.AddComponent<LayoutElement>().preferredWidth = 38f;
            outVal           = valGo.AddComponent<TextMeshProUGUI>();
            outVal.text      = "0"; outVal.fontSize = 11f;
            outVal.alignment = TextAlignmentOptions.Center; outVal.color = TEXT_PRIMARY;

            var plusGo = CreateUI("Plus", row.transform);
            plusGo.AddComponent<LayoutElement>().preferredWidth = 24f;
            AddSmallBtn(plusGo, "+", onPlus);
        }

        private static void AddSmallBtn(GameObject go, string label, Action onClick)
        {
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor = BTN_NORMAL; c.highlightedColor = BTN_HOVER; c.pressedColor = BTN_ACTIVE;
            btn.colors = c; btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());
            AddCenteredText(go.transform, label, 11f, FontStyles.Bold, TEXT_PRIMARY);
        }

        // ── MakeDrop — floating panel factory ────────────────────────────────────
        // Exact copy of TileEditorUIBuilder.MakeDropdownPanel with qualified CreateUI calls.

        private static GameObject MakeDrop(
            string name, Transform canvasT,
            PanelDock dock, float xOff, float yOff, float width, float height,
            string title, out Transform contentOut, out DraggablePanel dragOut,
            bool narrowPanel = false)
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
            hdrHlg.spacing             = 0f;
            hdrHlg.childForceExpandWidth  = false;
            hdrHlg.childForceExpandHeight = true;
            hdrHlg.childControlWidth      = true;
            hdrHlg.childControlHeight     = true;
            hdrHlg.childAlignment         = TextAnchor.MiddleLeft;

            TextMeshProUGUI titleTmp = null;
            if (!narrowPanel)
            {
                hdrHlg.padding = new RectOffset(8, 8, 0, 0);
                var titleGo               = CreateUI("Title", hdrGo.transform);
                titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                titleTmp                  = titleGo.AddComponent<TextMeshProUGUI>();
                titleTmp.text             = title.ToUpper();
                titleTmp.fontSize         = 10f;
                titleTmp.fontStyle        = FontStyles.Bold;
                titleTmp.color            = TileEditorTheme.HeaderTitle;
                titleTmp.characterSpacing = 1.5f;
                titleTmp.alignment        = TextAlignmentOptions.Left;
                titleTmp.enableWordWrapping = false;
                titleTmp.overflowMode     = TextOverflowModes.Truncate;
                titleTmp.raycastTarget    = false;
            }

            // Separator between header and content
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

            // PanelChrome — participates in TileEditorTheme live-repaint
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

        // Mirrors ApplyDock from TileEditorUIBuilder.LeftPanel.cs exactly.
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
    }
}
