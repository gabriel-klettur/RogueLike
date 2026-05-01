using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Semantic menu-input helpers. Layers on top of <see cref="KeyboardInputManager"/>
    /// to expose the four directions + Confirm + Cancel + AnyKey that menu code
    /// almost always wants. Every method ORs the new InputSystem result with the
    /// legacy <see cref="UnityEngine.Input"/> backend (via KeyboardInputManager)
    /// so menus stay navigable when the new pipeline drops OS events.
    ///
    /// <para>
    /// Use <see cref="KeyboardInputManager"/> directly for non-menu keyboard
    /// reads (Enter in chat, Escape in modals, F2 to rename, etc.).
    /// </para>
    /// </summary>
    public static class InputCompat
    {
        public static bool NavUpPressed()
            => KeyboardInputManager.WasKeyPressedThisFrame(Key.UpArrow, KeyCode.UpArrow)
            || KeyboardInputManager.WasKeyPressedThisFrame(Key.W, KeyCode.W);

        public static bool NavDownPressed()
            => KeyboardInputManager.WasKeyPressedThisFrame(Key.DownArrow, KeyCode.DownArrow)
            || KeyboardInputManager.WasKeyPressedThisFrame(Key.S, KeyCode.S);

        public static bool NavLeftPressed()
            => KeyboardInputManager.WasKeyPressedThisFrame(Key.LeftArrow, KeyCode.LeftArrow)
            || KeyboardInputManager.WasKeyPressedThisFrame(Key.A, KeyCode.A);

        public static bool NavRightPressed()
            => KeyboardInputManager.WasKeyPressedThisFrame(Key.RightArrow, KeyCode.RightArrow)
            || KeyboardInputManager.WasKeyPressedThisFrame(Key.D, KeyCode.D);

        public static bool ConfirmPressed()
            => KeyboardInputManager.WasEnterPressedThisFrame()
            || KeyboardInputManager.WasKeyPressedThisFrame(Key.Space, KeyCode.Space);

        public static bool CancelPressed()
            => KeyboardInputManager.WasEscapePressedThisFrame();

        public static bool AnyKeyPressed()
            => KeyboardInputManager.WasAnyKeyPressedThisFrame();

        // Forwarders kept for backwards compatibility with existing callers.
        // New code should call KeyboardInputManager directly for non-menu keys.

        public static bool KeyPressed(Key newKey, KeyCode legacyKey)
            => KeyboardInputManager.WasKeyPressedThisFrame(newKey, legacyKey);

        public static bool KeyHeld(Key newKey, KeyCode legacyKey)
            => KeyboardInputManager.IsKeyPressed(newKey, legacyKey);
    }
}
