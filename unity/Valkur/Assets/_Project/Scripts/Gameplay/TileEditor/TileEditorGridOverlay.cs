using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

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
    public class TileEditorGridOverlay : MonoBehaviour
    {
        // White at 20% alpha — subtle grid guides without saturating the screen.
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.20f);

        // Cyan  — hovered cell (follows mouse).
        private static readonly Color HoverColor    = new Color(0f,  1f,  1f,  1f);
        // Green — last selected / single-click cell.
        private static readonly Color SelectedColor = new Color(0f,  1f,  0f,  1f);
        // Yellow — cells painted during an active brush drag.
        private static readonly Color BrushColor    = new Color(1f,  1f,  0f,  1f);

        // Red overlay drawn over every solid Collision cell when the Colliders panel
        // toggles "Show Colliders" ON. The fill is intentionally translucent so the
        // underlying ground/floor tile remains visible; the border is fully opaque.
        private static readonly Color ColliderFillColor   = new Color(1f, 0.10f, 0.15f, 0.32f);
        private static readonly Color ColliderBorderColor = new Color(1f, 0.10f, 0.15f, 1f);

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

        // Collider overlay state (Colliders panel).
        private Tilemap _collisionTilemap;
        private bool    _showColliderOverlay;

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

        /// <summary>Set the brush size used to render the cyan hover and green selection borders (1–5).</summary>
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
        /// Bind the Collision tilemap that the overlay should sample to draw the red
        /// collider visualization. Pass null to disable collider sampling entirely.
        /// </summary>
        public void SetCollisionTilemap(Tilemap tilemap) => _collisionTilemap = tilemap;

        /// <summary>Enable or disable the red collider overlay.</summary>
        public void SetShowColliderOverlay(bool show) => _showColliderOverlay = show;

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

        // ── Rendering ────────────────────────────────────────────────────────

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (_mat == null || _targetCamera == null) return;
            if (cam != _targetCamera) return;

            DrawGrid(cam);
        }

        private void DrawGrid(Camera cam)
        {
            // Camera world-space orthographic bounds.
            float halfH  = cam.orthographicSize;
            float halfW  = halfH * cam.aspect;
            Vector3 pos  = cam.transform.position;

            float left   = pos.x - halfW;
            float right  = pos.x + halfW;
            float bottom = pos.y - halfH;
            float top    = pos.y + halfH;

            // Integer tile boundaries, extended by Margin to avoid visible seams on pan.
            int xMin = Mathf.FloorToInt(left)   - Margin;
            int xMax = Mathf.CeilToInt(right)   + Margin;
            int yMin = Mathf.FloorToInt(bottom) - Margin;
            int yMax = Mathf.CeilToInt(top)     + Margin;

            // Defensive cap: at extreme zoom-out the grid would emit thousands of GL
            // line segments per frame. Skip drawing rather than tank FPS.
            const int MaxLinesPerAxis = 400;
            if ((xMax - xMin) > MaxLinesPerAxis || (yMax - yMin) > MaxLinesPerAxis)
                return;

            // Update hovered cell from current mouse position.
            Vector3 mouseWorld = cam.ScreenToWorldPoint(
                new Vector3(Input.mousePosition.x, Input.mousePosition.y, -cam.transform.position.z));
            _hoverCell = new Vector2Int(Mathf.FloorToInt(mouseWorld.x), Mathf.FloorToInt(mouseWorld.y));

            _mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;

            // ── Regular grid (lines) ──────────────────────────────────────────
            GL.Begin(GL.LINES);
            GL.Color(GridColor);

            for (int x = xMin; x <= xMax; x++)
            {
                GL.Vertex3(x, yMin, 0f);
                GL.Vertex3(x, yMax, 0f);
            }
            for (int y = yMin; y <= yMax; y++)
            {
                GL.Vertex3(xMin, y, 0f);
                GL.Vertex3(xMax, y, 0f);
            }
            GL.End();

            // ── Collider overlay: red fill + red border for every solid Collision cell ──
            // Drawn between the grid lines and the cell highlights so the hover/selection
            // borders remain on top of the collider shading.
            if (_showColliderOverlay && _collisionTilemap != null)
                DrawColliderOverlay(cam, xMin, yMin, xMax, yMax);

            // ── Cell highlight quads: hover (cyan) / selected (green) / brush (yellow) ──
            // Thickness is the same for all; compute once from current zoom level.
            float pixelSize = (cam.orthographicSize * 2f) / Screen.height;
            float t = pixelSize * HoverThicknessPx;

            GL.Begin(GL.QUADS);

            // Yellow (Brush/Erase) or Green (Select) — all cells in the current stroke.
            Color strokeColor = _currentTool == TileEditorState.Tool.Select ? SelectedColor : BrushColor;
            foreach (var c in _brushCells)
                DrawBorderQuads(c.x, c.y, strokeColor, t);

            // Green — selected / last-clicked cell, sized to brush footprint (cursor = top-left,
            // footprint extends right + down).
            if (_selectedCell.HasValue)
                DrawBorderRect(_selectedCell.Value.x, _selectedCell.Value.y - (_brushSize - 1),
                               _brushSize, _brushSize, SelectedColor, t);

            // Cyan — hover, sized to brush footprint (drawn last / on top).
            DrawBorderRect(_hoverCell.x, _hoverCell.y - (_brushSize - 1),
                           _brushSize, _brushSize, HoverColor, t);

            GL.End();

            GL.PopMatrix();
        }

        /// <summary>
        /// Emits 4 GL quads that form a thick border around the tile cell at (cx, cy).
        /// Must be called between GL.Begin(GL.QUADS) and GL.End().
        /// </summary>
        private static void DrawBorderQuads(float cx, float cy, Color color, float t)
        {
            DrawBorderRect(cx, cy, 1, 1, color, t);
        }

        /// <summary>
        /// Emits 4 GL quads forming a thick border around an N×M cell rectangle whose
        /// bottom-left corner is at (cx, cy). Must be called between GL.Begin(GL.QUADS) and GL.End().
        /// </summary>
        private static void DrawBorderRect(float cx, float cy, int w, int h, Color color, float t)
        {
            float x0 = cx;
            float y0 = cy;
            float x1 = cx + w;
            float y1 = cy + h;

            GL.Color(color);

            // Bottom
            GL.Vertex3(x0,     y0,     0f); GL.Vertex3(x1,     y0,     0f);
            GL.Vertex3(x1,     y0 + t, 0f); GL.Vertex3(x0,     y0 + t, 0f);
            // Top
            GL.Vertex3(x0,     y1 - t, 0f); GL.Vertex3(x1,     y1 - t, 0f);
            GL.Vertex3(x1,     y1,     0f); GL.Vertex3(x0,     y1,     0f);
            // Left (inset so corners don't overlap)
            GL.Vertex3(x0,     y0 + t, 0f); GL.Vertex3(x0 + t, y0 + t, 0f);
            GL.Vertex3(x0 + t, y1 - t, 0f); GL.Vertex3(x0,     y1 - t, 0f);
            // Right
            GL.Vertex3(x1 - t, y0 + t, 0f); GL.Vertex3(x1,     y0 + t, 0f);
            GL.Vertex3(x1,     y1 - t, 0f); GL.Vertex3(x1 - t, y1 - t, 0f);
        }

        /// <summary>
        /// Draws a translucent red fill and an opaque red border for every solid cell
        /// of the bound Collision tilemap that lies within the visible rect.
        /// Uses <see cref="Tilemap.GetTilesBlock"/> to fetch all candidate tiles in a
        /// single managed call instead of N individual <c>GetTile</c> queries.
        /// </summary>
        private void DrawColliderOverlay(Camera cam, int xMin, int yMin, int xMax, int yMax)
        {
            // Clip the visible window against the tilemap's painted bounds so we never
            // sample empty regions of the world (massive zones with sparse colliders).
            var bounds = _collisionTilemap.cellBounds;
            int sx = Mathf.Max(xMin, bounds.xMin);
            int sy = Mathf.Max(yMin, bounds.yMin);
            int ex = Mathf.Min(xMax, bounds.xMax);
            int ey = Mathf.Min(yMax, bounds.yMax);

            int w = ex - sx;
            int h = ey - sy;
            if (w <= 0 || h <= 0) return;
            if (w * h > MaxColliderCells) return;

            var rect = new BoundsInt(sx, sy, 0, w, h, 1);
            TileBase[] tiles;
            try { tiles = _collisionTilemap.GetTilesBlock(rect); }
            catch { return; }
            if (tiles == null || tiles.Length == 0) return;

            float pixelSize = (cam.orthographicSize * 2f) / Screen.height;
            float t = pixelSize * ColliderBorderThicknessPx;

            // Translucent red fill — one quad per painted cell.
            GL.Begin(GL.QUADS);
            GL.Color(ColliderFillColor);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                int cx = sx + (i % w);
                int cy = sy + (i / w);
                GL.Vertex3(cx,        cy,        0f);
                GL.Vertex3(cx + 1f,   cy,        0f);
                GL.Vertex3(cx + 1f,   cy + 1f,   0f);
                GL.Vertex3(cx,        cy + 1f,   0f);
            }
            GL.End();

            // Opaque red border — 4 thin quads per painted cell.
            GL.Begin(GL.QUADS);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                int cx = sx + (i % w);
                int cy = sy + (i / w);
                DrawBorderQuads(cx, cy, ColliderBorderColor, t);
            }
            GL.End();
        }
    }
}
