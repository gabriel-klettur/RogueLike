using UnityEngine.InputSystem;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        partial void SetupInputActions()
        {
            _pauseAction = new InputAction("Pause",   binding: "<Keyboard>/escape");
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
