using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the "PLAYER LAYER" diagnostic dropdown for the runtime Tile Editor.
    ///
    /// Position: bottom-right, immediately to the LEFT of the Layers panel.
    /// Visibility: managed by <see cref="TileEditorManager.ApplyPlayerLayerPanelVisibility"/> —
    /// the panel is shown whenever the editor is active AND
    /// <see cref="TileEditorState.ShowPlayerLayer"/> is ON. Independent of the
    /// Colliders / Layer Jumps panels: the readout is useful whenever the
    /// author touches anything layer-related (paint colliders, paint jumps,
    /// test the DevConsole `layer N` command, debug a future spell, etc.).
    ///
    /// Content (two lines):
    ///   • "Layer:     0 — Ground"      — source of truth: the player's
    ///     <see cref="World.Layering.VisualLayerOccupant.CurrentVisualLayer"/>.
    ///   • "Underfoot: 0, 5"            — observed by
    ///     <see cref="World.Layering.VisualLayerProbe.Sample"/> against the live
    ///     visual tilemaps at the player's position. Lists every layer index
    ///     that currently has a non-empty tile under the player.
    ///
    /// The two lines surface the layered-world model: when the logical layer
    /// diverges from what the world has under the player's feet, that's a cue
    /// for the designer that a trigger zone / stairs / layer-jump is missing.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Position: bottom-right, immediately to the LEFT of the Layers dropdown.
        //   PlayerLayer.X = (Layers gap) + (Layers width) + (gap) → stacks to the
        //   left along the right edge. Y matches Layers so both share the bottom row.
        private static float PlayerLayerX => PANEL_GAP + LAYERS_DROP_W + PANEL_GAP;
        private static float PlayerLayerY => PANEL_GAP;

        private static void BuildPlayerLayerDropdown(Transform canvasT, ref UIRefs refs)
        {
            refs.PlayerLayerDropdown = MakeDropdownPanel("PlayerLayerDropdown", canvasT,
                PanelDock.BottomRight, PlayerLayerX, PlayerLayerY,
                PLAYER_LAYER_DROP_W, PLAYER_LAYER_DROP_H,
                "Player Layer", out var content, out refs.PlayerLayerPanelDrag);

            var t = content;

            refs.PlayerLayerLogicalLabel   = BuildPlayerLayerReadoutLine(t, "Layer: —");
            refs.PlayerLayerUnderfootLabel = BuildPlayerLayerReadoutLine(t, "Underfoot: —");

            // Hidden until the editor activates AND ShowPlayerLayer is ON. The
            // manager calls SetActive(true) via ApplyPlayerLayerPanelVisibility.
            refs.PlayerLayerDropdown.SetActive(false);
        }

        private static TextMeshProUGUI BuildPlayerLayerReadoutLine(Transform parent, string initialText)
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
