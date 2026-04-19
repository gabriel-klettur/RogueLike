using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Attaches to a floating Tile-Editor panel root (RectTransform).
    /// Features:
    ///   • Draggable from <see cref="DragHeader"/> only.
    ///   • Clamped to Canvas bounds at all times.
    ///   • Auto-snap to canvas edges and to neighbouring panel edges on release.
    ///   • Smooth snap animation (lerp, unscaled time).
    ///   • Focus management: clicking the panel brings it to the front (SetAsLastSibling).
    ///   • Minimize / Maximize / Close via header buttons.
    ///   • Set <see cref="EnableInterPanelSnap"/> = false per instance, or
    ///     <see cref="GlobalInterPanelSnap"/> = false to disable globally.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DraggablePanel : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        // ── Inspector-wired by TileEditorUIBuilder ──────────────────────────
        [Tooltip("Header RectTransform — drag is only initiated when pointer presses here.")]
        public RectTransform DragHeader;

        [Tooltip("Container for all panel content below the header. Hidden when minimized.")]
        public GameObject ContentRoot;

        [Tooltip("Height (canvas units) to expand to when maximized.")]
        public float MaximizedHeight = 700f;

        [SerializeField, Tooltip("Canvas-unit proximity to an edge that triggers auto-snap.")]
        public float SnapTolerance = 18f;

        [SerializeField, Tooltip("Lerp speed for snap animation (unscaled time).")]
        public float SnapAnimSpeed = 14f;

        [SerializeField, Tooltip("Whether this panel can snap to other DraggablePanel edges.")]
        public bool EnableInterPanelSnap = true;

        /// <summary>Global toggle — set false to disable inter-panel snapping for ALL panels.</summary>
        public static bool GlobalInterPanelSnap = true;

        /// <summary>
        /// Pixels reserved at the TOP of the canvas that no panel may overlap (e.g. menu bar height).
        /// Set once at startup by <c>TileEditorManager</c>. Affects clamping AND canvas-edge snap.
        /// </summary>
        public static float TopReservedPx;

        /// <summary>Pixels reserved at the BOTTOM of the canvas that no panel may overlap.</summary>
        public static float BottomReservedPx;

        /// <summary>Pixels reserved at the LEFT  of the canvas that no panel may overlap.</summary>
        public static float LeftReservedPx;

        /// <summary>Pixels reserved at the RIGHT of the canvas that no panel may overlap.</summary>
        public static float RightReservedPx;

        // ── External callback — set by TileEditorUI.Builder ────────────────
        public System.Action OnClose;

        // ── Resize-tracking enums ───────────────────────────────────────────
        private enum EdgeSnapX { None, Left, Right }
        private enum EdgeSnapY { None, Top, Bottom }

        // ── Private state ───────────────────────────────────────────────────
        private RectTransform _rt;
        private RectTransform _canvasRt;
        private Canvas        _canvas;

        private bool  _isDragging;
        private bool  _anchorNormalized;   // accessible within class for inter-panel snap

        private bool  _minimized;
        private bool  _maximized;
        private float _restoredHeight;

        private Vector2 _snapTarget;
        private bool    _snapping;

        // ── Edge-snap affinity (used to respond to canvas resize) ───────────
        // Tracks which canvas edges this panel is currently anchored to and the
        // pixel offset from that edge.  Set on first normalization (from initial
        // dock) and updated whenever the user drags/snaps the panel.
        private EdgeSnapX _edgeSnapX    = EdgeSnapX.None;
        private EdgeSnapY _edgeSnapY    = EdgeSnapY.None;
        private float     _edgeOffsetX;   // px offset from the snapped x-edge
        private float     _edgeOffsetY;   // px offset from the snapped y-edge
        private Vector2   _lastCanvasSize; // for resize detection (valid once normalized)

        // ── Static panel registry (class-level, all active panels) ──────────
        private static readonly List<DraggablePanel> _allPanels = new List<DraggablePanel>();

        // ── Life-cycle ──────────────────────────────────────────────────────
        private void Awake()
        {
            _rt             = GetComponent<RectTransform>();
            _canvas         = GetComponentInParent<Canvas>();
            _canvasRt       = _canvas != null ? _canvas.GetComponent<RectTransform>() : null;
            _restoredHeight = _rt.sizeDelta.y;
        }

        private void OnEnable()
        {
            if (!_allPanels.Contains(this)) _allPanels.Add(this);
            // Normalize anchor eagerly so other panels can snap to us even before we're dragged.
            if (!_anchorNormalized)
                StartCoroutine(NormalizeNextFrame());
        }

        private void OnDisable()
        {
            _allPanels.Remove(this);
            _snapping = false;
        }

        private void OnDestroy() => _allPanels.Remove(this);

        private IEnumerator NormalizeNextFrame()
        {
            yield return null;  // wait one frame for canvas layout to be fully computed
            NormalizeAnchor();
        }

        // ── IPointerDownHandler — focus ─────────────────────────────────────
        public void OnPointerDown(PointerEventData _) => transform.SetAsLastSibling();

        // ── Drag handlers ───────────────────────────────────────────────────
        public void OnBeginDrag(PointerEventData e)
        {
            _snapping   = false;
            _isDragging = DragHeader != null &&
                          RectTransformUtility.RectangleContainsScreenPoint(
                              DragHeader, e.pressPosition, e.pressEventCamera);
            if (_isDragging) NormalizeAnchor();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_isDragging || _canvasRt == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, e.position, e.pressEventCamera, out var cur);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, e.position - e.delta, e.pressEventCamera, out var prev);
            _rt.anchoredPosition += cur - prev;
            ClampToBounds();
        }

        public void OnEndDrag(PointerEventData _)
        {
            if (!_isDragging) return;
            _isDragging = false;
            TrySnap();
            // Update edge affinity so resize can maintain this panel's position
            // relative to whatever canvas edge it just settled near.
            UpdateEdgeAffinityFromDrag(_snapping ? _snapTarget : _rt.anchoredPosition);
        }

        // ── Snap animation + canvas-resize response ─────────────────────────
        private void Update()
        {
            if (_snapping)
            {
                _rt.anchoredPosition = Vector2.Lerp(
                    _rt.anchoredPosition, _snapTarget,
                    Time.unscaledDeltaTime * SnapAnimSpeed);
                if (Vector2.Distance(_rt.anchoredPosition, _snapTarget) < 0.5f)
                {
                    _rt.anchoredPosition = _snapTarget;
                    _snapping = false;
                }
            }

            // Detect canvas resize and re-position normalized panels accordingly.
            // Un-normalized panels still use corner anchors, so Unity handles them.
            if (_anchorNormalized && _canvasRt != null && _lastCanvasSize.sqrMagnitude > 0f)
            {
                var cur = new Vector2(_canvasRt.rect.width, _canvasRt.rect.height);
                if (cur != _lastCanvasSize)
                {
                    OnCanvasSizeChanged(cur);
                    _lastCanvasSize = cur;
                }
            }
        }

        // ── Window controls ─────────────────────────────────────────────────

        /// <summary>Collapse the panel to header height only.</summary>
        public void Minimize()
        {
            if (_minimized) return;
            _minimized = true;
            _maximized = false;
            if (ContentRoot != null) ContentRoot.SetActive(false);
            float hdrH = DragHeader != null ? DragHeader.sizeDelta.y : 24f;
            _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, hdrH);
        }

        /// <summary>Toggle between maximized (full canvas height) and restored size.</summary>
        public void Maximize()
        {
            _minimized = false;
            if (ContentRoot != null) ContentRoot.SetActive(true);
            _maximized = !_maximized;
            if (_maximized)
            {
                float targetH = _canvasRt != null
                    ? Mathf.Max(_restoredHeight, _canvasRt.rect.height - 34f)
                    : MaximizedHeight;
                _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, targetH);
            }
            else
            {
                _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, _restoredHeight);
            }
        }

        /// <summary>Close (hide) this panel via the registered callback.</summary>
        public void ClosePanel() => OnClose?.Invoke();

        // ── Anchor normalization ────────────────────────────────────────────
        /// <summary>
        /// Converts the panel from its initial corner anchor/pivot to a uniform top-left (0,1)
        /// anchor so that anchoredPosition = (pixelsFromLeft, -pixelsFromTop).
        /// This is required for consistent clamping, snapping, and inter-panel comparison
        /// across all screen resolutions.
        /// </summary>
        private void NormalizeAnchor()
        {
            if (_anchorNormalized || _canvasRt == null) return;

            // Capture initial dock from corner anchor BEFORE modifying anything.
            // TopRight/BottomRight panels have anchorMin.x == 1; Bottom panels have anchorMin.y == 0.
            bool  wasRightAnchored  = _rt.anchorMin.x > 0.5f;
            bool  wasBottomAnchored = _rt.anchorMin.y < 0.5f;
            float cornerOffX = Mathf.Abs(_rt.anchoredPosition.x);
            float cornerOffY = Mathf.Abs(_rt.anchoredPosition.y);

            _anchorNormalized = true;

            // Capture the world-space top-left corner before touching anchors.
            var corners = new Vector3[4];
            _rt.GetWorldCorners(corners);
            // corners[1] = world-space top-left corner (Unity winding: BL, TL, TR, BR).

            _rt.anchorMin = new Vector2(0f, 1f);
            _rt.anchorMax = new Vector2(0f, 1f);
            _rt.pivot     = new Vector2(0f, 1f);

            var cam = _canvas.worldCamera;
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, screenPos, cam, out Vector2 localPos);

            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            // localPos is canvas-center-relative; shift to top-left-relative.
            _rt.anchoredPosition = localPos - new Vector2(-cW * 0.5f, cH * 0.5f);

            // Record the initial dock affinity so resize can maintain edge offsets.
            _edgeSnapX   = wasRightAnchored  ? EdgeSnapX.Right  : EdgeSnapX.Left;
            _edgeSnapY   = wasBottomAnchored ? EdgeSnapY.Bottom : EdgeSnapY.Top;
            _edgeOffsetX = cornerOffX;
            _edgeOffsetY = cornerOffY;

            // Start canvas-size monitoring.
            _lastCanvasSize = new Vector2(cW, cH);
        }

        // ── Canvas-resize response ──────────────────────────────────────────

        /// <summary>
        /// Called when the canvas rect changes size.  Panels that are edge-snapped
        /// maintain their pixel offset from that edge; all others are clamped to the
        /// new bounds so they never float off-screen.
        /// </summary>
        private void OnCanvasSizeChanged(Vector2 newSize)
        {
            if (_snapping) _snapping = false;  // abort in-progress snap

            float pW = _rt.rect.width;
            float pH = _rt.rect.height;
            var   p  = _rt.anchoredPosition;

            switch (_edgeSnapX)
            {
                case EdgeSnapX.Left:  p.x = _edgeOffsetX;                        break;
                case EdgeSnapX.Right: p.x = newSize.x - pW - _edgeOffsetX;       break;
                // None: keep current x, ClampToBounds will correct if out of range
            }
            switch (_edgeSnapY)
            {
                case EdgeSnapY.Top:    p.y = -_edgeOffsetY;                       break;
                case EdgeSnapY.Bottom: p.y = -(newSize.y - pH - _edgeOffsetY);    break;
                // None: keep current y
            }

            _rt.anchoredPosition = p;
            ClampToBounds();
        }

        /// <summary>
        /// Updates edge-snap affinity after a drag (or snap-animation settle).
        /// If <paramref name="targetPos"/> is within <see cref="SnapTolerance"/> of a canvas
        /// edge, that edge is recorded; otherwise the axis is cleared to None so the panel
        /// is only clamped (not repositioned) on resize.
        /// </summary>
        private void UpdateEdgeAffinityFromDrag(Vector2 targetPos)
        {
            if (_canvasRt == null) return;
            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            float pW = _rt.rect.width;
            float pH = _rt.rect.height;

            float leftEdge   = LeftReservedPx;
            float rightEdge  = cW - pW - RightReservedPx;
            float topEdge    = -TopReservedPx;
            float bottomEdge = -(cH - pH - BottomReservedPx);

            if (Mathf.Abs(targetPos.x - leftEdge) < SnapTolerance)
            { _edgeSnapX = EdgeSnapX.Left;   _edgeOffsetX = LeftReservedPx;   }
            else if (Mathf.Abs(targetPos.x - rightEdge) < SnapTolerance)
            { _edgeSnapX = EdgeSnapX.Right;  _edgeOffsetX = RightReservedPx;  }
            else
            { _edgeSnapX = EdgeSnapX.None; }

            if (Mathf.Abs(targetPos.y - topEdge) < SnapTolerance)
            { _edgeSnapY = EdgeSnapY.Top;    _edgeOffsetY = TopReservedPx;    }
            else if (Mathf.Abs(targetPos.y - bottomEdge) < SnapTolerance)
            { _edgeSnapY = EdgeSnapY.Bottom; _edgeOffsetY = BottomReservedPx; }
            else
            { _edgeSnapY = EdgeSnapY.None; }
        }

        // ── Bounds clamping ─────────────────────────────────────────────────
        private void ClampToBounds()
        {
            if (_canvasRt == null) return;
            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            float pW = _rt.rect.width;
            float pH = _rt.rect.height;

            // Reserved zones (e.g. menu bar at top) restrict the usable region.
            float xMin =  LeftReservedPx;
            float xMax =  Mathf.Max(xMin, cW - pW - RightReservedPx);
            float yMin = -Mathf.Max(0f, cH - pH - TopReservedPx - BottomReservedPx) - BottomReservedPx;
            float yMax = -TopReservedPx;

            var p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, xMin, xMax);
            p.y = Mathf.Clamp(p.y, yMin, yMax);
            _rt.anchoredPosition = p;
        }

        // ── Combined snap logic ─────────────────────────────────────────────
        private void TrySnap()
        {
            if (_canvasRt == null) return;
            var p = _rt.anchoredPosition;
            var s = p;

            TryCanvasEdgeSnap(ref s, p);

            if (EnableInterPanelSnap && GlobalInterPanelSnap)
                TryPanelToPanelSnap(ref s, p);

            if (s != p) { _snapTarget = s; _snapping = true; }
        }

        /// <summary>Snap to the four canvas edges if within <see cref="SnapTolerance"/>.</summary>
        private void TryCanvasEdgeSnap(ref Vector2 s, Vector2 p)
        {
            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            float pW = _rt.rect.width;
            float pH = _rt.rect.height;

            // Edge positions accounting for reserved zones.
            float leftEdge   = LeftReservedPx;
            float rightEdge  = cW - pW - RightReservedPx;
            float topEdge    = -TopReservedPx;
            float bottomEdge = -(cH - pH - BottomReservedPx);

            if (Mathf.Abs(p.x - leftEdge)   < SnapTolerance) s.x = leftEdge;
            else if (Mathf.Abs(rightEdge - p.x) < SnapTolerance) s.x = rightEdge;

            if (Mathf.Abs(p.y - topEdge)    < SnapTolerance) s.y = topEdge;
            else if (Mathf.Abs(p.y - bottomEdge) < SnapTolerance) s.y = bottomEdge;
        }

        /// <summary>
        /// Snap this panel's edges to nearby edges of other active, normalized panels.
        ///
        /// Coordinate system (after NormalizeAnchor — anchor + pivot = top-left):
        ///   p.x  = panel left edge (px from canvas left)
        ///   p.y  = panel top  edge (negative px from canvas top, 0 = canvas top)
        ///   p.y - pH = panel bottom edge
        ///   p.x + pW = panel right  edge
        ///
        /// Snapping rules:
        ///   Horizontal (left/right): only when panels share vertical space or have aligned tops.
        ///   Vertical   (top/bottom): only when panels share horizontal space or have aligned lefts.
        /// </summary>
        private void TryPanelToPanelSnap(ref Vector2 s, Vector2 p)
        {
            if (!_anchorNormalized) return;
            float pW = _rt.rect.width;
            float pH = _rt.rect.height;

            foreach (var other in _allPanels)
            {
                if (other == this || other == null || !other.gameObject.activeInHierarchy) continue;
                if (!other._anchorNormalized || other._rt == null) continue;

                var   op = other._rt.anchoredPosition;
                float ow = other._rt.rect.width;
                float oh = other._rt.rect.height;

                // Vertical overlap: both panels share some horizontal strip.
                // p.y > op.y - oh  →  my top is above other's bottom
                // p.y < op.y + pH  →  my top is below other's top + my height
                bool vOverlap = p.y > op.y - oh && p.y < op.y + pH;

                // Horizontal overlap: both panels share some vertical strip.
                bool hOverlap = p.x < op.x + ow && p.x + pW > op.x;

                // ── Horizontal snap (left/right edges) ───────────────────────
                if (vOverlap || Mathf.Abs(p.y - op.y) < SnapTolerance)
                {
                    // My left edge → other's right edge
                    if (Mathf.Abs(p.x - (op.x + ow)) < SnapTolerance)
                        s.x = op.x + ow;
                    // My right edge → other's left edge
                    if (Mathf.Abs((p.x + pW) - op.x) < SnapTolerance)
                        s.x = op.x - pW;
                }

                // ── Vertical snap (top/bottom edges) ─────────────────────────
                if (hOverlap || Mathf.Abs(p.x - op.x) < SnapTolerance)
                {
                    // My top edge → other's bottom edge  (s.y = op.y - oh)
                    if (Mathf.Abs(p.y - (op.y - oh)) < SnapTolerance)
                        s.y = op.y - oh;
                    // My bottom edge → other's top edge  (s.y = op.y + pH)
                    if (Mathf.Abs((p.y - pH) - op.y) < SnapTolerance)
                        s.y = op.y + pH;
                }
            }
        }
    }
}
