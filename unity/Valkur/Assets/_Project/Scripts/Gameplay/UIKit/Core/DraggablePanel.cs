using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UIKit
{
    /// <summary>
    /// Floating panel root used by every editor and HUD widget. Features:
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
        /// Set once at startup by the host editor. Affects clamping AND canvas-edge snap.
        /// </summary>
        public static float TopReservedPx;

        /// <summary>Pixels reserved at the BOTTOM of the canvas that no panel may overlap.</summary>
        public static float BottomReservedPx;

        /// <summary>Pixels reserved at the LEFT  of the canvas that no panel may overlap.</summary>
        public static float LeftReservedPx;

        /// <summary>Pixels reserved at the RIGHT of the canvas that no panel may overlap.</summary>
        public static float RightReservedPx;

        public System.Action OnClose;

        private enum EdgeSnapX { None, Left, Right }
        private enum EdgeSnapY { None, Top, Bottom }

        private RectTransform _rt;
        private RectTransform _canvasRt;
        private Canvas        _canvas;

        private bool  _isDragging;
        private bool  _anchorNormalized;

        private bool  _minimized;
        private bool  _maximized;
        private float _restoredHeight;

        private Vector2 _snapTarget;
        private bool    _snapping;

        private EdgeSnapX _edgeSnapX    = EdgeSnapX.None;
        private EdgeSnapY _edgeSnapY    = EdgeSnapY.None;
        private float     _edgeOffsetX;
        private float     _edgeOffsetY;
        private Vector2   _lastCanvasSize;

        private static readonly List<DraggablePanel> _allPanels = new List<DraggablePanel>();

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
            EnsureCloseButton();
            if (!_anchorNormalized)
            {
                StartCoroutine(NormalizeNextFrame());
                // First enable only. Re-running this on every re-show would fight a host
                // that is deliberately re-opening a panel the user closed last session.
                ApplyRememberedVisibility();
            }
        }

        private void OnDisable()
        {
            _allPanels.Remove(this);
            _snapping = false;
        }

        private void OnDestroy() => _allPanels.Remove(this);

        private IEnumerator NormalizeNextFrame()
        {
            yield return null;
            NormalizeAnchor();
        }

        public void OnPointerDown(PointerEventData _) => transform.SetAsLastSibling();

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
            UpdateEdgeAffinityFromDrag(_snapping ? _snapTarget : _rt.anchoredPosition);
        }

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
    }
}
