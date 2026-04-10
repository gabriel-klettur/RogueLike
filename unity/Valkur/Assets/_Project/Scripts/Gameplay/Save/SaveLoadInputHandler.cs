using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Handles Ctrl+F5 / Ctrl+F9 quick-save/quick-load input independently of PlayerController.
    /// Bare F5 = Entities Editor, bare F9 = Debug Overlay (matching Python bindings).
    /// Extracted from PlayerController to enforce single responsibility.
    /// Attach to the same GameObject as SaveService or any persistent object.
    /// </summary>
    public class SaveLoadInputHandler : MonoBehaviour
    {
        private InputAction _quickSaveAction;
        private InputAction _quickLoadAction;
        private InputAction _ctrlModifier;

        private void Awake()
        {
            _quickSaveAction = new InputAction("QuickSave", InputActionType.Button, "<Keyboard>/f5");
            _quickLoadAction = new InputAction("QuickLoad", InputActionType.Button, "<Keyboard>/f9");
            _ctrlModifier = new InputAction("CtrlModSave", InputActionType.Button, "<Keyboard>/leftCtrl");
            _quickSaveAction.Enable();
            _quickLoadAction.Enable();
            _ctrlModifier.Enable();
        }

        private void Update()
        {
            if (_quickSaveAction != null && _quickSaveAction.WasPerformedThisFrame() && _ctrlModifier.IsPressed())
            {
                if (SaveService.Instance != null)
                    SaveService.Instance.QuickSave();
            }

            if (_quickLoadAction != null && _quickLoadAction.WasPerformedThisFrame() && _ctrlModifier.IsPressed())
            {
                if (SaveService.Instance != null)
                    SaveService.Instance.QuickLoad();
            }
        }

        private void OnDisable()
        {
            _quickSaveAction?.Disable();
            _quickLoadAction?.Disable();
            _ctrlModifier?.Disable();
        }

        private void OnDestroy()
        {
            _quickSaveAction?.Dispose();
            _quickLoadAction?.Dispose();
            _ctrlModifier?.Dispose();
        }
    }
}
