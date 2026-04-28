using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay;
using System.Collections;

namespace Valkur.Tests.EditMode.Gameplay.TileEditor
{
    [TestFixture]
    public class TileEditorZoomTests
    {
        private GameObject _cameraSetupGo;
        private GameObject _tileEditorGo;
        private CameraSetup _cameraSetup;
        private TileEditorManager _tileEditor;
        private TileEditorInputHandler _inputHandler;
        private Camera _mainCamera;

        [SetUp]
        public void SetUp()
        {
            // Create main camera
            var cameraGo = new GameObject("Main Camera");
            _mainCamera = cameraGo.AddComponent<Camera>();
            _mainCamera.orthographic = true;
            _mainCamera.tag = "MainCamera";

            // Create CameraSetup with Cinemachine
            _cameraSetupGo = new GameObject("CameraSetup");
            _cameraSetup = _cameraSetupGo.AddComponent<CameraSetup>();

            // Create TileEditorManager
            _tileEditorGo = new GameObject("TileEditorManager");
            _tileEditor = _tileEditorGo.AddComponent<TileEditorManager>();
            
            // Create input handler for testing
            _inputHandler = new TileEditorInputHandler();
            _inputHandler.CreateActions();
        }

        [TearDown]
        public void TearDown()
        {
            if (_inputHandler != null)
                _inputHandler.Dispose();
            
            Object.DestroyImmediate(_tileEditorGo);
            Object.DestroyImmediate(_cameraSetupGo);
            Object.DestroyImmediate(_mainCamera.gameObject);
            
            // Reset singleton instances
            CameraSetup cameraSetupInstance = CameraSetup.Instance;
            if (cameraSetupInstance != null)
                Object.DestroyImmediate(cameraSetupInstance.gameObject);
        }

        [Test]
        public void CameraSetup_SetTileEditorZoom_AppliesCorrectSize()
        {
            // Arrange
            float expectedSize = 7.5f;

            // Act
            _cameraSetup.SetTileEditorZoom(expectedSize);

            // Assert - The zoom should be applied in the next Update frame
            // We need to call Update manually since we're not in play mode
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            float actualSize = _cameraSetup.GetCurrentOrthographicSize();
            Assert.AreEqual(expectedSize, actualSize, 0.01f, "Tile editor zoom should be applied correctly");
        }

        [Test]
        public void CameraSetup_SetTileEditorZoom_ClampsToBounds()
        {
            // Arrange
            float tooSmallSize = 0.1f;
            float tooLargeSize = 100f;

            // Act - Test minimum bound
            _cameraSetup.SetTileEditorZoom(tooSmallSize);
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float clampedSmallSize = _cameraSetup.GetCurrentOrthographicSize();

            // Act - Test maximum bound
            _cameraSetup.SetTileEditorZoom(tooLargeSize);
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float clampedLargeSize = _cameraSetup.GetCurrentOrthographicSize();

            // Assert
            Assert.AreEqual(0.5f, clampedSmallSize, 0.01f, "Zoom should be clamped to minimum 0.5");
            Assert.AreEqual(50f, clampedLargeSize, 0.01f, "Zoom should be clamped to maximum 50");
        }

        [Test]
        public void TileEditorInputHandler_PollZoom_ReturnsScrollDelta()
        {
            // Arrange
            var mouse = InputSystem.AddDevice<Mouse>();
            
            // Act
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1) });
            InputSystem.Update();

            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.IsTrue(scrollDelta > 0, "Should detect positive scroll delta");
        }

        [Test]
        public void TileEditorInputHandler_PollZoom_ReturnsZeroWhenNoScroll()
        {
            // Arrange
            var mouse = InputSystem.AddDevice<Mouse>();
            
            // Act - No scroll event
            InputSystem.Update();

            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when no scroll input");
        }

        [Test]
        public void TileEditorInputHandler_PollZoom_BlockedWhenPointerOverUI()
        {
            // Arrange
            var mouse = InputSystem.AddDevice<Mouse>();
            var eventSystemGo = new GameObject("EventSystem");
            var eventSystem = eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            
            // Simulate pointer over UI
            varInputModule = eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            
            // Act
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1) });
            InputSystem.Update();

            float scrollDelta = _inputHandler.PollZoom();

            // Assert
            Assert.AreEqual(0f, scrollDelta, "Should return 0 when pointer is over UI");

            // Cleanup
            Object.DestroyImmediate(eventSystemGo);
        }

        [UnityTest]
        public IEnumerator TileEditor_HandleCameraZoom_UpdatesCameraSize()
        {
            // Arrange
            _tileEditor.SetGridBuilder(null); // Skip grid setup for test
            float initialSize = _cameraSetup.GetCurrentOrthographicSize();
            
            // Simulate scroll input
            var mouse = InputSystem.AddDevice<Mouse>();
            
            // Act
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1) });
            InputSystem.Update();

            // Trigger the zoom handling (normally called in Update)
            _tileEditor.Invoke("HandleCameraZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Wait for CameraSetup to process the zoom request
            yield return null;
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Assert
            float newSize = _cameraSetup.GetCurrentOrthographicSize();
            Assert.AreNotEqual(initialSize, newSize, "Camera size should change after zoom");
            Assert.IsTrue(newSize < initialSize, "Scroll up should zoom in (decrease orthographic size)");
        }

        [Test]
        public void TileEditor_HandleCameraZoom_RespectsBounds()
        {
            // Arrange
            _tileEditor.SetGridBuilder(null);
            
            // Simulate multiple large scroll inputs to try to exceed bounds
            var mouse = InputSystem.AddDevice<Mouse>();
            
            float minSize = 0.5f;
            float maxSize = 50f;

            // Act - Try to zoom beyond minimum
            for (int i = 0; i < 100; i++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1) });
                InputSystem.Update();
                _tileEditor.Invoke("HandleCameraZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            
            float finalMinSize = _cameraSetup.GetCurrentOrthographicSize();

            // Reset and try to zoom beyond maximum
            _cameraSetup.SetTileEditorZoom(10f); // Reset to middle value
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            for (int i = 0; i < 100; i++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, -1) });
                InputSystem.Update();
                _tileEditor.Invoke("HandleCameraZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            
            float finalMaxSize = _cameraSetup.GetCurrentOrthographicSize();

            // Assert
            Assert.AreEqual(minSize, finalMinSize, 0.01f, "Zoom should not go below minimum bound");
            Assert.AreEqual(maxSize, finalMaxSize, 0.01f, "Zoom should not go above maximum bound");
        }

        [Test]
        public void CameraSetup_GetCurrentOrthographicSize_ReturnsValidValue()
        {
            // Act
            float size = _cameraSetup.GetCurrentOrthographicSize();

            // Assert
            Assert.IsTrue(size > 0, "Should return positive orthographic size");
        }
    }
}
