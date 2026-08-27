using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Enemies.FSM
{
    public static partial class FSMEditorUIBuilder
    {
        // ── Menu helpers (shared) ─────────────────────────────────────────────────

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

            var img   = go.AddComponent<Image>();
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

        // ── Tools Panel (60 px narrow, top-left) ──────────────────────────────────
        // Mirrors the Python fsm_toolbar (undo / redo). Save / Reload mirror the
        // FSM persistence service shortcuts (Ctrl+S, F3).

        private static void BuildToolsPanel(Transform canvasT, ref UIRefs refs,
            Action onUndo, Action onRedo, Action onSave, Action onReload,
            Action onToggleBuiltIn)
        {
            refs.ToolsDropdown = MakeDrop("FSMToolsPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                TOOLS_W, TOOLS_H, "Tools", out var t, out refs.ToolsPanelDrag,
                narrowPanel: true);

            refs.UndoBtnImg   = AddActionBtn(t, "Undo",   BTN_H, onUndo);
            refs.RedoBtnImg   = AddActionBtn(t, "Redo",   BTN_H, onRedo);
            refs.SaveBtnImg   = AddActionBtn(t, "Save",   BTN_H, onSave);
            refs.ReloadBtnImg = AddActionBtn(t, "Reload", BTN_H, onReload);

            // Shows/hides the code-owned edges. Keeps its own label so the button states
            // what it will do next rather than what is currently true, which is the one
            // thing a toggle button can get wrong.
            refs.BuiltInBtnImg = AddActionBtn(t, "Built-in", BTN_H, onToggleBuiltIn);
            refs.BuiltInBtnTmp = refs.BuiltInBtnImg.GetComponentInChildren<TextMeshProUGUI>();

            refs.ToolsDropdown.SetActive(false);
        }

        private static Image AddActionBtn(Transform parent, string label, float height, Action onClick)
        {
            var go = CreateUI($"Act_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img   = go.AddComponent<Image>();
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
            return img;
        }
    }
}
