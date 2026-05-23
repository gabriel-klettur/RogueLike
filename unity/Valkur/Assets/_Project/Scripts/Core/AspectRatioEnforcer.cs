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

        private float TargetAspect => targetAspectWidth / targetAspectHeight;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
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

        private void UpdateViewport()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            float windowAspect = (float)Screen.width / Screen.height;
            float scaleHeight = windowAspect / TargetAspect;

            var rect = new Rect();

            // Critical: round the viewport rect so the resulting pixelRect
            // has INTEGER dimensions. With the naive fractional rect (e.g.
            // 0.972), Unity ends up with pixelRect.height = 819.5 — a half-
            // pixel that Game View composites with a sub-pixel offset,
            // producing visible horizontal seam lines across the tilemap
            // ("the blue/black lines" reported on 2026-05-16). Anchoring the
            // rect to integer pixels eliminates the composite drift; the
            // letterbox/pillarbox bars stay perfectly black either way.

            if (scaleHeight < 1.0f)
            {
                // Pillarbox — window is taller than target. Round the inner
                // height to an integer pixel count, recompute the rect from
                // the rounded value. Forcing even avoids the rare half-row
                // off-by-one at odd screen heights.
                int innerPxH = Mathf.RoundToInt(scaleHeight * Screen.height);
                if ((innerPxH & 1) == 1) innerPxH--;
                if (innerPxH < 2) innerPxH = 2;
                int innerY = (Screen.height - innerPxH) / 2;
                rect.width  = 1f;
                rect.height = (float)innerPxH / Screen.height;
                rect.x      = 0f;
                rect.y      = (float)innerY / Screen.height;
            }
            else
            {
                // Letterbox — window is wider than target. Round the inner
                // width to an integer pixel count.
                float scaleWidth = 1.0f / scaleHeight;
                int innerPxW = Mathf.RoundToInt(scaleWidth * Screen.width);
                if ((innerPxW & 1) == 1) innerPxW--;
                if (innerPxW < 2) innerPxW = 2;
                int innerX = (Screen.width - innerPxW) / 2;
                rect.width  = (float)innerPxW / Screen.width;
                rect.height = 1f;
                rect.x      = (float)innerX / Screen.width;
                rect.y      = 0f;
            }

            _cam.rect = rect;
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
        }
#endif
    }
}
