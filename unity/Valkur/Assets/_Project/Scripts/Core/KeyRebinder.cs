using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Valkur.Core
{
    /// <summary>
    /// Listens for the next key press from keyboard or mouse and reports back its
    /// <see cref="Key"/> / mouse-button name in the string format used by
    /// <see cref="GameSettings"/> (e.g. "w", "Escape", "LeftButton").
    ///
    /// Mirrors Python input_service rebinding: the user clicks a row, presses a key,
    /// the new binding is written to GameSettings and saved.
    /// </summary>
    public sealed class KeyRebinder : IDisposable
    {
        /// <summary>Fired once when a key/button is captured. Argument is the string label.</summary>
        public event Action<string> Completed;
        public event Action Cancelled;

        private readonly InputAction _captureAction;
        private bool _active;

        public bool IsActive => _active;

        public KeyRebinder()
        {
            _captureAction = new InputAction("KeyRebinderCapture", InputActionType.PassThrough);
            _captureAction.AddBinding("<Keyboard>/anyKey");
            _captureAction.AddBinding("<Mouse>/leftButton");
            _captureAction.AddBinding("<Mouse>/rightButton");
            _captureAction.AddBinding("<Mouse>/middleButton");
            _captureAction.performed += OnPerformed;
        }

        public void Start()
        {
            if (_active) return;
            _active = true;
            _captureAction.Enable();
        }

        public void Cancel()
        {
            if (!_active) return;
            _active = false;
            _captureAction.Disable();
            Cancelled?.Invoke();
        }

        public void Dispose()
        {
            _captureAction.performed -= OnPerformed;
            _captureAction.Disable();
            _captureAction.Dispose();
        }

        private void OnPerformed(InputAction.CallbackContext ctx)
        {
            if (!_active) return;
            var ctrl = ctx.control;
            if (ctrl == null) return;

            // Keyboard key
            if (ctrl is KeyControl kc)
            {
                _active = false;
                _captureAction.Disable();
                // Skip the "anyKey" meta-control
                if (kc.name == "anyKey") return;
                Completed?.Invoke(NormalizeKeyName(kc.keyCode.ToString()));
                return;
            }
            // Mouse buttons
            if (ctrl is ButtonControl bc)
            {
                _active = false;
                _captureAction.Disable();
                string name = bc.name;
                if (name == "leftButton")   { Completed?.Invoke("LeftButton"); return; }
                if (name == "rightButton")  { Completed?.Invoke("RightButton"); return; }
                if (name == "middleButton") { Completed?.Invoke("MiddleButton"); return; }
                Completed?.Invoke(name);
                return;
            }
        }

        /// <summary>
        /// Converts Unity's <see cref="Key"/> enum names to the short format GameSettings expects.
        /// e.g. "W" -> "w", "Escape" stays as-is, "Digit1" -> "1".
        /// </summary>
        private static string NormalizeKeyName(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return keyName;
            // Letters: single-char keys become lowercase
            if (keyName.Length == 1) return keyName.ToLowerInvariant();
            if (keyName.StartsWith("Digit") && keyName.Length == 6) return keyName.Substring(5);
            if (keyName.StartsWith("Numpad") && keyName.Length > 6) return keyName; // Numpad1 etc stays
            return keyName;
        }
    }
}
