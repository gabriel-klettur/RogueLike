using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

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
        private bool _ownsQuickSave;
        private bool _ownsQuickLoad;
        private bool _ownsCtrl;

        private void Awake()
        {
            _quickSaveAction = EditorHotkeyBindings.Resolve(EditorHotkeyBindings.Hotkey.QuickSave,    out _ownsQuickSave);
            _quickLoadAction = EditorHotkeyBindings.Resolve(EditorHotkeyBindings.Hotkey.QuickLoad,    out _ownsQuickLoad);
            _ctrlModifier    = EditorHotkeyBindings.Resolve(EditorHotkeyBindings.Hotkey.CtrlModifier, out _ownsCtrl);
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
            // Only disable shared actions when we created them locally; the
            // InputService keeps its own actions enabled across scene loads.
            if (_ownsQuickSave) _quickSaveAction?.Disable();
            if (_ownsQuickLoad) _quickLoadAction?.Disable();
            if (_ownsCtrl)      _ctrlModifier?.Disable();
        }

        private void OnDestroy()
        {
            if (_ownsQuickSave) _quickSaveAction?.Dispose();
            if (_ownsQuickLoad) _quickLoadAction?.Dispose();
            if (_ownsCtrl)      _ctrlModifier?.Dispose();
        }
    }
}
