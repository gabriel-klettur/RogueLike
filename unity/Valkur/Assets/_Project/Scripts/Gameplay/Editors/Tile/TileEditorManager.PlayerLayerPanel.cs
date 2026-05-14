using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// PLAYER LAYER diagnostic panel — the bottom-right readout that surfaces
    /// the player's logical visual layer + which visual layers currently have
    /// tiles at the player's foot position.
    ///
    /// Independent of any specific authoring tool. As of M1.8c the panel is a
    /// standard menu-bar dropdown (toggled via the "Player Layer" button next
    /// to "Jumps") — same pattern as every other Tile Editor dropdown. Auto-
    /// opens together with the rest of the main panels when F8 activates.
    /// </summary>
    public partial class TileEditorManager
    {
        // Cached refs resolved lazily — avoid per-frame FindObjectOfType cost
        // while the panel is being ticked.
        private VisualLayerOccupant _playerLayerOccupant;
        private readonly bool[] _underfootScratch = new bool[9];

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
            Vector2Int? cell = null;
            if (_playerLayerOccupant != null)
            {
                layer = _playerLayerOccupant.CurrentVisualLayer;
                layerName = _playerLayerOccupant.LayerName;
                var pos = _playerLayerOccupant.transform.position;
                populated = VisualLayerProbe.Sample(pos, worldGridBuilder, _underfootScratch);
                // Compute the player cell using the SAME math the
                // LayerJumpTriggerSystem uses to look up jumps. Surfacing this
                // value in the panel lets the author confirm visually that
                // "the cell I painted" == "the cell the trigger samples".
                cell = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
            }
            else
            {
                // Clear the scratch buffer so the underfoot line shows "(none)"
                // instead of stale data from a previous owner.
                for (int i = 0; i < _underfootScratch.Length; i++) _underfootScratch[i] = false;
            }

            _ui.RefreshPlayerLayerPanel(layer, layerName,
                populated > 0 ? _underfootScratch : null,
                cell);
        }
    }
}
