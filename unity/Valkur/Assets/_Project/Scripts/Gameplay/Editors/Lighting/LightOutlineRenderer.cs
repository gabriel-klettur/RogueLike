using UnityEngine;
using Valkur.UIKit;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// World-space marker drawn on a placed light while the Lighting Editor's Alt overlay is
    /// on. Two concentric <see cref="LineRenderer"/>s, the same shape the Spawner Editor uses:
    ///   • Outer ring  → the light's own <c>pointLightOuterRadius</c> (per instance)
    ///   • Inner blob  → a small thick circle that reads as a clickable centre dot
    ///
    /// Mirrors <c>SpawnerOutlineRenderer</c> and is a separate type for the same reason that one
    /// is: the centre dot stays constant while the outer ring varies per instance.
    ///
    /// The one real difference is <see cref="SetVisible"/>: this renderer NEVER consults its
    /// target's <c>activeInHierarchy</c>. A light's GameObject is deactivated by two gates that
    /// have nothing to do with whether the light exists — the day/night window and the viewport
    /// cull — and hiding the marker with them would blank the overlay in daylight and off
    /// screen, which is precisely when the author cannot see the lights themselves.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightOutlineRenderer : MonoBehaviour
    {
        private const int   CIRCLE_SEGMENTS = 32;
        private const int   CENTER_SEGMENTS = 16;
        private const float DEFAULT_RADIUS  = 1f;
        private const float MIN_RADIUS      = 0.25f;

        // Centre dot — small thick circle, looks like a filled marker.
        private const float CENTER_DOT_RADIUS          = 0.10f;
        private const float CENTER_DOT_THICKNESS       = 0.18f;
        private const float CENTER_DOT_HOVER_RADIUS    = 0.16f;
        private const float CENTER_DOT_HOVER_THICKNESS = 0.26f;
        private static readonly Color CENTER_DOT_COLOR       = UITheme.MARKER_DOT;
        private static readonly Color CENTER_DOT_HOVER_COLOR = UITheme.MARKER_DOT_HOVER;

        private LineRenderer _ring;
        private LineRenderer _centerDot;

        private static Material s_lineMat;

        /// <summary>
        /// Domain Reload is OFF, so a Material cached in a static survives Stop and comes back as
        /// a destroyed Unity object on the next Play. Reset it rather than adding a line to
        /// Tests/EditMode/Baselines/unreset-statics.txt — the five older outline renderers are
        /// grandfathered there, and a ratchet that keeps accepting new entries is a list.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_lineMat = null;
        }

        private Transform _target;
        private float     _radius         = DEFAULT_RADIUS;
        private float     _thicknessWorld = 0.06f;
        private Color     _color          = UITheme.MARKER_RING;
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
        /// Repaint the ring. A derived light — one owned by a building — is drawn in a different
        /// colour from an authored one, because the editor refuses to move or delete it and a
        /// marker that looks identical promises an edit that will be turned down.
        /// </summary>
        public void SetColor(Color color)
        {
            if (_color == color) return;
            _color = color;
            ApplyVisuals();
        }

        /// <summary>
        /// Toggles the hover affordance on the centre dot — when hovered, the dot grows and
        /// turns brighter cyan to signal "click to select".
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
            // Deliberately no activeInHierarchy test on the target — see the class summary.
            if (_target == null || _ring == null) { SetVisible(false); return; }

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
