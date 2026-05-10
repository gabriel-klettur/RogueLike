using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Lightweight per-slot pointer hook for the F8 tileset picker. Replaces
    /// <c>EventTrigger</c> on each cell so that mouse-wheel events bubble up
    /// to the parent <c>ScrollRect</c> uninterrupted, and captures drag
    /// gestures at the slot level so the parent ScrollRect doesn't absorb
    /// them as scroll-drags (which would prevent rect-selection from working).
    ///
    /// Button dispatch (single component, three behaviours):
    ///   • <b>LMB</b>   — fires the down/enter/up/drag callbacks → drives
    ///                    SINGLE/RECT/MULTI selection in <c>TileEditorUI.TilesetView</c>.
    ///   • <b>MMB</b>   — pans the parent <c>ScrollRect</c> (standard
    ///                    "hand-tool" panning UX; horizontal + vertical).
    ///   • Other       — ignored, so e.g. RMB clicks don't accidentally
    ///                    select or pan.
    ///
    /// Why we implement <see cref="IBeginDragHandler"/> on the slot at all:
    /// Unity's event system marks the closest ancestor with that interface
    /// as the drag target during a press. Without it, drags would bubble up
    /// to the ScrollRect and (a) consume LMB as a scroll gesture, (b) make
    /// <see cref="IPointerEnterHandler"/> unreliable on peer slots while
    /// the drag is in flight. Anchoring the drag here keeps both gestures
    /// (rect-select on LMB, pan on MMB) under one roof.
    /// </summary>
    public class TilesetSlotPointerEvents : MonoBehaviour,
        IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action OnDownAction;
        public Action OnEnterAction;
        public Action OnUpAction;
        public Action<PointerEventData> OnDragAction;

        // Cached parent ScrollRect for MMB-pan. Resolved lazily so the
        // component works regardless of when it's added in the build order.
        private ScrollRect _parentScrollCache;
        private bool       _parentScrollResolved;
        // True while a middle-click drag is in flight; OnDrag uses it to
        // route mouse delta to ScrollRect panning instead of the selection
        // action delegate.
        private bool _middleButtonPanning;

        public void OnPointerDown(PointerEventData eventData)
        {
            // Only LMB triggers selection — MMB-click on a slot must not
            // change the selection (it's reserved for starting a pan).
            if (eventData.button != PointerEventData.InputButton.Left) return;
            OnDownAction?.Invoke();
        }

        // PointerEnter has no associated button — it fires on hover. The
        // selection logic filters by drag state internally so unconditional
        // forwarding is safe.
        public void OnPointerEnter(PointerEventData eventData) => OnEnterAction?.Invoke();

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            OnUpAction?.Invoke();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Anchor the drag to this slot regardless of button so the parent
            // ScrollRect never sees the gesture. The body decides what to do
            // based on which button started the drag.
            if (eventData.button == PointerEventData.InputButton.Middle)
                _middleButtonPanning = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_middleButtonPanning)
            {
                ApplyMiddleClickPan(eventData.delta);
                return;
            }
            // Only LMB drags drive the rect-selection delegate. RMB / other
            // buttons are ignored so the picker doesn't react to phantom
            // drag input from non-canonical mice.
            if (eventData.button != PointerEventData.InputButton.Left) return;
            OnDragAction?.Invoke(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Middle)
                _middleButtonPanning = false;
            // LMB EndDrag is intentionally silent — the rect-selection commit
            // happens in OnPointerUp which fires alongside OnEndDrag.
        }

        // ── MMB pan ─────────────────────────────────────────────────────────

        private void ApplyMiddleClickPan(Vector2 delta)
        {
            var sr = ResolveParentScrollRect();
            if (sr == null || sr.content == null || sr.viewport == null) return;

            // Photoshop / Figma hand-tool convention: the cursor "grabs" the
            // content. Drag the cursor in direction D → content slides in D.
            // ScrollRect's normalizedPosition is inverted relative to the
            // content's anchoredPosition for the Y axis (1=top, 0=bottom),
            // so we subtract on both axes.
            float availX = sr.content.rect.width  - sr.viewport.rect.width;
            float availY = sr.content.rect.height - sr.viewport.rect.height;

            if (sr.horizontal && availX > 0f)
                sr.horizontalNormalizedPosition = Mathf.Clamp01(
                    sr.horizontalNormalizedPosition - delta.x / availX);
            if (sr.vertical && availY > 0f)
                sr.verticalNormalizedPosition = Mathf.Clamp01(
                    sr.verticalNormalizedPosition - delta.y / availY);
        }

        private ScrollRect ResolveParentScrollRect()
        {
            if (_parentScrollResolved) return _parentScrollCache;
            // Walk inactive parents too — the picker can be momentarily inactive
            // during hot-reload or panel-close transitions, and tests build the
            // hierarchy inactive on purpose (avoids the UGUI 2022.3
            // Selectable.OnEnable flaky array overflow).
            _parentScrollCache    = GetComponentInParent<ScrollRect>(includeInactive: true);
            _parentScrollResolved = true;
            return _parentScrollCache;
        }
    }
}
