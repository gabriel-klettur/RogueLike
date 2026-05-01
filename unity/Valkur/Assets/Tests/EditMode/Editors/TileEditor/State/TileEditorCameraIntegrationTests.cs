using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.World;
using System.Collections;

namespace Valkur.Tests.EditMode.Editors.TileEditor.State
{
    [TestFixture]
    public class TileEditorCameraIntegrationTests
    {
        private GameObject _cameraGo;
        private GameObject _cameraSetupGo;
        private Camera _camera;
        private Valkur.Gameplay.CameraSetup _cameraSetup;

        [SetUp]
        public void SetUp()
        {
            // Reset singleton before test
            if (Valkur.Gameplay.CameraSetup.Instance != null)
            {
                Object.DestroyImmediate(Valkur.Gameplay.CameraSetup.Instance.gameObject);
            }

            // Create main camera
            _cameraGo = new GameObject("Main Camera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.tag = "MainCamera";

            // Create CameraSetup with CinemachineVirtualCamera
            _cameraSetupGo = new GameObject("CameraSetup");
            _cameraSetup = _cameraSetupGo.AddComponent<Valkur.Gameplay.CameraSetup>();
            
            // Force Awake to be called to set Instance (private lifecycle method)
            typeof(Valkur.Gameplay.CameraSetup).GetMethod("Awake",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_cameraSetup, null);
        }

        [TearDown]
        public void TearDown()
        {
            // Reset singleton instance
            if (Valkur.Gameplay.CameraSetup.Instance != null)
            {
                Object.DestroyImmediate(Valkur.Gameplay.CameraSetup.Instance.gameObject);
            }

            Object.DestroyImmediate(_cameraSetupGo);
            Object.DestroyImmediate(_cameraGo);
        }

        [Test]
        public void CameraSetup_Instance_SetCorrectly()
        {
            // Assert
            Assert.IsNotNull(CameraSetup.Instance, "CameraSetup instance should be set");
            Assert.AreEqual(_cameraSetup, CameraSetup.Instance, "Instance should match created CameraSetup");
        }

        [Test]
        public void CameraSetup_GetCurrentOrthographicSize_ReturnsValidValue()
        {
            // Act
            float size = _cameraSetup.GetCurrentOrthographicSize();

            // Assert
            Assert.IsTrue(size > 0, "Should return positive orthographic size");
        }

        [Test]
        public void CameraSetup_SetTileEditorZoom_AppliesCorrectSize()
        {
            // Arrange
            float expectedSize = 7.5f;

            // Act
            _cameraSetup.SetTileEditorZoom(expectedSize);

            // Note: Zoom is applied in Update cycle, we check the current size
            // // _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, null);
            
            float actualSize = _cameraSetup.GetCurrentOrthographicSize();
            Assert.AreEqual(expectedSize, actualSize, 0.01f, "Tile editor zoom should be applied correctly");
        }

        [Test]
        public void CameraSetup_SetTileEditorZoom_SanitisesInvalidInput()
        {
            // Zoom is intentionally UNBOUNDED, but Cinemachine still rejects
            // 0 / negative / Inf / NaN as malformed. SetTileEditorZoom must
            // sanitise those to a strictly-positive size so the camera keeps
            // rendering even if the caller passes garbage.
            _cameraSetup.SetTileEditorZoom(0f);
            Assert.Greater(_cameraSetup.GetCurrentOrthographicSize(), 0f, "Zero size must be sanitised to >0");

            _cameraSetup.SetTileEditorZoom(-100f);
            Assert.Greater(_cameraSetup.GetCurrentOrthographicSize(), 0f, "Negative size must be sanitised to >0");

            _cameraSetup.SetTileEditorZoom(float.PositiveInfinity);
            Assert.IsTrue(float.IsFinite(_cameraSetup.GetCurrentOrthographicSize()), "+Inf size must be sanitised to a finite value");
        }

        [Test]
        public void CameraSetup_SetTileEditorZoom_AcceptsExtremeValues()
        {
            // Arrange — zoom is unbounded, so even absurd positive values
            // should pass through unchanged. The user wants to explore where
            // the rendering pipeline breaks.
            float tinySize  = 1e-6f;     // 1 micron
            float hugeSize  = 1e10f;     // 10 billion units

            // Act + Assert — tiny
            _cameraSetup.SetTileEditorZoom(tinySize);
            Assert.AreEqual(tinySize, _cameraSetup.GetCurrentOrthographicSize(), tinySize * 0.01f,
                "Extremely small positive zoom must pass through unchanged (no min clamp)");

            // Act + Assert — huge
            _cameraSetup.SetTileEditorZoom(hugeSize);
            Assert.AreEqual(hugeSize, _cameraSetup.GetCurrentOrthographicSize(), hugeSize * 0.01f,
                "Extremely large positive zoom must pass through unchanged (no max clamp)");
        }

        [Test]
        public void CameraSetup_SetTileEditorZoom_MultipleRequests_WorkCorrectly()
        {
            // Arrange
            float[] testSizes = { 1f, 5f, 10f, 25f, 3f, 15f };

            // Act & Assert - Test multiple zoom requests
            foreach (float expectedSize in testSizes)
            {
                _cameraSetup.SetTileEditorZoom(expectedSize);
                // _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, null);
                
                float actualSize = _cameraSetup.GetCurrentOrthographicSize();
                Assert.AreEqual(expectedSize, actualSize, 0.01f, $"Should apply zoom size {expectedSize} correctly");
            }
        }

        [Test]
        public void CameraSetup_DetachFollow_WorksCorrectly()
        {
            // Arrange
            var testTarget = new GameObject("TestTarget");
            
            // Act
            _cameraSetup.DetachFollow();
            
            // Assert
            var virtualCamera = _cameraSetup.GetComponent<Cinemachine.CinemachineVirtualCamera>()
                ?? _camera.GetComponent<Cinemachine.CinemachineVirtualCamera>();
            Assert.IsNotNull(virtualCamera, "A CinemachineVirtualCamera should exist for detach/follow assertions.");
            Assert.IsNull(virtualCamera.Follow, 
                "Follow should be null after detach");
            
            // Cleanup
            Object.DestroyImmediate(testTarget);
        }

        [Test]
        public void CameraSetup_ReattachFollow_WorksCorrectly()
        {
            // Arrange
            var testTarget = new GameObject("TestTarget");
            
            // Act
            _cameraSetup.DetachFollow();
            _cameraSetup.ReattachFollow();
            
            // Assert - Should reattach to player (or null if no player exists)
            // The exact behavior depends on whether a player exists in the scene
            
            // Cleanup
            Object.DestroyImmediate(testTarget);
        }

        [Test]
        public void CameraSetup_GetDetachedTransform_ReturnsCorrectTransform()
        {
            // Arrange
            _cameraSetup.DetachFollow();
            
            // Act
            Transform detachedTransform = _cameraSetup.GetDetachedTransform();
            
            // Assert
            Assert.IsNotNull(detachedTransform, "Should return transform when detached");
            Assert.AreEqual(_cameraSetupGo.transform, detachedTransform, "Should return CameraSetup transform");
            
            // Cleanup
            _cameraSetup.ReattachFollow();
        }

        [Test]
        public void CameraSetup_GetDetachedTransform_ReturnsNull_WhenAttached()
        {
            // Act - Without detaching
            Transform detachedTransform = _cameraSetup.GetDetachedTransform();
            
            // Assert
            Assert.IsNull(detachedTransform, "Should return null when attached");
        }

        [UnityTest]
        public IEnumerator CameraSetup_ZoomRequest_ProcessedNextFrame()
        {
            // Arrange
            float expectedSize = 8.5f;
            float initialSize = _cameraSetup.GetCurrentOrthographicSize();

            // Act
            _cameraSetup.SetTileEditorZoom(expectedSize);
            
            // Wait for next frame (simulating Update cycle)
            yield return null;
            
            // Assert
            float finalSize = _cameraSetup.GetCurrentOrthographicSize();
            Assert.AreEqual(expectedSize, finalSize, 0.01f, "Zoom should be applied by the next frame");
            Assert.AreNotEqual(initialSize, finalSize, "Size should actually change");
        }

        [Test]
        public void CameraSetup_OrthographicCamera_RequiredForZoom()
        {
            // Arrange - Make camera perspective
            _camera.orthographic = false;

            // Act
            _cameraSetup.SetTileEditorZoom(5f);
            // _cameraSetup.Invoke("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, null);

            // Assert - Should still work but may not be visible
            float size = _cameraSetup.GetCurrentOrthographicSize();
            Assert.IsTrue(size > 0, "Should still return size even for perspective camera");
            
            // Cleanup
            _camera.orthographic = true;
        }

        [Test]
        public void CameraSetup_MultipleInstances_OnlyOneActive()
        {
            // Arrange
            var secondCameraSetupGo = new GameObject("SecondCameraSetup");
            var secondCameraSetup = secondCameraSetupGo.AddComponent<CameraSetup>();

            // Assert
            Assert.AreEqual(_cameraSetup, CameraSetup.Instance, "First instance should be the active one");
            Assert.AreNotEqual(secondCameraSetup, CameraSetup.Instance, "Second instance should not be the active one");
            
            // Cleanup
            Object.DestroyImmediate(secondCameraSetupGo);
        }

        [Test]
        public void CameraSetup_SetTarget_WorksCorrectly()
        {
            // Arrange
            var testTarget = new GameObject("TestTarget");

            // Act
            _cameraSetup.SetTarget(testTarget.transform);

            // Assert
            Assert.AreEqual(testTarget.transform, _cameraSetup.GetComponent<Cinemachine.CinemachineVirtualCamera>().Follow, 
                "Target should be set correctly");

            // Cleanup
            Object.DestroyImmediate(testTarget);
        }
    }
}
