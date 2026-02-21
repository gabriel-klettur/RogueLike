using UnityEngine;
using Cinemachine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Configures the Cinemachine virtual camera to follow the player.
    /// Finds the player by tag at runtime.
    /// </summary>
    [RequireComponent(typeof(CinemachineVirtualCamera))]
    public class CameraSetup : MonoBehaviour
    {
        [SerializeField] private float orthoSize = 5f;

        private CinemachineVirtualCamera _vcam;

        private void Awake()
        {
            _vcam = GetComponent<CinemachineVirtualCamera>();
            _vcam.m_Lens.OrthographicSize = orthoSize;
        }

        private void Start()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
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
