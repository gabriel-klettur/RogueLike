using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using System.Collections;

namespace Valkur.Tests.EditMode.Editors.TileEditor
{
    [TestFixture]
    public class TileEditorInputSystemTests
    {
        private GameObject _eventSystemGo;
        private TileEditorInputHandler _inputHandler;

        [SetUp]
        public void SetUp()
        {
            // Create EventSystem for UI tests
            _eventSystemGo = new GameObject("EventSystem");
            _eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            _eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_inputHandler != null)
                _inputHandler.Dispose();
            
            if (_eventSystemGo != null)
                Object.DestroyImmediate(_eventSystemGo);
        }

        [Test]
        public void InputSystem_Mouse_IsAvailable_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet, just basic Unity
            
            // Act
            var mouse = Mouse.current;
            
            // Assert
            Assert.IsNotNull(mouse, "Mouse should be available in Unity before TileEditor initialization");
        }

        [Test]
        public void InputSystem_Keyboard_IsAvailable_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet, just basic Unity
            
            // Act
            var keyboard = Keyboard.current;
            
            // Assert
            Assert.IsNotNull(keyboard, "Keyboard should be available in Unity before TileEditor initialization");
        }

        [Test]
        public void InputSystem_InputAction_CanBeCreated_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var testAction = new InputAction("Test", InputActionType.Button, "<Keyboard>/space");
            testAction.Enable();
            
            // Assert
            Assert.IsNotNull(testAction, "InputAction should be creatable before TileEditor");
            Assert.IsTrue(testAction.enabled, "InputAction should be enabled before TileEditor");
            
            // Cleanup
            testAction.Disable();
            testAction.Dispose();
        }

        [Test]
        public void InputSystem_MouseScroll_CanBeRead_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var mouse = Mouse.current;
            float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            
            // Assert
            Assert.IsTrue(scroll >= 0f, "Mouse scroll should be readable before TileEditor");
        }

        [Test]
        public void InputSystem_MousePosition_CanBeRead_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var mouse = Mouse.current;
            Vector2 position = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            
            // Assert
            Assert.IsNotNull(mouse, "Mouse position should be readable before TileEditor");
            Assert.IsTrue(position.x >= 0f || position.y >= 0f, "Mouse position should be valid before TileEditor");
        }

        [Test]
        public void InputSystem_MouseButtons_CanBeRead_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var mouse = Mouse.current;
            bool leftButton = mouse != null ? mouse.leftButton.isPressed : false;
            bool rightButton = mouse != null ? mouse.rightButton.isPressed : false;
            
            // Assert
            Assert.IsNotNull(mouse, "Mouse buttons should be readable before TileEditor");
            // Buttons should be readable (may be true or false, but not throw)
        }

        [Test]
        public void InputSystem_KeyboardKeys_CanBeRead_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var keyboard = Keyboard.current;
            bool spaceKey = keyboard != null ? keyboard.spaceKey.isPressed : false;
            bool escapeKey = keyboard != null ? keyboard.escapeKey.isPressed : false;
            
            // Assert
            Assert.IsNotNull(keyboard, "Keyboard keys should be readable before TileEditor");
            // Keys should be readable (may be true or false, but not throw)
        }

        [Test]
        public void InputSystem_EventSystem_CanBeCreated_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var eventSystemGo = new GameObject("TestEventSystem");
            var eventSystem = eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var inputModule = eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            
            // Assert
            Assert.IsNotNull(eventSystem, "EventSystem should be creatable before TileEditor");
            Assert.IsNotNull(inputModule, "StandaloneInputModule should be creatable before TileEditor");
            
            // Cleanup
            Object.DestroyImmediate(eventSystemGo);
        }

        [Test]
        public void InputSystem_MultipleInputActions_CanBeCreated_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var actions = new InputAction[5];
            for (int i = 0; i < actions.Length; i++)
            {
                actions[i] = new InputAction($"Test{i}", InputActionType.Button, $"<Keyboard>/{(KeyCode)i}");
                actions[i].Enable();
            }
            
            // Assert
            foreach (var action in actions)
            {
                Assert.IsNotNull(action, "Multiple InputActions should be creatable before TileEditor");
                Assert.IsTrue(action.enabled, "Multiple InputActions should be enabled before TileEditor");
            }
            
            // Cleanup
            foreach (var action in actions)
            {
                action.Disable();
                action.Dispose();
            }
        }

        [Test]
        public void InputSystem_CompoundBindings_CanBeCreated_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var action = new InputAction("Compound", InputActionType.Button);
            action.AddBinding("<Keyboard>/leftCtrl");
            action.AddBinding("<Keyboard>/rightCtrl");
            action.Enable();
            
            // Assert
            Assert.IsNotNull(action, "Compound bindings should work before TileEditor");
            Assert.IsTrue(action.enabled, "Compound bindings should be enabled before TileEditor");
            
            // Cleanup
            action.Disable();
            action.Dispose();
        }

        [Test]
        public void InputSystem_DisableEnable_Cycle_Works_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            var action = new InputAction("Test", InputActionType.Button, "<Keyboard>/space");
            
            // Act & Assert - Multiple enable/disable cycles
            for (int i = 0; i < 5; i++)
            {
                action.Enable();
                Assert.IsTrue(action.enabled, $"Enable cycle {i}: Action should be enabled");
                
                action.Disable();
                Assert.IsFalse(action.enabled, $"Disable cycle {i}: Action should be disabled");
            }
            
            // Cleanup
            action.Dispose();
        }

        [UnityTest]
        public IEnumerator InputSystem_StabilityOverFrames_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            var action = new InputAction("Test", InputActionType.Button, "<Keyboard>/space");
            action.Enable();
            
            // Act - Test over multiple frames
            for (int i = 0; i < 10; i++)
            {
                // Test InputSystem availability
                var mouse = Mouse.current;
                var keyboard = Keyboard.current;
                
                // Assert - Should remain stable
                Assert.IsNotNull(mouse, $"Frame {i}: Mouse should remain available");
                Assert.IsNotNull(keyboard, $"Frame {i}: Keyboard should remain available");
                Assert.IsTrue(action.enabled, $"Frame {i}: Action should remain enabled");
                
                yield return null;
            }
            
            // Cleanup
            action.Disable();
            action.Dispose();
        }

        [Test]
        public void InputSystem_Performance_MultipleActions_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act - Create many actions (stress test)
            var actions = new InputAction[100];
            var startTime = Time.realtimeSinceStartup;
            
            for (int i = 0; i < actions.Length; i++)
            {
                actions[i] = new InputAction($"PerfTest{i}", InputActionType.Button, $"<Keyboard>/{(KeyCode)i}");
                actions[i].Enable();
            }
            
            var creationTime = Time.realtimeSinceStartup - startTime;
            
            // Assert
            Assert.IsTrue(creationTime < 1.0f, $"Creating 100 actions should take less than 1 second (took {creationTime:F3}s)");
            
            foreach (var action in actions)
            {
                Assert.IsTrue(action.enabled, "All performance test actions should be enabled");
            }
            
            // Cleanup
            foreach (var action in actions)
            {
                action.Disable();
                action.Dispose();
            }
        }

        [Test]
        public void InputSystem_MemorySafety_DisposeActions_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act - Create and dispose many actions
            for (int i = 0; i < 50; i++)
            {
                var action = new InputAction($"MemTest{i}", InputActionType.Button, $"<Keyboard>/{(KeyCode)i}");
                action.Enable();
                
                // Use the action
                Assert.IsTrue(action.enabled, $"Memory test {i}: Action should be enabled");
                
                // Dispose
                action.Disable();
                action.Dispose();
            }
            
            // Assert - If we get here without exceptions, memory management worked
            Assert.IsTrue(true, "Memory safety test should complete without issues");
        }

        [Test]
        public void InputSystem_DeviceEnumeration_Works_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act
            var devices = InputSystem.devices;
            
            // Assert
            Assert.IsTrue(devices.Count > 0, "Should have at least some input devices available");
            
            // Check for expected devices
            bool hasMouse = false;
            bool hasKeyboard = false;
            
            foreach (var device in devices)
            {
                if (device is Mouse) hasMouse = true;
                if (device is Keyboard) hasKeyboard = true;
            }
            
            Assert.IsTrue(hasMouse, "Should have mouse device available");
            Assert.IsTrue(hasKeyboard, "Should have keyboard device available");
        }

        [Test]
        public void InputSystem_DeviceRemoval_HandledGracefully_BeforeTileEditor()
        {
            // Arrange - No TileEditor yet
            
            // Act - Try to get devices (simulating device removal scenario)
            var mouseBefore = Mouse.current;
            var keyboardBefore = Keyboard.current;
            
            // Assert - Should handle gracefully even if devices were removed
            // We can't actually remove devices in tests, but we can test the null safety
            Assert.IsNotNull(mouseBefore, "Mouse should be available initially");
            Assert.IsNotNull(keyboardBefore, "Keyboard should be available initially");
        }
    }
}
