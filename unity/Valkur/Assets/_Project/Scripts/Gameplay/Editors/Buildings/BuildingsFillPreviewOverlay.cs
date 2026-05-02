using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// World-space yellow blinking overlay that previews which cells the Fill tool
    /// will populate when committed. Uses RenderPipelineManager.endCameraRendering
    /// for URP 2022 compatibility (same pattern as TileEditorGridOverlay).
    ///
    /// Color: yellow semi-transparent (1, 1, 0, 0.4) with sine-wave alpha blink.
    /// Lifecycle: lazy-instantiated by BuildingsRuntimeEditor.Fill when entering
    /// AwaitingTile step; hidden (not destroyed) on ExitFillMode for reuse.
    /// </summary>
    public class BuildingsFillPreviewOverlay : MonoBehaviour
    {
        // Yellow blinking color matching TileEditorGridOverlay.FillPreviewColor.
        private static readonly Color FillColor = new Color(1f, 1f, 0f, 0.4f);
        private const float BlinkAmplitude = 0.2f;  // alpha oscillates by ±0.2
        private const float BlinkFrequency = 3f;    // radians per second

        [Tooltip("Camera used to set up the GL projection matrix. Bound via Initialize().")]
        [SerializeField] private Camera _targetCamera;

        private Material _mat;
        private readonly List<Vector3> _cellOrigins = new List<Vector3>();
        private float _blinkTime;

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Bind to the main camera. Must be called once before the overlay draws.</summary>
        public void Initialize(Camera cam)
        {
            _targetCamera = cam;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("[BuildingsFillPreviewOverlay] Shader 'Hidden/Internal-Colored' not found. " +
                                 "Fill preview will not render. Check URP shader stripping settings.");
                return;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",     (int)CullMode.Off);
            _mat.SetInt("_ZWrite",   0);
            _mat.SetInt("_ZTest",    (int)CompareFunction.Always);
        }

        /// <summary>
        /// Update the set of cells to preview. Each cell is rendered as a 1×1 world-unit
        /// quad positioned at tilemap.GetCellCenterWorld(c) minus (0.5, 0.5) so the quad
        /// aligns with the tile cell boundaries (matching TileEditorGridOverlay).
        /// </summary>
        public void SetCells(IEnumerable<Vector3Int> cells, Tilemap tilemap)
        {
            _cellOrigins.Clear();
            if (cells == null || tilemap == null) return;
            foreach (var c in cells)
            {
                // GetCellCenterWorld returns the center; shift to cell origin (bottom-left)
                // so the drawn quad covers exactly the tile cell (0..1, 0..1 in cell space).
                Vector3 center = tilemap.GetCellCenterWorld(c);
                _cellOrigins.Add(new Vector3(center.x - 0.5f, center.y - 0.5f, 0f));
            }
        }

        /// <summary>Clear all preview cells (stops drawing).</summary>
        public void Clear()
        {
            _cellOrigins.Clear();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            _cellOrigins.Clear();
        }

        private void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (_mat != null) Destroy(_mat);
        }

        // ── Rendering ────────────────────────────────────────────────────────────

        private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _targetCamera && _targetCamera != null) return;
            if (_mat == null) return;
            if (_cellOrigins.Count == 0) return;

            _blinkTime += Time.deltaTime;
            float alpha = FillColor.a + Mathf.Sin(_blinkTime * BlinkFrequency) * BlinkAmplitude;
            alpha = Mathf.Clamp01(alpha);
            var drawColor = new Color(FillColor.r, FillColor.g, FillColor.b, alpha);

            _mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;

            GL.Begin(GL.QUADS);
            GL.Color(drawColor);
            for (int i = 0; i < _cellOrigins.Count; i++)
            {
                float x = _cellOrigins[i].x;
                float y = _cellOrigins[i].y;
                GL.Vertex3(x,        y,        0f);
                GL.Vertex3(x + 1f,   y,        0f);
                GL.Vertex3(x + 1f,   y + 1f,   0f);
                GL.Vertex3(x,        y + 1f,   0f);
            }
            GL.End();

            GL.PopMatrix();
        }
    }
}
