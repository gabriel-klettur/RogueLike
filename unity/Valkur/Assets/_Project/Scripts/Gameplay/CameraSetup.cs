using UnityEngine;
using Cinemachine;
using Valkur.Core;

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

        public void SetTarget(Transform target)
        {
            if (_vcam != null)
                _vcam.Follow = target;
        }
    }
}
