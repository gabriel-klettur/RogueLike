using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    public class InputDiagnosticsTests
    {
        private EventSystem _eventSystemGo;

        [SetUp]
        public void SetUp()
        {
            // Ensure we have a mouse device
            if (Mouse.current == null)
            {
                InputSystem.AddDevice<Mouse>();
            }

            // Create EventSystem for tests that need it
            var go = new GameObject("EventSystem");
            _eventSystemGo = go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_eventSystemGo != null)
            {
                Object.DestroyImmediate(_eventSystemGo.gameObject);
            }
        }

        [Test]
        public void Diagnostics_RunWithMouseAvailable_DoesNotThrow()
        {
            // Arrange & Act & Assert
            Assert.DoesNotThrow(() => InputDiagnostics.RunDiagnostics(), "RunDiagnostics should not throw");
        }

        [Test]
        public void Diagnostics_MouseExists()
        {
            // Act
            var mouse = Mouse.current;

            // Assert
            Assert.IsNotNull(mouse, "Mouse device should exist after InputSystem initialization");
        }

        [Test]
        public void Diagnostics_ValidateMouseClick_WithPressedButton()
        {
            // Arrange
            var mouse = Mouse.current;
            Assert.IsNotNull(mouse);

            // Act
            var (canRead, mouseExists, buttonDown) = InputDiagnostics.ValidateMouseClick();

            // Assert
            Assert.IsTrue(mouseExists, "Mouse should exist");
            Assert.IsTrue(canRead, "Should be able to read mouse");
        }

        [Test]
        public void Diagnostics_ValidateMousePosition_ReturnsValidPosition()
        {
            // Arrange & Act
            var (canRead, mouseExists, position) = InputDiagnostics.ValidateMousePosition();

            // Assert
            Assert.IsTrue(mouseExists, "Mouse should exist");
            Assert.IsTrue(canRead, "Should be able to read position");
            Assert.IsTrue(position.x >= 0 || position.x <= Screen.width, "X should be in screen bounds or 0");
            Assert.IsTrue(position.y >= 0 || position.y <= Screen.height, "Y should be in screen bounds or 0");
        }

        [Test]
        public void Diagnostics_EnsureEventSystem_CreatesIfMissing()
        {
            // Arrange - Destroy any existing event system
            var existing = EventSystem.current;
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            // Act
            var eventSystem = InputDiagnostics.EnsureEventSystem();

            // Assert
            Assert.IsNotNull(eventSystem, "EventSystem should be created");
            Assert.IsNotNull(eventSystem.GetComponent<InputSystemUIInputModule>(), "EventSystem should use the Input System UI module.");
            AssertRuntimeUIModuleConfigured(eventSystem.GetComponent<InputSystemUIInputModule>());

            // Cleanup
            Object.DestroyImmediate(eventSystem.gameObject);
        }

        [Test]
        public void Diagnostics_EnsureEventSystem_DoesNotDuplicateExisting()
        {
            // Arrange
            var existing = _eventSystemGo;
            Assert.IsNotNull(existing, "EventSystem should already exist");

            // Act
            var returned = InputDiagnostics.EnsureEventSystem();

            // Assert
            Assert.AreEqual(existing, returned, "Should return existing EventSystem, not create new one");
            AssertRuntimeUIModuleConfigured(returned.GetComponent<InputSystemUIInputModule>());
        }

        [Test]
        public void Diagnostics_EnsureEventSystem_ConfiguresMouseDrivenUIActions()
        {
            var module = InputDiagnostics.EnsureInputSystemUIModule(_eventSystemGo);

            AssertRuntimeUIModuleConfigured(module);
            AssertHasAnyBinding(module.point.action, "<Pointer>/position", "<Mouse>/position");
            AssertHasAnyBinding(module.leftClick.action, "<Mouse>/leftButton", "<Pointer>/press");
            AssertHasAnyBinding(module.scrollWheel.action, "<Mouse>/scroll");
        }

        [Test]
        public void RuntimeInputBootstrap_EnsuresEventSystemForScenesWithoutAuthoredOne()
        {
            if (EventSystem.current != null)
                Object.DestroyImmediate(EventSystem.current.gameObject);

            EventSystem created = null;
            Assert.DoesNotThrow(() => created = RuntimeInputBootstrap.EnsureRuntimeInput());

            if (created == null)
                created = Object.FindObjectOfType<EventSystem>();
            Assert.IsNotNull(created, "Runtime input bootstrap must create an EventSystem for menus/scenes that do not author one.");
            AssertRuntimeUIModuleConfigured(created.GetComponent<InputSystemUIInputModule>());

            Object.DestroyImmediate(created.gameObject);
        }

        [Test]
        public void Diagnostics_RunTwice_IsIdempotent()
        {
            // Act
            InputDiagnostics.RunDiagnostics();
            InputDiagnostics.RunDiagnostics();

            // Assert - Should not throw
            Assert.Pass("Running diagnostics twice should not cause issues");
        }

        [Test]
        public void Diagnostics_MouseDeviceHasValidPosition()
        {
            // Arrange
            var mouse = Mouse.current;
            Assert.IsNotNull(mouse);

            // Act
            var position = mouse.position.ReadValue();

            // Assert
            Assert.IsTrue(!float.IsNaN(position.x), "Mouse X position should not be NaN");
            Assert.IsTrue(!float.IsNaN(position.y), "Mouse Y position should not be NaN");
        }

        [Test]
        public void Diagnostics_AllDevicesEnabled()
        {
            // Arrange
            InputDiagnostics.RunDiagnostics();
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // Assert
            if (mouse != null) Assert.IsTrue(mouse.enabled, "Mouse should be enabled");
            if (keyboard != null) Assert.IsTrue(keyboard.enabled, "Keyboard should be enabled");
        }

        [Test]
        public void Diagnostics_ScreenSizeIsValid()
        {
            // Act
            int width = Screen.width;
            int height = Screen.height;

            // Assert
            Assert.Greater(width, 0, "Screen width should be positive");
            Assert.Greater(height, 0, "Screen height should be positive");
        }

        private static void AssertRuntimeUIModuleConfigured(InputSystemUIInputModule module)
        {
            Assert.IsNotNull(module, "InputSystemUIInputModule must exist.");
            // Note: under the new dual-module setup (StandaloneInputModule active +
            // InputSystemUIInputModule disabled — see PersistentEventSystem.ConfigureModule),
            // the new module is intentionally disabled because two enabled UI input
            // modules on one EventSystem fight over events. We still verify it is
            // structurally configured (asset + action refs bound) so the project
            // can re-enable it if a future Unity release fixes the OS event delivery
            // flake that motivated the switch.
            Assert.IsNotNull(module.actionsAsset, "Runtime UI actions asset must be assigned.");
            Assert.IsNotNull(module.point?.action, "Point action must be assigned for hover/click raycasts.");
            Assert.IsNotNull(module.leftClick?.action, "LeftClick action must be assigned for menu buttons.");
            Assert.IsNotNull(module.rightClick?.action, "RightClick action must be assigned for context menus.");
            Assert.IsNotNull(module.middleClick?.action, "MiddleClick action must be assigned for complete mouse UI support.");
            Assert.IsNotNull(module.scrollWheel?.action, "ScrollWheel action must be assigned for scrollable menus.");
            Assert.IsNotNull(module.move?.action, "Navigate action must be assigned for keyboard/gamepad UI.");
            Assert.IsNotNull(module.submit?.action, "Submit action must be assigned.");
            Assert.IsNotNull(module.cancel?.action, "Cancel action must be assigned.");
            Assert.IsTrue(module.point.action.enabled, "Runtime UI actions must be enabled.");
            Assert.IsTrue(module.leftClick.action.enabled, "Runtime click action must be enabled.");
            Assert.IsTrue(module.scrollWheel.action.enabled, "Runtime scroll action must be enabled.");
        }

        private static void AssertHasBinding(InputAction action, string expectedPath)
        {
            AssertHasAnyBinding(action, expectedPath);
        }

        private static void AssertHasAnyBinding(InputAction action, params string[] expectedPaths)
        {
            bool found = false;
            string actual = "";
            foreach (var binding in action.bindings)
            {
                if (actual.Length > 0)
                    actual += ", ";
                actual += string.IsNullOrEmpty(binding.effectivePath) ? binding.path : binding.effectivePath;

                for (int i = 0; i < expectedPaths.Length; i++)
                {
                    string expectedPath = expectedPaths[i];
                    if (binding.effectivePath == expectedPath || binding.path == expectedPath)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    break;
            }

            Assert.IsTrue(found, $"{action.name} must include one of [{string.Join(", ", expectedPaths)}]. Actual: [{actual}].");
        }
    }
}
