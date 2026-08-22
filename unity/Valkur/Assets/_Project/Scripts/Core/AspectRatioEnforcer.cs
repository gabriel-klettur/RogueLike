using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Enforces a fixed aspect ratio (default 2:1, matching Python's 1600×800).
    /// Adds letterbox/pillarbox bars when the window doesn't match the target ratio.
    /// Attach to the Main Camera.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AspectRatioEnforcer : MonoBehaviour
    {
        [SerializeField, Tooltip("Target aspect ratio width component.")]
        private float targetAspectWidth = 2f;

        [SerializeField, Tooltip("Target aspect ratio height component.")]
        private float targetAspectHeight = 1f;

        [SerializeField, Tooltip("Color for letterbox/pillarbox bars.")]
        private Color barColor = Color.black;

        private Camera _cam;
        private Camera _barCam;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        // Target aspect reduced to an exact integer ratio (2:1 by default).
        // See CacheIntegerRatio for why the float form isn't enough.
        private int _ratioW = 2;
        private int _ratioH = 1;

        private float TargetAspect => targetAspectWidth / targetAspectHeight;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            CacheIntegerRatio();
            SetupBarCamera();
            UpdateViewport();
        }

        private void Update()
        {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                UpdateViewport();
            }
        }

        private void SetupBarCamera()
        {
            // Background camera that renders the letterbox/pillarbox bars
            var barGo = new GameObject("LetterboxCamera");
            barGo.transform.SetParent(transform);
            _barCam = barGo.AddComponent<Camera>();
            _barCam.depth = _cam.depth - 1;
            _barCam.cullingMask = 0;
            _barCam.clearFlags = CameraClearFlags.SolidColor;
            _barCam.backgroundColor = barColor;
            _barCam.orthographic = true;
        }

        /// <summary>
        /// Reduce the authored float aspect to an exact integer ratio p:q.
        ///
        /// The viewport is then quantised to (k·p) × (k·q), which is the only
        /// construction where <c>pixelWidth / pixelHeight</c> comes out
        /// bit-exactly equal to the target. Rounding the two axes
        /// independently — the pre-2026-08-22 form — drifts: a 1366×768 window
        /// produced a 1366×682 viewport whose aspect is 2.002933. That fraction
        /// of a percent breaks the HORIZONTAL half of
        /// <c>CameraSetup.SnapOrthoSize</c>'s whole-pixel-per-texel guarantee
        /// (the snap solves ortho size from pixelHeight only; X inherits it
        /// solely through <c>Camera.aspect</c>), so tile quad edges land
        /// mid-pixel and the black camera background shows through as vertical
        /// seam lines across the tilemap.
        /// </summary>
        private void CacheIntegerRatio()
        {
            ReduceRatio(targetAspectWidth, targetAspectHeight, out _ratioW, out _ratioH);
        }

        /// <summary>
        /// Exact integer form of a float aspect. Internal-static so the EditMode
        /// suite can exercise the math without a Camera — same pattern as
        /// <c>CameraSetup.SnapOrthoSize</c>.
        /// </summary>
        internal static void ReduceRatio(float aspectW, float aspectH, out int p, out int q)
        {
            p = Mathf.Max(1, Mathf.RoundToInt(aspectW * 1000f));
            q = Mathf.Max(1, Mathf.RoundToInt(aspectH * 1000f));
            int g = Gcd(p, q);
            p /= g;
            q /= g;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0) { int t = a % b; a = b; b = t; }
            return a < 1 ? 1 : a;
        }

        /// <summary>
        /// Largest exact-ratio box, measured in WHOLE pixels, that fits a window
        /// of <paramref name="screenW"/> × <paramref name="screenH"/>, centred.
        ///
        /// Both dimensions come from one integer scalar k, so the ratio is exact
        /// and the pixel rect is integer on every axis — the two properties the
        /// seam depends on. (Integer pixel rect alone was the 2026-05-16 fix for
        /// the horizontal composite line; exact ratio is the missing half that
        /// killed the vertical ones.)
        ///
        /// Waste is at most (p-1) px of width and (q-1) px of height — one pixel
        /// at the default 2:1. The leftover becomes letterbox / pillarbox bars,
        /// which the bar camera paints black.
        ///
        /// PURE FUNCTION ON PURPOSE. The 2026-08-22 aspect-drift bug survived a
        /// test suite because <c>UpdateViewport</c> read <c>Screen.*</c> directly,
        /// so EditMode could only ever assert against whatever size the Game View
        /// happened to be at. Everything that decides the viewport now lives here,
        /// where the suite can sweep every resolution Valkur ships on.
        /// </summary>
        internal static RectInt ComputeViewport(int screenW, int screenH, int ratioW, int ratioH)
        {
            int sw = Mathf.Max(1, screenW);
            int sh = Mathf.Max(1, screenH);
            int p  = Mathf.Max(1, ratioW);
            int q  = Mathf.Max(1, ratioH);

            int k = Mathf.Max(1, Mathf.Min(sw / p, sh / q));
            int innerW = k * p;
            int innerH = k * q;

            // NO Min(sw, ...) clamp here. For any window at least one ratio
            // unit wide and tall, k*p <= sw and k*q <= sh already hold, so the
            // clamp would be dead code — except on a window SMALLER than p x q,
            // where it silently returned an off-ratio box (a 1x1 window gave a
            // 1x1 viewport, aspect 1:1). That is the exact class of "integer but
            // wrong ratio" bug this method exists to prevent, so the ratio is
            // kept unconditionally and the degenerate window gets the minimum
            // exact box instead. Nothing renders meaningfully at that size.
            int x = Mathf.Max(0, (sw - innerW) / 2);
            int y = Mathf.Max(0, (sh - innerH) / 2);

            return new RectInt(x, y, innerW, innerH);
        }

        private void UpdateViewport()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            int sw = Mathf.Max(1, Screen.width);
            int sh = Mathf.Max(1, Screen.height);
            var box = ComputeViewport(sw, sh, _ratioW, _ratioH);

            _cam.rect = new Rect(
                (float)box.x / sw, (float)box.y / sh,
                (float)box.width / sw, (float)box.height / sh);
        }

        private void OnDestroy()
        {
            if (_barCam != null)
            {
                Destroy(_barCam.gameObject);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (targetAspectWidth <= 0f) targetAspectWidth = 1f;
            if (targetAspectHeight <= 0f) targetAspectHeight = 1f;
            CacheIntegerRatio();
        }
#endif
    }
}
