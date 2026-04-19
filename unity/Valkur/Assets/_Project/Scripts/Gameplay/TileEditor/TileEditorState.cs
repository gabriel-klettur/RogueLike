using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Holds all mutable state for the runtime tile editor.
    /// Maps to Python's TileEditorState + MapEditorState.
    /// </summary>
    public class TileEditorState
    {
        public enum Tool
        {
            Select,
            Brush,
            Eraser,
            Eyedropper,
            Fill
        }

        /// <summary>
        /// Sub-mode used by the dedicated Colliders panel. When non-None, mouse painting
        /// affects the <see cref="TilemapLayerSetup.TilemapLayer.Collision"/> layer using the
        /// configured <see cref="BrushSize"/>, regardless of the currently selected layer.
        /// </summary>
        public enum ColliderMode
        {
            /// <summary>Colliders panel is not driving input.</summary>
            None,
            /// <summary>Brush paints invisible collision tiles (red overlay).</summary>
            Draw,
            /// <summary>Eraser removes collision tiles (red overlay).</summary>
            Erase
        }

        public bool Active;
        public Tool CurrentTool = Tool.Select;
        public TilemapLayerSetup.TilemapLayer CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
        public TileBase SelectedTile;
        public int SelectedCatalogIndex = -1;
        public string SelectedCategory = "";
        public int BrushSize = 1;
        public bool IsDragging;

        /// <summary>
        /// When true, the Tile Editor draws an opaque red fill + red border for every cell
        /// painted on the Collision tilemap layer, so the otherwise-invisible collider
        /// shapes can be authored visually. Toggled from the Colliders panel.
        /// </summary>
        public bool ShowColliderOverlay;

        /// <summary>
        /// Active collider authoring mode. When <see cref="ColliderMode.Draw"/> or
        /// <see cref="ColliderMode.Erase"/>, mouse input paints/erases the Collision layer
        /// instead of the currently selected drawing layer.
        /// </summary>
        public ColliderMode CurrentColliderMode = ColliderMode.None;

        /// <summary>
        /// World-space cell last interacted with (click/place/eyedrop).
        /// Shown as a GREEN outline. Maps to Python's selected_tile (.x,.y).
        /// </summary>
        public Vector3Int? SelectedCellPos;

        /// <summary>
        /// Cells actively painted during the current brush drag.
        /// Shown as YELLOW outlines. Cleared on mouse-up.
        /// </summary>
        public readonly HashSet<Vector3Int> BrushStrokeCells = new HashSet<Vector3Int>();

        // Undo support
        public const int MAX_UNDO = 50;
    }
}
