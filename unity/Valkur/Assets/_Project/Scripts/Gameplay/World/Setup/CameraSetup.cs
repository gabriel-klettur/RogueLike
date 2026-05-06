using UnityEngine;
using UnityEngine.U2D;
using Cinemachine;
using UnityEngine.EventSystems;
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
        // Unity-aware singleton accessor. The getter routes through Unity's
        // overloaded == operator so callers that do `CameraSetup.Instance?.X()`
        // see a real C# null when the underlying GameObject has been destroyed
        // (e.g. between EditMode tests where Domain Reload is OFF and the
        // static field would otherwise hold a "fake null" reference). Without
        // this, the null-conditional (?.) bypasses the Unity null check and
        // proceeds to dereference a dangling pointer.
        private static CameraSetup _instance;
        public static CameraSetup Instance
        {
            get => _instance != null ? _instance : null;
            private set => _instance = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _instance = null;
        }

        [SerializeField] private float orthoSize = 5f;
        [SerializeField] private float cameraZOffset = -10f;
        // Multiplicative zoom: each scroll detent multiplies/divides ortho size by
        // (1 ± zoomSpeed). Same feel at every zoom level (small step when close,
        // large step when far). The zoom is clamped to playable bounds — without
        // a clamp the multiplicative model lets a few scroll-out detents inflate
        // ortho size to ~50+, which renders the player / NPCs / buildings as
        // sub-pixel placeholders and looks like assets disappeared.
        [SerializeField, Tooltip("Per-detent multiplicative zoom factor. 0.25 = 25% per click.")]
        private float zoomSpeed = 0.25f;
        [SerializeField, Tooltip("Lowest ortho size the player can reach by zooming in. Below this, sprites alias and the SRP gives up.")]
        private float minZoomOrthoSize = 2f;
        [SerializeField, Tooltip("Highest ortho size the player can reach by zooming out during gameplay. Beyond this, entities become sub-pixel.")]
        private float maxZoomOrthoSize = 25f;
        [SerializeField, Tooltip("Highest ortho size any in-game editor can request. Designers want effectively unbounded zoom-out for layout work, so this is set extremely high; the only purpose of the cap is to reject ortho ∞ / NaN drift that would crash the SRP.")]
        private float maxEditorZoomOrthoSize = 4000f;

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

            // Any active runtime editor drives the camera through SetEditorZoom()
            // (the shared EditorCameraZoomController owned by each editor handles
            // wheel input). While an editor is active the gameplay zoom-clamp
            // would otherwise drag the lens back into the [min, maxGameplay]
            // range every frame and undo the editor's framing, capping zoom-out
            // at gameplay's tighter limit even though the user explicitly asked
            // for editor-wide unbounded zoom-out.
            if (GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive)
                return;

            // Continuous gameplay-zoom clamp. Runs every frame regardless of
            // input. Three reasons we cannot rely on the scroll-handler clamp
            // alone:
            //   1. Hot-reload during Play does NOT call Awake/Start again, so
            //      a lens left at ortho 50+ from before a script recompile
            //      stays at 50 until the player scrolls.
            //   2. The vcam's serialized OrthographicSize in the scene file
            //      may already be out-of-bounds when the scene loads.
            //   3. Other systems (legacy code, future editors) might assign
            //      _vcam.m_Lens.OrthographicSize directly without going
            //      through SetTileEditorZoom — without this safety net the
            //      camera could strand the player as a sub-pixel dot.
            // The clamp only narrows; it never expands a sane lens.
            float liveSize = _vcam.m_Lens.OrthographicSize;
            float liveClamped = Mathf.Clamp(liveSize, minZoomOrthoSize, maxZoomOrthoSize);
            if (!Mathf.Approximately(liveSize, liveClamped))
            {
                _vcam.m_Lens.OrthographicSize = liveClamped;
                ApplyCompatibilityLensSize(liveClamped);
            }

            // Avoid zooming while scrolling focused UI widgets.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float scrollY = Valkur.Core.Input.MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(scrollY) < 0.1f)
                return;

            // Multiplicative zoom: same feel at every distance. Scroll up → zoom in
            // (factor < 1), scroll down → zoom out (factor > 1). Clamped to
            // [minZoomOrthoSize, maxZoomOrthoSize] so a stray scroll-burst can't
            // push ortho size to a value where every entity becomes a sub-pixel
            // dot (which previously looked like assets had disappeared from the
            // scene).
            float currentSize = _vcam.m_Lens.OrthographicSize;
            float zoomFactor = 1f - Mathf.Sign(scrollY) * zoomSpeed;
            float nextSize = Mathf.Clamp(currentSize * zoomFactor,
                                         minZoomOrthoSize, maxZoomOrthoSize);
            _vcam.m_Lens.OrthographicSize = nextSize;
            ApplyCompatibilityLensSize(nextSize);
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
            // Defensive guard for stale references — between EditMode tests with
            // Domain Reload off, callers may hold a "fake null" CameraSetup whose
            // underlying GameObject was destroyed. The Unity == operator detects
            // that, but C#'s ?. does not, so the guard belongs at method entry.
            if (this == null) return;
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
            if (this == null) return;
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
        /// Tells the virtual camera that the follow target was instantly warped
        /// by <paramref name="positionDelta"/> world units, so its internal
        /// damping / pixel-perfect state is updated without a smooth-lerp catch-up.
        /// No-op when the vcam has no follow target. Used by editors that
        /// teleport the player (e.g. "create new blank map" → spawn at origin).
        /// </summary>
        public void SnapToFollowTarget(Vector3 positionDelta)
        {
            if (this == null || _vcam == null) return;
            var followed = _vcam.Follow;
            if (followed != null)
                _vcam.OnTargetObjectWarped(followed, positionDelta);
            if (_compatibilityVcam != null && _compatibilityVcam.Follow != null)
                _compatibilityVcam.OnTargetObjectWarped(_compatibilityVcam.Follow, positionDelta);
        }

        /// <summary>
        /// Request a zoom change from the Tile Editor. This will be applied in the next Update frame.
        /// </summary>
        /// <param name="targetSize">The desired orthographic size</param>
        public void SetTileEditorZoom(float targetSize) => SetEditorZoom(targetSize);

        /// <summary>
        /// Request a zoom change from any in-game runtime editor. Sanitises +
        /// clamps to <c>[minZoomOrthoSize, maxEditorZoomOrthoSize]</c> (NaN/0/+Inf
        /// rejected — Cinemachine stops rendering with malformed lens values),
        /// then applies it on the next Update frame so the gameplay zoom clamp
        /// can't claw it back. The maxEditor cap is set extremely high so for
        /// any practical map this behaves as "unbounded zoom-out".
        /// </summary>
        public void SetEditorZoom(float targetSize)
        {
            EnsureCompatibilityVcam();
            float sanitisedSize = targetSize;
            if (!(sanitisedSize > 0f) || float.IsInfinity(sanitisedSize))
                sanitisedSize = minZoomOrthoSize;
            sanitisedSize = Mathf.Clamp(sanitisedSize, minZoomOrthoSize, maxEditorZoomOrthoSize);
            _tileEditorTargetSize    = sanitisedSize;
            _tileEditorZoomRequested = true;
            if (_vcam != null)
                _vcam.m_Lens.OrthographicSize = sanitisedSize;
            ApplyCompatibilityLensSize(sanitisedSize);
        }

        /// <summary>
        /// Frame the camera on a world-space rectangle (in tiles, Y-up). Detaches
        /// the follow target if attached, re-centres the vcam transform on
        /// <paramref name="rect"/>'s centre, and resizes the lens so the rect
        /// (plus <paramref name="paddingWu"/> world-unit padding) fits inside
        /// the viewport on both axes. Used by editors for "double-click a zone
        /// to centre and frame it".
        /// </summary>
        public void FrameRect(RectInt rect, float paddingWu = 2f)
        {
            if (this == null || _vcam == null) return;
            if (rect.width <= 0 || rect.height <= 0) return;

            DetachFollow();
            var t = GetDetachedTransform();
            if (t == null) return;

            float centerX = rect.x + rect.width  * 0.5f;
            float centerY = rect.y + rect.height * 0.5f;
            t.position = new Vector3(centerX, centerY, t.position.z);

            float aspect = 16f / 9f;
            var cam = Camera.main;
            if (cam != null && cam.aspect > 0f) aspect = cam.aspect;

            float halfW = rect.width  * 0.5f + paddingWu;
            float halfH = rect.height * 0.5f + paddingWu;
            float orthoForHeight = halfH;
            float orthoForWidth  = halfW / aspect;
            float ortho = Mathf.Max(orthoForHeight, orthoForWidth);

            SetEditorZoom(ortho);
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
