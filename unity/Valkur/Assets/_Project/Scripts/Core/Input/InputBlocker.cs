using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Central flag consulted by MouseInputManager / KeyboardInputManager /
    /// EditorHotkeyBindings to suppress gameplay-bound input while a modal
    /// panel (chat, dev console) holds focus.
    ///
    /// Disabling InputService.Gameplay.Map alone is not enough: the helpers
    /// in this folder OR-fallback to UnityEngine.Input legacy and to
    /// Mouse.current / Keyboard.current directly to survive the Unity 2022.3
    /// InputSystem event-drop bug. Map.Disable only covers callsites that
    /// route through bound actions; it leaves the dozens of helper-polling
    /// callsites untouched.
    ///
    /// ChatInputGate calls SetBlocked(true) on chat or console open, and
    /// SetBlocked(false) once both are closed.
    ///
    /// IsAlwaysAllowedKey lists the small set of keys that must keep working
    /// even while a panel is up: ~ (DevConsole toggle), Enter (ChatUI submit
    /// / open / close), and Escape (universal cancel).
    /// </summary>
    public static class InputBlocker
    {
        public static bool IsGameplayBlocked { get; private set; }
        public static event Action<bool> OnBlockChanged;

        public static void SetBlocked(bool blocked)
        {
            if (blocked == IsGameplayBlocked) return;
            IsGameplayBlocked = blocked;
            OnBlockChanged?.Invoke(blocked);
        }

        public static bool IsAlwaysAllowedKey(Key key) =>
            key == Key.Escape || key == Key.Backquote ||
            key == Key.Enter || key == Key.NumpadEnter;

        public static bool IsAlwaysAllowedKey(KeyCode key) =>
            key == KeyCode.Escape || key == KeyCode.BackQuote ||
            key == KeyCode.Return || key == KeyCode.KeypadEnter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            IsGameplayBlocked = false;
            OnBlockChanged = null;
        }
    }
}
