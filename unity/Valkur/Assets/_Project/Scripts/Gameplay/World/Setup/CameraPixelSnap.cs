using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Snaps the camera transform to the screen-pixel grid after Cinemachine
    /// writes its tracked position each frame. Eliminates the 1-pixel "seam"
    /// between adjacent sprites that appears when one world unit doesn't map
    /// to an integer number of screen pixels — the typical case in Free
    /// Aspect with no <c>PixelPerfectCamera</c> (which is intentionally
    /// disabled in this project; see <see cref="CameraSetup.SetupPixelPerfectCamera"/>).
    ///
    /// Why max execution order: Cinemachine's brain writes the camera
    /// transform during LateUpdate at default order. We must run AFTER it
    /// so our snapped position is what the renderer actually sees.
    ///
    /// Scope is intentionally limited to <b>position snap only</b>.
    /// Rewriting <c>cam.orthographicSize</c> here would override the user's
    /// zoom each frame (which broke both gameplay zoom UX and in-game
    /// editor zoom UX in past iterations). Game-View-only composite
    /// artifacts ("horizontal/vertical seam lines" visible in the editor
    /// but not in builds) are addressed by integer-pixel rounding in
    /// <see cref="Core.AspectRatioEnforcer"/>, not here.
    /// </summary>
    [DefaultExecutionOrder(int.MaxValue)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraPixelSnap : MonoBehaviour
    {
        private Camera _cam;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            int pxW = _cam.pixelWidth;
            int pxH = _cam.pixelHeight;
            if (pxW <= 0 || pxH <= 0) return;

            // Compute world-units-per-screen-pixel independently per axis.
            // When AspectRatioEnforcer letterboxes/pillarboxes, screen pixels
            // are not perfectly square (cam.aspect is locked to 2.0 but the
            // viewport pixel ratio drifts by a fraction of a percent).
            // Snapping both axes with the same wpp would leave 1-px seams
            // on the axis with the larger pixel size.
            float wppY = (_cam.orthographicSize * 2f) / pxH;
            float wppX = (_cam.orthographicSize * 2f * _cam.aspect) / pxW;
            if (wppX <= 0f || wppY <= 0f) return;
            if (float.IsNaN(wppX) || float.IsNaN(wppY)) return;
            if (float.IsInfinity(wppX) || float.IsInfinity(wppY)) return;

            Vector3 p = transform.position;
            p.x = Mathf.Round(p.x / wppX) * wppX;
            p.y = Mathf.Round(p.y / wppY) * wppY;
            transform.position = p;
        }
    }
}
