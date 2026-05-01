using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Draws the active paths of a <see cref="CompositeCollider2D"/> as cyan wireframe
    /// lines in the game view during play mode. Used by BuildingsRuntimeEditor to
    /// make wall / tilemap collision visible alongside building collision overlays.
    ///
    /// Added programmatically by <c>BuildingsRuntimeEditor.ToggleCollidersVisible()</c>;
    /// hidden from the Add Component menu to avoid accidental manual attachment.
    ///
    /// Rendering uses <see cref="LineRenderer"/> child GameObjects (one per composite
    /// path) so the lines pass through the URP 2D rendering pipeline correctly —
    /// matching the approach used by <see cref="BuildingColliderDebugOverlay"/>.
    /// <c>GL.Lines</c> + <c>OnRenderObject</c> is invisible in URP 2D.
    /// </summary>
    [AddComponentMenu("")]          // Hidden — managed by BuildingsRuntimeEditor
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CompositeCollider2D))]
    public sealed class TilemapColliderDebugOverlay : MonoBehaviour
    {
        private const float LINE_WIDTH    = 0.05f;
        private const float Z_OFFSET      = -0.1f;
        private const string VISUAL_PREFIX = "_TilemapCollDebug_";

        private static Material s_mat;
        private static readonly Color PathColor = new Color(0f, 0.85f, 1f, 0.90f);  // cyan

        private CompositeCollider2D _composite;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();

        // Reusable buffer — grows as needed, never shrinks.
        private Vector2[] _pathBuffer = new Vector2[256];

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            _composite = GetComponent<CompositeCollider2D>();
            enabled = false;   // stay off until BuildingsRuntimeEditor activates us
        }

        private void OnDestroy() => ClearLines();

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Show or hide the tilemap collision overlay.
        /// Calling <c>SetVisible(true)</c> samples the current composite paths and
        /// creates one <see cref="LineRenderer"/> child per path. Calling
        /// <c>SetVisible(false)</c> destroys those children immediately.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (visible)
            {
                enabled = true;
                RebuildLines();
            }
            else
            {
                ClearLines();
                enabled = false;
            }
        }

        // ── Line management ───────────────────────────────────────────────────────

        private void RebuildLines()
        {
            ClearLines();
            if (_composite == null) return;

            EnsureMaterial();

            for (int p = 0; p < _composite.pathCount; p++)
            {
                int count = _composite.GetPathPointCount(p);
                if (count < 2) continue;

                if (_pathBuffer.Length < count)
                    _pathBuffer = new Vector2[count + 64];

                _composite.GetPath(p, _pathBuffer);

                // Child lives under this transform so its local space == composite
                // local space, matching the coordinate system of GetPath().
                var child = new GameObject($"{VISUAL_PREFIX}{p}")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                child.transform.SetParent(transform, worldPositionStays: false);

                var lr = child.AddComponent<LineRenderer>();
                lr.useWorldSpace          = false;   // positions are in composite local space
                lr.loop                   = true;    // close the polygon
                lr.widthMultiplier        = LINE_WIDTH;
                lr.material               = s_mat;
                lr.startColor             = PathColor;
                lr.endColor               = PathColor;
                lr.positionCount          = count;
                lr.shadowCastingMode      = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows         = false;
                lr.sortingLayerName       = "Overhead";
                lr.sortingOrder           = 32000;

                for (int i = 0; i < count; i++)
                    lr.SetPosition(i, new Vector3(_pathBuffer[i].x, _pathBuffer[i].y, Z_OFFSET));

                _lines.Add(lr);
            }
        }

        private void ClearLines()
        {
            foreach (var lr in _lines)
            {
                if (lr != null)
                    DestroyImmediate(lr.gameObject);
            }
            _lines.Clear();
        }

        // ── Material ──────────────────────────────────────────────────────────────

        private static void EnsureMaterial()
        {
            if (s_mat != null) return;

            // Use the same shader as BuildingColliderDebugOverlay for URP 2D compatibility.
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Sprites/Default");

            if (shader != null)
                s_mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }
}
