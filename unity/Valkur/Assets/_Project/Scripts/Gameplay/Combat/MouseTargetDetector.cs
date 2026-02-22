using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Detects entities under the mouse cursor via Physics2D raycast.
    /// Fires OnTargetChanged when the hovered entity changes.
    /// Works with any entity that has a Collider2D + Health component.
    /// </summary>
    public class MouseTargetDetector : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private LayerMask detectableLayers;
        [SerializeField] private float maxDistance = 50f;
        [SerializeField] private float raycastRadius = 0.2f;

        private GameObject _currentTarget;
        private Camera _mainCamera;

        /// <summary>Fired when the hovered target changes. Null means no target.</summary>
        public event Action<GameObject> OnTargetChanged;

        /// <summary>Current hovered target (may be null).</summary>
        public GameObject CurrentTarget => _currentTarget;

        public void SetDetectableLayers(LayerMask layers)
        {
            detectableLayers = layers;
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 mouseWorld = _mainCamera.ScreenToWorldPoint(mouse.position.ReadValue());
            var hit = Physics2D.OverlapCircle(mouseWorld, raycastRadius, detectableLayers);

            GameObject newTarget = null;
            if (hit != null)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    newTarget = hit.gameObject;
            }

            if (newTarget != _currentTarget)
            {
                _currentTarget = newTarget;
                OnTargetChanged?.Invoke(_currentTarget);
            }
        }
    }
}
