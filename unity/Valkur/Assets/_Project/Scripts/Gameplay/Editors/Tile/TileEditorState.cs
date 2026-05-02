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

        /// <summary>
        /// Sub-mode of the <see cref="Tool.Select"/> tool, exposed via the SelectModes panel.
        /// Drives <c>HandleSelectInput</c> dispatch.
        /// </summary>
        public enum SelectMode
        {
            /// <summary>Click replaces the selection with the brush footprint at the cursor.</summary>
            Single,
            /// <summary>Click-and-drag defines a rectangle; release commits every cell inside.</summary>
            Rect,
            /// <summary>Each click unions the brush footprint into the existing selection.</summary>
            Multi
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
        /// When true, the white per-tile cell grid is rendered by the GL overlay so painters
        /// can see exactly which cell they target. Toggled from the View panel.
        /// </summary>
        public bool ShowGridLines = true;

        /// <summary>
        /// When true, the GL overlay draws a thick coloured border around every zone
        /// (matches the Map Editor's zone outlines). Toggled from the View panel.
        /// </summary>
        public bool ShowZoneGrid;

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

        // ── Select tool (sub-modes + clipboard) ────────────────────────────────

        /// <summary>
        /// Active sub-mode of <see cref="Tool.Select"/>. Defaults to
        /// <see cref="SelectMode.Single"/>; reset to Single whenever the user leaves
        /// and re-enters the Select tool (matches the user-validated UX decision).
        /// </summary>
        public SelectMode CurrentSelectMode = SelectMode.Single;

        /// <summary>
        /// Cells currently in the user's persistent selection, drawn as GREEN outlines
        /// while Select is the active tool. Distinct from <see cref="BrushStrokeCells"/>,
        /// which is the ephemeral preview during a Brush/Eraser drag. Cleared whenever
        /// the user leaves Select.
        /// </summary>
        public readonly HashSet<Vector3Int> SelectedCells = new HashSet<Vector3Int>();

        /// <summary>Cell where the active <see cref="SelectMode.Rect"/> drag started, or null when not dragging.</summary>
        public Vector3Int? RectDragStart;

        /// <summary>Cell where the active <see cref="SelectMode.Rect"/> drag is now (live preview), or null when not dragging.</summary>
        public Vector3Int? RectDragCurrent;

        /// <summary>
        /// In-memory tile clipboard populated by Copy / Cut and consumed by Paste.
        /// Survives tool changes — only reset on a subsequent Copy/Cut. Lost when
        /// the editor is closed (matches OS-clipboard semantics for runtime tools).
        /// </summary>
        public TileClipboard Clipboard;

        // Undo support
        public const int MAX_UNDO = 50;
    }
}
