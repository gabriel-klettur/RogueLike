using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Frustum-based entity culling for offscreen NPCs.
    /// Maps to Python's frustum_culling.py (OFFSCREEN_UPDATE_INTERVAL=8, critical states always update).
    /// 
    /// Attach to any entity that should throttle updates when offscreen.
    /// Uses the main camera's viewport to determine visibility.
    /// Offscreen entities update every N frames instead of every frame.
    /// </summary>
    public class EntityCulling : MonoBehaviour
    {
        [SerializeField] private int offscreenUpdateInterval = 8;
        [SerializeField] private float viewportMargin = 0.15f;

        private Camera _mainCamera;
        private int _entityHash;
        private bool _isVisible;
        private bool _forcedActive;

        /// <summary>True if the entity is within the camera viewport (+ margin).</summary>
        public bool IsVisible => _isVisible;

        /// <summary>True if the entity should update this frame (visible or interval hit).</summary>
        public bool ShouldUpdate => _isVisible || _forcedActive || IsIntervalFrame();

        private void Awake()
        {
            _entityHash = GetInstanceID();
        }

        private void Start()
        {
            _mainCamera = Camera.main;
        }

        private void LateUpdate()
        {
            _forcedActive = false;

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                _isVisible = true;
                return;
            }

            Vector3 viewportPos = _mainCamera.WorldToViewportPoint(transform.position);
            _isVisible = viewportPos.x >= -viewportMargin && viewportPos.x <= 1f + viewportMargin
                      && viewportPos.y >= -viewportMargin && viewportPos.y <= 1f + viewportMargin
                      && viewportPos.z > 0f;
        }

        /// <summary>
        /// Force this entity to update next frame regardless of visibility.
        /// Use for critical state changes (damage, death, attack).
        /// </summary>
        public void ForceActiveNextFrame()
        {
            _forcedActive = true;
        }

        private bool IsIntervalFrame()
        {
            if (offscreenUpdateInterval <= 1) return true;
            return (Time.frameCount % offscreenUpdateInterval) == (_entityHash % offscreenUpdateInterval);
        }
    }
}
