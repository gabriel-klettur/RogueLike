using System;
using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Detects entities under the mouse cursor via Physics2D raycast.
    /// Fires OnTargetChanged when the hovered entity changes.
    /// Works with any entity that has a Collider2D + Health component.
    /// Uses MouseInputManager for centralized input handling.
    /// </summary>
    public class MouseTargetDetector : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private LayerMask detectableLayers;
#pragma warning disable CS0414
        [SerializeField] private float maxDistance = 50f;
#pragma warning restore CS0414
        [SerializeField] private float raycastRadius = 0.2f;

        private GameObject _currentTarget;

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
            Tick();
        }

        public void Tick()
        {
            if (!MouseInputManager.TryGetWorldMousePosition(out Vector2 mouseWorld))
            {
                SetCurrentTarget(null);
                return;
            }

            // Check if there's a collider at the mouse position
            var hit = Physics2D.OverlapCircle(mouseWorld, raycastRadius, detectableLayers);

            GameObject newTarget = null;
            if (hit != null)
            {
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    newTarget = hit.gameObject;
            }

            SetCurrentTarget(newTarget);
        }

        private void SetCurrentTarget(GameObject newTarget)
        {
            if (newTarget == _currentTarget)
                return;

            _currentTarget = newTarget;
            OnTargetChanged?.Invoke(_currentTarget);
        }
    }
}
