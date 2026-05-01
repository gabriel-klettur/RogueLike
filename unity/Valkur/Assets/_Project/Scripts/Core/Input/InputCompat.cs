using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Compat shim that ORs the new <see cref="UnityEngine.InputSystem"/> against
    /// the legacy <see cref="UnityEngine.Input"/> backend for the most common
    /// keys used by Valkur's menus and gameplay. Required because under
    /// Unity 2022.3.62f1 in the Editor the new InputSystem package
    /// intermittently drops OS event delivery while the legacy backend keeps
    /// working — the new device's <c>wasPressedThisFrame</c> stays false even
    /// though the user clearly pressed the key.
    ///
    /// Project setting <c>activeInputHandler = 2</c> ("Both") enables both
    /// backends, so the legacy <see cref="UnityEngine.Input"/> APIs are always
    /// available as a fallback.
    ///
    /// All methods return <c>true</c> if EITHER backend reports the press —
    /// this is the safe fallback because the legacy backend never spuriously
    /// fires (it requires a real OS event), and the new backend only adds
    /// presses if it's actually working.
    /// </summary>
    public static class InputCompat
    {
        // ── Direction (menu navigation) ──────────────────────────────────────

        public static bool NavUpPressed()
        {
            var k = Keyboard.current;
            bool n = k != null && (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame);
            bool l = UnityEngine.Input.GetKeyDown(KeyCode.UpArrow) || UnityEngine.Input.GetKeyDown(KeyCode.W);
            return n || l;
        }

        public static bool NavDownPressed()
        {
            var k = Keyboard.current;
            bool n = k != null && (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame);
            bool l = UnityEngine.Input.GetKeyDown(KeyCode.DownArrow) || UnityEngine.Input.GetKeyDown(KeyCode.S);
            return n || l;
        }

        public static bool NavLeftPressed()
        {
            var k = Keyboard.current;
            bool n = k != null && (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame);
            bool l = UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow) || UnityEngine.Input.GetKeyDown(KeyCode.A);
            return n || l;
        }

        public static bool NavRightPressed()
        {
            var k = Keyboard.current;
            bool n = k != null && (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame);
            bool l = UnityEngine.Input.GetKeyDown(KeyCode.RightArrow) || UnityEngine.Input.GetKeyDown(KeyCode.D);
            return n || l;
        }

        // ── Confirm / Cancel ─────────────────────────────────────────────────

        public static bool ConfirmPressed()
        {
            var k = Keyboard.current;
            bool n = k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame);
            bool l = UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                     UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter) ||
                     UnityEngine.Input.GetKeyDown(KeyCode.Space);
            return n || l;
        }

        public static bool CancelPressed()
        {
            var k = Keyboard.current;
            bool n = k != null && k.escapeKey.wasPressedThisFrame;
            bool l = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            return n || l;
        }

        // ── Any key (press-to-start) ─────────────────────────────────────────

        public static bool AnyKeyPressed()
        {
            var k = Keyboard.current;
            bool n = k != null && k.anyKey.wasPressedThisFrame;
            bool l = UnityEngine.Input.anyKeyDown;
            return n || l;
        }

        // ── Generic key check (used by SaveLoad / DevConsole / etc.) ─────────

        /// <summary>
        /// Returns true if the given <see cref="Key"/> was pressed this frame
        /// in EITHER backend. Pass the corresponding <see cref="KeyCode"/> for
        /// the legacy fallback.
        /// </summary>
        public static bool KeyPressed(Key newKey, KeyCode legacyKey)
        {
            var k = Keyboard.current;
            bool n = k != null && k[newKey].wasPressedThisFrame;
            bool l = UnityEngine.Input.GetKeyDown(legacyKey);
            return n || l;
        }

        public static bool KeyHeld(Key newKey, KeyCode legacyKey)
        {
            var k = Keyboard.current;
            bool n = k != null && k[newKey].isPressed;
            bool l = UnityEngine.Input.GetKey(legacyKey);
            return n || l;
        }
    }
}
