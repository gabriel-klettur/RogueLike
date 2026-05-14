using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// PLAYER LAYER diagnostic panel — the bottom-right readout that surfaces the
    /// player's logical visual layer + which visual layers currently have tiles
    /// at the player's foot position.
    ///
    /// Independent of any specific authoring tool: visible whenever the Tile
    /// Editor is active AND <see cref="TileEditorState.ShowPlayerLayer"/> is ON.
    /// Decoupled (in M1.8b) from the Colliders system — previously the panel
    /// was misnamed "COLLIDERS LAYER" and gated on Show Colliders, which made no
    /// sense once Layer Jumps and DevConsole layer commands started using the
    /// same info.
    /// </summary>
    public partial class TileEditorManager
    {
        // Cached refs resolved lazily — avoid per-frame FindObjectOfType cost
        // while the panel is being ticked.
        private VisualLayerOccupant _playerLayerOccupant;
        private readonly bool[] _underfootScratch = new bool[9];

        /// <summary>
        /// User clicked the "Show Player Layer" toggle in the View panel.
        /// Flips the state flag and re-evaluates the panel visibility + refreshes
        /// the View panel's toggle visuals so the row reflects the new state.
        /// </summary>
        internal void OnShowPlayerLayerClicked()
        {
            _state.ShowPlayerLayer = !_state.ShowPlayerLayer;
            ApplyPlayerLayerPanelVisibility();
            _ui?.RefreshViewToggles();
            _ui?.SetStatus(_state.ShowPlayerLayer ? "Player layer panel visible" : "Player layer panel hidden");
        }

        /// <summary>
        /// Show / hide the "PLAYER LAYER" panel based on whether the user is in a
        /// state where the readout is useful: editor active AND toggle ON. Hidden
        /// in every other state (game mode, editor with toggle OFF).
        /// </summary>
        internal void ApplyPlayerLayerPanelVisibility()
        {
            if (_ui == null) return;
            var panel = _ui.GetPlayerLayerPanel();
            if (panel == null) return;
            bool show = _state != null && _state.Active && _state.ShowPlayerLayer;
            if (panel.activeSelf != show)
                panel.SetActive(show);
        }

        /// <summary>
        /// Refresh the panel's two readout lines with the player's logical
        /// <see cref="VisualLayerOccupant.CurrentVisualLayer"/> + the set of
        /// visual layers that currently have a tile under the player's feet
        /// (sampled via <see cref="VisualLayerProbe"/>). Called once per frame
        /// by <see cref="Update"/> while the panel is visible — short-circuits
        /// when the panel isn't active so cost is effectively zero outside the
        /// authoring path.
        /// </summary>
        internal void TickPlayerLayerPanel()
        {
            if (_ui == null) return;
            var panel = _ui.GetPlayerLayerPanel();
            if (panel == null || !panel.activeSelf) return;

            // Re-resolve the player occupant lazily — it can be nulled out
            // between deaths / respawns, so a null check is cheaper than a hard
            // singleton subscription.
            if (_playerLayerOccupant == null)
                _playerLayerOccupant = FindObjectOfType<VisualLayerOccupant>();

            int layer = -1;
            string layerName = null;
            int populated = 0;
            if (_playerLayerOccupant != null)
            {
                layer = _playerLayerOccupant.CurrentVisualLayer;
                layerName = _playerLayerOccupant.LayerName;
                populated = VisualLayerProbe.Sample(_playerLayerOccupant.transform.position,
                                                     worldGridBuilder, _underfootScratch);
            }
            else
            {
                // Clear the scratch buffer so the underfoot line shows "(none)"
                // instead of stale data from a previous owner.
                for (int i = 0; i < _underfootScratch.Length; i++) _underfootScratch[i] = false;
            }

            _ui.RefreshPlayerLayerPanel(layer, layerName,
                populated > 0 ? _underfootScratch : null);
        }
    }
}
