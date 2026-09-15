using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Gameplay.Combat.Feedback
{
    /// <summary>
    /// Pins the ring that opens at the mouse pointer when the player acts.
    ///
    /// <para>Built through <c>BuildRig</c> directly: in Edit Mode a component's <c>Start</c>
    /// never runs, so a test that only adds the component measures nothing.</para>
    /// </summary>
    public class CursorImpactFXTests
    {
        private GameObject _holder;
        private CursorImpactFX _fx;

        [SetUp]
        public void SetUp()
        {
            _holder = new GameObject("CursorImpactFXTests.Holder");
            _fx = _holder.AddComponent<CursorImpactFX>();
            _fx.BuildRig();
        }

        [TearDown]
        public void TearDown()
        {
            // Awake never ran, so OnDestroy never will either: the canvas is not a child of
            // the holder and has to be taken down by hand.
            if (_fx != null && _fx.OverlayCanvas != null)
                Object.DestroyImmediate(_fx.OverlayCanvas.gameObject);
            if (_holder != null) Object.DestroyImmediate(_holder);
        }

        // ── Structure ─────────────────────────────────────────────────────────────────

        [Test]
        public void Canvas_IsScreenSpaceOverlay_AndDrawsAboveEveryOtherCanvas()
        {
            Assert.IsNotNull(_fx.OverlayCanvas);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, _fx.OverlayCanvas.renderMode);
            // Chat is 100 and toasts are 200; a cursor accent belongs over whatever it is on.
            Assert.Greater(_fx.OverlayCanvas.sortingOrder, 200);
        }

        [Test]
        public void Canvas_HasNoRaycaster_AndNoLayerEatsClicks()
        {
            // A full-screen click-eater over every panel in the game would break the very
            // action that spawned the ring, and only while it was on screen — an intermittent,
            // un-reproducible input bug is the worst kind this could cause.
            Assert.IsNull(_fx.OverlayCanvas.GetComponent<GraphicRaycaster>(),
                "a GraphicRaycaster here would swallow clicks");
            Assert.IsFalse(_fx.RingImage.raycastTarget, "ring must not be a raycast target");
            Assert.IsFalse(_fx.CoreImage.raycastTarget, "core must not be a raycast target");
        }

        [Test]
        public void Canvas_HasNoScaler_SoSizesAreTrueScreenPixels()
        {
            // A cursor accent should be the size the OS pointer is, which does not grow with
            // the render resolution. The scaler's absence is also what lets the pointer
            // position be written straight into `position`.
            Assert.IsNull(_fx.OverlayCanvas.GetComponent<CanvasScaler>(),
                "a scaler would swell the ring on a 4K display and break the pixel identity");
        }

        // ── Behaviour ─────────────────────────────────────────────────────────────────

        [Test]
        public void AtRest_NothingIsDrawn()
        {
            _fx.Tick(1f / 60f, new Vector2(400f, 300f));
            Assert.IsFalse(_fx.IsShowing);
            Assert.IsFalse(_fx.RingImage.enabled);
            Assert.IsFalse(_fx.CoreImage.enabled);
        }

        [Test]
        public void Fire_OpensARingThatExpandsAndFades()
        {
            _fx.Fire();
            _fx.Tick(1f / 240f, new Vector2(400f, 300f));
            Assert.IsTrue(_fx.IsShowing);
            Assert.IsTrue(_fx.RingImage.enabled);

            float earlyDiameter = _fx.RingDiameterPx;
            float earlyAlpha = _fx.RingAlpha;

            for (int i = 0; i < 6; i++) _fx.Tick(1f / 120f, new Vector2(400f, 300f));

            Assert.Greater(_fx.RingDiameterPx, earlyDiameter, "the ring opens outward");
            Assert.Less(_fx.RingAlpha, earlyAlpha, "and fades as it goes");
        }

        [Test]
        public void Core_DiesBeforeTheRing_SoTheyReadAsTwoBeats()
        {
            _fx.Fire();
            // Step past the core's own life but not the ring's.
            for (int i = 0; i < 8; i++) _fx.Tick(0.014f, new Vector2(400f, 300f));

            Assert.IsTrue(_fx.IsShowing, "the ring is still alive");
            Assert.AreEqual(0f, _fx.CoreAlpha, 1e-4f, "the core has already gone");
            Assert.Greater(_fx.RingAlpha, 0f, "the ring has not");
        }

        [Test]
        public void ItFollowsThePointer_ForItsWholeLife()
        {
            _fx.Fire();
            _fx.Tick(1f / 120f, new Vector2(100f, 120f));
            Assert.AreEqual(new Vector2(100f, 120f), (Vector2)_fx.Root.position);

            // Whipping the mouse mid-effect must drag it along: anchored, it would read as a
            // mark on the ground you clicked rather than as the cursor reacting.
            _fx.Tick(1f / 120f, new Vector2(640f, 480f));
            Assert.AreEqual(new Vector2(640f, 480f), (Vector2)_fx.Root.position);
        }

        [Test]
        public void Fire_RestartsRatherThanStacking()
        {
            _fx.Fire();
            _fx.Tick(1f / 240f, new Vector2(400f, 300f));
            float firstDiameter = _fx.RingDiameterPx;

            for (int i = 0; i < 5; i++) _fx.Tick(0.02f, new Vector2(400f, 300f));
            Assert.Greater(_fx.RingDiameterPx, firstDiameter, "it must have opened");

            _fx.Fire();
            _fx.Tick(1f / 240f, new Vector2(400f, 300f));
            Assert.AreEqual(firstDiameter, _fx.RingDiameterPx, 1e-3f,
                "a re-fire restarts the ring; stacking would make a harvest rhythm a smear");
        }

        [Test]
        public void ItSettlesHidden_AndThenStopsWritingAltogether()
        {
            _fx.Fire();
            for (int i = 0; i < 40; i++) _fx.Tick(1f / 60f, new Vector2(400f, 300f));
            Assert.IsFalse(_fx.IsShowing);
            Assert.IsFalse(_fx.RingImage.enabled);

            // Once settled it writes NOTHING — a Graphic colour set dirties the canvas batch,
            // and this thing is idle for almost the whole session. Poking a sentinel in is the
            // only way to see the absence of a write.
            var sentinel = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            _fx.RingImage.color = sentinel;
            var moved = new Vector3(11f, 22f, 0f);
            _fx.Root.position = moved;
            _fx.Tick(1f / 60f, new Vector2(999f, 999f));

            Assert.AreEqual(sentinel, _fx.RingImage.color, "an idle effect must not repaint");
            Assert.AreEqual(moved, _fx.Root.position, "nor reposition");
        }

        // ── Wiring ────────────────────────────────────────────────────────────────────

        [Test]
        public void Source_ReadsThePointerThroughMouseInputManager_NeverTheDeviceDirectly()
        {
            string path = Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Combat/Feedback/CursorImpactFX.cs");
            string code = StripComments(File.ReadAllText(path));

            StringAssert.Contains("MouseInputManager.GetScreenMousePosition", code,
                "the pointer read must go through the centralized helper — it carries the "
                + "freeze tracker for the 2022.3 InputSystem event-drop bug");
            StringAssert.DoesNotContain("Mouse.current", code);
            StringAssert.DoesNotContain("UnityEngine.Input.", code);
            // Hiding the OS pointer is the design this feature deliberately did NOT take.
            StringAssert.DoesNotContain("Cursor.visible", code);
            StringAssert.DoesNotContain("Cursor.SetCursor", code);
        }

        [Test]
        public void PlayerController_FiresIt_OnACastAndOnEveryWorkSwing()
        {
            // A ring that nothing fires is the authored-and-inert shape this project keeps
            // rediscovering. Asserted against the SOURCE because driving a real cast or a real
            // harvest blow needs a spell catalog, an animator and a node.
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Player");

            string owner = StripComments(File.ReadAllText(Path.Combine(dir, "PlayerController.cs")));
            StringAssert.Contains("_cursorImpact.Fire()", owner, "the act seam must fire the ring");
            StringAssert.Contains("GetComponent<CursorImpactFX>()", owner, "and must resolve it");

            string movement = StripComments(File.ReadAllText(Path.Combine(dir, "PlayerController.Movement.cs")));
            StringAssert.Contains("!sameCastStillPlaying) NotifyPlayerActed()", movement,
                "a cast notifies, gated so a channelled beam does not hold it lit");

            string harvest = StripComments(File.ReadAllText(Path.Combine(dir, "PlayerController.Harvest.cs")));
            StringAssert.Contains("NotifyPlayerActed()", harvest, "every work swing notifies");
        }

        [Test]
        public void EntitySetup_AddsIt_ToThePlayerOnly()
        {
            string path = Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Bootstrap/EntitySetup.Visuals.cs");
            string code = StripComments(File.ReadAllText(path));
            StringAssert.Contains("AddComponent<CursorImpactFX>()", code,
                "nothing would instantiate it otherwise");
            StringAssert.Contains("CompareTag(\"Player\") && go.GetComponent<CursorImpactFX>()", code,
                "it decorates the POINTER, and no NPC has one");
        }

        /// <summary>Comments are allowed to NAME a trap; code is not allowed to USE it.</summary>
        private static string StripComments(string src) =>
            string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//")
                                                      && !l.TrimStart().StartsWith("///")));
    }
}
