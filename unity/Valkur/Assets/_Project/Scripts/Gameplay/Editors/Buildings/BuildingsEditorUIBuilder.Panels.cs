using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Buildings
{
    public static partial class BuildingsEditorUIBuilder
    {

        /// <summary>Tab key that means "do not filter". Kept out of the enum on purpose.</summary>
        internal const string CATEGORY_ALL_KEY = "__all";

        /// <summary>
        /// Tabs per row in the category block. Three used to match the picker grid below it,
        /// but the taxonomy now declares 15 categories plus "All", and three per row stacks
        /// them 6 rows deep — 142 px of tab strip taken out of a 564 px panel, straight off
        /// the picker's own height. Four per row brings that back to 4 rows / 94 px and
        /// still leaves ~92 px per label of the panel's 368 px content width, above the
        /// 80 px floor BuildingsCategoryTabsIntegrationTests holds labels to.
        /// </summary>
        private const int CATEGORY_TAB_COLUMNS = 4;

        private static void BuildBuildingsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged,
            Action<string> onCategoryChanged)
        {
            float buildX = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.BuildingsDropdown = MakeDrop("BuildingsPanel", canvasT,
                PanelDock.TopLeft, buildX, PANEL_TOP_OFFSET,
                BUILDINGS_W, BUILDINGS_H, "Buildings", out var t, out refs.BuildingsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search buildings\u2026",
                v => onSearchChanged?.Invoke(v ?? ""));

            // Category tabs. 969 templates in one flat grid is unusable: 147 of them are
            // tree variants and another 101 are ground flora, so everything else is a long
            // scroll away. Search still runs across whatever the active tab shows, so
            // "All" plus a query behaves exactly as the picker did before the tabs existed.
            var catStrip = TabStrip.CreateWrapped(t, "BuildingCategoryTabStrip",
                CATEGORY_TAB_COLUMNS, rowHeight: 22f);
            catStrip.LabelFontSize = 10f;
            refs.CategoryTabStrip = catStrip;
            catStrip.AddTab(CATEGORY_ALL_KEY, "All", null);
            foreach (var cat in BuildingCategory.TabOrder)
                catStrip.AddTab(cat.ToString(), BuildingCategory.Label(cat), null);
            catStrip.TabChanged += (_, key) => onCategoryChanged?.Invoke(key);

            // Grid picker — needs an explicit LayoutElement so it fills the
            // remaining panel height inside the parent VerticalLayoutGroup
            // (otherwise the scroll collapses to 0 px and the grid is invisible).
            var (pickerScroll, pickerContent) = EditorUIHelpers.MakeGridPicker(
                t, "BuildingGrid", 3, 80f, 4f);
            var pickerLE = pickerScroll.gameObject.AddComponent<LayoutElement>();
            pickerLE.flexibleHeight = 1f;
            pickerLE.minHeight      = 200f;
            // Thin gold scrollbar (matches Tiles editor style).
            EditorUIHelpers.AddVerticalScrollbar(pickerScroll);
            refs.PickerContent     = pickerContent;
            refs.PickerScroll      = pickerScroll;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            BuildResizeGrip(refs.BuildingsDropdown, BUILDINGS_PANEL_MIN_SIZE, BUILDINGS_PANEL_MAX_SIZE);

            refs.BuildingsDropdown.SetActive(false);
        }

        // ── Resize grip ───────────────────────────────────────────────────────────

        private const float RESIZE_GRIP_PX = 16f;

        /// <summary>
        /// Smallest the Buildings panel may get. The width is what two picker columns plus
        /// the scrollbar need; below that the category tabs start wrapping their labels.
        /// </summary>
        private static readonly Vector2 BUILDINGS_PANEL_MIN_SIZE = new Vector2(232f, 300f);
        private static readonly Vector2 BUILDINGS_PANEL_MAX_SIZE = new Vector2(1600f, 1400f);

        /// <summary>
        /// Adds the triangular drag handle every other resizable editor panel carries
        /// (Tile, Items, Particles, Spells) to the bottom-right corner of a panel built by
        /// <see cref="MakeDrop"/>, which pivots top-left — the corner
        /// <see cref="PanelResizeHandle"/> expects.
        ///
        /// The size is persisted for free: <c>DraggablePanel.CaptureState</c> records
        /// <c>sizeDelta</c> and the workspace layer writes it with the rest of the layout.
        /// </summary>
        private static void BuildResizeGrip(GameObject panelRoot, Vector2 minSize, Vector2 maxSize)
        {
            if (panelRoot == null) return;
            var panelRt = panelRoot.GetComponent<RectTransform>();
            if (panelRt == null) return;

            var go = CreateUI("ResizeHandle", panelRoot.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(RESIZE_GRIP_PX, RESIZE_GRIP_PX);

            var tri = go.AddComponent<TriangleHandleGraphic>();
            tri.color         = TileEditorTheme.Border;
            tri.raycastTarget = true;

            var handle = go.AddComponent<PanelResizeHandle>();
            handle.Target  = panelRt;
            handle.MinSize = minSize;
            handle.MaxSize = maxSize;
        }

        // ── Colliders Panel ───────────────────────────────────────────────────────
        // Sits between Buildings and Properties. Provides:
        //   • Visibility toggle for the per-building collider overlay (red shapes).
        //   • Scope toggle (CG = shared by image / CU = unique to this instance).
        //   • Brush ON/OFF + Action (# Solid / . Walkable) + Size preset buttons (1–8) + stepper.
        //   • Grid resolution (cols × rows) — the topology the brush paints into.
        //   • Status (target id, scope, how many buildings it edits, grid size, dirty, brush).
        //   • Save Colliders.
        // Keyboard shortcuts (handled in BuildingsRuntimeEditor while panel is open):
        //   B  → toggle brush ON/OFF
        //   #  → set action = Solid
        //   .  → set action = Walkable
        //   [  → brush size −1     ]  → brush size +1
        //   Tab→ toggle scope CG ↔ CU

        private static void BuildCollidersPanel(Transform canvasT, ref UIRefs refs,
            Action onToggleVisible,
            Action onScopeToggle,
            Action onBrushPaint, Action onBrushErase,
            Action<int>  onBrushSizeChanged,
            Action       onBrushSizeStepDown,
            Action       onBrushSizeStepUp,
            Action onGridColsMinus, Action onGridColsPlus,
            Action onGridRowsMinus, Action onGridRowsPlus)
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

            // ── Action: # Solid / . Walkable (clicking the active action toggles it off) ──
            AddSectionLabel(t, "Brush Action");
            var actionRow = CreateUI("ActionRow", t);
            actionRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var ahlg = actionRow.AddComponent<HorizontalLayoutGroup>();
            ahlg.spacing                = 4f;
            ahlg.childForceExpandWidth  = true;
            ahlg.childForceExpandHeight = true;
            ahlg.childControlWidth      = true;
            ahlg.childControlHeight     = true;

            // Named after the cell state they write, which is also what the grid file and
            // the hint call them. The same action was "Erase" on the button, "Walk" in the
            // enum and "." in the data — three names for one thing.
            refs.CollPaintBtnImg = AddBrushActionBtn(actionRow.transform, "# Solid",    onBrushPaint);
            refs.CollEraseBtnImg = AddBrushActionBtn(actionRow.transform, ". Walkable", onBrushErase);

            // ── Brush size: preset buttons (1–8) + stepper (−/value/+), matching Tile Editor UX ──
            BuildSeparator(t);
            AddSectionLabel(t, "Brush Size [ / ]");
            BuildCollBrushSizePresetRow(t, ref refs, onBrushSizeChanged);
            BuildSeparator(t);
            BuildCollBrushSizeStepperRow(t, ref refs, onBrushSizeStepDown, onBrushSizeStepUp);

            // ── Collider grid resolution (moved here from Properties) ──
            // The topology every control above paints into. It edits the SHARED logical grid
            // for CG buildings (every instance of the same image gets the same N×M) or the
            // per-instance grid for CU.
            BuildSeparator(t);
            AddSectionLabel(t, "Grid Resolution");
            BuildZRow(t, "Cols", onGridColsMinus, onGridColsPlus, out refs.GridColsVal);
            BuildZRow(t, "Rows", onGridRowsMinus, onGridRowsPlus, out refs.GridRowsVal);

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

            // ── Hint text ──
            var hintGo = CreateUI("Hint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 64f;
            refs.CollHintText                     = hintGo.AddComponent<TextMeshProUGUI>();
            refs.CollHintText.text                =
                "LMB drags on the building to apply.\n" +
                "B  brush on/off   ·   [ ]  brush size   ·   Tab  scope\n" +
                "#  solid   ·   .  walkable   (click the active one to switch the brush off)";
            refs.CollHintText.fontSize            = 10f;
            refs.CollHintText.color               = TEXT_MUTED;
            refs.CollHintText.alignment           = TextAlignmentOptions.TopLeft;
            refs.CollHintText.enableWordWrapping  = true;

            BuildResizeGrip(refs.CollidersDropdown, COLLIDERS_PANEL_MIN_SIZE, COLLIDERS_PANEL_MAX_SIZE);

            refs.CollidersDropdown.SetActive(false);
        }

        /// <summary>Floor: the eight brush-size presets stop fitting on one row below this.</summary>
        private static readonly Vector2 COLLIDERS_PANEL_MIN_SIZE = new Vector2(200f, 320f);
        private static readonly Vector2 COLLIDERS_PANEL_MAX_SIZE = new Vector2(900f, 1400f);

        private const int CollBrushSizeMin = 1;
        private const int CollBrushSizeMax = 8;

        private static void BuildCollBrushSizePresetRow(Transform parent, ref UIRefs refs, Action<int> onChanged)
        {
            var row = CreateUI("CollSizePresetRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 32f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 3f;
            h.childForceExpandWidth  = true;
            h.childForceExpandHeight = true;
            h.childControlWidth      = true;
            h.childControlHeight     = true;
            h.padding = new RectOffset(2, 2, 0, 0);

            var imgs = refs.CollBrushSizePresetImgs;
            var lbls = refs.CollBrushSizePresetLabels;

            for (int i = CollBrushSizeMin; i <= CollBrushSizeMax; i++)
            {
                int size = i;
                var btnGo = CreateUI($"CollSize_{size}", row.transform);
                var img = btnGo.AddComponent<Image>();
                img.color = (size == 1) ? BTN_ACTIVE : BTN_NORMAL;

                var btn = btnGo.AddComponent<Button>();
                var c = btn.colors;
                c.normalColor      = img.color;
                c.highlightedColor = BTN_HOVER;
                c.pressedColor     = BTN_ACTIVE;
                c.selectedColor    = img.color;
                btn.colors = c;
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => onChanged?.Invoke(size));

                var lblGo  = CreateUI("Lbl", btnGo.transform);
                var lblRt  = lblGo.GetComponent<RectTransform>();
                lblRt.anchorMin = Vector2.zero;
                lblRt.anchorMax = Vector2.one;
                lblRt.offsetMin = Vector2.zero;
                lblRt.offsetMax = Vector2.zero;
                var tmp = lblGo.AddComponent<TextMeshProUGUI>();
                tmp.text         = size.ToString();
                tmp.fontSize     = 11f;
                tmp.fontStyle    = FontStyles.Bold;
                tmp.alignment    = TextAlignmentOptions.Center;
                tmp.color        = (size == 1) ? ACCENT : TEXT_SECONDARY;
                tmp.raycastTarget = false;

                imgs.Add(img);
                lbls.Add(tmp);
            }
        }

        private static void BuildCollBrushSizeStepperRow(Transform parent, ref UIRefs refs,
            Action onStepDown, Action onStepUp)
        {
            var row = CreateUI("CollSizeStepperRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing                = 4f;
            h.childForceExpandWidth  = false;
            h.childForceExpandHeight = true;
            h.childControlWidth      = true;
            h.childControlHeight     = true;
            h.padding = new RectOffset(2, 2, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;

            var lbl = CreateUI("LL", row.transform);
            lbl.AddComponent<LayoutElement>().preferredWidth = 44f;
            var lt = lbl.AddComponent<TextMeshProUGUI>();
            lt.text      = "Size";
            lt.fontSize  = 10f;
            lt.alignment = TextAlignmentOptions.Left;
            lt.color     = TEXT_MUTED;

            var minus = CreateUI("Minus", row.transform);
            minus.AddComponent<LayoutElement>().preferredWidth = 28f;
            var minusBtn = minus.AddComponent<Button>();
            var minusImg = minus.AddComponent<Image>();
            minusImg.color = BTN_NORMAL;
            minusBtn.targetGraphic = minusImg;
            AddCenteredText(minus.transform, "-", 12f, FontStyles.Bold, TEXT_PRIMARY);

            var val = CreateUI("Val", row.transform);
            val.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.CollBrushSizeLabel           = val.AddComponent<TextMeshProUGUI>();
            refs.CollBrushSizeLabel.text       = "1x1";
            refs.CollBrushSizeLabel.fontSize   = 13f;
            refs.CollBrushSizeLabel.fontStyle  = FontStyles.Bold;
            refs.CollBrushSizeLabel.alignment  = TextAlignmentOptions.Center;
            refs.CollBrushSizeLabel.color      = ACCENT;

            var plus = CreateUI("Plus", row.transform);
            plus.AddComponent<LayoutElement>().preferredWidth = 28f;
            var plusBtn = plus.AddComponent<Button>();
            var plusImg = plus.AddComponent<Image>();
            plusImg.color = BTN_NORMAL;
            plusBtn.targetGraphic = plusImg;
            AddCenteredText(plus.transform, "+", 12f, FontStyles.Bold, TEXT_PRIMARY);

            minusBtn.onClick.AddListener(() => onStepDown?.Invoke());
            plusBtn.onClick.AddListener(()  => onStepUp?.Invoke());
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

    }
}