using UnityEngine.InputSystem;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        /// <summary>
        /// Per-direction keyboard navigation actions for the menu list. Owned and
        /// disposed by <see cref="MainMenuUI"/>; the canonical asset's UI map covers
        /// pointer + EventSystem routing through <see cref="Valkur.Core.Input.InputService"/>,
        /// while these handle "previous / next option" semantics that a Vector2
        /// composite cannot express cleanly.
        /// </summary>
        private void SetupInputActions()
        {
            _navUpAction = new InputAction("MenuNavUp", InputActionType.Button);
            _navUpAction.AddBinding("<Keyboard>/upArrow");
            _navUpAction.AddBinding("<Keyboard>/w");
            _navUpAction.Enable();

            _navDownAction = new InputAction("MenuNavDown", InputActionType.Button);
            _navDownAction.AddBinding("<Keyboard>/downArrow");
            _navDownAction.AddBinding("<Keyboard>/s");
            _navDownAction.Enable();

            _navLeftAction = new InputAction("MenuNavLeft", InputActionType.Button);
            _navLeftAction.AddBinding("<Keyboard>/leftArrow");
            _navLeftAction.AddBinding("<Keyboard>/a");
            _navLeftAction.Enable();

            _navRightAction = new InputAction("MenuNavRight", InputActionType.Button);
            _navRightAction.AddBinding("<Keyboard>/rightArrow");
            _navRightAction.AddBinding("<Keyboard>/d");
            _navRightAction.Enable();

            _confirmAction = new InputAction("MenuConfirm", InputActionType.Button);
            _confirmAction.AddBinding("<Keyboard>/enter");
            _confirmAction.AddBinding("<Keyboard>/space");
            _confirmAction.Enable();

            _cancelAction = new InputAction("MenuCancel", InputActionType.Button);
            _cancelAction.AddBinding("<Keyboard>/escape");
            _cancelAction.Enable();
        }
    }
}
