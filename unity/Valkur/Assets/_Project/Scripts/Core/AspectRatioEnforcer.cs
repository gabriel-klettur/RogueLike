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

            if (scaleHeight < 1.0f)
            {
                // Pillarbox — window is taller than target, add bars top/bottom
                rect.width = 1.0f;
                rect.height = scaleHeight;
                rect.x = 0f;
                rect.y = (1.0f - scaleHeight) / 2.0f;
            }
            else
            {
                // Letterbox — window is wider than target, add bars left/right
                float scaleWidth = 1.0f / scaleHeight;
                rect.width = scaleWidth;
                rect.height = 1.0f;
                rect.x = (1.0f - scaleWidth) / 2.0f;
                rect.y = 0f;
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
