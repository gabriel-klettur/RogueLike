using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// World-space tile grid overlay for the Tile Editor.
    /// Draws white cell borders matching the 1×1 world-unit tile grid (PPU=16, cellSize=1)
    /// so painters can see exactly which tiles they are affecting.
    ///
    /// Uses RenderPipelineManager.endCameraRendering for URP 2022 compatibility.
    /// OnRenderObject + GL is unreliable in URP because Unity's SRP replaces the
    /// built-in rendering loop and Camera.current may be null during that callback.
    /// endCameraRendering fires after each camera finishes rendering and always
    /// provides the correct Camera reference.
    ///
    /// Activation is managed by TileEditorManager: the GameObject is enabled/disabled
    /// together with the rest of the editor visuals.
    /// </summary>
    public partial class TileEditorGridOverlay : MonoBehaviour
    {
        // White at 20% alpha — subtle grid guides without saturating the screen.
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.20f);

        // Cyan  — hovered cell (follows mouse).
        private static readonly Color HoverColor    = new Color(0f,  1f,  1f,  1f);
        // Green — last selected / single-click cell.
        private static readonly Color SelectedColor = new Color(0f,  1f,  0f,  1f);
        // Yellow — cells painted during an active brush drag.
        private static readonly Color BrushColor    = new Color(1f,  1f,  0f,  1f);
        // Yellow semi-transparent blinking — Fill preview area when hovering with Fill tool.
        private static readonly Color FillPreviewColor = new Color(1f,  1f,  0f,  0.4f);

        // Red overlay drawn over every solid Collision cell when the Colliders panel
        // toggles "Show Colliders" ON. The fill is intentionally translucent so the
        // underlying ground/floor tile remains visible; the border is fully opaque.
        private static readonly Color ColliderFillColor   = new Color(1f, 0.10f, 0.15f, 0.32f);
        private static readonly Color ColliderBorderColor = new Color(1f, 0.10f, 0.15f, 1f);

        // Blue overlay (M1.8) drawn over every cell that has a LayerJumpMap entry
        // when "Show Layer Jumps" is ON. Distinct from the red Colliders to avoid
        // visual confusion when both overlays are visible at once.
        private static readonly Color LayerJumpFillColor   = new Color(0.10f, 0.45f, 1f, 0.32f);
        private static readonly Color LayerJumpBorderColor = new Color(0.10f, 0.45f, 1f, 1f);

        // Border thickness of all cell highlights, in screen pixels.
        private const float HoverThicknessPx = 3f;
        // Border thickness used for collider cells (slightly thinner than the hover ring).
        private const float ColliderBorderThicknessPx = 2f;
        // Defensive cap: skip drawing collider overlay if the visible tile rect would
        // require more than this many GetTilesBlock entries (extreme zoom-out).
        private const int MaxColliderCells = 20000;

        // Extra cells drawn beyond the visible edge to avoid pop-in when panning.
        private const int Margin = 2;

        private Material _mat;
        private Camera _targetCamera;

        // State pushed each frame by TileEditorManager.
        private Vector2Int  _hoverCell;
        private Vector2Int? _selectedCell;
        private int         _brushSize = 1;
        private TileEditorState.Tool _currentTool;
        private readonly HashSet<Vector2Int> _brushCells = new HashSet<Vector2Int>();

        // Persistent selection (Select tool, all sub-modes) — drawn as GREEN outlines
        // independent of the ephemeral _brushCells stroke preview.
        private readonly HashSet<Vector2Int> _selectedCells = new HashSet<Vector2Int>();
        // Clipboard snapshot — cells most recently Copy'd or Cut from the map.
        // Drawn as a THICK YELLOW border so the user can see what is "in clipboard"
        // independently of the current green selection. Cleared on ClearSelection
        // and on editor deactivate; survives layer changes and Paste.
        private readonly HashSet<Vector2Int> _copiedCells = new HashSet<Vector2Int>();
        // Live rectangle preview during a Rect-mode drag (anchors in cell space, inclusive).
        private Vector2Int? _rectDragStart;
        private Vector2Int? _rectDragCurrent;

        // Collider overlay state (Colliders panel).
        private Tilemap _collisionTilemap;
        private bool    _showColliderOverlay;
        // Layer Jumps overlay state (Layer Jumps panel, M1.8).
        private LayerJumpMap _layerJumpMap;
        private bool         _showLayerJumps;
        // Optional per-cell tag layer (CollisionTagMap). When non-null, each painted
        // collider cell receives a small corner marker coloured per its tag so the
        // user can tell at a glance which visual layer the collider applies to.
        private CollisionTagMap _collisionTagMap;

        // Tile-layer overlay state (View panel "Show Tile Layer"). The 9 tilemaps are
        // indexed by <see cref="TilemapLayerSetup.TilemapLayer"/>; per visible cell
        // we walk them top→bottom and stamp the digit of the first painted layer.
        // Sampling is viewport-bounded so the cost stays O(visible cells) regardless
        // of map size — at 1080p / orthoSize≈10 the visible window is ~700 cells.
        private Tilemap[] _layerTilemaps;
        private bool      _showTileLayer;
        // Cached "drawn already" bitmap, sized to the current viewport rect. Lets the
        // top→bottom layer walk skip cells a higher layer already drew without using
        // a HashSet (which would allocate + box int keys). Reused frame-to-frame and
        // grown only when the viewport expands.
        private bool[] _tileLayerDrawnGrid;
        // Cap analogous to MaxColliderCells — bail out at extreme zoom-out instead of
        // sampling 50k+ cells per frame.
        private const int MaxTileLayerCells = 20000;

        // View-panel toggles.
        private bool _showGridLines = true;

        // Fill preview state
        private Tilemap _fillPreviewTilemap;
        private TileBase _fillPreviewNewTile;
        private readonly HashSet<Vector2Int> _fillPreviewCells = new HashSet<Vector2Int>();
        private float _fillPreviewBlinkTime;
        // Reusable BFS buffers — avoids allocating a new HashSet/Queue every
        // frame inside CalculateFillPreview while the Fill tool is active
        // (used to fire ~120 Hz × 2 calls/frame, dominating GC churn).
        private readonly HashSet<Vector2Int> _fillPreviewVisited = new HashSet<Vector2Int>();
        private readonly Queue<Vector2Int> _fillPreviewQueue = new Queue<Vector2Int>();
        // 4-connected directions cached as a static — was a per-call new-allocation.
        private static readonly Vector2Int[] FillPreviewDirections = {
            new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
        };
        // Hover-cell key the current preview was computed for. Skip recompute
        // when the user keeps hovering the same cell (the common case while
        // the cursor sits still or the hover hasn't crossed a cell border).
        private Vector2Int _fillPreviewLastHover;
        private bool _fillPreviewLastValid;
        // Defensive cap: when zoomed out enough that the cursor's hover cell
        // can flicker over very large connected areas, skip the BFS rather than
        // pay 10k cells × 120fps. Floods larger than this just don't show a
        // preview ring; the actual Fill stroke still works (FloodFill in
        // TileBrush has its own cap).
        private const int FillPreviewMaxCells = 10000;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Bind to a camera. Must be called once before the overlay is enabled.</summary>
        public void Initialize(Camera cam)
        {
            _targetCamera = cam;

            // Hidden/Internal-Colored: Unity's built-in immediate-mode debug shader.
            // Supports vertex colors + alpha blending; available in URP 2022 projects.
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("[TileEditorGridOverlay] Shader 'Hidden/Internal-Colored' not found — " +
                                 "grid will not render. Check URP shader stripping settings.");
                return;
            }

            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",     (int)CullMode.Off);
            _mat.SetInt("_ZWrite",   0);
            // Always pass depth test so the grid overlays tile art cleanly.
            _mat.SetInt("_ZTest",    (int)CompareFunction.Always);
        }

        /// <summary>Set the currently selected cell (shown with GREEN border). Pass null to clear.</summary>
        public void SetSelectedCell(Vector3Int? cell)
        {
            _selectedCell = cell.HasValue ? new Vector2Int(cell.Value.x, cell.Value.y) : (Vector2Int?)null;
        }

        /// <summary>Set the brush size used to render the cyan hover and green selection borders (1–25).</summary>
        public void SetBrushSize(int size) => _brushSize = Mathf.Max(1, size);

        /// <summary>Set the active tool so stroke cells are tinted yellow (Brush/Erase) or green (Select).</summary>
        public void SetCurrentTool(TileEditorState.Tool tool) => _currentTool = tool;

        /// <summary>Replace the set of cells painted by the active brush stroke (shown in YELLOW). Pass null or empty to clear.</summary>
        public void SetBrushStrokeCells(IEnumerable<Vector3Int> cells)
        {
            _brushCells.Clear();
            if (cells == null) return;
            foreach (var c in cells)
                _brushCells.Add(new Vector2Int(c.x, c.y));
        }

        /// <summary>
        /// Replace the persistent selection drawn as GREEN borders by the Select tool.
        /// Independent from <see cref="SetBrushStrokeCells"/> so the user can see the
        /// committed selection while the Brush/Eraser stroke also draws yellow outlines.
        /// </summary>
        public void SetSelectedCells(IEnumerable<Vector3Int> cells)
        {
            _selectedCells.Clear();
            if (cells == null) return;
            foreach (var c in cells)
                _selectedCells.Add(new Vector2Int(c.x, c.y));
        }

        /// <summary>
        /// Configure the live rectangle preview shown during a <c>SelectMode.Rect</c>
        /// drag. Pass <c>null, null</c> to hide. Anchors are inclusive cell coordinates.
        /// </summary>
        public void SetRectDragPreview(Vector3Int? start, Vector3Int? current)
        {
            _rectDragStart   = start.HasValue   ? new Vector2Int(start.Value.x,   start.Value.y)   : (Vector2Int?)null;
            _rectDragCurrent = current.HasValue ? new Vector2Int(current.Value.x, current.Value.y) : (Vector2Int?)null;
        }

        /// <summary>
        /// Bind the Collision tilemap that the overlay should sample to draw the red
        /// collider visualization. Pass null to disable collider sampling entirely.
        /// </summary>
        public void SetCollisionTilemap(Tilemap tilemap) => _collisionTilemap = tilemap;

        /// <summary>Enable or disable the red collider overlay.</summary>
        public void SetShowColliderOverlay(bool show) => _showColliderOverlay = show;

        /// <summary>
        /// Bind the <see cref="CollisionTagMap"/> the overlay should sample for the
        /// per-tag corner markers drawn on top of the red collider fill. Pass null to
        /// disable the tag markers (the rest of the collider overlay continues to draw).
        /// </summary>
        public void SetCollisionTagMap(CollisionTagMap map) => _collisionTagMap = map;

        /// <summary>
        /// Bind the <see cref="LayerJumpMap"/> the overlay should sample for the
        /// blue jump-tile overlay (M1.8). Pass null to disable.
        /// </summary>
        public void SetLayerJumpMap(LayerJumpMap map) => _layerJumpMap = map;

        /// <summary>Enable or disable the blue layer-jumps overlay (M1.8).</summary>
        public void SetShowLayerJumps(bool show) => _showLayerJumps = show;

        /// <summary>
        /// Bind the 9 visual-layer tilemaps the overlay samples for the "Show Tile
        /// Layer" feature. Index matches <see cref="TilemapLayerSetup.TilemapLayer"/>
        /// (0 = Ground … 8 = OverheadDetails). The manager owns the array and reuses
        /// the same reference across frames so this is a cheap one-time bind. Pass
        /// null to disable sampling entirely.
        /// </summary>
        public void SetLayerTilemaps(Tilemap[] tilemaps) => _layerTilemaps = tilemaps;

        /// <summary>Enable or disable the per-tile layer-digit overlay.</summary>
        public void SetShowTileLayer(bool show) => _showTileLayer = show;

        /// <summary>Enable or disable the white per-tile grid lines.</summary>
        public void SetShowGridLines(bool show) => _showGridLines = show;

        /// <summary>
        /// Replace the set of cells currently in the tile clipboard. These are drawn as
        /// a thick bright-yellow border so the user can see the copy/cut source even after
        /// the green selection has changed. Pass <c>null</c> or empty to clear.
        /// </summary>
        public void SetCopiedCells(IEnumerable<Vector3Int> cells)
        {
            _copiedCells.Clear();
            if (cells == null) return;
            foreach (var c in cells)
                _copiedCells.Add(new Vector2Int(c.x, c.y));
        }

        /// <summary>
        /// Set the tilemap and new tile for Fill preview calculation. Invalidates
        /// the cached preview but does NOT run the BFS — the actual flood-fill
        /// is computed inside <see cref="DrawGrid"/> when the Fill tool is the
        /// active tool, so we don't pay for it twice per frame.
        /// </summary>
        public void SetFillPreview(Tilemap tilemap, TileBase newTile)
        {
            if (_fillPreviewTilemap != tilemap || _fillPreviewNewTile != newTile)
            {
                _fillPreviewTilemap = tilemap;
                _fillPreviewNewTile = newTile;
                _fillPreviewLastValid = false;
                _fillPreviewCells.Clear();
            }
        }

        /// <summary>Clear the Fill preview state.</summary>
        public void ClearFillPreview()
        {
            _fillPreviewTilemap = null;
            _fillPreviewNewTile = null;
            _fillPreviewCells.Clear();
            _fillPreviewLastValid = false;
        }

        /// <summary>
        /// Invalidate the Fill preview cache so the next frame recomputes the
        /// BFS even if the hover cell hasn't moved. Called by the manager after
        /// any edit (brush/erase/fill stroke, undo/redo) that may have changed
        /// the tilemap content the preview was sampled against.
        /// </summary>
        public void InvalidateFillPreview() => _fillPreviewLastValid = false;

        // ── Private Methods ─────────────────────────────────────────────────────

        /// <summary>
        /// Compute the Fill preview area based on current hover position. Skips
        /// entirely when the active tool isn't Fill, when no source / target
        /// tilemap is bound, or when the hover cell hasn't crossed a tile
        /// boundary since the previous compute (the common case while idle).
        /// Reuses the visited/queue buffers across frames to avoid GC churn.
        /// </summary>
        private void CalculateFillPreview()
        {
            if (_currentTool != TileEditorState.Tool.Fill
                || _fillPreviewTilemap == null
                || _fillPreviewNewTile == null)
            {
                if (_fillPreviewLastValid || _fillPreviewCells.Count > 0)
                {
                    _fillPreviewCells.Clear();
                    _fillPreviewLastValid = false;
                }
                return;
            }

            // Same hover cell as last compute → preview is still valid, no BFS.
            if (_fillPreviewLastValid && _fillPreviewLastHover == _hoverCell) return;

            _fillPreviewLastHover = _hoverCell;
            _fillPreviewLastValid = true;
            _fillPreviewCells.Clear();

            var startPos = new Vector3Int(_hoverCell.x, _hoverCell.y, 0);
            var targetTile = _fillPreviewTilemap.GetTile(startPos);

            // If target tile equals the new tile, painting would no-op — nothing
            // to highlight. Leave the cache valid so we don't BFS again until the
            // hover moves.
            if (targetTile == _fillPreviewNewTile) return;

            _fillPreviewVisited.Clear();
            _fillPreviewQueue.Clear();
            _fillPreviewQueue.Enqueue(_hoverCell);
            _fillPreviewVisited.Add(_hoverCell);

            int count = 0;
            while (_fillPreviewQueue.Count > 0 && count < FillPreviewMaxCells)
            {
                var pos = _fillPreviewQueue.Dequeue();
                var current = _fillPreviewTilemap.GetTile(new Vector3Int(pos.x, pos.y, 0));
                if (current != targetTile) continue;

                _fillPreviewCells.Add(pos);
                count++;

                for (int i = 0; i < FillPreviewDirections.Length; i++)
                {
                    var neighbor = pos + FillPreviewDirections[i];
                    if (_fillPreviewVisited.Add(neighbor))
                        _fillPreviewQueue.Enqueue(neighbor);
                }
            }
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void OnEnable()
        {
            // URP-safe hook: fires after each camera finishes rendering, with the
            // correct Camera reference passed in. Safe to call GL inside this callback.
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (_mat != null) Destroy(_mat);
        }

    }
}