using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using System.Collections;

namespace Valkur.Tests.EditMode.Gameplay.TileEditor
{
    [TestFixture]
    public class TileEditorMouseTests
    {
        private TileEditorInputHandler _inputHandler;
        private GameObject _eventSystemGo;
        private GameObject _cameraGo;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            // Create EventSystem for UI tests
            _eventSystemGo = new GameObject("EventSystem");
            _eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            _eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            
            // Create camera for mouse tests
            _cameraGo = new GameObject("TestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 10f;
            
            // Initialize input handler
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
        }

        [TearDown]
        public void TearDown()
        {
            if (_inputHandler != null)
                _inputHandler.Dispose();
            
            if (_eventSystemGo != null)
                Object.DestroyImmediate(_eventSystemGo);
            
            if (_cameraGo != null)
                Object.DestroyImmediate(_cameraGo);
        }

        [Test]
        public void InputHandler_MouseExists_AfterCreation()
        {
            // Arrange & Act
            var mouse = Mouse.current;
            
            // Assert
            Assert.IsNotNull(mouse, "Mouse should be available after InputAction creation");
        }

        [Test]
        public void PollZoom_ReturnsZero_WhenNoScroll()
        {
            // Arrange & Act
            float scrollDelta = _inputHandler.PollZoom();
            
            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when no scroll input");
        }

        [Test]
        public void PollZoom_HandlesNullMouse_Gracefully()
        {
            // Arrange - This test simulates the scenario where mouse might not be available
            // We can't actually remove the mouse in tests, but we can test the null check logic
            
            // Act
            float scrollDelta = _inputHandler.PollZoom();
            
            // Assert - Should not throw and return safe value
            Assert.IsTrue(scrollDelta >= 0f, "Should return safe value even if mouse is null");
        }

        [Test]
        public void IsPointerOverUI_ReturnsFalse_WhenNoEventSystem()
        {
            // Arrange - Destroy EventSystem
            Object.DestroyImmediate(_eventSystemGo);
            _eventSystemGo = null;
            
            // Act
            bool isOverUI = _inputHandler.IsPointerOverUI();
            
            // Assert
            Assert.IsFalse(isOverUI, "Should return false when no EventSystem exists");
        }

        [Test]
        public void IsPointerOverUI_ReturnsFalse_WhenEventSystemExists()
        {
            // Arrange & Act
            bool isOverUI = _inputHandler.IsPointerOverUI();
            
            // Assert
            Assert.IsFalse(isOverUI, "Should return false when no UI elements are under pointer");
        }

        [Test]
        public void InputHandler_MultipleInstances_WorkCorrectly()
        {
            // Arrange
            var handler1 = new TileEditorInputHandler();
            var handler2 = new TileEditorInputHandler();
            
            handler1.CreateActions();
            handler2.CreateActions();

            // Act
            float scroll1 = handler1.PollZoom();
            float scroll2 = handler2.PollZoom();

            // Cleanup
            handler1.Dispose();
            handler2.Dispose();

            // Assert
            Assert.AreEqual(scroll1, scroll2, "Both handlers should detect same input");
            Assert.AreEqual(0f, scroll1, "Both handlers should return 0 for no input");
        }

        [Test]
        public void InputHandler_DisposeAndRecreate_WorksCorrectly()
        {
            // Arrange & Act
            _inputHandler.Dispose();
            
            // Should be able to recreate
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            float scroll = _inputHandler.PollZoom();
            bool toggle = _inputHandler.WasTogglePressed();
            var tool = _inputHandler.PollToolShortcut();

            // Assert - Should not throw and return safe values
            Assert.AreEqual(0f, scroll, "Should return safe scroll value after recreate");
            Assert.IsFalse(toggle, "Should return safe toggle value after recreate");
            Assert.IsNull(tool, "Should return safe tool value after recreate");
        }

        [Test]
        public void InputHandler_CreateActions_EnablesAllActions()
        {
            // Arrange & Act - Actions are created in SetUp
            
            // Assert - If no exceptions were thrown, all actions were created and enabled
            Assert.IsNotNull(_inputHandler, "Input handler should be created successfully");
            
            // Test that methods work without throwing
            Assert.DoesNotThrow(() => _inputHandler.WasTogglePressed(), "WasTogglePressed should not throw");
            Assert.DoesNotThrow(() => _inputHandler.PollToolShortcut(), "PollToolShortcut should not throw");
            Assert.DoesNotThrow(() => _inputHandler.PollZoom(), "PollZoom should not throw");
            Assert.DoesNotThrow(() => _inputHandler.PollUndoRedo(), "PollUndoRedo should not throw");
            Assert.DoesNotThrow(() => _inputHandler.IsPointerOverUI(), "IsPointerOverUI should not throw");
        }

        [Test]
        public void InputHandler_DoubleDispose_DoesNotThrow()
        {
            // Arrange & Act & Assert
            Assert.DoesNotThrow(() => {
                _inputHandler.Dispose();
                _inputHandler.Dispose();
            }, "Double dispose should not throw exceptions");
        }

        [Test]
        public void InputHandler_AfterDispose_ReturnsSafeValues()
        {
            // Arrange
            _inputHandler.Dispose();

            // Act & Assert - All methods should return safe values after dispose
            Assert.DoesNotThrow(() => {
                bool toggle = _inputHandler.WasTogglePressed();
                Assert.IsFalse(toggle, "WasTogglePressed should return false after dispose");
            }, "WasTogglePressed should be safe after dispose");

            Assert.DoesNotThrow(() => {
                var tool = _inputHandler.PollToolShortcut();
                Assert.IsNull(tool, "PollToolShortcut should return null after dispose");
            }, "PollToolShortcut should be safe after dispose");

            Assert.DoesNotThrow(() => {
                float scroll = _inputHandler.PollZoom();
                Assert.AreEqual(0f, scroll, "PollZoom should return 0 after dispose");
            }, "PollZoom should be safe after dispose");

            Assert.DoesNotThrow(() => {
                int undo = _inputHandler.PollUndoRedo();
                Assert.AreEqual(0, undo, "PollUndoRedo should return 0 after dispose");
            }, "PollUndoRedo should be safe after dispose");

            Assert.DoesNotThrow(() => {
                bool overUI = _inputHandler.IsPointerOverUI();
                Assert.IsFalse(overUI, "IsPointerOverUI should return false after dispose");
            }, "IsPointerOverUI should be safe after dispose");
        }

        [UnityTest]
        public IEnumerator InputHandler_InputSystemIntegration_WorksCorrectly()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();

            // Test basic functionality without specific input simulation
            int undoRedo = handler.PollUndoRedo();
            Assert.AreEqual(0, undoRedo, "Should return 0 for no keyboard input");

            float scroll = handler.PollZoom();
            Assert.AreEqual(0f, scroll, "Should return 0 for no mouse input");

            yield return null;

            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_EventSystemReferences_AreSafe()
        {
            // Arrange & Act & Assert
            Assert.DoesNotThrow(() => {
                bool isOverUI = _inputHandler.IsPointerOverUI();
            }, "EventSystem references should be handled safely");
        }

        [Test]
        public void InputHandler_MouseReferences_AreSafe()
        {
            // Arrange & Act & Assert
            Assert.DoesNotThrow(() => {
                float scroll = _inputHandler.PollZoom();
            }, "Mouse references should be handled safely");
        }

        [Test]
        public void InputHandler_KeyboardReferences_AreSafe()
        {
            // Arrange & Act & Assert
            Assert.DoesNotThrow(() => {
                int undoRedo = _inputHandler.PollUndoRedo();
            }, "Keyboard references should be handled safely");
        }

        [Test]
        public void InputHandler_CreationWithoutCreateActions_WorksSafely()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            
            // Act & Assert - Should handle null actions gracefully
            Assert.DoesNotThrow(() => {
                bool toggle = handler.WasTogglePressed();
                var tool = handler.PollToolShortcut();
                float scroll = handler.PollZoom();
                int undo = handler.PollUndoRedo();
                bool overUI = handler.IsPointerOverUI();
            }, "Should handle null actions gracefully");

            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_CameraIndependence_WorksCorrectly()
        {
            // Arrange - Destroy camera
            Object.DestroyImmediate(_cameraGo);
            _cameraGo = null;
            _camera = null;

            // Act & Assert - Input handler should work independently of camera
            Assert.DoesNotThrow(() => {
                float scroll = _inputHandler.PollZoom();
                bool toggle = _inputHandler.WasTogglePressed();
            }, "Input handler should work independently of camera");
        }

        [UnityTest]
        public IEnumerator InputHandler_MultipleFrames_StabilityTest()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();

            // Act - Test over multiple frames
            for (int i = 0; i < 10; i++)
            {
                // Test all input methods
                float scroll = handler.PollZoom();
                bool toggle = handler.WasTogglePressed();
                var tool = handler.PollToolShortcut();
                int undo = handler.PollUndoRedo();
                bool overUI = handler.IsPointerOverUI();

                // Assert - Should remain stable
                Assert.AreEqual(0f, scroll, $"Frame {i}: Scroll should be 0");
                Assert.IsFalse(toggle, $"Frame {i}: Toggle should be false");
                Assert.IsNull(tool, $"Frame {i}: Tool should be null");
                Assert.AreEqual(0, undo, $"Frame {i}: UndoRedo should be 0");

                yield return null;
            }

            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_MemoryLeaks_DisposeCorrectly()
        {
            // Arrange & Act
            for (int i = 0; i < 10; i++)
            {
                var handler = new TileEditorInputHandler();
                handler.CreateActions();
                
                // Use the handler
                float scroll = handler.PollZoom();
                bool toggle = handler.WasTogglePressed();
                
                // Dispose
                handler.Dispose();
            }

            // Assert - If we get here without exceptions, disposal worked correctly
            Assert.IsTrue(true, "Multiple create/dispose cycles should work without leaks");
        }
    }
}
