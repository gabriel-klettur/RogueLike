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

            float current = camSetup.GetCurrentOrthographicSize();
            float factor  = 1f - Mathf.Sign(scrollY) * _zoomSpeed;
            float next    = current * factor;
            camSetup.SetEditorZoom(next);
        }
    }
}
