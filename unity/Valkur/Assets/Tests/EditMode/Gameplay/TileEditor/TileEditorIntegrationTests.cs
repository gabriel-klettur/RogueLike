using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay;
using System.Collections;

// Integration tests for Tile Editor functionality

namespace Valkur.Tests.EditMode.Gameplay.TileEditor
{
    [TestFixture]
    public class TileEditorIntegrationTests
    {
        private GameObject _cameraGo;
        private GameObject _cameraSetupGo;
        private GameObject _tileEditorGo;
        private Camera _camera;
        private Valkur.Gameplay.CameraSetup _cameraSetup;
        private TileEditorManager _tileEditor;
        private TileEditorState _state;

        [SetUp]
        public void SetUp()
        {
            // Create main camera
            _cameraGo = new GameObject("Main Camera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.tag = "MainCamera";

            // Create CameraSetup
            _cameraSetupGo = new GameObject("CameraSetup");
            _cameraSetup = _cameraSetupGo.AddComponent<Valkur.Gameplay.CameraSetup>();

            // Create TileEditorManager
            _tileEditorGo = new GameObject("TileEditorManager");
            _tileEditor = _tileEditorGo.AddComponent<TileEditorManager>();
            
            // Get state through reflection for testing
            _state = (TileEditorState)typeof(TileEditorManager)
                .GetField("_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(_tileEditor);
        }

        [TearDown]
        public void TearDown()
        {
            // Reset singleton instances
            if (Valkur.Gameplay.CameraSetup.Instance != null)
            {
                Object.DestroyImmediate(Valkur.Gameplay.CameraSetup.Instance.gameObject);
            }

            if (TileEditorManager.HasInstance)
            {
                Object.DestroyImmediate(TileEditorManager.Instance.gameObject);
            }

            Object.DestroyImmediate(_tileEditorGo);
            Object.DestroyImmediate(_cameraSetupGo);
            Object.DestroyImmediate(_cameraGo);
        }

        [Test]
        public void Integration_BrushSizeAndZoom_WorkTogether()
        {
            // Arrange
            float initialZoom = _cameraSetup.GetCurrentOrthographicSize();
            int initialBrushSize = _state.BrushSize;

            // Act - Change brush size
            _state.BrushSize = 10;
            
            // Act - Change zoom
            _cameraSetup.SetTileEditorZoom(8.0f);
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Assert
            Assert.AreEqual(10, _state.BrushSize, "Brush size should be updated");
            Assert.AreEqual(8.0f, _cameraSetup.GetCurrentOrthographicSize(), 0.01f, "Zoom should be updated");
            Assert.AreNotEqual(initialBrushSize, _state.BrushSize, "Brush size should change");
            Assert.AreNotEqual(initialZoom, _cameraSetup.GetCurrentOrthographicSize(), "Zoom should change");
        }

        [Test]
        public void Integration_BrushSizeRange_WorksWithFullRange()
        {
            // Act & Assert - Test full brush size range
            for (int size = TileEditorConstants.MinBrushSize; size <= TileEditorConstants.MaxBrushSize; size++)
            {
                _state.BrushSize = size;
                Assert.AreEqual(size, _state.BrushSize, $"Brush size {size} should be set correctly");
            }
        }

        [Test]
        public void Integration_ZoomRange_WorksWithFullRange()
        {
            // Act & Assert - Test zoom range
            float[] testSizes = { 1f, 5f, 10f, 25f, 40f };
            
            foreach (float expectedSize in testSizes)
            {
                _cameraSetup.SetTileEditorZoom(expectedSize);
                _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                float actualSize = _cameraSetup.GetCurrentOrthographicSize();
                Assert.AreEqual(expectedSize, actualSize, 0.01f, $"Zoom {expectedSize} should be applied correctly");
            }
        }

        [UnityTest]
        public IEnumerator Integration_InputAndState_WorkTogether()
        {
            // Arrange
            var mouse = InputSystem.AddDevice<Mouse>();
            
            // Act - Simulate brush size change through input
            // (In real scenario, this would be done through UI buttons)
            _state.BrushSize = 5;
            
            // Wait a frame for any async operations
            yield return null;
            
            // Act - Simulate zoom through input
            _cameraSetup.SetTileEditorZoom(12.0f);
            yield return null;
            
            // Assert
            Assert.AreEqual(5, _state.BrushSize, "Brush size should be set");
            Assert.AreEqual(12.0f, _cameraSetup.GetCurrentOrthographicSize(), 0.01f, "Zoom should be set");
        }

        [Test]
        public void Integration_BrushStrokeCells_WorksWithLargeBrush()
        {
            // Arrange
            _state.BrushSize = 10; // Large brush
            Vector3Int anchor = new Vector3Int(5, 5, 0);

            // Act - Simulate adding brush stroke cells
            for (int dy = 0; dy < _state.BrushSize; dy++)
            {
                for (int dx = 0; dx < _state.BrushSize; dx++)
                {
                    _state.BrushStrokeCells.Add(new Vector3Int(anchor.x + dx, anchor.y - dy, 0));
                }
            }

            // Assert
            Assert.AreEqual(100, _state.BrushStrokeCells.Count, "10x10 brush should have 100 cells");
            
            // Verify cells are in correct positions
            Assert.IsTrue(_state.BrushStrokeCells.Contains(anchor), "Should contain anchor cell");
            Assert.IsTrue(_state.BrushStrokeCells.Contains(new Vector3Int(14, -4, 0)), "Should contain far corner cell");
        }

        [Test]
        public void Integration_SelectedCellPosition_WorksWithLargeBrush()
        {
            // Arrange
            _state.BrushSize = 15;
            Vector3Int selectedCell = new Vector3Int(10, 20, 0);

            // Act
            _state.SelectedCellPos = selectedCell;

            // Assert
            Assert.AreEqual(selectedCell, _state.SelectedCellPos, "Selected cell position should be set");
        }

        [Test]
        public void Integration_CameraPanAndZoom_WorkTogether()
        {
            // Arrange
            float initialZoom = _cameraSetup.GetCurrentOrthographicSize();
            Vector3 initialPosition = _camera.transform.position;

            // Act - Detach for pan
            _cameraSetup.DetachFollow();
            
            // Act - Change zoom
            _cameraSetup.SetTileEditorZoom(6.0f);
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act - Move camera (simulate pan)
            _camera.transform.position = new Vector3(10, 5, initialPosition.z);

            // Assert
            Assert.AreEqual(6.0f, _cameraSetup.GetCurrentOrthographicSize(), 0.01f, "Zoom should work during pan");
            Assert.AreEqual(new Vector3(10, 5, initialPosition.z), _camera.transform.position, "Camera position should be set");
            
            // Cleanup
            _cameraSetup.ReattachFollow();
        }

        [Test]
        public void Integration_StatePersistence_WorksWithLargeBrush()
        {
            // Arrange
            _state.BrushSize = 20;
            _state.SelectedCellPos = new Vector3Int(15, 25, 0);
            _state.IsDragging = true;
            _state.BrushStrokeCells.Add(new Vector3Int(1, 1, 0));
            _state.BrushStrokeCells.Add(new Vector3Int(2, 2, 0));

            // Act - Create new state to simulate persistence
            var newState = new TileEditorState();
            newState.BrushSize = _state.BrushSize;
            newState.SelectedCellPos = _state.SelectedCellPos;
            newState.IsDragging = _state.IsDragging;

            // Copy brush stroke cells
            foreach (var cell in _state.BrushStrokeCells)
            {
                newState.BrushStrokeCells.Add(cell);
            }

            // Assert
            Assert.AreEqual(_state.BrushSize, newState.BrushSize, "Brush size should persist");
            Assert.AreEqual(_state.SelectedCellPos, newState.SelectedCellPos, "Selected cell should persist");
            Assert.AreEqual(_state.IsDragging, newState.IsDragging, "Dragging state should persist");
            Assert.AreEqual(_state.BrushStrokeCells.Count, newState.BrushStrokeCells.Count, "Brush stroke cells should persist");
        }

        [Test]
        public void Integration_Limits_WorkCorrectly()
        {
            // Arrange
            float minZoom = 0.5f;
            float maxZoom = 50f;
            int minBrush = TileEditorConstants.MinBrushSize;
            int maxBrush = TileEditorConstants.MaxBrushSize;

            // Act - Test zoom limits
            _cameraSetup.SetTileEditorZoom(minZoom - 0.1f); // Below minimum
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float clampedMinZoom = _cameraSetup.GetCurrentOrthographicSize();
            
            _cameraSetup.SetTileEditorZoom(maxZoom + 10f); // Above maximum
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float clampedMaxZoom = _cameraSetup.GetCurrentOrthographicSize();

            // Act - Test brush size limits
            _state.BrushSize = minBrush - 1; // Below minimum (should still work in state)
            int belowMinBrush = _state.BrushSize;
            
            _state.BrushSize = maxBrush + 5; // Above maximum (should still work in state)
            int aboveMaxBrush = _state.BrushSize;

            // Assert
            Assert.AreEqual(minZoom, clampedMinZoom, "Zoom should be clamped to minimum");
            Assert.AreEqual(maxZoom, clampedMaxZoom, "Zoom should be clamped to maximum");
            Assert.AreEqual(minBrush - 1, belowMinBrush, "State allows brush size below minimum");
            Assert.AreEqual(maxBrush + 5, aboveMaxBrush, "State allows brush size above maximum");
        }

        [UnityTest]
        public IEnumerator Integration_Performance_LargeBrushAndZoom()
        {
            // Arrange
            _state.BrushSize = 25; // Maximum brush size
            
            // Act - Test performance with many operations
            float startTime = Time.realtimeSinceStartup;
            
            // Simulate rapid brush size changes
            for (int i = 0; i < 10; i++)
            {
                _state.BrushSize = Random.Range(TileEditorConstants.MinBrushSize, TileEditorConstants.MaxBrushSize);
                yield return null;
            }
            
            // Simulate rapid zoom changes
            for (int i = 0; i < 10; i++)
            {
                _cameraSetup.SetTileEditorZoom(Random.Range(1f, 20f));
                _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                yield return null;
            }
            
            float endTime = Time.realtimeSinceStartup;
            float duration = endTime - startTime;

            // Assert
            Assert.IsTrue(duration < 1.0f, "Performance test should complete quickly");
            Assert.IsTrue(_state.BrushSize >= TileEditorConstants.MinBrushSize, "Brush size should remain valid");
            Assert.IsTrue(_cameraSetup.GetCurrentOrthographicSize() > 0, "Zoom should remain valid");
        }

        [Test]
        public void Integration_ErrorHandling_WorksCorrectly()
        {
            // Arrange
            var originalBrushSize = _state.BrushSize;
            var originalZoom = _cameraSetup.GetCurrentOrthographicSize();

            // Act - Test invalid zoom request
            _cameraSetup.SetTileEditorZoom(-5f); // Negative zoom
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Assert
            Assert.AreEqual(0.5f, _cameraSetup.GetCurrentOrthographicSize(), "Negative zoom should be clamped to minimum");
            
            // Act - Test extreme brush size
            _state.BrushSize = 100; // Very large brush
            
            // Assert
            Assert.AreEqual(100, _state.BrushSize, "State should allow extreme brush size (UI would limit this)");
            
            // Cleanup
            _state.BrushSize = originalBrushSize;
            _cameraSetup.SetTileEditorZoom(originalZoom);
            _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        [Test]
        public void Integration_Constants_Consistency()
        {
            // Assert all constants are consistent across the system
            Assert.AreEqual(1, TileEditorConstants.MinBrushSize, "MinBrushSize should be 1");
            Assert.AreEqual(25, TileEditorConstants.MaxBrushSize, "MaxBrushSize should be 25");
            
            // Assert zoom bounds are reasonable for the brush size range
            float zoomMin = 0.5f;
            float zoomMax = 50f;
            int brushRange = TileEditorConstants.MaxBrushSize - TileEditorConstants.MinBrushSize + 1;
            
            Assert.IsTrue(zoomMin < zoomMax, "Zoom minimum should be less than maximum");
            Assert.IsTrue(brushRange > 0, "Brush range should be positive");
            Assert.IsTrue(brushRange <= zoomMax, "Brush range should be reasonable for zoom range");
        }
    }
}
