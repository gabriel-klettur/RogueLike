using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the "COLLIDERS LAYER" diagnostic dropdown for the runtime Tile Editor.
    ///
    /// Position: bottom-right, immediately to the LEFT of the Layers panel.
    /// Visibility: managed by <see cref="TileEditorManager.ApplyColliderOverlayVisibility"/> —
    /// the panel is shown whenever the editor is active AND
    /// <see cref="TileEditorState.ShowColliderOverlay"/> is ON. Hidden in every
    /// other state (game mode, editor open with Show Colliders OFF, etc.) so the
    /// readout only takes screen real estate when the user is actually authoring
    /// per-layer collisions.
    ///
    /// Content (two lines):
    ///   • "Layer:     0 — Ground"      — source of truth: player's
    ///     <see cref="World.Layering.VisualLayerOccupant.CurrentVisualLayer"/>.
    ///   • "Underfoot: 0, 5"            — observed by
    ///     <see cref="World.Layering.VisualLayerProbe.Sample"/> against the live
    ///     visual tilemaps at the player's position. Lists the indices of every
    ///     layer that currently has a non-empty tile under the player.
    ///
    /// The two lines surface the layered-world model: when the logical layer
    /// diverges from what the world has under the player's feet, that's a cue
    /// for the designer that a trigger zone / portal / stairs is missing.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Position: bottom-right, immediately to the LEFT of the Layers dropdown.
        //   CollidersLayer.X = (Layers gap) + (Layers width) + (gap) → stacks to the
        //   left along the right edge. Y matches Layers so both share the bottom row.
        private static float CollidersLayerX => PANEL_GAP + LAYERS_DROP_W + PANEL_GAP;
        private static float CollidersLayerY => PANEL_GAP;

        private static void BuildCollidersLayerDropdown(Transform canvasT, ref UIRefs refs)
        {
            refs.CollidersLayerDropdown = MakeDropdownPanel("CollidersLayerDropdown", canvasT,
                PanelDock.BottomRight, CollidersLayerX, CollidersLayerY,
                COLLIDERS_LAYER_DROP_W, COLLIDERS_LAYER_DROP_H,
                "Colliders Layer", out var content, out refs.CollidersLayerPanelDrag);

            var t = content;

            refs.CollidersLayerLogicalLabel   = BuildReadoutLine(t, "Layer: —");
            refs.CollidersLayerUnderfootLabel = BuildReadoutLine(t, "Underfoot: —");

            // Hidden until the editor activates AND Show Colliders is ON. The
            // manager calls SetActive(true) via ApplyColliderOverlayVisibility.
            refs.CollidersLayerDropdown.SetActive(false);
        }

        private static TextMeshProUGUI BuildReadoutLine(Transform parent, string initialText)
        {
            var go = CreateUI("ReadoutLine", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = initialText;
            tmp.fontSize = 12f;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = TEXT_PRIMARY;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
