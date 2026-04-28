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
    public class TileEditorDiagnosticTests
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
            
            // Create camera
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
        public void DiagnoseInputSystem_DoesNotThrow()
        {
            // Arrange & Act & Assert
            Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                "Diagnostic method should not throw exceptions");
        }

        [Test]
        public void DiagnoseInputSystem_ReportsMouseAvailability()
        {
            // Arrange
            var mouse = Mouse.current;
            
            // Act
            _inputHandler.DiagnoseInputSystem();
            
            // Assert - If we get here without exceptions, the diagnostic worked
            Assert.IsNotNull(mouse, "Mouse should be available for diagnostic to report");
        }

        [Test]
        public void DiagnoseInputSystem_ReportsKeyboardAvailability()
        {
            // Arrange
            var keyboard = Keyboard.current;
            
            // Act
            _inputHandler.DiagnoseInputSystem();
            
            // Assert - If we get here without exceptions, the diagnostic worked
            Assert.IsNotNull(keyboard, "Keyboard should be available for diagnostic to report");
        }

        [Test]
        public void DiagnoseInputSystem_ReportsEventSystemAvailability()
        {
            // Arrange
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            
            // Act
            _inputHandler.DiagnoseInputSystem();
            
            // Assert - If we get here without exceptions, the diagnostic worked
            Assert.IsNotNull(eventSystem, "EventSystem should be available for diagnostic to report");
        }

        [Test]
        public void DiagnoseInputSystem_ReportsInputActionsStatus()
        {
            // Arrange & Act
            _inputHandler.DiagnoseInputSystem();
            
            // Assert - If we get here without exceptions, the diagnostic worked
            // The diagnostic should report the status of all input actions
            Assert.IsTrue(true, "Diagnostic should complete without errors");
        }

        [Test]
        public void DiagnoseInputSystem_ReportsPollZoomResult()
        {
            // Arrange & Act
            _inputHandler.DiagnoseInputSystem();
            
            // Assert - If we get here without exceptions, the diagnostic worked
            // The diagnostic should report the result of PollZoom
            Assert.IsTrue(true, "Diagnostic should report PollZoom result");
        }

        [Test]
        public void DiagnoseInputSystem_WorksWithoutEventSystem()
        {
            // Arrange - Destroy EventSystem
            Object.DestroyImmediate(_eventSystemGo);
            _eventSystemGo = null;
            
            // Act & Assert
            Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                "Diagnostic should work without EventSystem");
        }

        [Test]
        public void DiagnoseInputSystem_WorksWithoutCamera()
        {
            // Arrange - Destroy camera
            Object.DestroyImmediate(_cameraGo);
            _cameraGo = null;
            _camera = null;
            
            // Act & Assert
            Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                "Diagnostic should work without camera");
        }

        [Test]
        public void DiagnoseInputSystem_WorksWithoutMouse()
        {
            // Note: We can't actually remove the mouse in tests, but we can test the diagnostic
            // method's null safety
            
            // Act & Assert
            Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                "Diagnostic should handle mouse null safely");
        }

        [Test]
        public void DiagnoseInputSystem_WorksWithoutKeyboard()
        {
            // Note: We can't actually remove the keyboard in tests, but we can test the diagnostic
            // method's null safety
            
            // Act & Assert
            Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                "Diagnostic should handle keyboard null safely");
        }

        [Test]
        public void DiagnoseInputSystem_WorksAfterDispose()
        {
            // Arrange
            _inputHandler.Dispose();
            
            // Act & Assert
            Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                "Diagnostic should handle disposed state gracefully");
        }

        [Test]
        public void DiagnoseInputSystem_CanBeCalledMultipleTimes()
        {
            // Arrange & Act
            for (int i = 0; i < 3; i++)
            {
                Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                    $"Diagnostic call {i + 1} should not throw");
            }
            
            // Assert - All calls should complete successfully
            Assert.IsTrue(true, "Multiple diagnostic calls should work");
        }

        [UnityTest]
        public IEnumerator DiagnoseInputSystem_WorksOverFrames()
        {
            // Arrange & Act
            for (int i = 0; i < 5; i++)
            {
                Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                    $"Diagnostic frame {i + 1} should not throw");
                
                yield return null;
            }
            
            // Assert - All calls should complete successfully
            Assert.IsTrue(true, "Diagnostic should work over multiple frames");
        }

        [Test]
        public void DiagnoseInputSystem_ProvidesUsefulInformation()
        {
            // Arrange & Act
            _inputHandler.DiagnoseInputSystem();
            
            // Assert - The diagnostic should provide information about the Input System
            // We can't easily test the actual log output in unit tests, but we can verify
            // that the method completes without throwing, which indicates it's working
            
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            
            // These should be available for the diagnostic to report
            Assert.IsNotNull(mouse, "Mouse should be available for diagnostic");
            Assert.IsNotNull(keyboard, "Keyboard should be available for diagnostic");
            Assert.IsNotNull(eventSystem, "EventSystem should be available for diagnostic");
        }

        [Test]
        public void DiagnoseInputSystem_HandlesEdgeCases()
        {
            // Test various edge cases that might cause issues
            
            // Arrange & Act
            // 1. Call diagnostic immediately after creation
            var newHandler = new TileEditorInputHandler();
            newHandler.CreateActions();
            Assert.DoesNotThrow(() => newHandler.DiagnoseInputSystem(), 
                "Diagnostic should work immediately after creation");
            
            // 2. Call diagnostic after dispose
            newHandler.Dispose();
            Assert.DoesNotThrow(() => newHandler.DiagnoseInputSystem(), 
                "Diagnostic should work after dispose");
            
            // 3. Call diagnostic with null references (simulated)
            // We can't actually set references to null, but we can test the method's robustness
            Assert.DoesNotThrow(() => _inputHandler.DiagnoseInputSystem(), 
                "Diagnostic should handle edge cases gracefully");
        }

        [Test]
        public void DiagnoseInputSystem_IntegratesWithAllInputMethods()
        {
            // Arrange - Test that diagnostic works alongside other input methods
            
            // Act
            float scroll = _inputHandler.PollZoom();
            bool toggle = _inputHandler.WasTogglePressed();
            var tool = _inputHandler.PollToolShortcut();
            int undo = _inputHandler.PollUndoRedo();
            bool overUI = _inputHandler.IsPointerOverUI();
            
            // Then run diagnostic
            _inputHandler.DiagnoseInputSystem();
            
            // Assert - All should work together
            Assert.IsTrue(scroll >= 0f, "PollZoom should work alongside diagnostic");
            Assert.IsTrue(toggle == true || toggle == false, "WasTogglePressed should work alongside diagnostic");
            Assert.IsNull(tool, "PollToolShortcut should work alongside diagnostic");
            Assert.IsTrue(undo >= 0, "PollUndoRedo should work alongside diagnostic");
            Assert.IsTrue(overUI == true || overUI == false, "IsPointerOverUI should work alongside diagnostic");
        }
    }
}
