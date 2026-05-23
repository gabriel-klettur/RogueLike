using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Shared mouse-wheel zoom controller used by every runtime in-game editor.
    /// Mirrors <see cref="EditorCameraPanController"/>: a plain POCO that each
    /// editor owns and ticks every frame while active.
    ///
    /// Behaviour:
    ///   - Scroll up   → zoom in  (smaller ortho size, multiplicative).
    ///   - Scroll down → zoom out (larger  ortho size, multiplicative).
    ///   - Pointer over UI is ignored (matches gameplay zoom).
    ///   - Bypasses the gameplay clamp via <see cref="CameraSetup.SetEditorZoom"/>
    ///     so editors get the much wider editor cap (effectively unbounded
    ///     zoom-out for any practical map).
    /// </summary>
    public sealed class EditorCameraZoomController
    {
        private readonly float _zoomSpeed;

        public EditorCameraZoomController(float zoomSpeed = 0.25f)
        {
            _zoomSpeed = Mathf.Clamp(zoomSpeed, 0.01f, 0.9f);
        }

        public void Tick()
        {
            var camSetup = CameraSetup.Instance;
            if (camSetup == null) return;

            // Avoid zooming while a UI widget owns the pointer (panels, dialogs).
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float scrollY = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(scrollY) < 0.1f) return;

            // Hybrid step: PPU-aligned N-step inside the snap range (one detent
            // = one level — no "stuck on same N" when the multiplicative factor
            // is too small to escape an N=2→N=1 ratio of 2.0), then pure
            // multiplicative above the snap top so the panoramic
            // [N=1 level, maxEditorZoomOrthoSize] range stays smooth.
            float current = camSetup.GetCurrentOrthographicSize();
            int direction = scrollY > 0f ? +1 : -1;
            float next    = camSetup.ComputeEditorZoomNext(current, direction, _zoomSpeed);
            camSetup.SetEditorZoom(next);
        }
    }
}
