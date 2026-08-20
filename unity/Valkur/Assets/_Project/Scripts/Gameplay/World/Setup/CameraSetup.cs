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

        [Header("Pixel Perfect / Seam Snap")]
        [Tooltip("Pixels-per-unit for the (disabled) PixelPerfectCamera reference. Should match the " +
                 "tile PPU (32).")]
        [SerializeField] private int assetsPPU = 32;
        [Tooltip("Snap PPU for the ortho-size snap. Forces orthographicSize so each block of " +
                 "(assetsPPU/snapPPU) tile texels covers an integer number of screen pixels — that's " +
                 "enough to kill the seam, and a lower snap PPU densifies the zoom-level ladder so " +
                 "the scroll wheel can reach the full [minZoom, maxZoom] range. MUST be a positive " +
                 "divisor of assetsPPU (16 with assetsPPU=32 gives ~12 levels in [2, 25]).")]
        [SerializeField] private int snapPPU = 16;
        [Tooltip("Reference resolution width (logical pixels)")]
        [SerializeField] private int refResolutionX = 640;
        [Tooltip("Reference resolution height (logical pixels)")]
        [SerializeField] private int refResolutionY = 360;

        [Header("Seam Safety")]
        [Tooltip("Force Camera.backgroundColor to opaque black at startup. Tilemap chunk-boundary seams (sub-pixel float drift) reveal whatever lies behind the tile mesh; against a black background a residual seam is invisible, whereas Unity's default cyan-blue background shows up as a clear horizontal line.")]
        [SerializeField] private bool forceSafeBackgroundColor = true;
        [SerializeField] private Color safeBackgroundColor = Color.black;

        private CinemachineVirtualCamera _vcam;
        private CinemachineVirtualCamera _compatibilityVcam;
        private Transform _savedFollowTarget;
        private bool _detached;

        // Tile Editor zoom support
        private bool _tileEditorZoomRequested;
        private float _tileEditorTargetSize;

        // Render camera (the one with CinemachineBrain). CameraSetup lives on the
        // Cinemachine VCAM GameObject, not the brain camera, so we resolve lazily
        // and re-resolve if the reference goes null between EditMode tests.
        private Camera _renderCam;
        // Last seen render-camera pixelHeight; lets the live clamp detect a Game
        // View resize and re-align the PPU snap without writing every frame.
        private int _lastSnapPixelHeight;

        private void Awake()
        {
            Instance = this;
            _vcam = GetComponent<CinemachineVirtualCamera>();
            // SnapOrthoSize is a no-op when the render camera isn't ready yet
            // (Awake fires before any Camera has been Awake'd in some test rigs);
            // the live clamp in Update will re-snap once pixelHeight is valid.
            ApplyOrthoAndCompat(SnapOrthoSize(orthoSize, GetRenderPixelHeight(), snapPPU));

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

#if UNITY_EDITOR
        private void OnValidate()
        {
            // snapPPU must be a positive divisor of assetsPPU. A non-divisor
            // breaks the "1 tile texel = N screen pixels" invariant the snap
            // relies on to kill the seam: if (assetsPPU % snapPPU) != 0, then
            // an integer snap-texel count still maps to a fractional tile-texel
            // count → sub-pixel sampling → seam back. Clamp into a safe value
            // rather than failing silently in the inspector.
            if (snapPPU <= 0) snapPPU = Mathf.Max(1, assetsPPU);
            if (assetsPPU > 0 && (assetsPPU % snapPPU) != 0)
                snapPPU = assetsPPU;
        }
#endif

        private void Start()
        {
            var player = EntityRegistry.Player;
            if (player != null)
            {
                _vcam.Follow = player.transform;
            }

            // Start at the configured ortho size (default 5 — comfortable
            // gameplay framing). Players can scroll out to maxOrthoSize.
            // PPU-snap so the first rendered frame is already seam-free; if
            // the render camera isn't resolved yet the snap is a no-op and
            // the live clamp will re-align on the next Update.
            ApplyOrthoAndCompat(SnapOrthoSize(orthoSize, GetRenderPixelHeight(), snapPPU));

            // PixelPerfectCamera disabled at runtime: it conflicts with Cinemachine on
            // non‐even resolutions (Free Aspect / Game view). Once we lock to a fixed
            // resolution build, re‐enable via the scene or by uncommenting below.
            // SetupPixelPerfectCamera();

            // Enforce 2:1 aspect ratio (matching Python 1600×800)
            SetupAspectRatioEnforcer();

            // Snap camera to the screen-pixel grid each frame to avoid 1-pixel
            // tilemap-chunk seams when 1 world unit doesn't map to an integer
            // number of screen pixels (the default Free-Aspect case).
            SetupCameraPixelSnap();

            // Last line of defence against the "blue line between tiles"
            // visual regression. Tilemap chunk-boundary seams can still
            // appear as a sub-pixel gap on the rasterizer in extreme zoom
            // cases; against a black background that gap is invisible,
            // whereas Unity's default cyan-blue background paints it as a
            // clearly visible horizontal line. Independent fix to the
            // primary FullRect-mesh fix in ValkurAssetPostprocessor.
            ApplySafeBackgroundColor();
        }

        private void ApplySafeBackgroundColor()
        {
            if (!forceSafeBackgroundColor) return;
            ApplySafeBackgroundColorTo(ResolveMainCamera(), safeBackgroundColor);
        }

        // Extracted so EditMode tests can verify the policy in isolation
        // without depending on Camera.main, which is flaky during the test
        // setup phase (the tag system has not always re-indexed the new
        // GameObject by the time Start runs in a synthetic test scene).
        internal static void ApplySafeBackgroundColorTo(Camera cam, Color safeColor)
        {
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = safeColor;
        }

        // Camera.main caches the last-found MainCamera-tagged camera and can
        // return stale or null values right after a synthetic test creates a
        // freshly-tagged Camera. Walk the live camera list once as a fallback
        // so the safety net doesn't quietly no-op in tests or in scenes that
        // re-tag their main camera at runtime.
        private static Camera ResolveMainCamera()
        {
            var cam = Camera.main;
            if (cam != null) return cam;
            foreach (var c in Camera.allCameras)
            {
                if (c == null) continue;
                if (!c.gameObject.activeInHierarchy) continue;
                if (c.CompareTag("MainCamera")) return c;
            }
            return null;
        }

        private void SetupCameraPixelSnap()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (cam.GetComponent<CameraPixelSnap>() == null)
                cam.gameObject.AddComponent<CameraPixelSnap>();
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

            // Handle Tile Editor zoom requests first.
            // _tileEditorTargetSize was already PPU-snapped by SetEditorZoom, so
            // we re-apply through the central helper without re-snapping (avoids
            // drift if the render-cam pxH happens to be 0 this frame).
            if (_tileEditorZoomRequested)
            {
                ApplyOrthoAndCompat(_tileEditorTargetSize);
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
            //
            // After clamping, this is also the single point where we resnap the
            // PPU alignment on a Game View resize. Doing it here (instead of in
            // CameraPixelSnap) keeps the architectural boundary the test suite
            // enforces — see CameraPixelSnapTests.LateUpdate_DoesNotModifyOrthographicSize.
            int pxH = GetRenderPixelHeight();
            float liveSize = _vcam.m_Lens.OrthographicSize;
            float liveClamped = Mathf.Clamp(liveSize, minZoomOrthoSize, maxZoomOrthoSize);
            float liveSnapped = SnapOrthoSize(liveClamped, pxH, snapPPU);

            bool clampChanged   = !Mathf.Approximately(liveSize, liveClamped);
            bool resizeDetected = pxH > 0 && pxH != _lastSnapPixelHeight;
            bool snapDrifted    = pxH > 0 && !Mathf.Approximately(liveSnapped, liveSize);

            if (clampChanged || (resizeDetected && snapDrifted))
                ApplyOrthoAndCompat(liveSnapped);

            if (pxH > 0)
                _lastSnapPixelHeight = pxH;

            // Avoid zooming while scrolling focused UI widgets.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float scrollY = Valkur.Core.Input.MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(scrollY) < 0.1f)
                return;

            int scrollPxH = GetRenderPixelHeight();
            float currentSize = _vcam.m_Lens.OrthographicSize;
            float nextSize;

            if (scrollPxH > 0 && snapPPU > 0)
            {
                // PPU-aligned step. Scroll up = zoom in = larger N (more snap-texels
                // per screen pixel → smaller ortho). Stepping N directly (instead of
                // multiplying ortho and snapping after) guarantees each scroll detent
                // advances exactly one zoom level: a multiplicative step smaller than
                // the gap between adjacent PPU levels would round-trip back to the
                // same N and feel "stuck".
                int direction = scrollY > 0f ? +1 : -1;
                nextSize = ComputePpuStep(currentSize, direction, scrollPxH, snapPPU,
                                          minZoomOrthoSize, maxZoomOrthoSize);
            }
            else
            {
                // Fallback to legacy multiplicative zoom when pixelHeight isn't
                // available (early-frame, headless test rig). The live clamp's
                // resize-resnap will catch up once the render camera is ready.
                float zoomFactor = 1f - Mathf.Sign(scrollY) * zoomSpeed;
                nextSize = Mathf.Clamp(currentSize * zoomFactor,
                                       minZoomOrthoSize, maxZoomOrthoSize);
            }

            ApplyOrthoAndCompat(nextSize);
        }

        /// <summary>
        /// The transform the virtual camera is currently following. Additive read-only
        /// accessor: the camera feel director re-asserts its proxy every frame, because
        /// <see cref="SetTarget"/> writes Follow without updating the saved target, so an
        /// editor closing afterwards restores the player and silently kills the feel layer.
        /// </summary>
        public Transform GetFollowTarget() => _vcam != null ? _vcam.Follow : null;

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
            // PPU-snap so editor framing lands seam-free immediately. No-op when
            // the render camera isn't resolvable (test rigs); the live clamp will
            // re-snap on the next valid Update.
            sanitisedSize = SnapOrthoSize(sanitisedSize, GetRenderPixelHeight(), snapPPU);
            _tileEditorTargetSize    = sanitisedSize;
            _tileEditorZoomRequested = true;
            ApplyOrthoAndCompat(sanitisedSize);
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

        /// <summary>
        /// Snap an orthographic size so one tile texel maps to an integer number
        /// of screen pixels along the vertical axis. Eliminates the sub-pixel
        /// "seam line" that appears in the Game View when
        /// <c>(orthoSize × 2 × ppu) / pixelHeight ∉ ℤ</c>.
        ///
        /// Math: a tile texel covers <c>pixelHeight / (2 × orthoSize × ppu)</c>
        /// screen pixels. Force that to an integer <c>N ≥ 1</c> and reverse-solve:
        /// <c>orthoSize(N) = pixelHeight / (2 × ppu × N)</c>. The N closest to the
        /// requested ortho-size wins. Degenerate inputs (non-positive PPU/height/
        /// requested, NaN, +Inf) pass through unchanged so the snap silently
        /// no-ops for early-frame callers instead of forcing a guard at every
        /// write-site.
        ///
        /// Internal-static so the EditMode test suite can exercise the math
        /// without instantiating a CameraSetup (which requires a vcam + scene).
        /// </summary>
        internal static float SnapOrthoSize(float requested, int pixelHeight, int ppu)
        {
            if (pixelHeight <= 0 || ppu <= 0) return requested;
            if (!(requested > 0f) || float.IsInfinity(requested) || float.IsNaN(requested))
                return requested;

            float denom = 2f * ppu;
            float nCont = pixelHeight / (denom * requested);

            // Beyond the snap's top level: requested > pixelHeight/(2×ppu) means
            // each tile texel projects to less than one screen pixel, so the
            // "integer texels per screen pixel" invariant has no meaningful
            // value above N=1. Pass through unchanged so the in-game editors
            // (F6/F10/F11/Ctrl+F3) can still zoom out to maxEditorZoomOrthoSize
            // for layout-style panoramic views — at that ortho the seam isn't
            // visible anyway (the entire scene aliases together).
            if (nCont < 1f) return requested;

            int nFloor  = Mathf.Max(1, Mathf.FloorToInt(nCont));
            int nCeil   = nFloor + 1;
            float orthoHi = pixelHeight / (denom * nFloor); // smaller N → larger ortho
            float orthoLo = pixelHeight / (denom * nCeil);  // larger  N → smaller ortho
            return Mathf.Abs(orthoLo - requested) < Mathf.Abs(orthoHi - requested)
                ? orthoLo
                : orthoHi;
        }

        /// <summary>
        /// One scroll detent of the PPU-aligned zoom. Reverse-solves the current
        /// ortho size to its integer texel-per-pixel level <c>N</c>, advances N by
        /// <paramref name="direction"/>, then computes the matching ortho size and
        /// clamps to the allowed range. Stepping N rather than ortho avoids the
        /// "stuck zoom" case where a small multiplicative step would round back to
        /// the same N. Internal-static so EditMode tests can exercise it directly
        /// without driving the full <c>Update</c> loop.
        /// </summary>
        internal static float ComputePpuStep(float currentSize, int direction,
                                             int pixelHeight, int ppu,
                                             float minOrtho, float maxOrtho)
        {
            if (pixelHeight <= 0 || ppu <= 0 || !(currentSize > 0f))
                return Mathf.Clamp(currentSize, minOrtho, maxOrtho);

            float denom = 2f * ppu;
            int currentN = Mathf.Max(1, Mathf.RoundToInt(pixelHeight / (denom * currentSize)));
            int nextN    = direction > 0 ? currentN + 1 : Mathf.Max(1, currentN - 1);
            return Mathf.Clamp(pixelHeight / (denom * nextN), minOrtho, maxOrtho);
        }

        /// <summary>
        /// One scroll detent of editor-style zoom. Hybrid policy:
        ///   * Inside the snap range (current ≤ N=1 level): N-step like gameplay
        ///     scroll, so every detent advances exactly one PPU-aligned level.
        ///   * Above the snap range: pure multiplicative, so the user can
        ///     traverse the panoramic [N=1 level, maxEditorZoomOrthoSize] range
        ///     smoothly. <c>SetEditorZoom</c> bypasses the snap in that range
        ///     (<see cref="SnapOrthoSize"/> returns the raw value when
        ///     <c>nCont &lt; 1</c>), so the multiplicative result writes through
        ///     unchanged.
        ///
        /// Without the hybrid, multiplicative-only at factor 0.25 cannot cross
        /// the N=2 → N=1 boundary (their ratio is 2.0, factor is 1.25) and the
        /// editor scroll gets stuck at <c>pxH/(2×snapPPU×2)</c>. This was the
        /// regression the user reported on 2026-05-23.
        /// </summary>
        public float ComputeEditorZoomNext(float current, int direction, float multiplicativeFactor)
        {
            int pxH = GetRenderPixelHeight();

            if (pxH > 0 && snapPPU > 0)
            {
                float topSnapLevel = pxH / (2f * snapPPU);
                // Use N-step in two cases:
                //   (a) strictly inside the snap range — one detent = one level
                //   (b) sitting at the top level and zooming IN — go back into
                //       the range smoothly
                // Otherwise (at top zooming OUT, or anywhere above top): fall
                // through to multiplicative so the user can traverse the
                // panoramic [top, maxEditor] range without being clamped at N=1.
                bool insideSnapRange = current < topSnapLevel - 1e-3f;
                bool atTopGoingIn    = current <= topSnapLevel + 1e-3f && direction > 0;
                if (insideSnapRange || atTopGoingIn)
                {
                    return ComputePpuStep(current, direction, pxH, snapPPU,
                                          minZoomOrthoSize, maxEditorZoomOrthoSize);
                }
            }

            // Multiplicative path. direction +1 = zoom in (smaller ortho,
            // factor < 1); direction -1 = zoom out (factor > 1).
            float factor = 1f - direction * multiplicativeFactor;
            return Mathf.Clamp(current * factor, minZoomOrthoSize, maxEditorZoomOrthoSize);
        }

        /// <summary>
        /// Resolve the render camera and return its current <c>pixelHeight</c>.
        /// Returns 0 when no eligible camera exists (Awake-time, headless test
        /// rigs, EditMode tests that build a synthetic vcam); the snap functions
        /// treat 0 as "skip, no-op".
        ///
        /// Gated on <see cref="Application.isPlaying"/>: the snap is a runtime
        /// concern (it fights sub-pixel seams in the Game View while playing),
        /// and EditMode tests that synthesize CameraSetup instances assert raw
        /// post-clamp ortho values — see <c>CameraZoomClampTests</c>. Without
        /// this gate those tests get their ortho silently mutated by the snap,
        /// especially because Domain Reload is OFF so a <c>CameraPixelSnap</c>
        /// added during a previous Play session lingers on Camera.main into
        /// EditMode. The integration of "SetEditorZoom invokes SnapOrthoSize"
        /// is guarded at the source level in CameraOrthoSnapTests instead.
        /// </summary>
        private int GetRenderPixelHeight()
        {
            if (!Application.isPlaying && _renderCam == null) return 0;
            if (_renderCam == null)
                _renderCam = ResolveMainCamera();
            if (_renderCam == null) return 0;
            int pxH = _renderCam.pixelHeight;
            return pxH > 0 ? pxH : 0;
        }

        /// <summary>
        /// Write an orthographic size to both the primary vcam and the Editor
        /// compatibility vcam through a single channel. Every ortho write in
        /// this class goes through here so a future contributor cannot forget
        /// the compat-vcam mirror.
        /// </summary>
        private void ApplyOrthoAndCompat(float size)
        {
            if (_vcam != null)
                _vcam.m_Lens.OrthographicSize = size;
            ApplyCompatibilityLensSize(size);
        }

        /// <summary>
        /// The compatibility vcam exists only to keep older Editor scenes rendering when the
        /// primary vcam loses the priority election. Its body is Editor-only, but the method
        /// itself must NOT be: <see cref="SetTarget"/>, <see cref="DetachFollow"/>,
        /// <see cref="ReattachFollow"/> and <see cref="SetEditorZoom"/> all call
        /// <see cref="EnsureCompatibilityVcam"/> unguarded, so wrapping the declaration in
        /// <c>#if UNITY_EDITOR</c> made those four call sites reference a method that does
        /// not exist in a player build — CS0103, and the game does not compile outside the
        /// Editor. The guard belongs around the body.
        /// </summary>
        private void CacheCompatibilityVcam()
        {
#if UNITY_EDITOR
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
#endif
        }

        private void EnsureCompatibilityVcam()
        {
#if UNITY_EDITOR
            if (_compatibilityVcam == null)
                CacheCompatibilityVcam();
#endif
        }
    }
}
