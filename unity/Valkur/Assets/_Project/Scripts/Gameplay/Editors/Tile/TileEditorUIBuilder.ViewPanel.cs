using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the View dropdown panel. Three toggleable rows that mirror the UX of the
    /// "Show Colliders ON/OFF" row in the Colliders panel:
    ///   • Tiles Grid    — show/hide the white per-tile cell grid
    ///   • Zone Grid     — show/hide the cyan zone-boundary outlines
    ///   • Show Colliders — same toggle as in the Colliders panel (kept in sync)
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Continues the right-edge stack: Inspector → Colliders → Size → View.
        private static float ViewX => PANEL_GAP + TILE_INSPECTOR_DROP_W + PANEL_GAP
                                    + COLLIDERS_DROP_W + PANEL_GAP
                                    + SIZE_DROP_W + PANEL_GAP;
        private static float ViewY => PANEL_TOP_OFFSET;

        private static void BuildViewDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action onShowGridLinesClicked,
            System.Action onShowZoneGridClicked,
            System.Action onShowCollidersClicked,
            System.Action onShowLayerJumpsClicked,
            System.Action onShowTileLayerClicked)
        {
            refs.ViewDropdown = MakeDropdownPanel("ViewDropdown", canvasT,
                PanelDock.TopRight, ViewX, ViewY, VIEW_DROP_W, VIEW_DROP_H,
                "View", out var viewContent, out refs.ViewPanelDrag);

            var t = viewContent;

            BuildColliderToggleRow(t, "Tiles Grid",
                state.ShowGridLines, onShowGridLinesClicked,
                out refs.ShowGridLinesToggleImg, out refs.ShowGridLinesToggleLabel);

            BuildColliderToggleRow(t, "Zone Grid",
                state.ShowZoneGrid, onShowZoneGridClicked,
                out refs.ShowZoneGridToggleImg, out refs.ShowZoneGridToggleLabel);

            BuildColliderToggleRow(t, "Show Colliders",
                state.ShowColliderOverlay, onShowCollidersClicked,
                out refs.ViewShowCollidersToggleImg, out refs.ViewShowCollidersToggleLabel);

            BuildColliderToggleRow(t, "Show Layer Jumps",
                state.ShowLayerJumpsOverlay, onShowLayerJumpsClicked,
                out refs.ViewShowLayerJumpsToggleImg, out refs.ViewShowLayerJumpsToggleLabel);

            BuildColliderToggleRow(t, "Show Tile Layer",
                state.ShowTileLayerOverlay, onShowTileLayerClicked,
                out refs.ViewShowTileLayerToggleImg, out refs.ViewShowTileLayerToggleLabel);

            refs.ViewDropdown.SetActive(false);
        }
    }
}
