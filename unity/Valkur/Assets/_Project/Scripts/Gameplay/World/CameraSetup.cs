using UnityEngine;
using UnityEngine.U2D;
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
        public static CameraSetup Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance = null;
        }

        [SerializeField] private float orthoSize = 5f;
        [SerializeField] private float cameraZOffset = -10f;
        [SerializeField] private float zoomStep = 0.5f;
        [SerializeField] private float minOrthoSize = 3f;
        [SerializeField] private float maxOrthoSize = 14f;
        [SerializeField] private float mapEditorMaxZoomMultiplier = 20f;
        [SerializeField] private float mapEditorZoomStepMultiplier = 4f;

        [Header("Pixel Perfect")]
        [Tooltip("Assets pixels-per-unit for PixelPerfectCamera (should match tile PPU)")]
        [SerializeField] private int assetsPPU = 32;
        [Tooltip("Reference resolution width (logical pixels)")]
        [SerializeField] private int refResolutionX = 640;
        [Tooltip("Reference resolution height (logical pixels)")]
        [SerializeField] private int refResolutionY = 360;

        private CinemachineVirtualCamera _vcam;
        private Transform _savedFollowTarget;
        private bool _detached;

        private void Awake()
        {
            Instance = this;
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

            // Start at maximum zoom distance so the player sees the full area
            _vcam.m_Lens.OrthographicSize = maxOrthoSize;

            // PixelPerfectCamera disabled at runtime: it conflicts with Cinemachine on
            // non‐even resolutions (Free Aspect / Game view). Once we lock to a fixed
            // resolution build, re‐enable via the scene or by uncommenting below.
            // SetupPixelPerfectCamera();

            // Enforce 2:1 aspect ratio (matching Python 1600×800)
            SetupAspectRatioEnforcer();
        }

        private void SetupPixelPerfectCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var ppc = cam.GetComponent<PixelPerfectCamera>();
            if (ppc == null)
                ppc = cam.gameObject.AddComponent<PixelPerfectCamera>();

            ppc.assetsPPU = assetsPPU;
            ppc.refResolutionX = refResolutionX;
            ppc.refResolutionY = refResolutionY;
            ppc.upscaleRT = false;
            ppc.pixelSnapping = true;

            Debug.Log($"[CameraSetup] PixelPerfectCamera configured: PPU={assetsPPU}, ref={refResolutionX}x{refResolutionY}");
        }

        private void SetupAspectRatioEnforcer()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var enforcer = cam.GetComponent<AspectRatioEnforcer>();
            if (enforcer == null)
                cam.gameObject.AddComponent<AspectRatioEnforcer>();
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

        /// <summary>
        /// Detach the virtual camera from its follow target so its transform can be
        /// driven manually (used by runtime editors to free-pan the camera).
        /// </summary>
        public void DetachFollow()
        {
            if (_vcam == null || _detached) return;
            _savedFollowTarget = _vcam.Follow;
            _vcam.Follow = null;
            _detached = true;
        }

        /// <summary>
        /// Re-attach the virtual camera to its previously saved follow target.
        /// </summary>
        public void ReattachFollow()
        {
            if (_vcam == null || !_detached) return;
            _vcam.Follow = _savedFollowTarget;
            _savedFollowTarget = null;
            _detached = false;
        }

        /// <summary>
        /// While detached, returns the vcam transform so callers can move it directly.
        /// Returns null if attached (caller should not pan in that case).
        /// </summary>
        public Transform GetDetachedTransform()
        {
            return _detached && _vcam != null ? _vcam.transform : null;
        }
    }
}
