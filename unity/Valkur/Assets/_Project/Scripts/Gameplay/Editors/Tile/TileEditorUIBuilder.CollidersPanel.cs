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
        private static float CollidersX => PANEL_GAP + TILE_INSPECTOR_DROP_W + PANEL_GAP;
        private static float CollidersY => PANEL_TOP_OFFSET;

        private static void BuildCollidersDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action onShowCollidersClicked,
            System.Action onDrawCollidersClicked,
            System.Action onEraseCollidersClicked,
            System.Action<string> onCollisionTagChanged)
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

            // Apply-To-Layer header + tag picker row
            BuildApplyToLayerSection(t, state, ref refs, onCollisionTagChanged);

            refs.CollidersDropdown.SetActive(false);
        }

        // Constants for the tag-picker row.
        private const float TAG_BTN_SIZE        = 22f;
        private const float TAG_ROW_HEIGHT      = TAG_BTN_SIZE + 4f;
        private const float TAG_BTN_FONT_SIZE   = 11f;

        /// <summary>
        /// "APPLY TO LAYER" mini-section appended to the Colliders panel. A header label
        /// + a row of 10 small square buttons (* + 0..8). Click stamps
        /// <see cref="TileEditorState.ActiveCollisionTag"/> with the chosen value; the
        /// next Draw stroke uses it. The active button is highlighted using the same
        /// red-accent style as the Show / Draw / Erase toggles for consistency.
        /// </summary>
        private static void BuildApplyToLayerSection(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<string> onCollisionTagChanged)
        {
            var hdrGo = CreateUI("Label_ApplyToLayer", parent);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var hdrTmp = hdrGo.AddComponent<TextMeshProUGUI>();
            hdrTmp.text = "APPLY TO LAYER";
            hdrTmp.fontSize = 10f;
            hdrTmp.fontStyle = FontStyles.Bold;
            hdrTmp.alignment = TextAlignmentOptions.Left;
            hdrTmp.color = TEXT_MUTED;
            hdrTmp.characterSpacing = 2f;

            // Live value label echoing the active tag (so the user can see at a glance
            // what the next paint will stamp without scanning the row).
            var valueGo = CreateUI("ActiveTagLabel", parent);
            valueGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            refs.CollisionTagActiveLabel = valueGo.AddComponent<TextMeshProUGUI>();
            refs.CollisionTagActiveLabel.text = $"Active: {state.ActiveCollisionTag}";
            refs.CollisionTagActiveLabel.fontSize = 10f;
            refs.CollisionTagActiveLabel.alignment = TextAlignmentOptions.Left;
            refs.CollisionTagActiveLabel.color = ACCENT;
            refs.CollisionTagActiveLabel.raycastTarget = false;

            var row = CreateUI("TagRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = TAG_ROW_HEIGHT;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 2f;
            h.padding = new RectOffset(0, 0, 2, 0);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            int count = CollisionTagMap.ValidTags.Length;
            refs.CollisionTagButtons       = new Button[count];
            refs.CollisionTagButtonImgs    = new Image[count];
            refs.CollisionTagButtonLabels  = new TextMeshProUGUI[count];

            for (int i = 0; i < count; i++)
            {
                string tag = CollisionTagMap.ValidTags[i];
                bool active = tag == state.ActiveCollisionTag;
                BuildTagButton(row.transform, tag, active, onCollisionTagChanged,
                    out refs.CollisionTagButtons[i],
                    out refs.CollisionTagButtonImgs[i],
                    out refs.CollisionTagButtonLabels[i]);
            }
        }

        private static void BuildTagButton(Transform parent, string tag, bool active,
            System.Action<string> onClicked,
            out Button btn, out Image bg, out TextMeshProUGUI labelTmp)
        {
            var go = CreateUI($"Tag_{tag}", parent);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bg = go.AddComponent<Image>();
            bg.color = active ? new Color(COLLIDER_BORDER.r, COLLIDER_BORDER.g, COLLIDER_BORDER.b, 0.30f)
                              : BTN_NORMAL;

            btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = bg.color;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = bg;
            string cap = tag;
            btn.onClick.AddListener(() => onClicked?.Invoke(cap));

            var lblGo = CreateUI("Lbl", go.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.offsetMin = Vector2.zero;
            lblRt.offsetMax = Vector2.zero;
            labelTmp = lblGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = tag;
            labelTmp.fontSize = TAG_BTN_FONT_SIZE;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = active ? RED_ACCENT : TEXT_PRIMARY;
            labelTmp.raycastTarget = false;
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
