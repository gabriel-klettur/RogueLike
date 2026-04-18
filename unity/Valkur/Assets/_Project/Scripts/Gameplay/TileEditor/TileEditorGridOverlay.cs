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
        // White at 55% alpha — highly visible without fully obscuring tile art.
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.55f);

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
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",     (int)CullMode.Off);
            _mat.SetInt("_ZWrite",   0);
            // Always pass depth test so the grid overlays tile art cleanly.
            _mat.SetInt("_ZTest",    (int)CompareFunction.Always);
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
    }
}
