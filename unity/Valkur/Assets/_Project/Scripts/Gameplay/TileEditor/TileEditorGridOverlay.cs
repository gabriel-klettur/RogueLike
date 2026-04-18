using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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

        // Border thickness of all cell highlights, in screen pixels.
        private const float HoverThicknessPx = 3f;

        // Extra cells drawn beyond the visible edge to avoid pop-in when panning.
        private const int Margin = 2;

        private Material _mat;
        private Camera _targetCamera;

        // State pushed each frame by TileEditorManager.
        private Vector2Int  _hoverCell;
        private Vector2Int? _selectedCell;
        private readonly HashSet<Vector2Int> _brushCells = new HashSet<Vector2Int>();

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

        /// <summary>Replace the set of cells painted by the active brush stroke (shown in YELLOW). Pass null or empty to clear.</summary>
        public void SetBrushStrokeCells(IEnumerable<Vector3Int> cells)
        {
            _brushCells.Clear();
            if (cells == null) return;
            foreach (var c in cells)
                _brushCells.Add(new Vector2Int(c.x, c.y));
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

            // ── Cell highlight quads: hover (cyan) / selected (green) / brush (yellow) ──
            // Thickness is the same for all; compute once from current zoom level.
            float pixelSize = (cam.orthographicSize * 2f) / Screen.height;
            float t = pixelSize * HoverThicknessPx;

            GL.Begin(GL.QUADS);

            // Yellow — all cells touched during the current brush drag (drawn first / underneath).
            foreach (var c in _brushCells)
                DrawBorderQuads(c.x, c.y, BrushColor, t);

            // Green — selected / last-clicked cell.
            if (_selectedCell.HasValue)
                DrawBorderQuads(_selectedCell.Value.x, _selectedCell.Value.y, SelectedColor, t);

            // Cyan — cell currently under the mouse (drawn last / on top).
            DrawBorderQuads(_hoverCell.x, _hoverCell.y, HoverColor, t);

            GL.End();

            GL.PopMatrix();
        }

        /// <summary>
        /// Emits 4 GL quads that form a thick border around the tile cell at (cx, cy).
        /// Must be called between GL.Begin(GL.QUADS) and GL.End().
        /// </summary>
        private static void DrawBorderQuads(float cx, float cy, Color color, float t)
        {
            float x0 = cx;
            float y0 = cy;
            float x1 = cx + 1f;
            float y1 = cy + 1f;

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
    }
}
