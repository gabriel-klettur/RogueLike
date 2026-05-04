using UnityEngine.InputSystem;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        partial void SetupInputActions()
        {
            // ESC is reserved for the General Editor launcher (F-key catalogue).
            // Pause now opens with `P` from gameplay; ESC still navigates back from
            // sub-screens once the menu is open via the separate _cancel action.
            _pauseAction = new InputAction("Pause",   binding: "<Keyboard>/p");
            _navUp       = new InputAction("NavUp",   binding: "<Keyboard>/upArrow");
            _navDown     = new InputAction("NavDown", binding: "<Keyboard>/downArrow");
            _navLeft     = new InputAction("NavLeft",  binding: "<Keyboard>/leftArrow");
            _navRight    = new InputAction("NavRight", binding: "<Keyboard>/rightArrow");
            _confirm     = new InputAction("Confirm",  binding: "<Keyboard>/enter");
            _cancel      = new InputAction("Cancel",   binding: "<Keyboard>/escape");

            _pauseAction.Enable();
            _navUp.Enable();
            _navDown.Enable();
            _navLeft.Enable();
            _navRight.Enable();
            _confirm.Enable();
            _cancel.Enable();
        }
    }
}
