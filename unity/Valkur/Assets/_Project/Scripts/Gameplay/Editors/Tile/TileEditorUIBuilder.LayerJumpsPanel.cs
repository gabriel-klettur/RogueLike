using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World.Layering;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the "LAYER JUMPS" dropdown for the runtime Tile Editor (M1.8).
    /// Mirrors the Colliders panel almost 1:1 — Show / Draw / Erase toggles plus
    /// a TARGET LAYER picker row of 9 small buttons ("0".."8"). The picker uses
    /// <see cref="OnPointerClickRelay"/> instead of UGUI Buttons so the
    /// Selectable.OnDisable static-array race that bit us earlier never repeats
    /// here.
    ///
    /// Position: TopLeft, immediately below the Tools dropdown. Drag-handle on
    /// the panel header lets the user move it; the close button just hides the
    /// panel (the menu-bar "Jumps" button re-opens it).
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Stack TopLeft. Tools sits at PANEL_GAP / PANEL_TOP_OFFSET. Layer Jumps
        // drops below it with one PANEL_GAP of breathing room.
        private static float LayerJumpsX => PANEL_GAP;
        private static float LayerJumpsY => PANEL_TOP_OFFSET + TOOLS_DROP_H + PANEL_GAP;

        // Target-picker button geometry (mirrors the Apply-To-Layer buttons in
        // the Colliders panel but with 9 entries instead of 10).
        private const float JUMPS_BTN_SIZE       = 22f;
        private const float JUMPS_ROW_HEIGHT     = JUMPS_BTN_SIZE + 4f;
        private const float JUMPS_BTN_FONT_SIZE  = 11f;

        private static void BuildLayerJumpsDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action onShowJumpsClicked,
            System.Action onDrawJumpsClicked,
            System.Action onEraseJumpsClicked,
            System.Action<string> onTargetChanged)
        {
            refs.LayerJumpsDropdown = MakeDropdownPanel("LayerJumpsDropdown", canvasT,
                PanelDock.TopLeft, LayerJumpsX, LayerJumpsY,
                LAYER_JUMPS_DROP_W, LAYER_JUMPS_DROP_H,
                "Layer Jumps", out var content, out refs.LayerJumpsPanelDrag);

            var t = content;

            // Visualize toggle (mirror of Show Colliders).
            BuildColliderToggleRow(t, "Show Layer Jumps",
                state.ShowLayerJumpsOverlay, onShowJumpsClicked,
                out refs.ShowLayerJumpsToggleImg, out refs.ShowLayerJumpsToggleLabel);

            BuildSeparator(t);

            // Edit-mode header
            var editHdr = CreateUI("Label_EditJumps", t);
            editHdr.AddComponent<LayoutElement>().preferredHeight = 16f;
            var editTmp = editHdr.AddComponent<TextMeshProUGUI>();
            editTmp.text = "EDIT MODE";
            editTmp.fontSize = 10f;
            editTmp.fontStyle = FontStyles.Bold;
            editTmp.alignment = TextAlignmentOptions.Left;
            editTmp.color = TEXT_MUTED;
            editTmp.characterSpacing = 2f;

            BuildColliderToggleRow(t, "Draw Jumps",
                state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Draw, onDrawJumpsClicked,
                out refs.DrawLayerJumpsToggleImg, out refs.DrawLayerJumpsToggleLabel);

            BuildColliderToggleRow(t, "Erase Jumps",
                state.CurrentLayerJumpMode == TileEditorState.LayerJumpMode.Erase, onEraseJumpsClicked,
                out refs.EraseLayerJumpsToggleImg, out refs.EraseLayerJumpsToggleLabel);

            BuildSeparator(t);

            BuildTargetLayerSection(t, state, ref refs, onTargetChanged);

            refs.LayerJumpsDropdown.SetActive(false);
        }

        /// <summary>
        /// TARGET LAYER row inside the LAYER JUMPS panel: 9 small square buttons
        /// (0..8), no wildcard. Highlighted button = the currently active
        /// <see cref="TileEditorState.ActiveJumpTargetLayer"/>.
        /// </summary>
        private static void BuildTargetLayerSection(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<string> onTargetChanged)
        {
            var hdrGo = CreateUI("Label_TargetLayer", parent);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var hdrTmp = hdrGo.AddComponent<TextMeshProUGUI>();
            hdrTmp.text = "TARGET LAYER";
            hdrTmp.fontSize = 10f;
            hdrTmp.fontStyle = FontStyles.Bold;
            hdrTmp.alignment = TextAlignmentOptions.Left;
            hdrTmp.color = TEXT_MUTED;
            hdrTmp.characterSpacing = 2f;

            // Live value label echoing the active target so the user sees what
            // the next paint will stamp without scanning the row.
            var valueGo = CreateUI("ActiveTargetLabel", parent);
            valueGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            refs.LayerJumpsActiveLabel = valueGo.AddComponent<TextMeshProUGUI>();
            refs.LayerJumpsActiveLabel.text = $"Active: {state.ActiveJumpTargetLayer}";
            refs.LayerJumpsActiveLabel.fontSize = 10f;
            refs.LayerJumpsActiveLabel.alignment = TextAlignmentOptions.Left;
            refs.LayerJumpsActiveLabel.color = ACCENT;
            refs.LayerJumpsActiveLabel.raycastTarget = false;

            var row = CreateUI("JumpsTargetRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = JUMPS_ROW_HEIGHT;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 2f;
            h.padding = new RectOffset(0, 0, 2, 0);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            const int count = 9;
            refs.LayerJumpsTargetImgs   = new Image[count];
            refs.LayerJumpsTargetLabels = new TextMeshProUGUI[count];

            for (int i = 0; i < count; i++)
            {
                string target = i.ToString();
                bool active = target == state.ActiveJumpTargetLayer;
                BuildTargetLayerButton(row.transform, target, active, onTargetChanged,
                    out refs.LayerJumpsTargetImgs[i],
                    out refs.LayerJumpsTargetLabels[i]);
            }
        }

        private static void BuildTargetLayerButton(Transform parent, string target, bool active,
            System.Action<string> onClicked,
            out Image bg, out TextMeshProUGUI labelTmp)
        {
            var go = CreateUI($"Target_{target}", parent);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bg = go.AddComponent<Image>();
            bg.color = active ? new Color(ACCENT.r, ACCENT.g, ACCENT.b, 0.30f) : BTN_NORMAL;
            bg.raycastTarget = true;

            var relay = go.AddComponent<OnPointerClickRelay>();
            string cap = target;
            relay.OnClicked = () => onClicked?.Invoke(cap);

            var lblGo = CreateUI("Lbl", go.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.offsetMin = Vector2.zero;
            lblRt.offsetMax = Vector2.zero;
            labelTmp = lblGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = target;
            labelTmp.fontSize = JUMPS_BTN_FONT_SIZE;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = active ? ACCENT : TEXT_PRIMARY;
            labelTmp.raycastTarget = false;
        }
    }
}
