using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Valkur.Core.Input
{
    /// <summary>
    /// A modifier held plus a key pressed — "Shift + 1" — as ONE bindable control.
    ///
    /// <para><b>Why it exists.</b> The War keyboard carries every spell the grimoire teaches:
    /// seventy of them, against sixty-eight free single keys of which twenty-five live on a
    /// numpad or a navigation block that a laptop does not have. The professional answer is a
    /// second layer under the same fingers, and the InputSystem already has the shape for it —
    /// the <c>OneModifier</c> composite, with a <c>modifier</c> part and a <c>binding</c> part.
    /// The asset stays the only model: a chord is data in <c>ValkurInputActions</c>, not a
    /// modifier test written in C# beside a key.</para>
    ///
    /// <para><b>Why the rest of the input layer needs to be told.</b> Every reader in this
    /// project walks composite PARTS as independent controls, which is right for a WASD
    /// 2DVector and wrong for a chord in three separate ways: the legacy OR-gate would fire the
    /// spell on the modifier alone, the conflict scanner would see thirty-five actions on
    /// <c>leftShift</c> and paint it red, and the Controls editor would offer to rebind "the
    /// Shift of Shift+1" as a slot of its own. <see cref="Slots"/> is the one walk that
    /// collapses a chord into a single slot, and every one of those readers goes through it.</para>
    ///
    /// <para><b>A chord path</b> is <c>modifierPath &amp; buttonPath</c> — the character is
    /// not legal in an InputSystem control path, so a chord path can never be mistaken for a
    /// real one. It exists for the readers that key things BY PATH (the scanner, the board, the
    /// status line); the resolver carries the two halves as separate fields.</para>
    /// </summary>
    public static class InputChord
    {
        /// <summary>The InputSystem composite a chord is authored as.</summary>
        public const string CompositeName = "OneModifier";

        public const string ModifierPart = "modifier";
        public const string ButtonPart   = "binding";

        /// <summary>Joins the two halves of a chord path. Illegal in a control path.</summary>
        public const char Separator = '&';

        /// <summary>One bindable slot of an action — a plain control, one part of a
        /// non-chord composite, or a whole chord.</summary>
        public readonly struct Slot
        {
            /// <summary>Index into the binding array of the button — the binding an override
            /// must be applied to so a rebind moves the key and keeps the modifier.</summary>
            public readonly int Index;

            /// <summary>Action the binding belongs to (map-level walks need it).</summary>
            public readonly string Action;

            /// <summary>Composite part name for a non-chord composite ("up"), else empty.</summary>
            public readonly string Part;

            /// <summary>Effective path of the key. Empty means unassigned.</summary>
            public readonly string Path;

            /// <summary>Effective path of the modifier, for a chord. Empty for everything else.</summary>
            public readonly string ModifierPath;

            public Slot(int index, string action, string part, string path, string modifierPath)
            {
                Index = index;
                Action = action ?? "";
                Part = part ?? "";
                Path = path ?? "";
                ModifierPath = modifierPath ?? "";
            }

            public bool IsChord => !string.IsNullOrEmpty(ModifierPath);
            public bool IsBound => !string.IsNullOrEmpty(Path);

            /// <summary>The path readers key by: the chord path for a chord, else the path.</summary>
            public string KeyPath => IsChord && IsBound ? Compose(ModifierPath, Path) : Path;

            public string Label => !IsBound ? "" : IsChord ? LabelFor(ModifierPath, Path)
                                                           : InputControlPaths.LabelForPath(Path);
        }

        // ── Paths ────────────────────────────────────────────────────────────

        public static string Compose(string modifierPath, string buttonPath) =>
            modifierPath + Separator + buttonPath;

        public static bool IsChordPath(string path) =>
            !string.IsNullOrEmpty(path) && path.IndexOf(Separator) >= 0;

        public static bool TrySplit(string path, out string modifierPath, out string buttonPath)
        {
            modifierPath = buttonPath = null;
            if (!IsChordPath(path)) return false;
            int i = path.IndexOf(Separator);
            modifierPath = path.Substring(0, i);
            buttonPath = path.Substring(i + 1);
            return true;
        }

        /// <summary>"Shift+1". The modifier's own table label ("Shift izq.") is written for a
        /// key cap and is too long for a chord printed on a 34 px slot, so modifiers get the
        /// short name every game uses.</summary>
        public static string LabelFor(string modifierPath, string buttonPath)
        {
            string button = InputControlPaths.LabelForPath(buttonPath);
            return ShortModifier(modifierPath) + "+" + button;
        }

        /// <summary>The compact label, for surfaces with room for three glyphs: "S1", "SQ".</summary>
        public static string CompactLabelFor(string modifierPath, string buttonPath)
        {
            string button = InputControlPaths.LabelForPath(buttonPath);
            return ShortModifier(modifierPath).Substring(0, 1) + button;
        }

        public static string ShortModifier(string modifierPath)
        {
            string name = InputControlPaths.ControlNameOf(modifierPath) ?? "";
            if (name.EndsWith("Shift", StringComparison.OrdinalIgnoreCase)) return "Shift";
            if (name.EndsWith("Ctrl", StringComparison.OrdinalIgnoreCase))  return "Ctrl";
            if (name.EndsWith("Alt", StringComparison.OrdinalIgnoreCase))   return "Alt";
            return InputControlPaths.LabelForPath(modifierPath);
        }

        public static bool IsChordComposite(InputBinding header) =>
            header.isComposite && !string.IsNullOrEmpty(header.path) &&
            header.path.StartsWith(CompositeName, StringComparison.OrdinalIgnoreCase);

        // ── The one walk ─────────────────────────────────────────────────────

        /// <summary>
        /// Every bindable slot in <paramref name="bindings"/>, in array order. A chord
        /// composite becomes ONE slot; any other composite contributes its parts; composite
        /// headers name no control and contribute nothing.
        /// </summary>
        public static void Slots(ReadOnlyArray<InputBinding> bindings, List<Slot> into)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];

                if (IsChordComposite(b))
                {
                    string modifier = "", button = "";
                    int buttonIndex = -1;
                    int j = i + 1;
                    for (; j < bindings.Count && bindings[j].isPartOfComposite; j++)
                    {
                        var part = bindings[j];
                        if (string.Equals(part.name, ModifierPart, StringComparison.OrdinalIgnoreCase))
                            modifier = part.effectivePath ?? "";
                        else if (string.Equals(part.name, ButtonPart, StringComparison.OrdinalIgnoreCase))
                        { button = part.effectivePath ?? ""; buttonIndex = j; }
                    }

                    // A chord whose modifier was cleared degrades to its bare key rather than
                    // vanishing — the key is still what the player assigned.
                    if (buttonIndex >= 0)
                        into.Add(new Slot(buttonIndex, b.action, "", button,
                                          string.IsNullOrEmpty(modifier) ? "" : modifier));
                    i = j - 1;
                    continue;
                }

                if (b.isComposite) continue;
                into.Add(new Slot(i, b.action, b.isPartOfComposite ? b.name : "", b.effectivePath, ""));
            }
        }

        public static List<Slot> Slots(InputAction action)
        {
            var list = new List<Slot>(2);
            if (action != null) Slots(action.bindings, list);
            return list;
        }
    }
}
