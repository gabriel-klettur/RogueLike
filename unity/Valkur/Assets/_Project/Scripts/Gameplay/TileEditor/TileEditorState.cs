using UnityEngine.Tilemaps;
using Valkur.Gameplay.Rendering;

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
        public Tool CurrentTool = Tool.Brush;
        public TilemapLayerSetup.TilemapLayer CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
        public TileBase SelectedTile;
        public int SelectedCatalogIndex = -1;
        public string SelectedCategory = "";
        public int BrushSize = 1;
        public bool IsDragging;

        // Undo support
        public const int MAX_UNDO = 50;
    }
}
