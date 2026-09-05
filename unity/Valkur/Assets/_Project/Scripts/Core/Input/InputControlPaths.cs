using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>Which physical mouse control a binding names. <see cref="None"/> means the
    /// path is not a mouse button this project can bind.</summary>
    public enum MouseControl
    {
        None = 0,
        Left,
        Right,
        Middle,
        Forward,
        Back,
        WheelUp,
        WheelDown,
    }

    /// <summary>
    /// One physical control, in all four vocabularies this project speaks about input:
    /// the InputSystem <see cref="UnityEngine.InputSystem.Key"/>, the legacy
    /// <see cref="UnityEngine.KeyCode"/>, the binding PATH stored in the
    /// <c>.inputactions</c> asset, and the label a human reads on a key cap.
    /// </summary>
    public readonly struct InputControlEntry
    {
        public readonly Key Key;
        public readonly KeyCode Legacy;

        /// <summary>The control name Unity uses under <c>&lt;Keyboard&gt;/</c> — e.g. "a", "f8",
        /// "leftShift". Canonical camelCase; Unity's own lookup is case-insensitive.</summary>
        public readonly string ControlName;

        /// <summary>Full binding path, e.g. <c>&lt;Keyboard&gt;/leftShift</c>.</summary>
        public readonly string Path;

        /// <summary>What the key cap says.</summary>
        public readonly string Label;

        public InputControlEntry(Key key, KeyCode legacy, string controlName, string label)
        {
            Key         = key;
            Legacy      = legacy;
            ControlName = controlName;
            Path        = InputControlPaths.KeyboardPrefix + controlName;
            Label       = label;
        }

        public bool IsValid => !string.IsNullOrEmpty(ControlName);
    }

    /// <summary>
    /// The single translator between a binding PATH and the two runtime vocabularies that
    /// read input in this project.
    ///
    /// <para>WHY IT HAS TO EXIST. Every gameplay read here is an OR of the new InputSystem
    /// and the legacy backend — <see cref="KeyboardInputManager"/> and
    /// <see cref="MouseInputManager"/> do nothing else — because the Unity 2022.3 Editor
    /// intermittently drops InputSystem event delivery. That OR was fed by HARDCODED
    /// <see cref="KeyCode"/> literals sitting beside each action: <c>EnumerateSpellBindings</c>
    /// paired <c>SpellDarkball</c> with <c>KeyCode.Alpha1</c> in source. So an
    /// <c>ApplyBindingOverride</c> that moved darkball from <c>1</c> to <c>5</c> moved only
    /// half of it — <c>1</c> went on casting darkball through the legacy half, silently, and
    /// any rebinding UI built on overrides alone was a lie. Deriving the legacy half FROM the
    /// live path is what makes a rebind whole, and it is what lets a drawn keyboard highlight
    /// the truth rather than the intention.</para>
    ///
    /// <para>It is a static TABLE rather than a query against <see cref="Keyboard.current"/>
    /// on purpose: EditMode tests have no keyboard device, and a translator that answers
    /// nothing without hardware cannot be the thing the whole binding layer stands on.</para>
    ///
    /// <para>Every field is immutable and every collection is built once in the static
    /// constructor, so there is no mutable state for the Domain-Reload ratchet to reset.</para>
    /// </summary>
    public static class InputControlPaths
    {
        public const string KeyboardPrefix = "<Keyboard>/";
        public const string MousePrefix    = "<Mouse>/";

        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly InputControlEntry[] _entries;
        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<Key, int>     _byKey;
        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<KeyCode, int> _byLegacy;
        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<string, int>  _byControlName;

        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<string, MouseControl>  _mouseByControlName;
        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<MouseControl, string>  _mouseControlNames;
        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<MouseControl, KeyCode> _mouseLegacy;
        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<MouseControl, string>  _mouseLabels;

        /// <summary>Every keyboard control this project can bind, in table order.</summary>
        public static IReadOnlyList<InputControlEntry> Entries => _entries;

        static InputControlPaths()
        {
            _entries = BuildTable();

            _byKey         = new Dictionary<Key, int>(_entries.Length);
            _byLegacy      = new Dictionary<KeyCode, int>(_entries.Length);
            _byControlName = new Dictionary<string, int>(_entries.Length, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _entries.Length; i++)
            {
                var e = _entries[i];
                if (!_byKey.ContainsKey(e.Key)) _byKey[e.Key] = i;

                // KeyCode.None is the legitimate "no legacy equivalent" answer for the OEM
                // keys, and several entries share it. The reverse lookup for None is
                // meaningless and is refused in TryResolveLegacy rather than stored here.
                if (e.Legacy != KeyCode.None && !_byLegacy.ContainsKey(e.Legacy)) _byLegacy[e.Legacy] = i;

                if (!_byControlName.ContainsKey(e.ControlName)) _byControlName[e.ControlName] = i;
            }

            _mouseControlNames = new Dictionary<MouseControl, string>
            {
                { MouseControl.Left,      "leftButton"    },
                { MouseControl.Right,     "rightButton"   },
                { MouseControl.Middle,    "middleButton"  },
                { MouseControl.Forward,   "forwardButton" },
                { MouseControl.Back,      "backButton"    },
                { MouseControl.WheelUp,   "scroll/up"     },
                { MouseControl.WheelDown, "scroll/down"   },
            };

            _mouseByControlName = new Dictionary<string, MouseControl>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _mouseControlNames) _mouseByControlName[kv.Value] = kv.Key;

            // The legacy backend has no wheel BUTTON, so those answer KeyCode.None and the
            // OR-gate simply runs on one leg there.
            _mouseLegacy = new Dictionary<MouseControl, KeyCode>
            {
                { MouseControl.Left,      KeyCode.Mouse0 },
                { MouseControl.Right,     KeyCode.Mouse1 },
                { MouseControl.Middle,    KeyCode.Mouse2 },
                { MouseControl.Back,      KeyCode.Mouse3 },
                { MouseControl.Forward,   KeyCode.Mouse4 },
                { MouseControl.WheelUp,   KeyCode.None   },
                { MouseControl.WheelDown, KeyCode.None   },
            };

            _mouseLabels = new Dictionary<MouseControl, string>
            {
                { MouseControl.Left,      "Click izq."    },
                { MouseControl.Right,     "Click der."    },
                { MouseControl.Middle,    "Click central" },
                { MouseControl.Forward,   "Bot. adelante" },
                { MouseControl.Back,      "Bot. atras"    },
                { MouseControl.WheelUp,   "Rueda arriba"  },
                { MouseControl.WheelDown, "Rueda abajo"   },
            };
        }

        // ── Path classification ──────────────────────────────────────────────

        public static bool IsKeyboardPath(string path) =>
            !string.IsNullOrEmpty(path) &&
            path.StartsWith(KeyboardPrefix, StringComparison.OrdinalIgnoreCase);

        public static bool IsMousePath(string path) =>
            !string.IsNullOrEmpty(path) &&
            path.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>The part after the device prefix, or null when the path names no device
        /// this translator knows.</summary>
        public static string ControlNameOf(string path)
        {
            if (IsKeyboardPath(path)) return path.Substring(KeyboardPrefix.Length);
            if (IsMousePath(path))    return path.Substring(MousePrefix.Length);
            return null;
        }

        // ── Keyboard lookups ─────────────────────────────────────────────────

        public static bool TryResolvePath(string path, out InputControlEntry entry)
        {
            entry = default;
            if (!IsKeyboardPath(path)) return false;
            return TryResolveControlName(path.Substring(KeyboardPrefix.Length), out entry);
        }

        public static bool TryResolveControlName(string controlName, out InputControlEntry entry)
        {
            entry = default;
            if (string.IsNullOrWhiteSpace(controlName)) return false;
            if (!_byControlName.TryGetValue(controlName.Trim(), out int i)) return false;
            entry = _entries[i];
            return true;
        }

        public static bool TryResolveKey(Key key, out InputControlEntry entry)
        {
            entry = default;
            if (!_byKey.TryGetValue(key, out int i)) return false;
            entry = _entries[i];
            return true;
        }

        public static bool TryResolveLegacy(KeyCode legacy, out InputControlEntry entry)
        {
            entry = default;
            if (legacy == KeyCode.None) return false;
            if (!_byLegacy.TryGetValue(legacy, out int i)) return false;
            entry = _entries[i];
            return true;
        }

        /// <summary>Binding path for a <see cref="Key"/>, or null when it is not bindable.</summary>
        public static string PathForKey(Key key) =>
            TryResolveKey(key, out var e) ? e.Path : null;

        /// <summary>Binding path for a legacy <see cref="KeyCode"/> — keyboard OR mouse.
        /// Null when the code names neither.</summary>
        public static string PathForKeyCode(KeyCode legacy)
        {
            if (TryResolveLegacy(legacy, out var e)) return e.Path;
            foreach (var kv in _mouseLegacy)
                if (kv.Value != KeyCode.None && kv.Value == legacy)
                    return MousePrefix + _mouseControlNames[kv.Key];
            return null;
        }

        /// <summary>
        /// The legacy pair for a path. Answers <see cref="KeyCode.None"/> and
        /// <see cref="Key.None"/> for anything unresolvable, which every OR-gate in this
        /// project already treats as "this half is not available".
        /// </summary>
        public static void ResolveLegacyPair(string path, out Key key, out KeyCode legacy)
        {
            if (TryResolvePath(path, out var e)) { key = e.Key; legacy = e.Legacy; return; }
            key = Key.None;
            legacy = LegacyForMouse(ResolveMouse(path));
        }

        // ── Mouse lookups ────────────────────────────────────────────────────

        public static MouseControl ResolveMouse(string path)
        {
            if (!IsMousePath(path)) return MouseControl.None;
            var name = path.Substring(MousePrefix.Length).Trim();
            return _mouseByControlName.TryGetValue(name, out var m) ? m : MouseControl.None;
        }

        public static string PathForMouse(MouseControl control) =>
            _mouseControlNames.TryGetValue(control, out var n) ? MousePrefix + n : null;

        public static KeyCode LegacyForMouse(MouseControl control) =>
            _mouseLegacy.TryGetValue(control, out var k) ? k : KeyCode.None;

        // ── Human labels ─────────────────────────────────────────────────────

        /// <summary>
        /// What to print on a key cap or a binding chip for this path. Falls back to the raw
        /// control name so an unknown device still reads as SOMETHING — a blank chip is
        /// indistinguishable from an unbound action.
        /// </summary>
        public static string LabelForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (TryResolvePath(path, out var e)) return e.Label;
            var mouse = ResolveMouse(path);
            if (mouse != MouseControl.None) return _mouseLabels[mouse];
            return ControlNameOf(path) ?? path;
        }

        public static string LabelForMouse(MouseControl control) =>
            _mouseLabels.TryGetValue(control, out var l) ? l : "";

        // ── The table ────────────────────────────────────────────────────────

        private static InputControlEntry[] BuildTable()
        {
            return new[]
            {
                // Letters — the control name is the bare lowercase letter.
                E(Key.A, KeyCode.A, "a", "A"), E(Key.B, KeyCode.B, "b", "B"),
                E(Key.C, KeyCode.C, "c", "C"), E(Key.D, KeyCode.D, "d", "D"),
                E(Key.E, KeyCode.E, "e", "E"), E(Key.F, KeyCode.F, "f", "F"),
                E(Key.G, KeyCode.G, "g", "G"), E(Key.H, KeyCode.H, "h", "H"),
                E(Key.I, KeyCode.I, "i", "I"), E(Key.J, KeyCode.J, "j", "J"),
                E(Key.K, KeyCode.K, "k", "K"), E(Key.L, KeyCode.L, "l", "L"),
                E(Key.M, KeyCode.M, "m", "M"), E(Key.N, KeyCode.N, "n", "N"),
                E(Key.O, KeyCode.O, "o", "O"), E(Key.P, KeyCode.P, "p", "P"),
                E(Key.Q, KeyCode.Q, "q", "Q"), E(Key.R, KeyCode.R, "r", "R"),
                E(Key.S, KeyCode.S, "s", "S"), E(Key.T, KeyCode.T, "t", "T"),
                E(Key.U, KeyCode.U, "u", "U"), E(Key.V, KeyCode.V, "v", "V"),
                E(Key.W, KeyCode.W, "w", "W"), E(Key.X, KeyCode.X, "x", "X"),
                E(Key.Y, KeyCode.Y, "y", "Y"), E(Key.Z, KeyCode.Z, "z", "Z"),

                // Digit row — Unity names the control "1".."0", NOT "digit1".
                E(Key.Digit1, KeyCode.Alpha1, "1", "1"),
                E(Key.Digit2, KeyCode.Alpha2, "2", "2"),
                E(Key.Digit3, KeyCode.Alpha3, "3", "3"),
                E(Key.Digit4, KeyCode.Alpha4, "4", "4"),
                E(Key.Digit5, KeyCode.Alpha5, "5", "5"),
                E(Key.Digit6, KeyCode.Alpha6, "6", "6"),
                E(Key.Digit7, KeyCode.Alpha7, "7", "7"),
                E(Key.Digit8, KeyCode.Alpha8, "8", "8"),
                E(Key.Digit9, KeyCode.Alpha9, "9", "9"),
                E(Key.Digit0, KeyCode.Alpha0, "0", "0"),

                // Function row.
                E(Key.F1,  KeyCode.F1,  "f1",  "F1"),  E(Key.F2,  KeyCode.F2,  "f2",  "F2"),
                E(Key.F3,  KeyCode.F3,  "f3",  "F3"),  E(Key.F4,  KeyCode.F4,  "f4",  "F4"),
                E(Key.F5,  KeyCode.F5,  "f5",  "F5"),  E(Key.F6,  KeyCode.F6,  "f6",  "F6"),
                E(Key.F7,  KeyCode.F7,  "f7",  "F7"),  E(Key.F8,  KeyCode.F8,  "f8",  "F8"),
                E(Key.F9,  KeyCode.F9,  "f9",  "F9"),  E(Key.F10, KeyCode.F10, "f10", "F10"),
                E(Key.F11, KeyCode.F11, "f11", "F11"), E(Key.F12, KeyCode.F12, "f12", "F12"),

                // Punctuation / symbols.
                E(Key.Backquote,    KeyCode.BackQuote,    "backquote",    "`"),
                E(Key.Minus,        KeyCode.Minus,        "minus",        "-"),
                E(Key.Equals,       KeyCode.Equals,       "equals",       "="),
                E(Key.LeftBracket,  KeyCode.LeftBracket,  "leftBracket",  "["),
                E(Key.RightBracket, KeyCode.RightBracket, "rightBracket", "]"),
                E(Key.Backslash,    KeyCode.Backslash,    "backslash",    "\\"),
                E(Key.Semicolon,    KeyCode.Semicolon,    "semicolon",    ";"),
                E(Key.Quote,        KeyCode.Quote,        "quote",        "'"),
                E(Key.Comma,        KeyCode.Comma,        "comma",        ","),
                E(Key.Period,       KeyCode.Period,       "period",       "."),
                E(Key.Slash,        KeyCode.Slash,        "slash",        "/"),

                // Editing / navigation.
                E(Key.Space,      KeyCode.Space,      "space",      "Espacio"),
                E(Key.Enter,      KeyCode.Return,     "enter",      "Enter"),
                E(Key.Tab,        KeyCode.Tab,        "tab",        "Tab"),
                E(Key.Backspace,  KeyCode.Backspace,  "backspace",  "Borrar"),
                E(Key.Escape,     KeyCode.Escape,     "escape",     "Esc"),
                E(Key.Insert,     KeyCode.Insert,     "insert",     "Ins"),
                E(Key.Delete,     KeyCode.Delete,     "delete",     "Supr"),
                E(Key.Home,       KeyCode.Home,       "home",       "Inicio"),
                E(Key.End,        KeyCode.End,        "end",        "Fin"),
                E(Key.PageUp,     KeyCode.PageUp,     "pageUp",     "Re Pag"),
                E(Key.PageDown,   KeyCode.PageDown,   "pageDown",   "Av Pag"),
                E(Key.LeftArrow,  KeyCode.LeftArrow,  "leftArrow",  "Izq."),
                E(Key.RightArrow, KeyCode.RightArrow, "rightArrow", "Der."),
                E(Key.UpArrow,    KeyCode.UpArrow,    "upArrow",    "Arriba"),
                E(Key.DownArrow,  KeyCode.DownArrow,  "downArrow",  "Abajo"),

                // Modifiers and locks.
                E(Key.LeftShift,   KeyCode.LeftShift,    "leftShift",   "Shift izq."),
                E(Key.RightShift,  KeyCode.RightShift,   "rightShift",  "Shift der."),
                E(Key.LeftCtrl,    KeyCode.LeftControl,  "leftCtrl",    "Ctrl izq."),
                E(Key.RightCtrl,   KeyCode.RightControl, "rightCtrl",   "Ctrl der."),
                E(Key.LeftAlt,     KeyCode.LeftAlt,      "leftAlt",     "Alt"),
                E(Key.RightAlt,    KeyCode.RightAlt,     "rightAlt",    "Alt Gr"),
                E(Key.LeftMeta,    KeyCode.LeftWindows,  "leftMeta",    "Win izq."),
                E(Key.RightMeta,   KeyCode.RightWindows, "rightMeta",   "Win der."),
                E(Key.ContextMenu, KeyCode.Menu,         "contextMenu", "Menu"),
                E(Key.CapsLock,    KeyCode.CapsLock,     "capsLock",    "Bloq Mayus"),
                E(Key.NumLock,     KeyCode.Numlock,      "numLock",     "Bloq Num"),
                E(Key.ScrollLock,  KeyCode.ScrollLock,   "scrollLock",  "Bloq Despl"),
                E(Key.PrintScreen, KeyCode.Print,        "printScreen", "Impr Pant"),
                E(Key.Pause,       KeyCode.Pause,        "pause",       "Pausa"),

                // Numpad.
                E(Key.Numpad0, KeyCode.Keypad0, "numpad0", "Num 0"),
                E(Key.Numpad1, KeyCode.Keypad1, "numpad1", "Num 1"),
                E(Key.Numpad2, KeyCode.Keypad2, "numpad2", "Num 2"),
                E(Key.Numpad3, KeyCode.Keypad3, "numpad3", "Num 3"),
                E(Key.Numpad4, KeyCode.Keypad4, "numpad4", "Num 4"),
                E(Key.Numpad5, KeyCode.Keypad5, "numpad5", "Num 5"),
                E(Key.Numpad6, KeyCode.Keypad6, "numpad6", "Num 6"),
                E(Key.Numpad7, KeyCode.Keypad7, "numpad7", "Num 7"),
                E(Key.Numpad8, KeyCode.Keypad8, "numpad8", "Num 8"),
                E(Key.Numpad9, KeyCode.Keypad9, "numpad9", "Num 9"),
                E(Key.NumpadEnter,    KeyCode.KeypadEnter,    "numpadEnter",    "Num Enter"),
                E(Key.NumpadDivide,   KeyCode.KeypadDivide,   "numpadDivide",   "Num /"),
                E(Key.NumpadMultiply, KeyCode.KeypadMultiply, "numpadMultiply", "Num *"),
                E(Key.NumpadPlus,     KeyCode.KeypadPlus,     "numpadPlus",     "Num +"),
                E(Key.NumpadMinus,    KeyCode.KeypadMinus,    "numpadMinus",    "Num -"),
                E(Key.NumpadPeriod,   KeyCode.KeypadPeriod,   "numpadPeriod",   "Num ."),
                E(Key.NumpadEquals,   KeyCode.KeypadEquals,   "numpadEquals",   "Num ="),

                // ISO / regional extras. The legacy backend has no code for these at all, so
                // they bind through the new InputSystem only and the OR-gate runs on one leg.
                // They are in the table because an ISO-ES keyboard has five of them, and a
                // drawn keyboard that cannot name a key it draws has holes in it.
                E(Key.OEM1, KeyCode.None, "OEM1", "OEM1"),
                E(Key.OEM2, KeyCode.None, "OEM2", "OEM2"),
                E(Key.OEM3, KeyCode.None, "OEM3", "OEM3"),
                E(Key.OEM4, KeyCode.None, "OEM4", "OEM4"),
                E(Key.OEM5, KeyCode.None, "OEM5", "OEM5"),
            };
        }

        private static InputControlEntry E(Key key, KeyCode legacy, string controlName, string label)
            => new InputControlEntry(key, legacy, controlName, label);
    }
}
