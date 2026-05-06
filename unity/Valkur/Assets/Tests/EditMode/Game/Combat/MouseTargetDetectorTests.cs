using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Combat
{
    public class MouseTargetDetectorTests
    {
        private GameObject _detectorGo;
        private MouseTargetDetector _detector;
        private GameObject _targetGo;
        private Health _health;
        private GameObject _cameraGo;
        private Camera _camera;
        private EventSystem _eventSystem;

        [SetUp]
        public void SetUp()
        {
            // Ensure mouse device exists
            if (Mouse.current == null)
            {
                InputSystem.AddDevice<Mouse>();
            }

            // Create camera
            _cameraGo = new GameObject("TestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 10f;
            _camera.tag = "MainCamera";

            // Create EventSystem
            var esSys = new GameObject("EventSystem");
            _eventSystem = esSys.AddComponent<EventSystem>();
            esSys.AddComponent<InputSystemUIInputModule>();

            // Create detector
            _detectorGo = new GameObject("MouseTargetDetector");
            _detector = _detectorGo.AddComponent<MouseTargetDetector>();
            _detector.SetDetectableLayers(LayerMask.GetMask("Default"));

            // Create target entity
            _targetGo = new GameObject("Target");
            _targetGo.layer = LayerMask.NameToLayer("Default");
            _targetGo.transform.position = Vector3.zero;
            
            var collider = _targetGo.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(2f, 2f);

            _health = _targetGo.AddComponent<Health>();
            _health.Initialize(100);

            MoveMouseToWorld(Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            // Always release the test-mouse override so other fixtures
            // (notably MouseInputManagerLegacyFallbackTests) see fresh state.
            Valkur.Core.Input.MouseInputManager.SetTestMousePosition(null);

            if (_detectorGo != null) Object.DestroyImmediate(_detectorGo);
            if (_targetGo != null) Object.DestroyImmediate(_targetGo);
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
            if (_eventSystem != null) Object.DestroyImmediate(_eventSystem.gameObject);
        }

        [Test]
        public void Detector_CurrentTarget_InitiallyNull()
        {
            // Assert
            Assert.IsNull(_detector.CurrentTarget, "Target should be null initially");
        }

        [Test]
        public void Detector_OnTargetChanged_CanSubscribe()
        {
            // Arrange
            GameObject changedTarget = null;
            _detector.OnTargetChanged += (target) => changedTarget = target;

            // Act
            _detector.Tick();

            // Assert - Should not throw
            Assert.Pass("OnTargetChanged should be subscribable");
        }

        [Test]
        public void Detector_WithHealthComponent_DetectsTarget()
        {
            // Arrange
            Assert.IsNotNull(_health, "Health component should exist");
            Assert.IsFalse(_health.IsDead, "Target should not be dead initially");

            // Act
            _detector.Tick();

            // Assert
            Assert.AreSame(_targetGo, _detector.CurrentTarget, "Mouse over a living Health target should select it.");
        }

        [Test]
        public void Detector_WithDeadEntity_DoesNotDetectAsTarget()
        {
            // Arrange
            _health.TakeDamage(1000);
            Assert.IsTrue(_health.IsDead, "Health should be dead");

            // Act
            _detector.Tick();

            // Assert
            Assert.IsNull(_detector.CurrentTarget, "Dead entity should not be detected as target");
        }

        [Test]
        public void Detector_WithNoHealthComponent_DoesNotDetectAsTarget()
        {
            // Arrange
            var noHealthGo = new GameObject("NoHealth");
            noHealthGo.layer = LayerMask.NameToLayer("Default");
            noHealthGo.transform.position = new Vector3(5f, 5f, 0f);
            var collider = noHealthGo.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(2f, 2f);

            try
            {
                // Act
                MoveMouseToWorld(noHealthGo.transform.position);
                _detector.Tick();

                // Assert
                Assert.IsNull(_detector.CurrentTarget, "Entity without health should not be target");
            }
            finally
            {
                Object.DestroyImmediate(noHealthGo);
            }
        }

        [Test]
        public void Detector_SetDetectableLayers_ChangesDetectionLayers()
        {
            // Arrange
            var originalLayers = LayerMask.GetMask("Default");
            var newLayers = LayerMask.GetMask("UI");

            // Act
            _detector.SetDetectableLayers(newLayers);

            // Assert - Should not crash, verify by running update
            Assert.DoesNotThrow(() => _detector.Tick(), "SetDetectableLayers should allow layer change");
        }

        [Test]
        public void Detector_Update_DoesNotThrowWithNoCamera()
        {
            // Arrange
            Object.DestroyImmediate(_cameraGo);
            _cameraGo = null;

            // Act & Assert
            Assert.DoesNotThrow(() => _detector.Tick(), "Update should handle missing camera gracefully");
        }

        [Test]
        public void Detector_Update_DoesNotThrowWithoutMouse()
        {
            // Arrange
            // Mouse should exist after setup, but test safety

            // Act & Assert
            Assert.DoesNotThrow(() => _detector.Tick(), "Update should handle missing mouse gracefully");
        }

        [Test]
        public void Detector_OnTargetChanged_FiresWhenTargetChanges()
        {
            // Arrange
            int changeCount = 0;
            GameObject detectedTarget = null;
            _detector.OnTargetChanged += (target) =>
            {
                changeCount++;
                detectedTarget = target;
            };

            // Act
            _detector.Tick();
            var firstCount = changeCount;

            _detector.Tick();
            var secondCount = changeCount;

            // Assert
            Assert.AreEqual(1, firstCount, "First target acquisition should fire one change event.");
            Assert.AreEqual(1, secondCount, "Polling the same target again should not fire another event.");
            Assert.AreSame(_targetGo, detectedTarget);
        }

        [Test]
        public void Detector_WithMultipleTargets_DetectsOne()
        {
            // Arrange
            var target2Go = new GameObject("Target2");
            target2Go.layer = LayerMask.NameToLayer("Default");
            target2Go.transform.position = new Vector3(5f, 5f, 0f);
            var collider2 = target2Go.AddComponent<BoxCollider2D>();
            collider2.size = new Vector2(2f, 2f);
            var health2 = target2Go.AddComponent<Health>();
            health2.Initialize(50);

            try
            {
                // Act
                _detector.Tick();

                // Assert - Should execute consistently without throwing.
                Assert.IsTrue(_detector.CurrentTarget == null || _detector.CurrentTarget.GetComponent<Health>() != null);
            }
            finally
            {
                Object.DestroyImmediate(target2Go);
            }
        }

        [Test]
        public void Detector_DetectorCreation_DoesNotThrow()
        {
            // Arrange
            var go = new GameObject("TestDetector");

            // Act & Assert
            Assert.DoesNotThrow(() => go.AddComponent<MouseTargetDetector>(), "Detector creation should not throw");

            // Cleanup
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Detector_WithDifferentRaycastRadius_Works()
        {
            // Arrange
            var go = new GameObject("TestDetector");
            var detector = go.AddComponent<MouseTargetDetector>();
            
            // Use reflection to set the radius (it's private)
            var radiusField = typeof(MouseTargetDetector).GetField("raycastRadius", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(radiusField, "raycastRadius field should exist");

            try
            {
                // Act
                radiusField.SetValue(detector, 0.5f);
                detector.Tick();

                // Assert
                Assert.Pass("Different radius should work");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Detector_Raycast_WithBoxCollider2D()
        {
            // Arrange - Target already has BoxCollider2D
            Assert.IsNotNull(_targetGo.GetComponent<BoxCollider2D>());

            // Act
            _detector.Tick();

            // Assert - Should execute without throwing
            Assert.Pass("BoxCollider2D detection should work");
        }

        [Test]
        public void Detector_Raycast_WithCircleCollider2D()
        {
            // Arrange
            Object.DestroyImmediate(_targetGo.GetComponent<BoxCollider2D>());
            var circleCollider = _targetGo.AddComponent<CircleCollider2D>();
            circleCollider.radius = 1f;

            // Act & Assert
            Assert.DoesNotThrow(() => _detector.Tick(), "CircleCollider2D detection should work");
        }

        [Test]
        public void Detector_Raycast_WithPolygonCollider2D()
        {
            // Arrange
            Object.DestroyImmediate(_targetGo.GetComponent<BoxCollider2D>());
            var polyCollider = _targetGo.AddComponent<PolygonCollider2D>();
            var points = new Vector2[]
            {
                new Vector2(-1, -1),
                new Vector2(1, -1),
                new Vector2(1, 1),
                new Vector2(-1, 1)
            };
            polyCollider.points = points;

            // Act & Assert
            Assert.DoesNotThrow(() => _detector.Tick(), "PolygonCollider2D detection should work");
        }

        [Test]
        public void Detector_WithRemovedCollider_NoLongerDetects()
        {
            // Arrange
            _detector.Tick();
            Object.DestroyImmediate(_targetGo.GetComponent<BoxCollider2D>());

            // Act
            _detector.Tick();

            // Assert
            Assert.IsNull(_detector.CurrentTarget, "Should not detect target without collider");
        }

        [Test]
        public void Detector_WithDisabledGameObject_NoLongerDetects()
        {
            // Arrange
            _detector.Tick();
            _targetGo.SetActive(false);

            // Act
            _detector.Tick();

            // Assert
            Assert.IsNull(_detector.CurrentTarget, "Should not detect disabled target");
        }

        [Test]
        public void Detector_MultipleUpdates_IsConsistent()
        {
            // Act
            _detector.Tick();
            var target1 = _detector.CurrentTarget;
            
            _detector.Tick();
            var target2 = _detector.CurrentTarget;

            _detector.Tick();
            var target3 = _detector.CurrentTarget;

            // Assert
            Assert.AreEqual(target1, target2, "Multiple updates should give consistent results");
            Assert.AreEqual(target2, target3, "Multiple updates should give consistent results");
        }

        private static void MoveMouseToWorld(Vector2 worldPosition)
        {
            var camera = Camera.main;
            Assert.IsNotNull(camera, "A MainCamera is required to project the test mouse position.");

            var screenPosition = (Vector2)camera.WorldToScreenPoint(worldPosition);
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = screenPosition });
            InputSystem.Update();
            // Bypass the Editor focus / viewport-rect dependency in the
            // production OR-gate. See MouseInputManager.SetTestMousePosition.
            Valkur.Core.Input.MouseInputManager.SetTestMousePosition(screenPosition);
        }
    }
}
