using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Shared middle-mouse-button camera pan controller used by every runtime
    /// in-game editor (Tile, Buildings, Entities, Items, Inventory, Spells,
    /// Lighting, Particles, FSM).
    ///
    /// Mirrors Python <c>camera_pan.py</c> and the original
    /// <c>TileEditorManager.HandleCameraPan</c> /
    /// <c>BuildingsRuntimeEditor.HandleCameraPan</c> implementations:
    ///   - MMB press   -> detach camera from player follow, save anchor.
    ///   - MMB held    -> offset vcam from anchor by screen-space delta.
    ///   - MMB release -> stop panning; camera stays at the panned position.
    ///   - Editor close-> caller invokes <see cref="Reset"/> and
    ///                    <c>CameraSetup.ReattachFollow()</c> to restore follow.
    ///
    /// Owned (not a MonoBehaviour) by each editor; query input every frame via
    /// <see cref="Tick"/> while the editor is active.
    /// </summary>
    public sealed class EditorCameraPanController
    {
        private Camera  _mainCamera;
        private bool    _isPanning;
        private Vector2 _anchorScreenPos;
        private Vector3 _anchorCamPos;

        public bool IsPanning => _isPanning;

        /// <summary>
        /// Call every frame while the editor is active. Handles MMB press/hold/release
        /// and applies the resulting world delta to the detached vcam transform.
        /// Safe to call when no mouse / camera is present (early-outs).
        /// </summary>
        public void Tick()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            var camSetup = CameraSetup.Instance;
            if (camSetup == null) return;

            if (Valkur.Core.Input.MouseInputManager.WasMiddleMouseButtonPressedThisFrame())
            {
                // Idempotent: DetachFollow returns early if already detached
                // (BuildingsRuntimeEditor detaches in Activate; others rely on
                // first MMB press to detach lazily).
                camSetup.DetachFollow();
                Transform anchorT = camSetup.GetDetachedTransform();
                if (anchorT != null)
                {
                    _isPanning = true;
                    _anchorScreenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
                    _anchorCamPos    = anchorT.position;
                }
            }
            else if (Valkur.Core.Input.MouseInputManager.WasMiddleMouseButtonReleasedThisFrame())
            {
                // Camera stays at the panned position; ReattachFollow() is the
                // editor's responsibility on close (mirrors Tile + Buildings).
                _isPanning = false;
            }

            if (_isPanning && Valkur.Core.Input.MouseInputManager.IsMiddleMouseButtonPressed())
            {
                Transform vcamT = camSetup.GetDetachedTransform();
                if (vcamT == null) return;

                Vector2 currentScreenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
                Vector2 screenDelta      = currentScreenPos - _anchorScreenPos;

                float unitsPerPixel = _mainCamera.orthographicSize * 2f / Screen.height;
                Vector3 worldDelta  = new Vector3(screenDelta.x, screenDelta.y, 0f) * unitsPerPixel;
                Vector3 newPos      = _anchorCamPos - worldDelta;
                newPos.z            = vcamT.position.z;
                vcamT.position      = newPos;
            }
        }

        /// <summary>
        /// Stop tracking input. Does NOT call <c>CameraSetup.ReattachFollow</c> â€”
        /// the editor calls it itself on close so behaviour stays explicit.
        /// </summary>
        public void Reset()
        {
            _isPanning = false;
        }
    }
}
