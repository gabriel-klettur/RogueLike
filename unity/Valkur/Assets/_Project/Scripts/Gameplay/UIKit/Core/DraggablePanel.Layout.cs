using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UIKit
{
    public partial class DraggablePanel : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private void NormalizeAnchor()
        {
            if (_anchorNormalized || _canvasRt == null) return;

            bool  wasRightAnchored  = _rt.anchorMin.x > 0.5f;
            bool  wasBottomAnchored = _rt.anchorMin.y < 0.5f;
            float cornerOffX = Mathf.Abs(_rt.anchoredPosition.x);
            float cornerOffY = Mathf.Abs(_rt.anchoredPosition.y);

            _anchorNormalized = true;

            var corners = new Vector3[4];
            _rt.GetWorldCorners(corners);

            _rt.anchorMin = new Vector2(0f, 1f);
            _rt.anchorMax = new Vector2(0f, 1f);
            _rt.pivot     = new Vector2(0f, 1f);

            var cam = _canvas.worldCamera;
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, screenPos, cam, out Vector2 localPos);

            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            _rt.anchoredPosition = localPos - new Vector2(-cW * 0.5f, cH * 0.5f);

            _edgeSnapX   = wasRightAnchored  ? EdgeSnapX.Right  : EdgeSnapX.Left;
            _edgeSnapY   = wasBottomAnchored ? EdgeSnapY.Bottom : EdgeSnapY.Top;
            _edgeOffsetX = cornerOffX;
            _edgeOffsetY = cornerOffY;

            _lastCanvasSize = new Vector2(cW, cH);
        }

        /// <summary>
        /// Called when the canvas rect changes size. Panels that are
        /// edge-snapped maintain their pixel offset from that edge; all
        /// others are clamped to the new bounds so they never float
        /// off-screen.
        /// </summary>
        private void OnCanvasSizeChanged(Vector2 newSize)
        {
            if (_snapping) _snapping = false;

            float pW = _rt.rect.width;
            float pH = _rt.rect.height;
            var   p  = _rt.anchoredPosition;

            switch (_edgeSnapX)
            {
                case EdgeSnapX.Left:  p.x = _edgeOffsetX;                        break;
                case EdgeSnapX.Right: p.x = newSize.x - pW - _edgeOffsetX;       break;
            }
            switch (_edgeSnapY)
            {
                case EdgeSnapY.Top:    p.y = -_edgeOffsetY;                       break;
                case EdgeSnapY.Bottom: p.y = -(newSize.y - pH - _edgeOffsetY);    break;
            }

            _rt.anchoredPosition = p;
            ClampToBounds();
        }

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

        private void ClampToBounds()
        {
            if (_canvasRt == null) return;
            float cW = _canvasRt.rect.width;
            float cH = _canvasRt.rect.height;
            float pW = _rt.rect.width;
            float pH = _rt.rect.height;

            float xMin =  LeftReservedPx;
            float xMax =  Mathf.Max(xMin, cW - pW - RightReservedPx);
            float yMin = -Mathf.Max(0f, cH - pH - TopReservedPx - BottomReservedPx) - BottomReservedPx;
            float yMax = -TopReservedPx;

            var p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, xMin, xMax);
            p.y = Mathf.Clamp(p.y, yMin, yMax);
            _rt.anchoredPosition = p;
        }

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
        /// Snap this panel's edges to nearby edges of other active,
        /// normalized panels. Coordinate system after NormalizeAnchor:
        /// anchor + pivot = top-left so p.x is left edge, p.y is top edge.
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

                bool vOverlap = p.y > op.y - oh && p.y < op.y + pH;
                bool hOverlap = p.x < op.x + ow && p.x + pW > op.x;

                if (vOverlap || Mathf.Abs(p.y - op.y) < SnapTolerance)
                {
                    if (Mathf.Abs(p.x - (op.x + ow)) < SnapTolerance)
                        s.x = op.x + ow;
                    if (Mathf.Abs((p.x + pW) - op.x) < SnapTolerance)
                        s.x = op.x - pW;
                }

                if (hOverlap || Mathf.Abs(p.x - op.x) < SnapTolerance)
                {
                    if (Mathf.Abs(p.y - (op.y - oh)) < SnapTolerance)
                        s.y = op.y - oh;
                    if (Mathf.Abs((p.y - pH) - op.y) < SnapTolerance)
                        s.y = op.y + pH;
                }
            }
        }
    }
}
