using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Reads an action's LIVE bindings and answers the OR-gate with them.
    ///
    /// <para>THE DEFECT THIS CLOSES. Every gameplay read in Valkur is an OR of the new
    /// InputSystem and the legacy backend, because the Unity 2022.3 Editor intermittently
    /// drops InputSystem event delivery. The legacy half was fed by literals written beside
    /// each action in source — <c>SpellDarkball</c> paired with <c>KeyCode.Alpha1</c>, the
    /// dash also reading <c>RightShift</c> / <c>LeftCtrl</c> / <c>RightCtrl</c>, movement
    /// re-reading <c>Key.W</c> / <c>KeyCode.W</c>. An <c>ApplyBindingOverride</c> moved the
    /// action and left the literal exactly where it was, so a rebind was HALF applied: move
    /// darkball from <c>1</c> to <c>5</c> and <c>1</c> went on casting it, silently, forever.
    /// Any rebinding UI built on overrides alone was therefore a lie about its own effect.
    /// Deriving the legacy pair from the effective path is what makes a rebind whole.</para>
    ///
    /// <para>IT CACHES, AND THE CACHE IS VERSIONED. Resolving an action's bindings allocates,
    /// and <c>PollCombatActions</c> asks about twenty-four spell actions every frame. The
    /// cache is dropped wholesale whenever anything rebinds — <see cref="Invalidate"/> — which
    /// is a human-scale event, so there is no staleness window a player could observe.</para>
    /// </summary>
    public static class InputBindingResolver
    {
        /// <summary>One physical control an action is bound to, in both vocabularies.</summary>
        public readonly struct Binding
        {
            public readonly string Path;
            public readonly Key Key;
            public readonly KeyCode Legacy;
            public readonly MouseControl Mouse;

            /// <summary>
            /// The composite PART this binding fills — "up", "down", "left", "right" — or
            /// empty for a plain binding. Without it a 2DVector resolves to four
            /// indistinguishable controls, and the movement fallback cannot tell which way
            /// each one points: that is the whole reason <c>ReadInput</c> used to re-list
            /// W/A/S/D as literals instead of asking the action what it was bound to.
            /// </summary>
            public readonly string Part;

            /// <summary>Index into <c>action.bindings</c>. What
            /// <c>ApplyBindingOverride(int, string)</c> needs, so a rebind can move exactly
            /// the control the author clicked and not the action's first one.</summary>
            public readonly int Index;

            public Binding(string path, Key key, KeyCode legacy, MouseControl mouse,
                           string part, int index)
            {
                Path = path; Key = key; Legacy = legacy; Mouse = mouse;
                Part = part ?? ""; Index = index;
            }

            public bool IsKeyboard        => Key != Key.None;
            public bool IsMouse           => Mouse != MouseControl.None;
            public bool IsCompositePart   => !string.IsNullOrEmpty(Part);
        }

        private static readonly Dictionary<InputAction, Binding[]> _cache =
            new Dictionary<InputAction, Binding[]>();

        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Binding[] _empty = Array.Empty<Binding>();

        /// <summary>Drop every cached resolution. Called by anything that rebinds.</summary>
        public static void Invalidate() => _cache.Clear();

        // ── Resolution ───────────────────────────────────────────────────────

        /// <summary>
        /// Every control the action is bound to right now, overrides included. Composite
        /// HEADERS are skipped and composite PARTS are kept — a WASD Move resolves to four
        /// bindings, which is what a drawn keyboard has to highlight and what the movement
        /// fallback has to poll.
        /// </summary>
        public static Binding[] Resolve(InputAction action)
        {
            if (action == null) return _empty;
            if (_cache.TryGetValue(action, out var cached)) return cached;

            List<Binding> found = null;
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite) continue;                // the header names no control
                var path = b.effectivePath;                  // override first, asset behind it
                if (string.IsNullOrEmpty(path)) continue;

                InputControlPaths.ResolveLegacyPair(path, out var key, out var legacy);
                var mouse = InputControlPaths.ResolveMouse(path);
                if (key == Key.None && mouse == MouseControl.None && legacy == KeyCode.None)
                    continue;                                // a device this layer does not model

                (found ??= new List<Binding>(4))
                    .Add(new Binding(path, key, legacy, mouse, b.isPartOfComposite ? b.name : "", i));
            }

            var result = found == null ? _empty : found.ToArray();
            _cache[action] = result;
            return result;
        }

        /// <summary>The action's first bound control, for a chip that shows one key.</summary>
        public static Binding Primary(InputAction action)
        {
            var all = Resolve(action);
            return all.Length > 0 ? all[0] : default;
        }

        /// <summary>What to print for this action on a binding chip. Empty when unbound —
        /// which the editor renders as "sin asignar", never as a blank.</summary>
        public static string PrimaryLabel(InputAction action)
        {
            var all = Resolve(action);
            if (all.Length == 0) return "";
            if (all.Length == 1) return InputControlPaths.LabelForPath(all[0].Path);

            // A composite reads as its parts joined, which is how "WASD" stays one chip.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < all.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(InputControlPaths.LabelForPath(all[i].Path));
            }
            return sb.ToString();
        }

        // ── OR-gated reads ───────────────────────────────────────────────────

        /// <summary>
        /// Was this action triggered this frame, in EITHER backend? The legacy half is derived
        /// from the action's own live bindings, so it moves when the player rebinds.
        ///
        /// <para>The legacy reads go through <see cref="KeyboardInputManager"/> /
        /// <see cref="MouseInputManager"/> rather than raw <c>UnityEngine.Input</c>: those
        /// honour <see cref="InputBlocker"/>, and the raw half is exactly what used to cast a
        /// spell for every letter typed into the chat.</para>
        /// </summary>
        public static bool WasPerformedThisFrame(InputAction action)
        {
            if (action == null) return false;
            if (action.WasPerformedThisFrame()) return true;

            var all = Resolve(action);
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b.IsMouse)
                {
                    if (MouseWasPressedThisFrame(b.Mouse)) return true;
                    continue;
                }
                if (b.Legacy != KeyCode.None &&
                    KeyboardInputManager.WasKeyPressedThisFrame(b.Key, b.Legacy)) return true;
            }
            return false;
        }

        /// <summary>Is any of this action's controls held right now, in either backend?</summary>
        public static bool IsPressed(InputAction action)
        {
            if (action == null) return false;
            if (action.IsPressed()) return true;

            var all = Resolve(action);
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b.IsMouse)
                {
                    if (MouseIsPressed(b.Mouse)) return true;
                    continue;
                }
                if (b.Legacy != KeyCode.None &&
                    KeyboardInputManager.IsKeyPressed(b.Key, b.Legacy)) return true;
            }
            return false;
        }

        /// <summary>Was any of this action's controls released this frame? Needed by the three
        /// things in the combat poll that are HELD rather than fired — the left-held beam, the
        /// middle-click laser, a charging spell.</summary>
        public static bool WasReleasedThisFrame(InputAction action)
        {
            if (action == null) return false;
            if (action.WasReleasedThisFrame()) return true;

            var all = Resolve(action);
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b.IsMouse)
                {
                    if (MouseWasReleasedThisFrame(b.Mouse)) return true;
                    continue;
                }
                if (b.Legacy != KeyCode.None &&
                    KeyboardInputManager.WasKeyReleasedThisFrame(b.Key, b.Legacy)) return true;
            }
            return false;
        }

        /// <summary>
        /// Reads a 2DVector composite off the LEGACY backend, using the action's own part
        /// names to decide which way each control points.
        ///
        /// <para>This is the shape <c>ReadInput</c> used to hardcode: eight
        /// <c>KeyboardInputManager.IsKeyPressed(Key.A, KeyCode.A)</c>-style literals listing
        /// WASD and the arrow keys, which meant rebinding Move moved the InputSystem half and
        /// left the fallback walking the player with the old keys. Returns
        /// <see cref="Vector2.zero"/> when nothing is held, which every caller already treats
        /// as "the new backend's answer stands".</para>
        /// </summary>
        public static Vector2 ReadVectorFallback(InputAction action)
        {
            if (action == null) return Vector2.zero;

            float x = 0f, y = 0f;
            var all = Resolve(action);
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (!b.IsCompositePart || b.Legacy == KeyCode.None) continue;
                if (!KeyboardInputManager.IsKeyPressed(b.Key, b.Legacy)) continue;

                // Composite part names are the InputSystem's own and are fixed by the
                // composite type, not by the author — a 2DVector always names them
                // up/down/left/right.
                switch (b.Part)
                {
                    case "up":    y += 1f; break;
                    case "down":  y -= 1f; break;
                    case "left":  x -= 1f; break;
                    case "right": x += 1f; break;
                }
            }

            var v = new Vector2(x, y);
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        // ── Mouse plumbing ───────────────────────────────────────────────────

        private static bool MouseIsPressed(MouseControl control) => control switch
        {
            MouseControl.Left   => MouseInputManager.IsLeftMouseButtonPressed(),
            MouseControl.Right  => MouseInputManager.IsRightMouseButtonPressed(),
            MouseControl.Middle => MouseInputManager.IsMiddleMouseButtonPressed(),
            _                   => false,
        };

        private static bool MouseWasPressedThisFrame(MouseControl control) => control switch
        {
            MouseControl.Left      => MouseInputManager.WasLeftMouseButtonPressedThisFrame(),
            MouseControl.Right     => MouseInputManager.WasRightMouseButtonPressedThisFrame(),
            MouseControl.Middle    => MouseInputManager.WasMiddleMouseButtonPressedThisFrame(),
            MouseControl.WheelUp   => MouseInputManager.GetMouseWheelDelta() > 0f,
            MouseControl.WheelDown => MouseInputManager.GetMouseWheelDelta() < 0f,
            _                      => false,
        };

        private static bool MouseWasReleasedThisFrame(MouseControl control) => control switch
        {
            MouseControl.Left   => MouseInputManager.WasLeftMouseButtonReleasedThisFrame(),
            MouseControl.Right  => MouseInputManager.WasRightMouseButtonReleasedThisFrame(),
            MouseControl.Middle => MouseInputManager.WasMiddleMouseButtonReleasedThisFrame(),
            _                   => false,
        };

        /// <summary>Domain Reload is OFF: a cache keyed by <see cref="InputAction"/> would
        /// otherwise carry references to the previous session's zombie actions.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => _cache.Clear();

        public static void ResetForTests() => _cache.Clear();
    }
}
