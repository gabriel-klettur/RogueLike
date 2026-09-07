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
            Fill,
            /// <summary>
            /// Auto-tile region tool: drag a rectangle, then the system fills the
            /// rect with a chosen terrain and resolves the correct Blob16 variant
            /// for every cell in the rect + a one-cell ring around it.
            /// Driven by the active <see cref="TerrainCatalog"/> + <see cref="TerrainMap"/>.
            /// </summary>
            AutoTileRegion
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
        /// <summary>Footprint of the MANUAL brush, and of every other sized tool (eraser,
        /// collider paint, layer jumps, select). Never touched by the AUTO toggle.</summary>
        public int BrushSize = 1;

        /// <summary>
        /// Footprint of the AUTO brush, remembered separately from <see cref="BrushSize"/>.
        ///
        /// <para>The two tools want different sizes and neither should have to give way. A
        /// corner tile's four corners are VERTICES shared by four cells each, so an AUTO stroke
        /// under 2x2 leaves three of the four cells meeting at every corner it moved unpainted —
        /// hence the default. The manual brush has no such constraint and an author who paints
        /// single tiles wants 1x1. One shared number made using AUTO once overwrite the manual
        /// size, in memory and then on disk, and the manual brush looked like it had stopped
        /// responding to its own control.</para>
        /// </summary>
        public int AutoBrushSize = TileEditorConstants.AutoBrushSize;

        /// <summary>
        /// True while the AUTO brush is the tool actually being driven. AUTO is a modifier on
        /// the Brush tool alone, so the eraser, the collider paint and the layer-jump stamp keep
        /// using <see cref="BrushSize"/> even with the checkbox lit.
        /// </summary>
        public bool IsAutoBrushActive => AutoBrushMode && CurrentTool == Tool.Brush;

        /// <summary>The footprint the author is currently driving — what the +/- buttons move,
        /// what the label shows and what the cursor draws.</summary>
        public int ActiveBrushSize
        {
            get => IsAutoBrushActive ? AutoBrushSize : BrushSize;
            set { if (IsAutoBrushActive) AutoBrushSize = value; else BrushSize = value; }
        }
        public bool IsDragging;

        /// <summary>
        /// AUTO modifier for the Brush tool ("AUTO" checkbox in the Tiles panel).
        /// When true, <c>HandleBrushInput</c> stops stamping <see cref="SelectedTile"/>
        /// directly and instead paints the TERRAIN of the selected tile's pack (its
        /// <see cref="SelectedCategory"/>) into the editor's <c>TerrainMap</c>, letting
        /// <see cref="Valkur.Data.TilesetRuleset.Model"/>'s solver (Blob16 or Corner16)
        /// pick the correct sprite variant for every painted cell plus its neighbour
        /// ring — the freehand equivalent of <see cref="Tool.AutoTileRegion"/>'s
        /// click-drag rectangle, sharing the same solver and the same one-batch-per-
        /// stroke undo contract. Independent of <see cref="CurrentTool"/> (only takes
        /// effect while Brush is active) and, like <see cref="SelectedTile"/> and
        /// <see cref="BrushSize"/>, not reset when the editor is toggled off/on.
        /// </summary>
        public bool AutoBrushMode;

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
        /// When true, every painted cell in the viewport gets a small white digit "0".."8"
        /// stamped in its centre showing the index of the topmost visual layer that
        /// holds a tile at that cell (matches <see cref="World.TilemapLayerSetup.TilemapLayer"/>).
        /// Toggled from the View panel. Bounded by the viewport — only the cells currently
        /// on screen are sampled per frame, so the cost stays flat regardless of map size.
        /// </summary>
        public bool ShowTileLayerOverlay;

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
        /// Tag stamped into <see cref="CollisionTagMap"/> for every collider cell painted
        /// while <see cref="ColliderMode.Draw"/> is active. One of
        /// <see cref="CollisionTagMap.ValidTags"/> ("*" wildcard or "0".."8" matching a
        /// <see cref="World.TilemapLayerSetup.TilemapLayer"/>). Defaults to "*" so the
        /// pre-feature behaviour (collider applies to everything) stays the default the
        /// user has to opt out of.
        /// </summary>
        public string ActiveCollisionTag = CollisionTagMap.Wildcard;

        // ── Layer Jumps (M1.8) ────────────────────────────────────────────────

        /// <summary>
        /// Active edit mode of the Layer Jumps panel. Mirrors <see cref="ColliderMode"/>:
        /// when <see cref="LayerJumpMode.Draw"/> or <see cref="LayerJumpMode.Erase"/>,
        /// mouse input paints / erases entries in
        /// <see cref="World.Layering.LayerJumpMap"/> instead of any tilemap. Mutually
        /// exclusive with <see cref="CurrentColliderMode"/> — the manager turns one off
        /// when the other turns on.
        /// </summary>
        public enum LayerJumpMode { None, Draw, Erase }

        /// <summary>Active layer-jumps edit mode. None disables jump painting.</summary>
        public LayerJumpMode CurrentLayerJumpMode = LayerJumpMode.None;

        /// <summary>
        /// When true, the GL overlay paints a translucent blue square + white target-
        /// layer digit on every cell that has a layer-jump entry. Toggled by the
        /// Layer Jumps panel's "Show Layer Jumps" header AND by the View panel's
        /// duplicate row.
        /// </summary>
        public bool ShowLayerJumpsOverlay;

        /// <summary>
        /// Target visual layer stamped into every cell painted while
        /// <see cref="LayerJumpMode.Draw"/> is active. One of "0".."8" matching a
        /// <see cref="World.TilemapLayerSetup.TilemapLayer"/>. Defaults to "0" (Ground)
        /// so a freshly-painted jump always has a valid destination.
        /// </summary>
        public string ActiveJumpTargetLayer = "0";

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

        // ── Auto-tile region tool ──────────────────────────────────────────────

        /// <summary>
        /// Currently selected terrain ID for the <see cref="Tool.AutoTileRegion"/>
        /// tool (e.g. "grass", "dirt"). Empty when no terrain has been picked yet.
        /// </summary>
        public string SelectedTerrain = "";

        /// <summary>Cell where the active region drag started (left-mouse press), or null when not dragging.</summary>
        public Vector3Int? RegionDragStart;

        /// <summary>Cell where the active region drag is now (live preview), or null when not dragging.</summary>
        public Vector3Int? RegionDragCurrent;
    }
}
