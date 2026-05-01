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

        private static void BuildBuildingsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float buildX = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.BuildingsDropdown = MakeDrop("BuildingsPanel", canvasT,
                PanelDock.TopLeft, buildX, PANEL_TOP_OFFSET,
                BUILDINGS_W, BUILDINGS_H, "Buildings", out var t, out refs.BuildingsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search buildings\u2026",
                v => onSearchChanged?.Invoke(v ?? ""));

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

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            refs.BuildingsDropdown.SetActive(false);
        }

        // ── Colliders Panel ───────────────────────────────────────────────────────
        // Sits between Buildings and Properties. Provides:
        //   • Visibility toggle for the per-building collider overlay (red shapes).
        //   • Scope toggle (CG = shared by image / CU = unique to this instance).
        //   • Brush ON/OFF + Action (# Paint / . Erase) + Size preset buttons (1–8) + stepper.
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
            Action onBrushPaint, Action onBrushErase,
            Action<int>  onBrushSizeChanged,
            Action       onBrushSizeStepDown,
            Action       onBrushSizeStepUp)
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

            // ── Action: # Paint / . Erase (clicking the active action toggles it off) ──
            AddSectionLabel(t, "Brush Action");
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

            // ── Brush size: preset buttons (1–8) + stepper (−/value/+), matching Tile Editor UX ──
            BuildSeparator(t);
            AddSectionLabel(t, "Brush Size [ / ]");
            BuildCollBrushSizePresetRow(t, ref refs, onBrushSizeChanged);
            BuildSeparator(t);
            BuildCollBrushSizeStepperRow(t, ref refs, onBrushSizeStepDown, onBrushSizeStepUp);

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
                "# paint · . erase (click active to toggle off) · [ ] size · Tab scope · B on/off. LMB on building to apply.";
            refs.CollHintText.fontSize            = 9f;
            refs.CollHintText.color               = TEXT_MUTED;
            refs.CollHintText.alignment           = TextAlignmentOptions.TopLeft;
            refs.CollHintText.enableWordWrapping  = true;

            refs.CollidersDropdown.SetActive(false);
        }

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