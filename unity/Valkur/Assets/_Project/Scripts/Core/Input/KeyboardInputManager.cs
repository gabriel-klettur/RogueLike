using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Centralized keyboard fachade — the keyboard counterpart to
    /// <see cref="MouseInputManager"/>. Every public query ORs the new
    /// <see cref="UnityEngine.InputSystem"/> result with the legacy
    /// <see cref="UnityEngine.Input"/> backend so the game keeps responding
    /// to keys when the new InputSystem package drops OS event delivery
    /// (recurring Unity 2022.3.62f1 Editor bug — see <see cref="MouseInputManager"/>
    /// XML doc for the full diagnosis).
    ///
    /// <para>
    /// This is the ONLY allowed entry point for keyboard polling outside the
    /// canonical <see cref="InputService"/> action API. New code that reads
    /// keys must call methods here instead of touching <see cref="Keyboard.current"/>
    /// directly — otherwise the call silently dies under the bug.
    /// </para>
    ///
    /// <para>
    /// For semantic menu helpers (Nav up/down/left/right, Confirm, Cancel,
    /// AnyKey) use <see cref="InputCompat"/> which layers on top of this
    /// manager.
    /// </para>
    /// </summary>
    public static class KeyboardInputManager
    {
        // ── Generic Key/KeyCode pair API ─────────────────────────────────────

        /// <summary>True iff <paramref name="newKey"/> is held this frame in
        /// EITHER the new InputSystem or the legacy backend.
        /// Returns false while a modal panel (chat / dev console) holds focus,
        /// unless the key is on the always-allowed list (Esc, ~, Enter).</summary>
        public static bool IsKeyPressed(Key newKey, KeyCode legacyKey)
        {
            if (InputBlocker.IsGameplayBlocked &&
                !InputBlocker.IsAlwaysAllowedKey(newKey) &&
                !InputBlocker.IsAlwaysAllowedKey(legacyKey))
                return false;
            var kb = Keyboard.current;
            bool n = kb != null && kb[newKey].isPressed;
            return n || UnityEngine.Input.GetKey(legacyKey);
        }

        public static bool WasKeyPressedThisFrame(Key newKey, KeyCode legacyKey)
        {
            if (InputBlocker.IsGameplayBlocked &&
                !InputBlocker.IsAlwaysAllowedKey(newKey) &&
                !InputBlocker.IsAlwaysAllowedKey(legacyKey))
                return false;
            var kb = Keyboard.current;
            bool n = kb != null && kb[newKey].wasPressedThisFrame;
            return n || UnityEngine.Input.GetKeyDown(legacyKey);
        }

        public static bool WasKeyReleasedThisFrame(Key newKey, KeyCode legacyKey)
        {
            if (InputBlocker.IsGameplayBlocked &&
                !InputBlocker.IsAlwaysAllowedKey(newKey) &&
                !InputBlocker.IsAlwaysAllowedKey(legacyKey))
                return false;
            var kb = Keyboard.current;
            bool n = kb != null && kb[newKey].wasReleasedThisFrame;
            return n || UnityEngine.Input.GetKeyUp(legacyKey);
        }

        // ── Common-key helpers ──────────────────────────────────────────────
        // These avoid every callsite re-pairing Key.X with KeyCode.X. Add a
        // helper here whenever a new key starts appearing in 3+ callsites.

        public static bool WasEnterPressedThisFrame()
        {
            var kb = Keyboard.current;
            bool n = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            return n || UnityEngine.Input.GetKeyDown(KeyCode.Return)
                     || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter);
        }

        public static bool WasEscapePressedThisFrame()
            => WasKeyPressedThisFrame(Key.Escape, KeyCode.Escape);

        public static bool WasDeletePressedThisFrame()
            => WasKeyPressedThisFrame(Key.Delete, KeyCode.Delete);

        public static bool WasF2PressedThisFrame()
            => WasKeyPressedThisFrame(Key.F2, KeyCode.F2);

        public static bool WasQPressedThisFrame()
            => WasKeyPressedThisFrame(Key.Q, KeyCode.Q);

        public static bool WasEPressedThisFrame()
            => WasKeyPressedThisFrame(Key.E, KeyCode.E);

        public static bool IsLeftShiftPressed()
            => IsKeyPressed(Key.LeftShift, KeyCode.LeftShift);

        public static bool IsRightShiftPressed()
            => IsKeyPressed(Key.RightShift, KeyCode.RightShift);

        public static bool IsShiftHeld()
            => IsLeftShiftPressed() || IsRightShiftPressed();

        public static bool IsLeftCtrlPressed()
            => IsKeyPressed(Key.LeftCtrl, KeyCode.LeftControl);

        public static bool IsRightCtrlPressed()
            => IsKeyPressed(Key.RightCtrl, KeyCode.RightControl);

        public static bool IsCtrlHeld()
            => IsLeftCtrlPressed() || IsRightCtrlPressed();

        public static bool WasLeftCtrlPressedThisFrame()
            => WasKeyPressedThisFrame(Key.LeftCtrl, KeyCode.LeftControl);

        public static bool WasRightCtrlPressedThisFrame()
            => WasKeyPressedThisFrame(Key.RightCtrl, KeyCode.RightControl);

        public static bool WasCtrlPressedThisFrame()
            => WasLeftCtrlPressedThisFrame() || WasRightCtrlPressedThisFrame();

        public static bool IsLeftAltPressed()
            => IsKeyPressed(Key.LeftAlt, KeyCode.LeftAlt);

        public static bool IsRightAltPressed()
            => IsKeyPressed(Key.RightAlt, KeyCode.RightAlt);

        public static bool IsAltHeld()
            => IsLeftAltPressed() || IsRightAltPressed();

        // ── Navigation keys (console history / tab-complete) ────────────────

        public static bool WasTabPressedThisFrame()
            => WasKeyPressedThisFrame(Key.Tab, KeyCode.Tab);

        public static bool WasArrowUpPressedThisFrame()
            => WasKeyPressedThisFrame(Key.UpArrow, KeyCode.UpArrow);

        public static bool WasArrowDownPressedThisFrame()
            => WasKeyPressedThisFrame(Key.DownArrow, KeyCode.DownArrow);

        // ── Any-key (used by press-to-start / chat / any-input dismiss) ─────

        public static bool WasAnyKeyPressedThisFrame()
        {
            // Always-allowed keys (Esc, ~, Enter) shouldn't trigger any-key
            // listeners while a modal panel is up either — those listeners
            // are gameplay-side (e.g. press-to-start screen). Block wholesale.
            if (InputBlocker.IsGameplayBlocked) return false;
            var kb = Keyboard.current;
            bool n = kb != null && kb.anyKey.wasPressedThisFrame;
            return n || UnityEngine.Input.anyKeyDown;
        }
    }
}
