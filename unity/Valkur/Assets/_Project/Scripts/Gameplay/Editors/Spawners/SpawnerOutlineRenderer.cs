using UnityEngine;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// World-space outline drawn around a <see cref="SpawnerInstance"/> position.
    /// Used by the runtime Spawner Editor (F3) Alt-toggle to highlight every
    /// spawner on the map. Renders two concentric LineRenderers:
    ///   • Outer ring  → the spawner's <c>triggerRadius</c> (per-instance)
    ///   • Inner blob  → a small thick circle that reads as a clickable centre dot
    ///
    /// Mirrors <c>ParticleEmitterOutlineRenderer</c> but is split into a separate
    /// type so the centre dot can stay constant while the outer ring varies per
    /// instance via <see cref="SetRadius"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpawnerOutlineRenderer : MonoBehaviour
    {
        private const int   CIRCLE_SEGMENTS = 32;
        private const int   CENTER_SEGMENTS = 16;
        private const float DEFAULT_RADIUS  = 1f;
        private const float MIN_RADIUS      = 0.25f;

        // Centre dot — small thick circle, looks like a filled marker.
        private const float CENTER_DOT_RADIUS           = 0.10f;
        private const float CENTER_DOT_THICKNESS        = 0.18f;
        private const float CENTER_DOT_HOVER_RADIUS     = 0.16f;
        private const float CENTER_DOT_HOVER_THICKNESS  = 0.26f;
        private static readonly Color CENTER_DOT_COLOR       = new Color(1f, 0.95f, 0.30f, 1f);
        private static readonly Color CENTER_DOT_HOVER_COLOR = new Color(0.40f, 1f, 1f, 1f);

        private LineRenderer _ring;
        private LineRenderer _centerDot;

        private static Material s_lineMat;

        private Transform _target;
        private float     _radius          = DEFAULT_RADIUS;
        private float     _thicknessWorld  = 0.06f;
        private Color     _color           = new Color(1f, 0.65f, 0.20f, 0.85f);
        private bool      _hovered;

        public void Configure(Color color, float thicknessWorld, float radius)
        {
            _color          = color;
            _thicknessWorld = thicknessWorld;
            _radius         = radius > 0f ? Mathf.Max(radius, MIN_RADIUS) : DEFAULT_RADIUS;
            EnsureChildren();
            ApplyVisuals();
        }

        public void Follow(Transform target) => _target = target;

        public void SetRadius(float radius)
        {
            _radius = radius > 0f ? Mathf.Max(radius, MIN_RADIUS) : DEFAULT_RADIUS;
        }

        /// <summary>
        /// Toggles the hover affordance on the centre dot — when hovered, the
        /// dot grows + becomes brighter cyan to signal "click to inspect".
        /// </summary>
        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered) return;
            _hovered = hovered;
            ApplyCenterDotVisuals();
        }

        public bool IsHovered => _hovered;

        public void SetVisible(bool visible)
        {
            if (_ring      != null) _ring.enabled      = visible;
            if (_centerDot != null) _centerDot.enabled = visible;
        }

        private void EnsureChildren()
        {
            EnsureSharedMat();

            if (_ring == null)
            {
                var go = new GameObject("Ring");
                go.transform.SetParent(transform, false);
                _ring = ConfigureLineRenderer(go, CIRCLE_SEGMENTS, sortingOrder: 5000);
            }

            if (_centerDot == null)
            {
                var go = new GameObject("CenterDot");
                go.transform.SetParent(transform, false);
                // Centre dot renders on top of the ring.
                _centerDot = ConfigureLineRenderer(go, CENTER_SEGMENTS, sortingOrder: 5001);
                ApplyCenterDotVisuals();
            }
        }

        private void ApplyCenterDotVisuals()
        {
            if (_centerDot == null) return;
            float thickness = _hovered ? CENTER_DOT_HOVER_THICKNESS : CENTER_DOT_THICKNESS;
            Color color     = _hovered ? CENTER_DOT_HOVER_COLOR     : CENTER_DOT_COLOR;
            _centerDot.startWidth = thickness;
            _centerDot.endWidth   = thickness;
            _centerDot.startColor = color;
            _centerDot.endColor   = color;
        }

        private static void EnsureSharedMat()
        {
            if (s_lineMat != null) return;
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh != null) s_lineMat = new Material(sh);
        }

        private static LineRenderer ConfigureLineRenderer(GameObject go, int segments, int sortingOrder)
        {
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.positionCount     = segments;
            lr.numCornerVertices = 0;
            lr.numCapVertices    = 0;
            lr.alignment         = LineAlignment.View;
            lr.sortingLayerName  = "VFX";
            lr.sortingOrder      = sortingOrder;
            if (s_lineMat != null) lr.sharedMaterial = s_lineMat;
            return lr;
        }

        private void ApplyVisuals()
        {
            if (_ring == null) return;
            _ring.startColor = _color;
            _ring.endColor   = _color;
            _ring.startWidth = _thicknessWorld;
            _ring.endWidth   = _thicknessWorld;
        }

        private void LateUpdate()
        {
            if (_target == null || _ring == null) { SetVisible(false); return; }
            if (!_target.gameObject.activeInHierarchy) { SetVisible(false); return; }

            SetVisible(true);

            Vector3 center = new Vector3(_target.position.x, _target.position.y, 0f);

            DrawCircle(_ring,      center, _radius,                                          CIRCLE_SEGMENTS);
            DrawCircle(_centerDot, center, _hovered ? CENTER_DOT_HOVER_RADIUS : CENTER_DOT_RADIUS, CENTER_SEGMENTS);
        }

        private static void DrawCircle(LineRenderer lr, Vector3 center, float radius, int segments)
        {
            float step = 2f * Mathf.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * step;
                lr.SetPosition(i, center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }
        }
    }
}
