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
        // ONE FLAG, TWO INDEPENDENT OWNERS — and it used to be one flag with two
        // owners fighting, which is the exact shape this project already documents for
        // Health.SetInvincible. The loading screen blocks gameplay input for the whole
        // boot; ChatInputGate blocks it while a modal panel holds focus. They overlap
        // in real play, because the chat system is CREATED during the boot and its
        // gate's first Refresh() answers "no panel is open" — measured live, that
        // single call cleared the loading screen's block with the world still 40 %
        // assembled, and the player could walk into it.
        //
        // Neither owner may see the other's bit, so each writes its own and the answer
        // is the OR. That is what makes "blocked until the boot finishes" a property
        // rather than a race.
        private static bool _byPanel;
        private static bool _byBoot;

        public static bool IsGameplayBlocked => _byPanel || _byBoot;

        public static event Action<bool> OnBlockChanged;

        /// <summary>The modal-panel owner: chat and the dev console.</summary>
        public static void SetBlocked(bool blocked) => Set(ref _byPanel, blocked);

        /// <summary>
        /// The boot owner: the loading screen holds this from the moment a scene is
        /// activated until the boot sequence reports ready.
        /// </summary>
        public static void SetBootBlocked(bool blocked) => Set(ref _byBoot, blocked);

        private static void Set(ref bool slot, bool value)
        {
            if (slot == value) return;
            bool before = IsGameplayBlocked;
            slot = value;
            bool after = IsGameplayBlocked;
            if (before != after) OnBlockChanged?.Invoke(after);
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
            _byPanel = false;
            _byBoot = false;
            OnBlockChanged = null;
        }
    }
}
