using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.TileEditor
{
    public partial class DraggablePanel : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {

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