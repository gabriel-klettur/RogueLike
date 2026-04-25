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

    }
}