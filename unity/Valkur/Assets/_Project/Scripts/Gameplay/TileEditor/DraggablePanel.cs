using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Attaches to a floating Tile-Editor panel root (RectTransform).
    /// Features:
    ///   • Draggable from <see cref="DragHeader"/> only — never from panel content.
    ///   • Clamped to Canvas bounds at all times.
    ///   • Auto-snap to any canvas edge when released within <see cref="SnapTolerance"/> px.
    ///   • Smooth snap animation (lerp).
    ///   • Focus management: clicking anywhere on the panel brings it to the front (SetAsLastSibling).
    ///   • Minimize: collapses <see cref="ContentRoot"/> and shrinks the panel to header height.
    ///   • Maximize: expands the panel to fill the canvas height (minus the menu bar).
    ///   • Close: fires <see cref="OnClose"/> (wired by TileEditorUI.Builder after construction).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DraggablePanel : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        // ── Wired by TileEditorUIBuilder ────────────────────────────────────
        [Tooltip("Header RectTransform — drag is only initiated when pointer presses here.")]
        public RectTransform DragHeader;

        [Tooltip("Container for all panel content below the header. Hidden when minimized.")]
        public GameObject ContentRoot;

        [Tooltip("Height (in canvas units) to expand to when maximized.")]
        public float MaximizedHeight = 700f;

        [SerializeField, Tooltip("Distance in canvas units from an edge that triggers auto-snap.")]
        public float SnapTolerance = 18f;

        [SerializeField, Tooltip("Lerp speed for snap animation (uses unscaled time).")]
        public float SnapAnimSpeed = 14f;

        // ── External callback — set by TileEditorUI.Builder ────────────────
        public System.Action OnClose;

        // ── Private state ───────────────────────────────────────────────────
        private RectTransform _rt;
        private RectTransform _canvasRt;
        private Canvas        _canvas;

        private bool    _isDragging;
        private bool    _anchorNormalized;

        private bool    _minimized;
        private bool    _maximized;
        private float   _restoredHeight;

        private Vector2 _snapTarget;
        private bool    _snapping;

        // ── Life-cycle ──────────────────────────────────────────────────────
        private void Awake()
        {
            _rt             = GetComponent<RectTransform>();
            _canvas         = GetComponentInParent<Canvas>();
            _canvasRt       = _canvas != null ? _canvas.GetComponent<RectTransform>() : null;
            _restoredHeight = _rt.sizeDelta.y;
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
            if (_isDragging)
                NormalizeAnchor();
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
        }

        // ── Snap animation update ───────────────────────────────────────────
        private void Update()
        {
            if (!_snapping) return;
            _rt.anchoredPosition = Vector2.Lerp(
                _rt.anchoredPosition, _snapTarget,
                Time.unscaledDeltaTime * SnapAnimSpeed);
            if (Vector2.Distance(_rt.anchoredPosition, _snapTarget) < 0.5f)
            {
                _rt.anchoredPosition = _snapTarget;
                _snapping = false;
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

        /// <summary>
        /// Toggle between maximized (full canvas height) and restored (original height).
        /// Also un-minimizes the panel if it was minimized.
        /// </summary>
        public void Maximize()
        {
            _minimized = false;
            if (ContentRoot != null) ContentRoot.SetActive(true);

            _maximized = !_maximized;
            if (_maximized)
            {
                float targetH = _canvasRt != null
                    ? Mathf.Max(_restoredHeight, _canvasRt.rect.height - 34f) // 34 = menu bar
                    : MaximizedHeight;
                _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, targetH);
            }
            else
            {
                _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, _restoredHeight);
            }
        }

        /// <summary>Close (hide) this panel by invoking the registered close callback.</summary>
        public void ClosePanel() => OnClose?.Invoke();

        // ── Anchor normalization ────────────────────────────────────────────
        /// <summary>
        /// Converts the panel from any corner-based anchor/pivot to a uniform top-left (0,1)
        /// anchor + pivot so that anchoredPosition is (pixelsFromLeft, -pixelsFromTop).
        /// Called once on first drag so initial positioning uses the correct corner anchors
        /// at any resolution, while subsequent dragging uses a consistent coordinate system.
        /// </summary>
        private void NormalizeAnchor()
        {
            if (_anchorNormalized || _canvasRt == null) return;
            _anchorNormalized = true;

            // Capture the world-space top-left corner of the panel before touching anchors.
            var corners = new Vector3[4];
            _rt.GetWorldCorners(corners);
            // corners[1] = world-space top-left corner of the panel.

            // Switch to top-left anchor + pivot.
            _rt.anchorMin = new Vector2(0f, 1f);
            _rt.anchorMax = new Vector2(0f, 1f);
            _rt.pivot     = new Vector2(0f, 1f);

            // ScreenSpaceOverlay canvas: worldCamera is null, which is fine for these utils.
            var cam = _canvas.worldCamera;
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, screenPos, cam, out Vector2 localPos);

            // localPos is in canvas local space (center = 0,0).
            // Canvas top-left in that space = (-cW/2, +cH/2).
            // anchoredPosition = localPos − canvasTopLeft
            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            _rt.anchoredPosition = localPos - new Vector2(-cW * 0.5f, cH * 0.5f);
        }

        // ── Clamping & snapping ─────────────────────────────────────────────
        private void ClampToBounds()
        {
            if (_canvasRt == null) return;

            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            float pW = _rt.rect.width;
            float pH = _rt.rect.height;

            // With anchor=(0,1): x ∈ [0, cW-pW], y ∈ [-(cH-pH), 0]
            var p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 0f,              Mathf.Max(0f, cW - pW));
            p.y = Mathf.Clamp(p.y, -Mathf.Max(0f, cH - pH), 0f);
            _rt.anchoredPosition = p;
        }

        private void TrySnap()
        {
            if (_canvasRt == null) return;

            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            float pW = _rt.rect.width;
            float pH = _rt.rect.height;

            var p = _rt.anchoredPosition;
            var s = p;

            if (p.x < SnapTolerance)                s.x = 0f;
            else if (cW - pW - p.x < SnapTolerance) s.x = cW - pW;

            if (-p.y < SnapTolerance)               s.y = 0f;
            else if (cH - pH + p.y < SnapTolerance) s.y = -(cH - pH);

            if (s != p) { _snapTarget = s; _snapping = true; }
        }
    }
}
