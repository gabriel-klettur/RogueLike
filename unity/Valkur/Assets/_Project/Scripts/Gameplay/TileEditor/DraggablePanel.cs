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
    public partial class DraggablePanel : MonoBehaviour,
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
    }
}