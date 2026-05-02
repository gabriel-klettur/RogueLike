using UnityEngine;
using UnityEngine.U2D;
using Cinemachine;
using UnityEngine.EventSystems;
using Valkur.Core;
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
        // Multiplicative zoom: each scroll detent multiplies/divides ortho size by
        // (1 ± zoomSpeed). Same feel at every zoom level (small step when close,
        // large step when far). NO clamp — zoom is intentionally unbounded so we
        // can stress-test where Unity / Cinemachine break under floating-point
        // pressure. Multiplicative model guarantees `size * (1 - 0.25)` stays
        // strictly positive on the way in, so no floor is needed.
        [SerializeField, Tooltip("Per-detent multiplicative zoom factor. 0.25 = 25% per click. Zoom range is unbounded — keep scrolling out to find the breaking point.")]
        private float zoomSpeed = 0.25f;

        [Header("Pixel Perfect")]
        [Tooltip("Assets pixels-per-unit for PixelPerfectCamera (should match tile PPU)")]
        [SerializeField] private int assetsPPU = 32;
        [Tooltip("Reference resolution width (logical pixels)")]
        [SerializeField] private int refResolutionX = 640;
        [Tooltip("Reference resolution height (logical pixels)")]
        [SerializeField] private int refResolutionY = 360;

        private CinemachineVirtualCamera _vcam;
        private CinemachineVirtualCamera _compatibilityVcam;
        private Transform _savedFollowTarget;
        private bool _detached;
        
        // Tile Editor zoom support
        private bool _tileEditorZoomRequested;
        private float _tileEditorTargetSize;

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

#if UNITY_EDITOR
            CacheCompatibilityVcam();
#endif
        }

        private void OnDestroy()
        {
            // Clear the static handle when the underlying GameObject goes
            // away. Without this, an EditMode test that creates a
            // CameraSetup and tears it down leaves Instance pointing at
            // a destroyed-but-not-null component; the next test calling
            // CameraSetup.Instance?.ReattachFollow() walks straight into
            // a MissingReferenceException because Unity's null-overload
            // returns true but the C# reference is alive.
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            var player = EntityRegistry.Player;
            if (player != null)
            {
                _vcam.Follow = player.transform;
            }

            // Start at the configured ortho size (default 5 — comfortable
            // gameplay framing). Players can scroll out to maxOrthoSize.
            _vcam.m_Lens.OrthographicSize = orthoSize;

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

            // Handle Tile Editor zoom requests first
            if (_tileEditorZoomRequested)
            {
                _vcam.m_Lens.OrthographicSize = _tileEditorTargetSize;
                ApplyCompatibilityLensSize(_tileEditorTargetSize);
                _tileEditorZoomRequested = false;
                return;
            }

            // Acquire the follow target lazily: GameplaySceneSetup spawns the player
            // from a long coroutine, so EntityRegistry.Player is usually still null
            // when CameraSetup.Start() runs. Without this, the camera stays at the
            // origin and the player (spawned at ~75,75) renders off-screen.
            if (_vcam.Follow == null && !_detached)
            {
                var player = EntityRegistry.Player;
                if (player != null) _vcam.Follow = player.transform;
            }

            // Auto-recover from a stale "detached" state: if no runtime editor is
            // currently active but the camera is still flagged detached (some path
            // called DetachFollow without a matching ReattachFollow — e.g. an editor
            // closed via an unusual exit path, scene reload, or play-mode hot-reload),
            // restore the follow target so the camera doesn't drift away from the
            // player. Without this, the player can press F10 / F6 / etc., close the
            // editor, and find the world rendered as a blue void because the vcam
            // never got reattached.
            //
            // Only fires when a GameEditorManager exists — i.e. an actual runtime
            // session that owns the editor lifecycle. Without that guard, isolated
            // unit tests that exercise DetachFollow() in a stripped scene would
            // see Update() immediately undo the detach and fail their contract.
            bool noEditorActive = GameEditorManager.HasInstance &&
                                  !GameEditorManager.Instance.AnyEditorActive;
            if (_detached && noEditorActive)
            {
                ReattachFollow();
                if (_vcam.Follow == null)
                {
                    var player = EntityRegistry.Player;
                    if (player != null) _vcam.Follow = player.transform;
                }
            }

            // Editor-compatibility vcam fix: in the Editor we add a duplicate
            // CinemachineVirtualCamera to the Main Camera GameObject as a fallback
            // for play-mode pipelines that don't pick up the dedicated vcam GameObject.
            // Both vcams share Priority = 10 — Cinemachine breaks ties by recently-
            // activated, which non-deterministically picks the Main-Camera vcam (which
            // has Follow == null because it was constructed before the player spawned).
            // The Brain then renders from (0,0,0) and the world appears as a blue void.
            //
            // Two-line fix: keep the compatibility vcam's Follow target in lock-step
            // with the primary, and bump the primary's priority above 10 so it always
            // wins the active-camera election.
            if (_compatibilityVcam != null)
            {
                if (_compatibilityVcam.Follow != _vcam.Follow)
                    _compatibilityVcam.Follow = _vcam.Follow;
                if (_vcam.Priority <= _compatibilityVcam.Priority)
                    _vcam.Priority = _compatibilityVcam.Priority + 1;
            }

            // Tile editor drives the camera through SetTileEditorZoom() (it has
            // its own scroll handler that respects the active brush size).
            if (TileEditorManager.Instance != null && TileEditorManager.Instance.IsActive)
                return;

            // Avoid zooming while scrolling focused UI widgets.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float scrollY = Valkur.Core.Input.MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(scrollY) < 0.1f)
                return;

            // Multiplicative zoom: same feel at every distance. Scroll up → zoom in
            // (factor < 1, ortho asymptotes towards 0 but never reaches it), scroll
            // down → zoom out (factor > 1, ortho grows exponentially towards
            // float.MaxValue ≈ 3.4e38 before the rendering pipeline gives up).
            // No clamp by design — keep scrolling to find the breaking point.
            float currentSize = _vcam.m_Lens.OrthographicSize;
            float zoomFactor = 1f - Mathf.Sign(scrollY) * zoomSpeed;
            _vcam.m_Lens.OrthographicSize = currentSize * zoomFactor;
            ApplyCompatibilityLensSize(_vcam.m_Lens.OrthographicSize);
        }

        public void SetTarget(Transform target)
        {
            EnsureCompatibilityVcam();
            if (_vcam != null)
                _vcam.Follow = target;

            if (_compatibilityVcam != null)
                _compatibilityVcam.Follow = target;
        }

        /// <summary>
        /// Detach the virtual camera from its follow target so its transform can be
        /// driven manually (used by runtime editors to free-pan the camera).
        /// </summary>
        public void DetachFollow()
        {
            EnsureCompatibilityVcam();
            if (_vcam == null || _detached) return;
            _savedFollowTarget = _vcam.Follow;
            _vcam.Follow = null;
            if (_compatibilityVcam != null)
                _compatibilityVcam.Follow = null;
            _detached = true;
        }

        /// <summary>
        /// Re-attach the virtual camera to its previously saved follow target.
        /// </summary>
        public void ReattachFollow()
        {
            EnsureCompatibilityVcam();
            if (_vcam == null || !_detached) return;
            _vcam.Follow = _savedFollowTarget;
            if (_compatibilityVcam != null)
                _compatibilityVcam.Follow = _savedFollowTarget;
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

        /// <summary>
        /// Request a zoom change from the Tile Editor. This will be applied in the next Update frame.
        /// </summary>
        /// <param name="targetSize">The desired orthographic size</param>
        public void SetTileEditorZoom(float targetSize)
        {
            EnsureCompatibilityVcam();
            // No clamp — zoom is unbounded. The only sanitisation is rejecting
            // 0 / negative / NaN / +Inf because Cinemachine treats those as a
            // malformed lens config and stops rendering altogether. Anything
            // strictly positive (even 1e-30 or 1e30) is forwarded as-is so we
            // can stress-test the rendering pipeline.
            float sanitisedSize = targetSize;
            if (!(sanitisedSize > 0f) || float.IsInfinity(sanitisedSize))
                sanitisedSize = float.Epsilon; // smallest positive float — keeps Cinemachine alive
            _tileEditorTargetSize = sanitisedSize;
            _tileEditorZoomRequested = true;
            if (_vcam != null)
                _vcam.m_Lens.OrthographicSize = sanitisedSize;
            ApplyCompatibilityLensSize(sanitisedSize);
        }

        /// <summary>
        /// Get current orthographic size for reference
        /// </summary>
        public float GetCurrentOrthographicSize()
        {
            return _vcam != null ? _vcam.m_Lens.OrthographicSize : 5f;
        }

        private void ApplyCompatibilityLensSize(float size)
        {
            if (_compatibilityVcam != null)
                _compatibilityVcam.m_Lens.OrthographicSize = size;
        }

#if UNITY_EDITOR
        private void CacheCompatibilityVcam()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("MainCamera");
                if (tagged != null)
                    mainCamera = tagged.GetComponent<Camera>();
            }
            if (mainCamera == null)
                mainCamera = Object.FindObjectOfType<Camera>();

            if (mainCamera == null || mainCamera.gameObject == gameObject)
                return;

            _compatibilityVcam = mainCamera.GetComponent<CinemachineVirtualCamera>();
            if (_compatibilityVcam == null)
                _compatibilityVcam = mainCamera.gameObject.AddComponent<CinemachineVirtualCamera>();

            _compatibilityVcam.m_Lens.OrthographicSize = _vcam.m_Lens.OrthographicSize;
            _compatibilityVcam.Follow = _vcam.Follow;
        }

        private void EnsureCompatibilityVcam()
        {
            if (_compatibilityVcam == null)
                CacheCompatibilityVcam();
        }
#endif
    }
}
