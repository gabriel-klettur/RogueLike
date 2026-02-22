using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Handles F5/F9 quick-save/quick-load input independently of PlayerController.
    /// Extracted from PlayerController to enforce single responsibility.
    /// Attach to the same GameObject as SaveService or any persistent object.
    /// </summary>
    public class SaveLoadInputHandler : MonoBehaviour
    {
        private InputAction _quickSaveAction;
        private InputAction _quickLoadAction;

        private void Awake()
        {
            _quickSaveAction = new InputAction("QuickSave", InputActionType.Button, "<Keyboard>/f5");
            _quickLoadAction = new InputAction("QuickLoad", InputActionType.Button, "<Keyboard>/f9");
            _quickSaveAction.Enable();
            _quickLoadAction.Enable();
        }

        private void Update()
        {
            if (_quickSaveAction != null && _quickSaveAction.WasPerformedThisFrame())
            {
                if (SaveService.Instance != null)
                    SaveService.Instance.QuickSave();
            }

            if (_quickLoadAction != null && _quickLoadAction.WasPerformedThisFrame())
            {
                if (SaveService.Instance != null)
                    SaveService.Instance.QuickLoad();
            }
        }

        private void OnDisable()
        {
            _quickSaveAction?.Disable();
            _quickLoadAction?.Disable();
        }

        private void OnDestroy()
        {
            _quickSaveAction?.Dispose();
            _quickLoadAction?.Dispose();
        }
    }
}
