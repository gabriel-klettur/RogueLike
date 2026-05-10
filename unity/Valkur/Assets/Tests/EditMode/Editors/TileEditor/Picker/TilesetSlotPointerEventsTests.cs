using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Picker
{
    /// <summary>
    /// EditMode tests for <see cref="TilesetSlotPointerEvents"/> — the
    /// per-slot pointer hook that powers the F8 tile picker's selection AND
    /// middle-click panning of the parent ScrollRect.
    ///
    /// Coverage:
    ///   • LMB Down/Up fire their action delegates; MMB / RMB do not.
    ///   • PointerEnter is button-agnostic (no filter, no delegate skip).
    ///   • LMB drag routes through OnDragAction; MMB drag pans the
    ///     parent ScrollRect; other-button drags do nothing.
    ///   • Pan direction matches the standard hand-tool convention:
    ///     dragging the cursor in direction D slides the content in D
    ///     (i.e. ScrollRect.normalizedPosition moves opposite of the delta).
    ///   • The pan clamps at the [0, 1] normalized bounds.
    ///   • Pan is correctly disabled on a single-axis ScrollRect.
    /// </summary>
    [TestFixture]
    public class TilesetSlotPointerEventsTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        // ── Fixture builders ────────────────────────────────────────────────

        /// <summary>
        /// Builds an inactive Slot GameObject carrying a fresh
        /// <see cref="TilesetSlotPointerEvents"/>. Returned inactive so the
        /// component's Unity callbacks don't fire on Awake/OnEnable.
        /// </summary>
        private TilesetSlotPointerEvents BuildBareSlot()
        {
            var go = new GameObject("Slot", typeof(RectTransform));
            go.SetActive(false);
            _scene.Add(go);
            return go.AddComponent<TilesetSlotPointerEvents>();
        }

        /// <summary>
        /// Builds: ScrollRect → Viewport → Content → Slot. The Slot's
        /// <see cref="TilesetSlotPointerEvents.ResolveParentScrollRect"/>
        /// walks UP the hierarchy via <c>GetComponentInParent</c>, so the
        /// scrollrect must live on an ancestor GameObject. Returned inactive
        /// to avoid Scrollbar/Selectable initialization.
        /// </summary>
        private (TilesetSlotPointerEvents slot, ScrollRect scrollRect)
            BuildSlotInsideScrollRect(float contentW = 1000f, float contentH = 1000f,
                                      float viewportW = 200f, float viewportH = 200f,
                                      bool horizontal = true, bool vertical = true)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.SetActive(false);
            _scene.Add(scrollGo);
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = horizontal;
            sr.vertical   = vertical;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollGo.transform);
            _scene.Add(viewportGo);
            var viewportRt = (RectTransform)viewportGo.transform;
            viewportRt.sizeDelta = new Vector2(viewportW, viewportH);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform);
            _scene.Add(contentGo);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.sizeDelta = new Vector2(contentW, contentH);

            sr.viewport = viewportRt;
            sr.content  = contentRt;

            var slotGo = new GameObject("Slot", typeof(RectTransform));
            slotGo.transform.SetParent(contentGo.transform);
            _scene.Add(slotGo);
            var slot = slotGo.AddComponent<TilesetSlotPointerEvents>();

            // Park the scroll in the middle so we can test pan in both directions
            // without immediately hitting the clamp at 0 or 1.
            sr.horizontalNormalizedPosition = 0.5f;
            sr.verticalNormalizedPosition   = 0.5f;

            return (slot, sr);
        }

        private static PointerEventData MakePointer(PointerEventData.InputButton button,
                                                    Vector2 delta = default)
        {
            var ev = new PointerEventData(EventSystem.current)
            {
                button = button,
                delta  = delta,
            };
            return ev;
        }

        // ── Button filtering on pointer events ──────────────────────────────

        [Test]
        public void OnPointerDown_LMB_InvokesDownAction()
        {
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnDownAction = () => hits++;

            slot.OnPointerDown(MakePointer(PointerEventData.InputButton.Left));

            Assert.AreEqual(1, hits, "LMB down must fire OnDownAction.");
        }

        [Test]
        public void OnPointerDown_MMB_DoesNotInvokeDownAction()
        {
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnDownAction = () => hits++;

            slot.OnPointerDown(MakePointer(PointerEventData.InputButton.Middle));

            Assert.AreEqual(0, hits,
                "MMB down must NOT trigger selection — it's reserved for starting a pan.");
        }

        [Test]
        public void OnPointerDown_RMB_DoesNotInvokeDownAction()
        {
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnDownAction = () => hits++;

            slot.OnPointerDown(MakePointer(PointerEventData.InputButton.Right));

            Assert.AreEqual(0, hits,
                "RMB down must NOT trigger selection — keeps right-click free for future context menus.");
        }

        [Test]
        public void OnPointerUp_LMB_InvokesUpAction()
        {
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnUpAction = () => hits++;

            slot.OnPointerUp(MakePointer(PointerEventData.InputButton.Left));

            Assert.AreEqual(1, hits);
        }

        [Test]
        public void OnPointerUp_MMB_DoesNotInvokeUpAction()
        {
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnUpAction = () => hits++;

            slot.OnPointerUp(MakePointer(PointerEventData.InputButton.Middle));

            Assert.AreEqual(0, hits,
                "MMB up must NOT commit a rect — symmetric with the down filter.");
        }

        [Test]
        public void OnPointerEnter_IsButtonAgnostic()
        {
            // Hover events have no associated button; the component must not
            // attempt to filter them or the picker's drag-rect preview
            // wouldn't update as the cursor moves through peer slots.
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnEnterAction = () => hits++;

            // Default pointer (button = Left but the value doesn't matter for enter).
            slot.OnPointerEnter(new PointerEventData(EventSystem.current));

            Assert.AreEqual(1, hits);
        }

        // ── LMB drag routes through OnDragAction ────────────────────────────

        [Test]
        public void OnDrag_LMB_InvokesDragAction()
        {
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnDragAction = _ => hits++;

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Left));
            slot.OnDrag     (MakePointer(PointerEventData.InputButton.Left, delta: new Vector2(5f, 0f)));

            Assert.AreEqual(1, hits, "LMB drag must reach the rect-selection delegate.");
        }

        [Test]
        public void OnDrag_RMB_DoesNotInvokeDragAction()
        {
            var slot = BuildBareSlot();
            int hits = 0;
            slot.OnDragAction = _ => hits++;

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Right));
            slot.OnDrag     (MakePointer(PointerEventData.InputButton.Right, delta: new Vector2(5f, 0f)));

            Assert.AreEqual(0, hits,
                "RMB drag must be ignored — only LMB drives the rect selection.");
        }

        // ── MMB pan integration ─────────────────────────────────────────────

        [Test]
        public void MmbDrag_RightDelta_DecreasesHorizontalNormalizedPosition()
        {
            // Hand-tool convention: dragging the cursor RIGHT slides the
            // content RIGHT, exposing more of the LEFT side. ScrollRect.
            // horizontalNormalizedPosition (0 = leftmost) thus DECREASES.
            var (slot, sr) = BuildSlotInsideScrollRect();
            float before   = sr.horizontalNormalizedPosition;

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Middle));
            slot.OnDrag     (MakePointer(PointerEventData.InputButton.Middle, delta: new Vector2(80f, 0f)));

            Assert.Less(sr.horizontalNormalizedPosition, before,
                "Dragging MMB right must reduce horizontalNormalizedPosition — " +
                "the user sees content that was to the LEFT.");
        }

        [Test]
        public void MmbDrag_UpDelta_DecreasesVerticalNormalizedPosition()
        {
            // Drag UP (positive screen Y delta) → grab content and pull up →
            // user sees content BELOW the current view → norm.y decreases
            // (since norm.y=1 means top of content visible).
            var (slot, sr) = BuildSlotInsideScrollRect();
            float before   = sr.verticalNormalizedPosition;

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Middle));
            slot.OnDrag     (MakePointer(PointerEventData.InputButton.Middle, delta: new Vector2(0f, 80f)));

            Assert.Less(sr.verticalNormalizedPosition, before,
                "Dragging MMB up must reduce verticalNormalizedPosition — " +
                "the user sees content that was BELOW the previous view.");
        }

        [Test]
        public void MmbDrag_ClampsAtNormalizedBounds()
        {
            // Start at the LEFT (norm=0). Drag further left → must stay at 0,
            // not go negative.
            var (slot, sr) = BuildSlotInsideScrollRect();
            sr.horizontalNormalizedPosition = 0f;
            sr.verticalNormalizedPosition   = 0f;

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Middle));
            // delta.x < 0 would INCREASE norm.x (subtraction of negative). But
            // norm.x is already 0; clamp must prevent going negative even if
            // we drag in the wrong direction.
            slot.OnDrag(MakePointer(PointerEventData.InputButton.Middle, delta: new Vector2(800f, 800f)));

            Assert.GreaterOrEqual(sr.horizontalNormalizedPosition, 0f);
            Assert.LessOrEqual   (sr.horizontalNormalizedPosition, 1f);
            Assert.GreaterOrEqual(sr.verticalNormalizedPosition,   0f);
            Assert.LessOrEqual   (sr.verticalNormalizedPosition,   1f);
        }

        [Test]
        public void MmbDrag_VerticalOnlyScrollRect_DoesNotAlterHorizontalPosition()
        {
            // When the ScrollRect is configured vertical-only (e.g. the
            // categories scroll), MMB pan must NOT mutate the horizontal axis
            // even if delta.x ≠ 0.
            var (slot, sr) = BuildSlotInsideScrollRect(horizontal: false, vertical: true);
            float xBefore  = sr.horizontalNormalizedPosition;

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Middle));
            slot.OnDrag     (MakePointer(PointerEventData.InputButton.Middle, delta: new Vector2(100f, 0f)));

            Assert.AreEqual(xBefore, sr.horizontalNormalizedPosition, 0.0001f,
                "Vertical-only ScrollRect must ignore horizontal MMB delta.");
        }

        [Test]
        public void MmbDrag_NoParentScrollRect_NoException()
        {
            // The slot must degrade gracefully when there's no ScrollRect in
            // its ancestor chain (e.g. legacy callers that haven't wrapped
            // the picker in a ScrollRect yet).
            var slot = BuildBareSlot();

            Assert.DoesNotThrow(() =>
            {
                slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Middle));
                slot.OnDrag     (MakePointer(PointerEventData.InputButton.Middle, delta: new Vector2(50f, 50f)));
                slot.OnEndDrag  (MakePointer(PointerEventData.InputButton.Middle));
            });
        }

        [Test]
        public void OnEndDrag_Middle_StopsPanning_SubsequentDragsDoNothing()
        {
            // After EndDrag the slot must forget the panning state — a new
            // drag at a different button (or with no button info) must not
            // resume panning.
            var (slot, sr) = BuildSlotInsideScrollRect();

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Middle));
            slot.OnDrag     (MakePointer(PointerEventData.InputButton.Middle, delta: new Vector2(30f, 0f)));
            slot.OnEndDrag  (MakePointer(PointerEventData.InputButton.Middle));

            float xAfterPan = sr.horizontalNormalizedPosition;

            // Send another OnDrag without an OnBeginDrag — panning should be off.
            slot.OnDrag(MakePointer(PointerEventData.InputButton.Right, delta: new Vector2(30f, 0f)));

            Assert.AreEqual(xAfterPan, sr.horizontalNormalizedPosition, 0.0001f,
                "Once MMB EndDrag fires, subsequent drags must NOT continue panning.");
        }

        [Test]
        public void LmbDrag_DoesNotPanScrollRect()
        {
            // Critical: LMB drag must go to OnDragAction ONLY. If it also
            // panned the ScrollRect, rect-selection would scroll the picker
            // sideways every time the user dragged across multiple tiles.
            var (slot, sr) = BuildSlotInsideScrollRect();
            float xBefore  = sr.horizontalNormalizedPosition;
            float yBefore  = sr.verticalNormalizedPosition;

            slot.OnBeginDrag(MakePointer(PointerEventData.InputButton.Left));
            slot.OnDrag     (MakePointer(PointerEventData.InputButton.Left, delta: new Vector2(80f, 80f)));

            Assert.AreEqual(xBefore, sr.horizontalNormalizedPosition, 0.0001f,
                "LMB drag must NOT pan the ScrollRect — it's reserved for selection.");
            Assert.AreEqual(yBefore, sr.verticalNormalizedPosition,   0.0001f);
        }
    }
}
