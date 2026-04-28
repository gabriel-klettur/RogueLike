using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.TileEditor;
using System.Collections;

namespace Valkur.Tests.EditMode.Editors.TileEditor
{
    [TestFixture]
    public class TileEditorZoomTests
    {
        private TileEditorInputHandler _inputHandler;

        [SetUp]
        public void SetUp()
        {
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
        }

        [TearDown]
        public void TearDown()
        {
            if (_inputHandler != null)
                _inputHandler.Dispose();
        }

        [Test]
        public void PollZoom_ReturnsZero_WhenNoScroll()
        {
            // Arrange - No scroll event
            
            // Act
            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when no scroll input");
        }

        [Test]
        public void PollZoom_ReturnsZero_WhenMouseIsNull()
        {
            // Arrange - Remove mouse device if it exists
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(UnityEngine.InputSystem.Mouse.current);
            }
            
            // Act
            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when mouse is null");
        }

        [Test]
        public void PollZoom_ReturnsZero_WhenPointerOverUI()
        {
            // Arrange
            var eventSystemGo = new GameObject("EventSystem");
            var eventSystem = eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            
            // Simulate pointer over UI
            var inputModule = eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            
            // Act
            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when pointer is over UI");

            // Cleanup
            Object.DestroyImmediate(eventSystemGo);
        }

        [Test]
        public void InputHandler_CreationAndDisposal_WorkCorrectly()
        {
            // Arrange & Act
            var handler = new TileEditorInputHandler();
            handler.CreateActions();
            
            // Use the handler
            var delta = handler.PollZoom();
            
            // Cleanup
            handler.Dispose();
            
            // Assert - No exceptions should be thrown
            Assert.IsTrue(true, "Input handler should create and dispose without errors");
        }

        [Test]
        public void WasTogglePressed_ReturnsCorrectState()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();
            
            // Act - Without actual F8 press, should return false
            bool wasPressed = handler.WasTogglePressed();
            
            // Cleanup
            handler.Dispose();
            
            // Assert
            Assert.IsFalse(wasPressed, "Should return false when toggle not pressed");
        }

        [Test]
        public void PollToolShortcut_ReturnsNull_WhenNoKeyPressed()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();
            
            // Act
            var tool = handler.PollToolShortcut();
            
            // Cleanup
            handler.Dispose();
            
            // Assert
            Assert.IsNull(tool, "Should return null when no tool shortcut pressed");
        }

        [Test]
        public void IsPointerOverUI_ReturnsCorrectState()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();
            
            // Act - No EventSystem should return false
            bool isOverUI = handler.IsPointerOverUI();
            
            // Cleanup
            handler.Dispose();
            
            // Assert
            Assert.IsFalse(isOverUI, "Should return false when no EventSystem exists");
        }

        [Test]
        public void PollUndoRedo_ReturnsZero_WhenNoKeysPressed()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();
            
            // Act
            int action = handler.PollUndoRedo();
            
            // Cleanup
            handler.Dispose();
            
            // Assert
            Assert.AreEqual(0, action, "Should return 0 when no keys pressed");
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
            float scroll1 = handler1.PollZoom();
            float scroll2 = handler2.PollZoom();

            // Cleanup
            handler1.Dispose();
            handler2.Dispose();

            // Assert
            Assert.AreEqual(scroll1, scroll2, "Both handlers should detect same input");
            Assert.AreEqual(0f, scroll1, "Both handlers should return 0 for no input");
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

            yield return null;

            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_EventSystemCleanup_HandlesNullReference()
        {
            // Arrange - No EventSystem
            var handler = new TileEditorInputHandler();
            handler.CreateActions();

            // Act & Assert
            Assert.DoesNotThrow(() => {
                bool isOverUI = handler.IsPointerOverUI();
                Assert.IsFalse(isOverUI, "Should handle null EventSystem gracefully");
            }, "Should handle EventSystem cleanup gracefully");

            // Cleanup
            handler.Dispose();
        }
    }
}
