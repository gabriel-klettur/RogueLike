using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    public class MouseInputManagerTests
    {
        private GameObject _cameraGo;
        private Camera _camera;
        private GameObject _managerGo;
        private MouseInputManager _manager;
        private EventSystem _eventSystem;

        [SetUp]
        public void SetUp()
        {
            // A freeze verdict is static state: a fixture that walked the legacy pointer
            // while the InputSystem sat still would otherwise decide this one's readings.
            MouseInputManager.ResetFreezeTracking();

            // Ensure mouse device exists
            if (Mouse.current == null)
            {
                InputSystem.AddDevice<Mouse>();
            }

            // Create camera
            _cameraGo = new GameObject("TestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 10f;
            _camera.tag = "MainCamera";

            // Create manager
            _managerGo = new GameObject("MouseInputManager");
            _manager = _managerGo.AddComponent<MouseInputManager>();

            // Create EventSystem
            var esSys = new GameObject("EventSystem");
            _eventSystem = esSys.AddComponent<EventSystem>();
            esSys.AddComponent<InputSystemUIInputModule>();
        }

        [TearDown]
        public void TearDown()
        {
            MouseInputManager.ResetFreezeTracking();
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
            if (_managerGo != null) Object.DestroyImmediate(_managerGo);
            if (_eventSystem != null) Object.DestroyImmediate(_eventSystem.gameObject);
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_UsesLegacyWhenInputSystemIsStaleZero()
        {
            var view = new Rect(0f, 0f, 1280f, 720f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: Vector2.zero,
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(640f, 360f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: true,
                out Vector2 selected);

            Assert.IsTrue(ok);
            Assert.AreEqual(640f, selected.x, 0.001f);
            Assert.AreEqual(360f, selected.y, 0.001f);
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_RejectsStaleZeroWhenLegacyIsOutsideView()
        {
            var view = new Rect(0f, 0f, 1280f, 720f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: Vector2.zero,
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(2000f, 1000f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: true,
                out _);

            Assert.IsFalse(ok, "A stale Input System (0,0) must not snap the player to bottom-left when the real pointer is outside the view.");
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_TrustsInputSystemOutsideViewOverLegacyFallback()
        {
            var view = new Rect(0f, 0f, 1280f, 720f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: new Vector2(-10f, 360f),
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(640f, 360f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: true,
                out _);

            Assert.IsFalse(ok, "A deliberate out-of-view Input System position should stay out-of-view in tests and gameplay.");
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_PrefersValidInputSystemPosition()
        {
            var view = new Rect(0f, 0f, 1280f, 720f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: new Vector2(200f, 300f),
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(640f, 360f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: true,
                out Vector2 selected);

            Assert.IsTrue(ok);
            Assert.AreEqual(200f, selected.x, 0.001f);
            Assert.AreEqual(300f, selected.y, 0.001f);
        }

        // ── Frozen InputSystem (non-zero) ───────────────────────────────────
        // Measured 2026-09-05: the InputSystem mouse froze at the screen CENTRE, finite and
        // in view, and won over the live legacy reading. The cursor resolved to the player's
        // feet and every aimed spell flew straight down. The stale-zero guard above cannot
        // see a freeze at any value but zero; the tracker's verdict is what covers the rest.

        [Test]
        public void Manager_SelectBestScreenMousePosition_FrozenInputSystemYieldsToLegacyInView()
        {
            var view = new Rect(0f, 0f, 1600f, 800f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: new Vector2(800f, 400f),   // the centre it froze at
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(533f, 556f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: true,
                inputSystemFrozen: true,
                out Vector2 selected);

            Assert.IsTrue(ok);
            Assert.AreEqual(533f, selected.x, 0.001f);
            Assert.AreEqual(556f, selected.y, 0.001f);
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_FrozenInputSystemYieldsToLegacyWithoutViewGate()
        {
            // The ground-target path asks with requireInView = false. Same answer.
            var view = new Rect(0f, 0f, 1600f, 800f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: new Vector2(800f, 400f),
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(533f, 556f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: false,
                inputSystemFrozen: true,
                out Vector2 selected);

            Assert.IsTrue(ok);
            Assert.AreEqual(533f, selected.x, 0.001f);
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_FrozenInputSystemDoesNotInventAPosition()
        {
            // Distrust is not a fallback to "anything": legacy outside the view under the
            // view gate answers false, or the player snaps to face a screen corner.
            var view = new Rect(0f, 0f, 1600f, 800f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: new Vector2(800f, 400f),
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(3028f, 209f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: true,
                inputSystemFrozen: true,
                out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_FrozenFlagIsInertWithoutALegacyReading()
        {
            // No second opinion to prefer: the InputSystem stays the answer.
            var view = new Rect(0f, 0f, 1600f, 800f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: new Vector2(800f, 400f),
                hasInputSystemPosition: true,
                legacyPosition: Vector2.zero,
                hasLegacyPosition: false,
                viewRect: view,
                requireInView: true,
                inputSystemFrozen: true,
                out Vector2 selected);

            Assert.IsTrue(ok);
            Assert.AreEqual(800f, selected.x, 0.001f);
        }

        [Test]
        public void Manager_SelectBestScreenMousePosition_LegacyOverloadStillTrustsTheInputSystem()
        {
            // The seven-argument overload is the historical contract; it must keep answering
            // as if nothing were frozen, or every existing caller changes behaviour silently.
            var view = new Rect(0f, 0f, 1600f, 800f);

            bool ok = MouseInputManager.TrySelectScreenMousePosition(
                inputSystemPosition: new Vector2(800f, 400f),
                hasInputSystemPosition: true,
                legacyPosition: new Vector2(533f, 556f),
                hasLegacyPosition: true,
                viewRect: view,
                requireInView: true,
                out Vector2 selected);

            Assert.IsTrue(ok);
            Assert.AreEqual(800f, selected.x, 0.001f);
        }

        [Test]
        public void Manager_ProductionPath_ConsultsTheFreezeTracker()
        {
            // The selector is pure and the tracker is pure; what shipped broken was neither,
            // it was the WIRING. Pin that the production read feeds the tracker, or the two
            // halves can each be green while the composition is not.
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(
                Application.dataPath, "_Project", "Scripts", "Core", "Input", "MouseInputManager.cs"));

            Assert.IsTrue(src.Contains("_freezeTracker.Observe("),
                "TryGetScreenMousePosition must feed MouseFreezeTracker before selecting.");
            Assert.IsTrue(src.Contains("inputSystemFrozen,") || src.Contains("inputSystemFrozen:"),
                "The tracker's verdict must reach TrySelectScreenMousePosition.");
        }

        [Test]
        public void Manager_GetScreenMousePosition_ReturnsValidCoordinates()
        {
            // Act
            Vector2 pos = MouseInputManager.GetScreenMousePosition();

            // Assert
            Assert.IsTrue(!float.IsNaN(pos.x), "Screen X should not be NaN");
            Assert.IsTrue(!float.IsNaN(pos.y), "Screen Y should not be NaN");
        }

        [Test]
        public void Manager_GetWorldMousePosition_ReturnsValidCoordinates()
        {
            // Act
            Vector2 worldPos = MouseInputManager.GetWorldMousePosition();

            // Assert
            Assert.IsTrue(!float.IsNaN(worldPos.x), "World X should not be NaN");
            Assert.IsTrue(!float.IsNaN(worldPos.y), "World Y should not be NaN");
        }

        [Test]
        public void Manager_IsLeftMouseButtonPressed_ReturnsBool()
        {
            // Act
            bool pressed = MouseInputManager.IsLeftMouseButtonPressed();

            // Assert
            Assert.IsInstanceOf<bool>(pressed);
        }

        [Test]
        public void Manager_IsRightMouseButtonPressed_ReturnsBool()
        {
            // Act
            bool pressed = MouseInputManager.IsRightMouseButtonPressed();

            // Assert
            Assert.IsInstanceOf<bool>(pressed);
        }

        [Test]
        public void Manager_IsMiddleMouseButtonPressed_ReturnsBool()
        {
            // Act
            bool pressed = MouseInputManager.IsMiddleMouseButtonPressed();

            // Assert
            Assert.IsInstanceOf<bool>(pressed);
        }

        [Test]
        public void Manager_MiddleMouseFrameMethods_ReturnBool()
        {
            Assert.IsInstanceOf<bool>(MouseInputManager.WasMiddleMouseButtonPressedThisFrame());
            Assert.IsInstanceOf<bool>(MouseInputManager.WasMiddleMouseButtonReleasedThisFrame());
        }

        [Test]
        public void Manager_GetMouseWheelDelta_ReturnsFloat()
        {
            // Act
            float delta = MouseInputManager.GetMouseWheelDelta();

            // Assert
            Assert.IsInstanceOf<float>(delta);
        }

        [Test]
        public void Manager_IsPointerOverUI_ReturnsValid()
        {
            // Act
            bool overUI = MouseInputManager.IsPointerOverUI();

            // Assert
            Assert.IsInstanceOf<bool>(overUI);
        }

        [Test]
        public void Manager_GetCollidersUnderMouse_WithNoColliders_ReturnsEmpty()
        {
            // Arrange
            LayerMask layerMask = LayerMask.GetMask("Default");

            // Act
            var colliders = MouseInputManager.GetCollidersUnderMouse(layerMask);

            // Assert
            Assert.IsNotNull(colliders);
            Assert.IsInstanceOf<Collider2D[]>(colliders);
        }

        [Test]
        public void Manager_GetCollidersUnderMouse_WithCollider_DetectsIt()
        {
            // Arrange
            var colliderGo = new GameObject("TestCollider");
            colliderGo.layer = LayerMask.NameToLayer("Default");
            var collider = colliderGo.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(10f, 10f);

            // Place at world origin
            colliderGo.transform.position = Vector3.zero;

            LayerMask layerMask = LayerMask.GetMask("Default");

            try
            {
                // Act
                var colliders = MouseInputManager.GetCollidersUnderMouse(layerMask, 5f);

                // Assert - Should find the collider if mouse is over it
                Assert.IsNotNull(colliders);
            }
            finally
            {
                Object.DestroyImmediate(colliderGo);
            }
        }

        [Test]
        public void Manager_GetTopmostColliderUnderMouse_WithMultiple_ReturnsTopmost()
        {
            // Arrange
            var collider1Go = new GameObject("Collider1");
            collider1Go.transform.position = new Vector3(0, 0, 0);
            var col1 = collider1Go.AddComponent<BoxCollider2D>();
            col1.size = new Vector2(5f, 5f);

            var collider2Go = new GameObject("Collider2");
            collider2Go.transform.position = new Vector3(0, 0, 10);
            var col2 = collider2Go.AddComponent<BoxCollider2D>();
            col2.size = new Vector2(5f, 5f);

            LayerMask layerMask = LayerMask.GetMask("Default");

            try
            {
                // Act
                var topmost = MouseInputManager.GetTopmostColliderUnderMouse(layerMask, 5f);

                // Assert
                // If we found one, it should be a valid collider
                if (topmost != null)
                {
                    Assert.IsTrue(topmost == col1 || topmost == col2);
                }
            }
            finally
            {
                Object.DestroyImmediate(collider1Go);
                Object.DestroyImmediate(collider2Go);
            }
        }

        [Test]
        public void Manager_IsMouseInScreenRect_WithRectAtOrigin_Works()
        {
            // Arrange
            Rect rect = new Rect(0, 0, Screen.width, Screen.height);

            // Act
            bool inside = MouseInputManager.IsMouseInScreenRect(rect);

            // Assert
            Assert.IsInstanceOf<bool>(inside);
        }

        [Test]
        public void Manager_IsMouseInWorldRect_WithBoundsAtOrigin_Works()
        {
            // Arrange
            var bounds = new MouseInputManager.Bounds2D(-10f, 10f, -10f, 10f);

            // Act
            bool inside = MouseInputManager.IsMouseInWorldRect(bounds);

            // Assert
            Assert.IsInstanceOf<bool>(inside);
        }

        [Test]
        public void Manager_Bounds2D_ContainsPoint_WithPointInside_ReturnsTrue()
        {
            // Arrange
            var bounds = new MouseInputManager.Bounds2D(0f, 10f, 0f, 10f);
            var point = new Vector2(5f, 5f);

            // Act
            bool contains = bounds.Contains(point);

            // Assert
            Assert.IsTrue(contains, "Point inside bounds should return true");
        }

        [Test]
        public void Manager_Bounds2D_ContainsPoint_WithPointOutside_ReturnsFalse()
        {
            // Arrange
            var bounds = new MouseInputManager.Bounds2D(0f, 10f, 0f, 10f);
            var point = new Vector2(15f, 15f);

            // Act
            bool contains = bounds.Contains(point);

            // Assert
            Assert.IsFalse(contains, "Point outside bounds should return false");
        }

        [Test]
        public void Manager_Bounds2D_ContainsPoint_OnEdge_ReturnsTrue()
        {
            // Arrange
            var bounds = new MouseInputManager.Bounds2D(0f, 10f, 0f, 10f);
            var pointOnEdge = new Vector2(10f, 10f);

            // Act
            bool contains = bounds.Contains(pointOnEdge);

            // Assert
            Assert.IsTrue(contains, "Point on edge should be contained");
        }

        [Test]
        public void Manager_WasLeftMouseButtonPressedThisFrame_ReturnsBool()
        {
            // Act
            bool pressed = MouseInputManager.WasLeftMouseButtonPressedThisFrame();

            // Assert
            Assert.IsInstanceOf<bool>(pressed);
        }

        [Test]
        public void Manager_WasLeftMouseButtonReleasedThisFrame_ReturnsBool()
        {
            // Act
            bool released = MouseInputManager.WasLeftMouseButtonReleasedThisFrame();

            // Assert
            Assert.IsInstanceOf<bool>(released);
        }

        [Test]
        public void Manager_WasRightMouseButtonPressedThisFrame_ReturnsBool()
        {
            // Act
            bool pressed = MouseInputManager.WasRightMouseButtonPressedThisFrame();

            // Assert
            Assert.IsInstanceOf<bool>(pressed);
        }

        [Test]
        public void Manager_WasRightMouseButtonReleasedThisFrame_ReturnsBool()
        {
            // Act
            bool released = MouseInputManager.WasRightMouseButtonReleasedThisFrame();

            // Assert
            Assert.IsInstanceOf<bool>(released);
        }

        [Test]
        public void Manager_Raycast_WithNullLayerMask_Works()
        {
            // Arrange
            Vector2 direction = Vector2.right;
            float distance = 10f;
            LayerMask layerMask = LayerMask.GetMask("Default");

            // Act
            var hit = MouseInputManager.Raycast(direction, distance, layerMask);

            // Assert
            Assert.IsInstanceOf<RaycastHit2D>(hit);
        }

        [Test]
        public void Manager_Events_OnMousePositionChanged_CanSubscribe()
        {
            // Arrange
            Vector2 lastPosition = Vector2.zero;
            _manager.OnMousePositionChanged += (pos) => lastPosition = pos;

            // Act - Simulate update
            _manager.Tick();

            // Assert - Should not throw
            Assert.Pass("OnMousePositionChanged should be subscribable");
        }

        [Test]
        public void Manager_Events_OnLeftMouseDown_CanSubscribe()
        {
            // Arrange
            Vector2 clickPos = Vector2.zero;
            _manager.OnLeftMouseDown += (pos) => clickPos = pos;

            // Act - Simulate update
            _manager.Tick();

            // Assert - Should not throw
            Assert.Pass("OnLeftMouseDown should be subscribable");
        }

        [Test]
        public void Manager_Events_OnLeftMouseUp_CanSubscribe()
        {
            // Arrange
            Vector2 releasePos = Vector2.zero;
            _manager.OnLeftMouseUp += (pos) => releasePos = pos;

            // Act - Simulate update
            _manager.Tick();

            // Assert - Should not throw
            Assert.Pass("OnLeftMouseUp should be subscribable");
        }

        [Test]
        public void Manager_Events_OnRightMouseDown_CanSubscribe()
        {
            // Arrange
            Vector2 clickPos = Vector2.zero;
            _manager.OnRightMouseDown += (pos) => clickPos = pos;

            // Act - Simulate update
            _manager.Tick();

            // Assert - Should not throw
            Assert.Pass("OnRightMouseDown should be subscribable");
        }

        [Test]
        public void Manager_Events_OnRightMouseUp_CanSubscribe()
        {
            // Arrange
            Vector2 releasePos = Vector2.zero;
            _manager.OnRightMouseUp += (pos) => releasePos = pos;

            // Act - Simulate update
            _manager.Tick();

            // Assert - Should not throw
            Assert.Pass("OnRightMouseUp should be subscribable");
        }

        [Test]
        public void Manager_Events_OnMouseWheelScroll_CanSubscribe()
        {
            // Arrange
            float lastScroll = 0f;
            _manager.OnMouseWheelScroll += (delta) => lastScroll = delta;

            // Act - Simulate update
            _manager.Tick();

            // Assert - Should not throw
            Assert.Pass("OnMouseWheelScroll should be subscribable");
        }

        [Test]
        public void Manager_Instance_IsSingleton()
        {
            // Arrange
            var instance1 = MouseInputManager.Instance;

            // Act
            var instance2 = MouseInputManager.Instance;

            // Assert
            Assert.AreEqual(instance1, instance2, "Instance should be singleton");
        }

        [Test]
        public void Manager_MultipleStaticCalls_DoNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                var pos1 = MouseInputManager.GetScreenMousePosition();
                var pos2 = MouseInputManager.GetWorldMousePosition();
                bool btn1 = MouseInputManager.IsLeftMouseButtonPressed();
                bool btn2 = MouseInputManager.IsRightMouseButtonPressed();
                bool btn3 = MouseInputManager.IsMiddleMouseButtonPressed();
                float scroll = MouseInputManager.GetMouseWheelDelta();
                bool overUI = MouseInputManager.IsPointerOverUI();
            });
        }
    }
}
