using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// InputAction wrapper for map editor hotkeys and click intent.
    /// Keeps input concerns isolated from map editor orchestration.
    ///
    /// All polling — except the F11 toggle, which goes through
    /// <see cref="EditorHotkeyBindings"/> — routes through the centralized
    /// <see cref="MouseInputManager"/> / <see cref="KeyboardInputManager"/>
    /// facades. Those OR the new InputSystem with the legacy
    /// <see cref="UnityEngine.Input"/> backend, so the editor keeps responding
    /// when the new InputSystem package drops OS events (recurring Unity
    /// 2022.3 Editor bug). An earlier version created ad-hoc InputActions
    /// bound to <c>&lt;Mouse&gt;/leftButton</c> / <c>&lt;Keyboard&gt;/n</c> etc.
    /// directly, which silently died under that bug — making zone-select
    /// and the keyboard shortcuts unresponsive.
    /// </summary>
    public class MapEditorInputHandler : IDisposable
    {
        // Kept for the F11 toggle binding (resolved via the canonical
        // InputService asset). Tests reflect on this field by name.
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

        public void CreateActions()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleMap, out _ownsToggleAction);
        }

        public bool WasTogglePressed()
        {
            return EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleMap);
        }

        public bool WasSelectPressed()
            => MouseInputManager.WasLeftMouseButtonPressedThisFrame();

        public bool WasCreatePressed()
            => KeyboardInputManager.WasKeyPressedThisFrame(Key.N, KeyCode.N);

        public bool WasDuplicatePressed()
            => KeyboardInputManager.WasKeyPressedThisFrame(Key.D, KeyCode.D);

        public bool WasDeletePressed()
            => KeyboardInputManager.WasDeletePressedThisFrame();

        public bool WasRenamePressed()
            => KeyboardInputManager.WasKeyPressedThisFrame(Key.R, KeyCode.R);

        public bool WasToggleEditablePressed()
            => KeyboardInputManager.WasEPressedThisFrame();

        /// <summary>
        /// Ctrl+Z (no Shift) — Undo. Mirrors the Tile Editor / Buildings Editor
        /// hotkey contract so users with muscle memory across editors don't
        /// have to relearn anything.
        /// </summary>
        public bool WasUndoPressed()
            => KeyboardInputManager.IsCtrlHeld()
               && !KeyboardInputManager.IsShiftHeld()
               && KeyboardInputManager.WasKeyPressedThisFrame(Key.Z, KeyCode.Z);

        /// <summary>
        /// Ctrl+Y or Ctrl+Shift+Z — Redo. Both bindings are accepted because
        /// macOS muscle memory tends toward Cmd+Shift+Z, while Windows uses
        /// Ctrl+Y; the OR keeps both cohorts happy.
        /// </summary>
        public bool WasRedoPressed()
        {
            if (!KeyboardInputManager.IsCtrlHeld()) return false;
            if (KeyboardInputManager.WasKeyPressedThisFrame(Key.Y, KeyCode.Y)) return true;
            return KeyboardInputManager.IsShiftHeld()
                && KeyboardInputManager.WasKeyPressedThisFrame(Key.Z, KeyCode.Z);
        }

        public bool IsPointerOverUI()
        {
            return UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        public void Dispose()
        {
            if (_ownsToggleAction) DisposeAction(ref _toggleAction); else _toggleAction = null;
        }

        private static void DisposeAction(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Dispose();
            action = null;
        }
    }
}
