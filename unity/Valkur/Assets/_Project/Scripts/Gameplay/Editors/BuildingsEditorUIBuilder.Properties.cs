using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Buildings
{
    public static partial class BuildingsEditorUIBuilder
    {

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs,
            Action<float> onSplitChanged,
            Action onZBottomMinus, Action onZBottomPlus,
            Action onZTopMinus,    Action onZTopPlus,
            Action onGridColsMinus, Action onGridColsPlus,
            Action onGridRowsMinus, Action onGridRowsPlus,
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
                onGridColsMinus, onGridColsPlus, onGridRowsMinus, onGridRowsPlus,
                onScope, onPaintSolid, onPaintWalk, onSaveCU, onDelete, onReset);

            refs.InspectorRoot.SetActive(false);
            refs.PropsDropdown.SetActive(false);
        }

        private static void BuildInspectorControls(Transform parent, ref UIRefs refs,
            Action<float> onSplitChanged,
            Action onZBottomMinus, Action onZBottomPlus,
            Action onZTopMinus,    Action onZTopPlus,
            Action onGridColsMinus, Action onGridColsPlus,
            Action onGridRowsMinus, Action onGridRowsPlus,
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

            // Collider grid resolution (cols × rows). Edits the SHARED logical
            // grid for CG buildings (every instance of the same image gets the
            // same N×M topology) or the per-instance grid for CU buildings.
            BuildSeparator(parent);
            var gridLbl       = CreateUI("GridLbl", parent);
            gridLbl.AddComponent<LayoutElement>().preferredHeight = 18f;
            var gridLblTmp    = gridLbl.AddComponent<TextMeshProUGUI>();
            gridLblTmp.text   = "Collider grid resolution";
            gridLblTmp.fontSize = 10f;
            gridLblTmp.color  = TEXT_SECONDARY;
            BuildZRow(parent, "Cols", onGridColsMinus, onGridColsPlus, out refs.GridColsVal);
            BuildZRow(parent, "Rows", onGridRowsMinus, onGridRowsPlus, out refs.GridRowsVal);

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
            scopeBtn.AddComponent<LayoutElement>().preferredWidth = 72f;
            refs.ScopeBtnImg       = scopeBtn.AddComponent<Image>();
            refs.ScopeBtnImg.color = BTN_NORMAL;
            var sbtn               = scopeBtn.AddComponent<Button>();
            var sc                 = sbtn.colors;
            sc.normalColor = BTN_NORMAL; sc.highlightedColor = BTN_HOVER; sc.pressedColor = BTN_ACTIVE;
            sbtn.colors = sc; sbtn.targetGraphic = refs.ScopeBtnImg;
            if (onScope != null) sbtn.onClick.AddListener(() => onScope.Invoke());
            refs.ScopeBtnLabel = AddCenteredText(scopeBtn.transform, "Shared", 10f, FontStyles.Bold, TEXT_PRIMARY);

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

    }
}