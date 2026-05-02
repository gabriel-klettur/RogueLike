using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public static partial class TileEditorUIBuilder
    {
        // ═════════════════════════════════════════════════════════════════
        //  Panel dock layout (top-row Tools + Tiles, top-right Inspector, bottom-right Layers)
        // ═════════════════════════════════════════════════════════════════
        // Tools sits at top-left, just below the menu bar.
        // Tiles sits immediately to the right of Tools (same vertical row).
        // Inspector sits at top-right, same vertical row as Tools/Tiles.
        // Layers sits at the bottom-right corner.
        private static float ToolsX     => PANEL_GAP;
        private static float ToolsY     => PANEL_TOP_OFFSET;
        private static float TilesX     => PANEL_GAP + TOOLS_DROP_W + PANEL_GAP;
        private static float TilesY     => PANEL_TOP_OFFSET;
        private static float InspectorX => PANEL_GAP;   // from right edge
        private static float InspectorY => PANEL_TOP_OFFSET;
        private static float LayersX    => PANEL_GAP;   // from right edge
        private static float LayersY    => PANEL_GAP;   // from bottom edge

        // ═════════════════════════════════════════════════════════════════
        //  TOOLS DROPDOWN
        // ═════════════════════════════════════════════════════════════════

        private static void BuildToolsDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<int> onBrushSizeChanged,
            System.Action onUndo = null,
            System.Action onRedo = null)
        {
            refs.ToolsDropdown = MakeDropdownPanel("ToolsDropdown", canvasT,
                PanelDock.TopLeft, ToolsX, ToolsY, TOOLS_DROP_W, TOOLS_DROP_H,
                "Tools", out var toolsContent, out refs.ToolsPanelDrag);

            var t = toolsContent;

            // Single-column icon toolbar — inner width (60-8-8=44) = BTN_H → square
            const float BTN_H = 44f;
            CreateToolBtn(t, "Select",  "S",      TileEditorState.Tool.Select,      state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Brush",   "B",      TileEditorState.Tool.Brush,       state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Erase",   "E",      TileEditorState.Tool.Eraser,      state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Fill",    "F",      TileEditorState.Tool.Fill,        state, ref refs, onToolChanged, BTN_H);
            CreateToolBtn(t, "Pick",    "I",      TileEditorState.Tool.Eyedropper,  state, ref refs, onToolChanged, BTN_H);

            BuildSeparator(t);

            CreateActionBtn(t, "Undo", "Ctrl+Z",       BTN_H, onUndo);
            CreateActionBtn(t, "Redo", "Ctrl+Shift+Z", BTN_H, onRedo);

            // No Save button: every edit path (brush/eraser/fill/colliders/cut/paste/
            // auto-gen/clear-all) auto-flushes via _persistence.SaveAllDirty() on
            // mouse-up. Manual save was redundant.

            refs.ToolsDropdown.SetActive(false);
        }

        private static void CreateActionBtn(Transform parent, string label, string shortcut,
            float height, System.Action onClick)
        {
            var go = CreateUI($"Action_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.spacing = 1f;
            vl.padding = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 9f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color = TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 11f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = shortcut;
            keyTmp.fontSize = 7f;
            keyTmp.alignment = TextAlignmentOptions.Center;
            keyTmp.color = TEXT_MUTED;
        }

        private static void CreateToolBtn(Transform parent, string label, string shortcut,
            TileEditorState.Tool tool, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged, float height = 44f)
        {
            var go = CreateUI($"Tool_{tool}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            bool active = tool == state.CurrentTool;
            img.color = active ? BTN_ACTIVE : BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            var cap = tool;
            btn.onClick.AddListener(() => onToolChanged?.Invoke(cap));

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.spacing = 1f;
            vl.padding = new RectOffset(2, 2, 4, 4);

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 9f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color = active ? ACCENT : TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 11f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = shortcut;
            keyTmp.fontSize = 7f;
            keyTmp.alignment = TextAlignmentOptions.Center;
            keyTmp.color = TEXT_MUTED;

            refs.ToolButtonImages[tool] = img;
            refs.ToolButtonTexts[tool] = lblTmp;
        }

        // ═════════════════════════════════════════════════════════════════
        //  TILES DROPDOWN (categories + tile grid + selected preview)
        // ═════════════════════════════════════════════════════════════════

    }
}