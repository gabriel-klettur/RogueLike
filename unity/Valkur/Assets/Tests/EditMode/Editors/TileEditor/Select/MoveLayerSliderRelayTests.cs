using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Select
{
    /// <summary>
    /// Pins the firing contract of <see cref="MoveLayerSliderRelay"/>, the bridge
    /// component that lets the SelectModes panel's Move-To-Layer slider commit on
    /// pointer release. The relay is tiny but mission-critical: a regression that
    /// either double-fires the commit (Move executes twice per drag) or never
    /// fires (Move silently doesn't run, the very symptom we just fixed) would
    /// break the feature for the user.
    /// </summary>
    [TestFixture]
    public class MoveLayerSliderRelayTests
    {
        private GameObject _go;
        private MoveLayerSliderRelay _relay;
        private int _fireCount;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("RelayHost");
            _relay = _go.AddComponent<MoveLayerSliderRelay>();
            _fireCount = 0;
            _relay.OnReleased = () => _fireCount++;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        /// <summary>
        /// A click on the slider track with no drag fires only PointerUp.
        /// Must commit exactly once.
        /// </summary>
        [Test]
        public void PointerUpAlone_FiresExactlyOnce()
        {
            _relay.OnPointerUp(new PointerEventData(EventSystem.current));
            Assert.AreEqual(1, _fireCount, "PointerUp alone should fire OnReleased exactly once.");
        }

        /// <summary>
        /// A drag that releases off the Selectable produces only EndDrag (no
        /// PointerUp on the receiver). Must commit exactly once.
        /// </summary>
        [Test]
        public void EndDragAlone_FiresExactlyOnce()
        {
            _relay.OnEndDrag(new PointerEventData(EventSystem.current));
            Assert.AreEqual(1, _fireCount, "EndDrag alone should fire OnReleased exactly once.");
        }

        /// <summary>
        /// THE regression guard: a drag that ends ON the slider produces BOTH
        /// PointerUp and EndDrag in the same Unity frame. The debounce window
        /// (50 ms, larger than any single-frame gap) must coalesce them to a
        /// single OnReleased — otherwise every drag-commit would double-execute
        /// the Move (origin already cleared, second fire becomes a no-op, but
        /// edits-counter doubles and "Moved X cells" status would lie).
        /// </summary>
        [Test]
        public void PointerUpThenEndDrag_SameFrame_FiresOnce()
        {
            var data = new PointerEventData(EventSystem.current);
            _relay.OnPointerUp(data);
            _relay.OnEndDrag(data);

            Assert.AreEqual(1, _fireCount,
                "PointerUp + EndDrag in the same frame must debounce to a single commit.");
        }

        /// <summary>
        /// Order independence: EndDrag → PointerUp must also coalesce (Unity does
        /// not guarantee the dispatch order between these two interfaces).
        /// </summary>
        [Test]
        public void EndDragThenPointerUp_SameFrame_FiresOnce()
        {
            var data = new PointerEventData(EventSystem.current);
            _relay.OnEndDrag(data);
            _relay.OnPointerUp(data);

            Assert.AreEqual(1, _fireCount,
                "EndDrag + PointerUp (reverse order) must debounce to a single commit.");
        }

        /// <summary>
        /// A null callback must not throw — the relay is added by the UI builder
        /// before the wiring layer assigns <see cref="MoveLayerSliderRelay.OnReleased"/>,
        /// so an event may arrive in the gap between AddComponent and the listener
        /// being attached. Failing here would surface as an NRE that breaks the
        /// whole editor panel.
        /// </summary>
        [Test]
        public void NullCallback_DoesNotThrow()
        {
            _relay.OnReleased = null;
            Assert.DoesNotThrow(() => _relay.OnPointerUp(new PointerEventData(EventSystem.current)));
            Assert.DoesNotThrow(() => _relay.OnEndDrag(new PointerEventData(EventSystem.current)));
        }

    }
}
