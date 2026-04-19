using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the Colliders dropdown panel for the runtime Tile Editor.
    /// Three controls live here:
    ///   • Show Colliders   — visualize/hide red overlay on Collision tiles
    ///   • Draw Colliders   — when ON, mouse paints invisible collision tiles using the configured brush size
    ///   • Erase Colliders  — when ON, mouse erases collision tiles using the configured brush size
    /// Draw and Erase are mutually exclusive; toggling one OFFs the other (handled in the manager).
    /// Sits TopRight of the canvas, immediately to the LEFT of the Inspector dropdown.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // X offset (pixels from right edge): InspectorX is `PANEL_GAP`, so the
        // Colliders panel must skip past Inspector's full width plus a gap.
        private static float CollidersX => PANEL_GAP + INSPECTOR_DROP_W + PANEL_GAP;
        private static float CollidersY => PANEL_TOP_OFFSET;

        private static void BuildCollidersDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action onShowCollidersClicked,
            System.Action onDrawCollidersClicked,
            System.Action onEraseCollidersClicked)
        {
            refs.CollidersDropdown = MakeDropdownPanel("CollidersDropdown", canvasT,
                PanelDock.TopRight, CollidersX, CollidersY, COLLIDERS_DROP_W, COLLIDERS_DROP_H,
                "Colliders", out var collidersContent, out refs.CollidersPanelDrag);

            var t = collidersContent;

            // Visualize toggle
            BuildColliderToggleRow(t, "Show Colliders",
                state.ShowColliderOverlay, onShowCollidersClicked,
                out refs.ShowCollidersToggleImg, out refs.ShowCollidersToggleLabel);

            BuildSeparator(t);

            // Edit-mode header
            var editHdr = CreateUI("Label_Edit", t);
            editHdr.AddComponent<LayoutElement>().preferredHeight = 16f;
            var editTmp = editHdr.AddComponent<TextMeshProUGUI>();
            editTmp.text = "EDIT MODE";
            editTmp.fontSize = 10f;
            editTmp.fontStyle = FontStyles.Bold;
            editTmp.alignment = TextAlignmentOptions.Left;
            editTmp.color = TEXT_MUTED;
            editTmp.characterSpacing = 2f;

            // Draw-collider toggle
            BuildColliderToggleRow(t, "Draw Colliders",
                state.CurrentColliderMode == TileEditorState.ColliderMode.Draw, onDrawCollidersClicked,
                out refs.DrawCollidersToggleImg, out refs.DrawCollidersToggleLabel);

            // Erase-collider toggle
            BuildColliderToggleRow(t, "Erase Colliders",
                state.CurrentColliderMode == TileEditorState.ColliderMode.Erase, onEraseCollidersClicked,
                out refs.EraseCollidersToggleImg, out refs.EraseCollidersToggleLabel);

            BuildSeparator(t);

            // Brush size hint (pulled from the menu bar's brush nav).
            var hintGo = CreateUI("Hint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 30f;
            refs.CollidersHintText = hintGo.AddComponent<TextMeshProUGUI>();
            refs.CollidersHintText.text =
                "Brush size respects the Size selector in the menu bar. " +
                "Toggle off both Draw/Erase to return to normal tile editing.";
            refs.CollidersHintText.fontSize = 9f;
            refs.CollidersHintText.alignment = TextAlignmentOptions.TopLeft;
            refs.CollidersHintText.color = TEXT_MUTED;
            refs.CollidersHintText.enableWordWrapping = true;

            refs.CollidersDropdown.SetActive(false);
        }

        /// <summary>
        /// One togglable row used by all three Colliders panel controls. The container
        /// is a Button + Image (color reflects ON/OFF) with a child TMP label so we never
        /// run into the Image+TMP-on-same-GameObject NRE pattern documented in the project.
        /// The actual ON/OFF state is owned by <see cref="TileEditorManager"/>; this builder
        /// just emits a click signal — the manager flips state and calls
        /// <see cref="TileEditorUI.RefreshColliderToggles"/> to repaint visuals.
        /// </summary>
        private static void BuildColliderToggleRow(Transform parent, string label,
            bool initialOn, System.Action onClicked,
            out Image bgImg, out TextMeshProUGUI labelTmp)
        {
            var row = CreateUI($"Toggle_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 30f;

            bgImg = row.AddComponent<Image>();
            bgImg.color = initialOn ? new Color(COLLIDER_BORDER.r, COLLIDER_BORDER.g, COLLIDER_BORDER.b, 0.30f)
                                    : BTN_NORMAL;

            var btn = row.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = bgImg.color;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = bgImg;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(8, 8, 2, 2);
            h.childAlignment = TextAnchor.MiddleLeft;

            // Indicator dot (circle-ish square with red tint when ON).
            var dotGo = CreateUI("Dot", row.transform);
            dotGo.AddComponent<LayoutElement>().preferredWidth = 14f;
            var dotImg = dotGo.AddComponent<Image>();
            dotImg.color = initialOn ? COLLIDER_BORDER : new Color(0.4f, 0.4f, 0.45f, 1f);

            // Label
            var lblGo = CreateUI("Lbl", row.transform);
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            labelTmp = lblGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = label;
            labelTmp.fontSize = 12f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.alignment = TextAlignmentOptions.Left;
            labelTmp.color = initialOn ? RED_ACCENT : TEXT_PRIMARY;

            // State label (ON/OFF)
            var stateGo = CreateUI("State", row.transform);
            stateGo.AddComponent<LayoutElement>().preferredWidth = 34f;
            var stateTmp = stateGo.AddComponent<TextMeshProUGUI>();
            stateTmp.text = initialOn ? "ON" : "OFF";
            stateTmp.fontSize = 10f;
            stateTmp.fontStyle = FontStyles.Bold;
            stateTmp.alignment = TextAlignmentOptions.Right;
            stateTmp.color = initialOn ? RED_ACCENT : TEXT_MUTED;

            btn.onClick.AddListener(() => onClicked?.Invoke());
        }
    }
}
