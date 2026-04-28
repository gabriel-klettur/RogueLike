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
    public class TileEditorInputTests
    {
        private TileEditorInputHandler _inputHandler;
        private GameObject _eventSystemGo;

        [SetUp]
        public void SetUp()
        {
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
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
        public void InputHandler_Creation_WorksCorrectly()
        {
            // Arrange & Act
            var handler = new TileEditorInputHandler();
            
            // Assert
            Assert.IsNotNull(handler, "Input handler should be created");
            
            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_CreateActions_DoesNotThrow()
        {
            // Arrange & Act & Assert
            Assert.DoesNotThrow(() => _inputHandler.CreateActions(), 
                "CreateActions should not throw exceptions");
        }

        [Test]
        public void InputHandler_Dispose_WorksCorrectly()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();

            // Act & Assert
            Assert.DoesNotThrow(() => handler.Dispose(), 
                "Dispose should not throw exceptions");
        }

        [Test]
        public void InputHandler_DoubleDispose_DoesNotThrow()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();

            // Act & Assert
            Assert.DoesNotThrow(() => {
                handler.Dispose();
                handler.Dispose();
            }, "Double dispose should not throw exceptions");
        }

        [Test]
        public void WasTogglePressed_ReturnsFalse_WhenNotPressed()
        {
            // Act
            bool wasPressed = _inputHandler.WasTogglePressed();

            // Assert
            Assert.IsFalse(wasPressed, "Should return false when toggle not pressed");
        }

        [Test]
        public void PollToolShortcut_ReturnsNull_WhenNoKeyPressed()
        {
            // Act
            var tool = _inputHandler.PollToolShortcut();

            // Assert
            Assert.IsNull(tool, "Should return null when no tool shortcut pressed");
        }

        [Test]
        public void IsPointerOverUI_ReturnsFalse_WhenNoEventSystem()
        {
            // Arrange
            Object.DestroyImmediate(_eventSystemGo);
            _eventSystemGo = null;

            // Act
            bool isOverUI = _inputHandler.IsPointerOverUI();

            // Assert
            Assert.IsFalse(isOverUI, "Should return false when no EventSystem");
        }

        [Test]
        public void IsPointerOverUI_ReturnsTrue_WhenEventSystemExists()
        {
            // Act
            bool isOverUI = _inputHandler.IsPointerOverUI();

            // Assert
            Assert.IsTrue(isOverUI, "Should return true when EventSystem exists");
        }

        [Test]
        public void PollZoom_ReturnsZero_WhenNoMouse()
        {
            // Arrange
            InputSystem.RemoveDevice(Mouse.current);

            // Act
            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when no mouse device");
        }

        [Test]
        public void PollUndoRedo_ReturnsZero_WhenNoKeysPressed()
        {
            // Act
            int action = _inputHandler.PollUndoRedo();

            // Assert
            Assert.AreEqual(0, action, "Should return 0 when no keys pressed");
        }

        [UnityTest]
        public IEnumerator InputHandler_MultipleOperations_WorkCorrectly()
        {
            // Arrange
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();

            // Act - Simulate multiple inputs
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1) });
            InputSystem.Update();

            float scrollDelta = _inputHandler.PollZoom();
            bool wasToggle = _inputHandler.WasTogglePressed();
            var tool = _inputHandler.PollToolShortcut();
            int undoRedo = _inputHandler.PollUndoRedo();

            yield return null;

            // Assert
            Assert.IsTrue(scrollDelta > 0, "Should detect scroll");
            Assert.IsFalse(wasToggle, "Should not detect toggle");
            Assert.IsNull(tool, "Should not detect tool shortcut");
            Assert.AreEqual(0, undoRedo, "Should not detect undo/redo");
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

        [Test]
        public void InputHandler_CreatedWithNullActions_DoesNotThrow()
        {
            // Arrange & Act & Assert
            var handler = new TileEditorInputHandler();
            
            // Should not throw even without calling CreateActions
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
        public void InputHandler_MultipleInstances_WorkIndependently()
        {
            // Arrange
            var handler1 = new TileEditorInputHandler();
            var handler2 = new TileEditorInputHandler();
            
            handler1.CreateActions();
            handler2.CreateActions();

            // Act
            var mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1) });
            InputSystem.Update();

            float scroll1 = handler1.PollZoom();
            float scroll2 = handler2.PollZoom();

            // Cleanup
            handler1.Dispose();
            handler2.Dispose();

            // Assert
            Assert.AreEqual(scroll1, scroll2, "Both handlers should detect same input");
            Assert.IsTrue(scroll1 > 0, "Both handlers should detect scroll up");
        }

        [UnityTest]
        public IEnumerator InputHandler_InputSystemIntegration_WorksCorrectly()
        {
            // Arrange
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();

            // Test scroll input
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 2) });
            InputSystem.Update();

            float scrollDelta = _inputHandler.PollZoom();
            Assert.IsTrue(scrollDelta > 0, "Should detect scroll up");

            yield return null;

            // Test keyboard input simulation
            // Note: Actual keyboard shortcuts would need specific key events
            int undoRedo = _inputHandler.PollUndoRedo();
            Assert.AreEqual(0, undoRedo, "Should return 0 for no keyboard input");
        }

        [Test]
        public void InputHandler_EventSystemCleanup_HandlesNullReference()
        {
            // Arrange
            Object.DestroyImmediate(_eventSystemGo);
            _eventSystemGo = null;

            // Act & Assert
            Assert.DoesNotThrow(() => {
                bool isOverUI = _inputHandler.IsPointerOverUI();
                Assert.IsFalse(isOverUI, "Should handle null EventSystem gracefully");
            }, "Should handle EventSystem cleanup gracefully");
        }
    }
}
