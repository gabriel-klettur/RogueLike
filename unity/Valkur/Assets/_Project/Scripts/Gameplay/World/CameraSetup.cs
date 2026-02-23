using UnityEngine;
using Cinemachine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Configures the Cinemachine virtual camera to follow the player.
    /// Finds the player by tag at runtime.
    /// Sets up a Transposer body with Z offset so the camera stays behind the 2D plane.
    /// </summary>
    [RequireComponent(typeof(CinemachineVirtualCamera))]
    public class CameraSetup : MonoBehaviour
    {
        [SerializeField] private float orthoSize = 5f;
        [SerializeField] private float cameraZOffset = -10f;
        [SerializeField] private float zoomStep = 0.5f;
        [SerializeField] private float minOrthoSize = 3f;
        [SerializeField] private float maxOrthoSize = 14f;
        [SerializeField] private float mapEditorMaxZoomMultiplier = 20f;
        [SerializeField] private float mapEditorZoomStepMultiplier = 4f;

        private CinemachineVirtualCamera _vcam;

        private void Awake()
        {
            _vcam = GetComponent<CinemachineVirtualCamera>();
            _vcam.m_Lens.OrthographicSize = orthoSize;

            // Add Transposer body for follow with Z offset
            var transposer = _vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_FollowOffset = new Vector3(0f, 0f, cameraZOffset);
            transposer.m_XDamping = 0f;
            transposer.m_YDamping = 0f;
            transposer.m_ZDamping = 0f;
        }

        private void Start()
        {
            var player = EntityRegistry.Player;
            if (player != null)
            {
                _vcam.Follow = player.transform;
            }
        }

        private void Update()
        {
            if (_vcam == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            // Tile editor reserves wheel scrolling for layer switching.
            if (TileEditorManager.Instance != null && TileEditorManager.Instance.IsActive)
                return;

            // Avoid zooming while scrolling focused UI widgets.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.1f)
                return;

            bool mapEditorActive = MapEditorManager.Instance != null && MapEditorManager.Instance.IsActive;
            float step = zoomStep * (mapEditorActive ? mapEditorZoomStepMultiplier : 1f);
            float maxSize = mapEditorActive
                ? maxOrthoSize * Mathf.Max(1f, mapEditorMaxZoomMultiplier)
                : maxOrthoSize;

            float targetSize = _vcam.m_Lens.OrthographicSize - Mathf.Sign(scrollY) * step;
            _vcam.m_Lens.OrthographicSize = Mathf.Clamp(targetSize, minOrthoSize, maxSize);
        }

        public void SetTarget(Transform target)
        {
            if (_vcam != null)
                _vcam.Follow = target;
        }
    }
}
