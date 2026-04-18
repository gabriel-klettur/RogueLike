using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// World-space tile grid overlay for the Tile Editor.
    /// Draws white cell borders matching the 1×1 world-unit tile grid (PPU = 16, cellSize = 1)
    /// so painters can see exactly which tiles they are affecting.
    ///
    /// Rendering uses GL immediate mode inside OnRenderObject, which is called by Unity's
    /// rendering loop regardless of pipeline (works in URP 2022). The grid is confined to the
    /// camera's visible frustum and recomputed every frame so it remains correct when the
    /// player pans or zooms.
    ///
    /// Activation is managed by TileEditorManager: the GameObject is enabled/disabled
    /// together with the rest of the editor visuals.
    /// </summary>
    public class TileEditorGridOverlay : MonoBehaviour
    {
        // White at 20% alpha — readable without obscuring tile art.
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.20f);

        // Extra cells drawn beyond the visible edge to avoid pop-in when panning.
        private const int Margin = 2;

        private Material _mat;
        private Camera _targetCamera;

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
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite",   0);
            // Always pass depth test so the grid overlays tile art cleanly.
            _mat.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        // ── Rendering ────────────────────────────────────────────────────────

        /// <summary>
        /// Called by Unity after the scene is rendered for each camera.
        /// We guard against non-target cameras (e.g. scene view, secondary cams).
        /// Vertex coordinates are in world space: GL.LoadProjectionMatrix +
        /// GL.modelview = worldToCameraMatrix together replicate the standard MVP.
        /// </summary>
        private void OnRenderObject()
        {
            if (_mat == null || _targetCamera == null) return;

            var cam = Camera.current;
            if (cam == null || cam != _targetCamera) return;

            // Camera world-space bounds (orthographic).
            float halfH  = cam.orthographicSize;
            float halfW  = halfH * cam.aspect;
            Vector3 pos  = cam.transform.position;

            float left   = pos.x - halfW;
            float right  = pos.x + halfW;
            float bottom = pos.y - halfH;
            float top    = pos.y + halfH;

            // Integer tile coords, extended by Margin to hide seams at edges.
            int xMin = Mathf.FloorToInt(left)   - Margin;
            int xMax = Mathf.CeilToInt(right)   + Margin;
            int yMin = Mathf.FloorToInt(bottom) - Margin;
            int yMax = Mathf.CeilToInt(top)     + Margin;

            _mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;

            GL.Begin(GL.LINES);
            GL.Color(GridColor);

            // Vertical cell borders (one line per column boundary).
            for (int x = xMin; x <= xMax; x++)
            {
                GL.Vertex3(x, yMin, 0f);
                GL.Vertex3(x, yMax, 0f);
            }

            // Horizontal cell borders (one line per row boundary).
            for (int y = yMin; y <= yMax; y++)
            {
                GL.Vertex3(xMin, y, 0f);
                GL.Vertex3(xMax, y, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
