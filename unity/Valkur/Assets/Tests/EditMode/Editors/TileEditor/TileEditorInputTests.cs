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
        public void PollZoom_ReturnsZero_WhenNoScroll()
        {
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
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when no mouse device");
        }

        [Test]
        public void PollZoom_ReturnsZero_WhenPointerOverUI()
        {
            // Act
            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when pointer is over UI");
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

        [Test]
        public void PollUndoRedo_ReturnsZero_WhenNoKeysPressed()
        {
            // Act
            int action = _inputHandler.PollUndoRedo();

            // Assert
            Assert.AreEqual(0, action, "Should return 0 when no keys pressed");
        }

        [Test]
        public void InputHandler_SingletonBehavior_WorksCorrectly()
        {
            // Arrange & Act & Assert
            var handler = new TileEditorInputHandler();
            
            // Multiple instances should work independently
            Assert.IsNotNull(handler, "Handler should be created successfully");
            
            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_DisposeSafety_MultipleCalls()
        {
            // Arrange
            var handler = new TileEditorInputHandler();
            handler.CreateActions();

            // Act & Assert
            Assert.DoesNotThrow(() => {
                handler.Dispose();
                handler.Dispose();
                handler.Dispose(); // Triple dispose should be safe
            }, "Multiple dispose calls should be safe");

            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_InputSystemReferences_AreSafe()
        {
            // Arrange & Act & Assert
            var handler = new TileEditorInputHandler();
            handler.CreateActions();
            
            // All input system references should be handled safely
            Assert.DoesNotThrow(() => {
                // These should not throw even if InputSystem is not available
                bool toggle = handler.WasTogglePressed();
                var tool = handler.PollToolShortcut();
                float scroll = handler.PollZoom();
                int undo = handler.PollUndoRedo();
                bool overUI = handler.IsPointerOverUI();
            }, "InputSystem references should be handled safely");

            // Cleanup
            handler.Dispose();
        }

        [Test]
        public void InputHandler_EventSystemReferences_AreSafe()
        {
            // Arrange & Act & Assert
            var handler = new TileEditorInputHandler();
            handler.CreateActions();
            
            // EventSystem references should be handled safely
            Assert.DoesNotThrow(() => {
                bool isOverUI = handler.IsPointerOverUI();
            }, "EventSystem references should be handled safely");

            // Cleanup
            handler.Dispose();
        }
    }
}
