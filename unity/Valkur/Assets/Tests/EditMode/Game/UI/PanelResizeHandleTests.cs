using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Pins <see cref="PanelResizeHandle"/>, the project's single drag-to-resize
    /// implementation — used by four runtime editors (F1 Particles, F4 Spells, F7 Items,
    /// F8 Tile) and now by the NPC chat panel.
    ///
    /// <para>The corner option was added for the chat panel, whose pivot is bottom-left. The
    /// most important assertions here are the ones that say the DEFAULT still behaves exactly
    /// as it did before that option existed: four editors depend on it and none of them passes
    /// a corner.</para>
    /// </summary>
    [TestFixture]
    public class PanelResizeHandleTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private PanelResizeHandle CreateHandle(Vector2 startSize, out RectTransform target)
        {
            var panelGo = new GameObject("Panel");
            _created.Add(panelGo);
            target = panelGo.AddComponent<RectTransform>();
            target.sizeDelta = startSize;

            var gripGo = new GameObject("Grip");
            _created.Add(gripGo);
            gripGo.transform.SetParent(panelGo.transform, false);

            var handle = gripGo.AddComponent<PanelResizeHandle>();
            handle.Target = target;
            handle.MinSize = new Vector2(100f, 100f);
            handle.MaxSize = new Vector2(2000f, 2000f);
            return handle;
        }

        private static PointerEventData At(Vector2 position) =>
            new PointerEventData(EventSystem.current) { position = position };

        /// <summary>One press-and-drag, which is how the component is always driven.</summary>
        private static void Drag(PanelResizeHandle handle, Vector2 from, Vector2 to)
        {
            handle.OnPointerDown(At(from));
            handle.OnDrag(At(to));
        }

        // ---- The default corner, which four editors rely on ------------------

        [Test]
        public void DefaultCorner_IsBottomRight()
        {
            var handle = CreateHandle(new Vector2(400f, 300f), out _);

            Assert.AreEqual(ResizeGripCorner.BottomRight, handle.Corner,
                "Adding the corner option must not have changed a single editor. None of the " +
                "four resizable editors sets Corner, so the default IS their behaviour.");
        }

        [Test]
        public void BottomRight_GrowsRightAndDown()
        {
            var handle = CreateHandle(new Vector2(400f, 300f), out var target);

            // Screen Y runs UP, so dragging the cursor DOWN is a negative delta.
            Drag(handle, new Vector2(500f, 500f), new Vector2(560f, 460f));

            Assert.AreEqual(460f, target.sizeDelta.x, 0.001f, "Right by 60.");
            Assert.AreEqual(340f, target.sizeDelta.y, 0.001f,
                "Down by 40. A top-left-pivoted panel grows downward, so a cursor moving " +
                "down must ADD height — the Y axis is inverted against the screen.");
        }

        // ---- The corner the chat panel needs ---------------------------------

        [Test]
        public void TopRight_GrowsRightAndUp()
        {
            var handle = CreateHandle(new Vector2(400f, 300f), out var target);
            handle.Corner = ResizeGripCorner.TopRight;

            Drag(handle, new Vector2(500f, 500f), new Vector2(560f, 540f));

            Assert.AreEqual(460f, target.sizeDelta.x, 0.001f,
                "X is the same for both corners — both grips are on the right.");
            Assert.AreEqual(340f, target.sizeDelta.y, 0.001f,
                "Up by 40. A bottom-left-pivoted panel has its bottom edge nailed down, so " +
                "it can only grow upward and the Y axis is NOT inverted.");
        }

        [Test]
        public void TheTwoCorners_DisagreeOnlyOnTheVerticalAxis()
        {
            var bottomRight = CreateHandle(new Vector2(400f, 300f), out var brTarget);
            var topRight = CreateHandle(new Vector2(400f, 300f), out var trTarget);
            topRight.Corner = ResizeGripCorner.TopRight;

            var from = new Vector2(500f, 500f);
            var to = new Vector2(570f, 555f);
            Drag(bottomRight, from, to);
            Drag(topRight, from, to);

            Assert.AreEqual(brTarget.sizeDelta.x, trTarget.sizeDelta.x, 0.001f,
                "Only the vertical differs. If the horizontal ever diverges, the corner " +
                "option has grown into something else.");
            Assert.AreNotEqual(brTarget.sizeDelta.y, trTarget.sizeDelta.y,
                "The same upward drag must grow one and shrink the other.");
        }

        // ---- Clamping --------------------------------------------------------

        [Test]
        public void Size_IsHeldBetweenMinAndMax()
        {
            var handle = CreateHandle(new Vector2(400f, 300f), out var target);

            Drag(handle, new Vector2(500f, 500f), new Vector2(-5000f, 5000f));
            Assert.AreEqual(new Vector2(100f, 100f), target.sizeDelta, "Clamped to MinSize.");

            Drag(handle, new Vector2(500f, 500f), new Vector2(5000f, -5000f));
            Assert.AreEqual(new Vector2(2000f, 2000f), target.sizeDelta, "Clamped to MaxSize.");
        }

        [Test]
        public void DraggingPastTheMinimumAndBack_ReturnsToWhereTheCursorIs()
        {
            var handle = CreateHandle(new Vector2(400f, 300f), out var target);

            handle.OnPointerDown(At(new Vector2(500f, 500f)));
            handle.OnDrag(At(new Vector2(-2000f, 500f)));   // far past the 100 floor
            handle.OnDrag(At(new Vector2(560f, 500f)));     // back to +60

            Assert.AreEqual(460f, target.sizeDelta.x, 0.001f,
                "Size is measured from where the drag STARTED, not accumulated per frame. " +
                "An incremental implementation would have had the clamp eat the excursion, " +
                "leaving the panel offset from the cursor by however far it swallowed.");
        }

        // ---- The end-of-drag notification ------------------------------------

        [Test]
        public void Resized_FiresOnceOnEndDrag_WithTheFinalSize()
        {
            var handle = CreateHandle(new Vector2(400f, 300f), out _);

            int calls = 0;
            Vector2 reported = Vector2.zero;
            handle.Resized += size => { calls++; reported = size; };

            handle.OnPointerDown(At(new Vector2(500f, 500f)));
            handle.OnDrag(At(new Vector2(520f, 490f)));
            handle.OnDrag(At(new Vector2(560f, 460f)));

            Assert.AreEqual(0, calls,
                "Not per frame. The one thing a listener reliably does here is persist the " +
                "result, and a write per frame is a file write per frame.");

            handle.OnEndDrag(At(new Vector2(560f, 460f)));

            Assert.AreEqual(1, calls);
            Assert.AreEqual(new Vector2(460f, 340f), reported,
                "The reported size must be the size the panel actually settled on, or a " +
                "listener persists something the player never saw.");
        }

        [Test]
        public void NoTarget_IsInertRatherThanThrowing()
        {
            var gripGo = new GameObject("Grip");
            _created.Add(gripGo);
            var handle = gripGo.AddComponent<PanelResizeHandle>();

            Assert.DoesNotThrow(() =>
            {
                handle.OnPointerDown(At(Vector2.zero));
                handle.OnDrag(At(Vector2.one * 50f));
                handle.OnEndDrag(At(Vector2.one * 50f));
            }, "A handle built before its panel exists must not throw on the first drag.");
        }
    }
}
