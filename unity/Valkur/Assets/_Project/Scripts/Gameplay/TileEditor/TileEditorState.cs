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

        public bool Active;
        public Tool CurrentTool = Tool.Select;
        public TilemapLayerSetup.TilemapLayer CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
        public TileBase SelectedTile;
        public int SelectedCatalogIndex = -1;
        public string SelectedCategory = "";
        public int BrushSize = 1;
        public bool IsDragging;

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
