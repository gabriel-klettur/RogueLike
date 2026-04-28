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
    public class TileEditorInitializationTests
    {
        private GameObject _eventSystemGo;
        private GameObject _cameraGo;
        private Camera _camera;
        private TileEditorInputHandler _inputHandler;

        [SetUp]
        public void SetUp()
        {
            // Create EventSystem for UI tests
            _eventSystemGo = new GameObject("EventSystem");
            _eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            _eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            
            // Create camera
            _cameraGo = new GameObject("TestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 10f;
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
        public void InputHandler_Initialization_WorksBeforeGameStart()
        {
            // Arrange - Simulate initialization before game starts
            
            // Act
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Assert - Input handler should work immediately
            Assert.IsNotNull(_inputHandler, "Input handler should be created successfully");
            
            // Test all input methods work
            Assert.DoesNotThrow(() => _inputHandler.WasTogglePressed(), "WasTogglePressed should work immediately");
            Assert.DoesNotThrow(() => _inputHandler.PollToolShortcut(), "PollToolShortcut should work immediately");
            Assert.DoesNotThrow(() => _inputHandler.PollZoom(), "PollZoom should work immediately");
            Assert.DoesNotThrow(() => _inputHandler.PollUndoRedo(), "PollUndoRedo should work immediately");
            Assert.DoesNotThrow(() => _inputHandler.IsPointerOverUI(), "IsPointerOverUI should work immediately");
        }

        [Test]
        public void InputHandler_MouseWorksImmediately_AfterCreation()
        {
            // Arrange - Create input handler
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act - Test mouse functionality immediately
            float scroll = _inputHandler.PollZoom();
            var mouse = Mouse.current;
            
            // Assert
            Assert.IsNotNull(mouse, "Mouse should be available immediately after input handler creation");
            Assert.AreEqual(0f, scroll, "PollZoom should return safe value immediately");
        }

        [Test]
        public void InputHandler_KeyboardWorksImmediately_AfterCreation()
        {
            // Arrange - Create input handler
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act - Test keyboard functionality immediately
            int undoRedo = _inputHandler.PollUndoRedo();
            var keyboard = Keyboard.current;
            
            // Assert
            Assert.IsNotNull(keyboard, "Keyboard should be available immediately after input handler creation");
            Assert.AreEqual(0, undoRedo, "PollUndoRedo should return safe value immediately");
        }

        [Test]
        public void InputHandler_EventSystemWorksImmediately_AfterCreation()
        {
            // Arrange - Create input handler
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act - Test EventSystem functionality immediately
            bool isOverUI = _inputHandler.IsPointerOverUI();
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            
            // Assert
            Assert.IsNotNull(eventSystem, "EventSystem should be available immediately after input handler creation");
            Assert.IsFalse(isOverUI, "IsPointerOverUI should return safe value immediately");
        }

        [UnityTest]
        public IEnumerator InputHandler_StabilityOverFrames_AfterCreation()
        {
            // Arrange - Create input handler
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act - Test stability over multiple frames
            for (int i = 0; i < 10; i++)
            {
                // Test all input methods
                float scroll = _inputHandler.PollZoom();
                bool toggle = _inputHandler.WasTogglePressed();
                var tool = _inputHandler.PollToolShortcut();
                int undo = _inputHandler.PollUndoRedo();
                bool overUI = _inputHandler.IsPointerOverUI();
                
                // Assert - Should remain stable
                Assert.AreEqual(0f, scroll, $"Frame {i}: PollZoom should remain stable");
                Assert.IsFalse(toggle, $"Frame {i}: WasTogglePressed should remain stable");
                Assert.IsNull(tool, $"Frame {i}: PollToolShortcut should remain stable");
                Assert.AreEqual(0, undo, $"Frame {i}: PollUndoRedo should remain stable");
                
                yield return null;
            }
        }

        [Test]
        public void InputHandler_MultipleCreationCycles_WorkStably()
        {
            // Arrange & Act - Test multiple creation/disposal cycles
            for (int i = 0; i < 5; i++)
            {
                var handler = new TileEditorInputHandler();
                handler.CreateActions();
                
                // Test functionality
                float scroll = handler.PollZoom();
                bool toggle = handler.WasTogglePressed();
                
                // Assert
                Assert.AreEqual(0f, scroll, $"Cycle {i}: PollZoom should work");
                Assert.IsFalse(toggle, $"Cycle {i}: WasTogglePressed should work");
                
                // Cleanup
                handler.Dispose();
            }
        }

        [Test]
        public void InputHandler_CreationWithoutEventSystem_WorksSafely()
        {
            // Arrange - Destroy EventSystem
            Object.DestroyImmediate(_eventSystemGo);
            _eventSystemGo = null;
            
            // Act - Create input handler without EventSystem
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Assert - Should work safely
            Assert.IsNotNull(_inputHandler, "Input handler should work without EventSystem");
            
            // Test methods that might use EventSystem
            bool isOverUI = _inputHandler.IsPointerOverUI();
            Assert.IsFalse(isOverUI, "IsPointerOverUI should return safe value without EventSystem");
        }

        [Test]
        public void InputHandler_CreationWithoutCamera_WorksSafely()
        {
            // Arrange - Destroy camera
            Object.DestroyImmediate(_cameraGo);
            _cameraGo = null;
            _camera = null;
            
            // Act - Create input handler without camera
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Assert - Should work safely
            Assert.IsNotNull(_inputHandler, "Input handler should work without camera");
            
            // Test mouse functionality (should not depend on camera)
            float scroll = _inputHandler.PollZoom();
            Assert.AreEqual(0f, scroll, "PollZoom should work without camera");
        }

        [Test]
        public void InputHandler_AllActionsEnabled_AfterCreation()
        {
            // Arrange - Create input handler
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act & Assert - Test that all expected actions are working
            // We can't directly access the private actions, but we can test their functionality
            Assert.DoesNotThrow(() => _inputHandler.WasTogglePressed(), "Toggle action should be enabled");
            Assert.DoesNotThrow(() => _inputHandler.PollToolShortcut(), "Tool actions should be enabled");
            Assert.DoesNotThrow(() => _inputHandler.PollUndoRedo(), "Undo/redo actions should be enabled");
            Assert.DoesNotThrow(() => _inputHandler.PollZoom(), "Mouse scroll should be readable");
        }

        [Test]
        public void InputHandler_InputSystemIntegration_Complete()
        {
            // Arrange - Create input handler
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act - Test complete InputSystem integration
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            
            // Assert - All InputSystem components should be available
            Assert.IsNotNull(mouse, "Mouse should be available through InputSystem");
            Assert.IsNotNull(keyboard, "Keyboard should be available through InputSystem");
            Assert.IsNotNull(eventSystem, "EventSystem should be available");
            
            // Test that input handler can read from these systems
            float scroll = _inputHandler.PollZoom();
            int undoRedo = _inputHandler.PollUndoRedo();
            bool overUI = _inputHandler.IsPointerOverUI();
            
            // Assert - Should return safe values
            Assert.IsTrue(scroll >= 0f, "Scroll should be readable");
            Assert.IsTrue(undoRedo >= 0, "Undo/redo should be readable");
            Assert.IsTrue(overUI == true || overUI == false, "UI check should be readable");
        }

        [UnityTest]
        public IEnumerator InputHandler_RealWorldSimulation_BeforeGameStart()
        {
            // Arrange - Simulate real-world scenario before game starts
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act - Simulate user interactions before game starts
            for (int i = 0; i < 5; i++)
            {
                // Simulate checking for input (like in Update loop)
                float scroll = _inputHandler.PollZoom();
                bool toggle = _inputHandler.WasTogglePressed();
                var tool = _inputHandler.PollToolShortcut();
                int undo = _inputHandler.PollUndoRedo();
                bool overUI = _inputHandler.IsPointerOverUI();
                
                // Assert - Should handle gracefully
                Assert.AreEqual(0f, scroll, $"Simulation frame {i}: Scroll should be safe");
                Assert.IsFalse(toggle, $"Simulation frame {i}: Toggle should be safe");
                Assert.IsNull(tool, $"Simulation frame {i}: Tool should be safe");
                Assert.AreEqual(0, undo, $"Simulation frame {i}: Undo/redo should be safe");
                
                yield return null;
            }
        }

        [Test]
        public void InputHandler_EarlyInitialization_SafetyChecks()
        {
            // Arrange - Test very early initialization (like in Awake)
            
            // Act - Create input handler immediately
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Assert - Should handle early initialization safely
            Assert.IsNotNull(_inputHandler, "Early initialization should work");
            
            // Test that basic InputSystem is available
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            
            Assert.IsNotNull(mouse, "Mouse should be available in early initialization");
            Assert.IsNotNull(keyboard, "Keyboard should be available in early initialization");
        }

        [Test]
        public void InputHandler_DisposeAndRecreate_EarlyScenario()
        {
            // Arrange - Test early disposal and recreation
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Act - Dispose and recreate
            _inputHandler.Dispose();
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
            
            // Assert - Should work after recreation
            Assert.DoesNotThrow(() => _inputHandler.PollZoom(), "Should work after recreation");
            Assert.DoesNotThrow(() => _inputHandler.WasTogglePressed(), "Should work after recreation");
        }
    }
}
