using System.Collections.Generic;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.UIKit
{
    /// <summary>Physical keyboard shape. The two differ in the row-4 extra key and the width
    /// of Enter / left shift, which is enough to make one look wrong to a user of the
    /// other.</summary>
    public enum KeyboardLayoutKind
    {
        /// <summary>105-key ISO — the extra key left of Z, tall Enter drawn wide.</summary>
        Iso = 0,
        /// <summary>104-key ANSI — no extra key, wide Enter, wide left shift.</summary>
        Ansi = 1,
    }

    /// <summary>One drawn key cap.</summary>
    public readonly struct KeyCapSpec
    {
        /// <summary>Unity control name under <c>&lt;Keyboard&gt;/</c>. Empty means a SPACER —
        /// a gap between blocks, not a key.</summary>
        public readonly string ControlName;

        /// <summary>Width in cap units. 1 is a letter key.</summary>
        public readonly float Width;

        public KeyCapSpec(string controlName, float width = 1f)
        {
            ControlName = controlName ?? "";
            Width = width;
        }

        public bool IsSpacer => string.IsNullOrEmpty(ControlName);

        public static KeyCapSpec Gap(float width = 0.5f) => new KeyCapSpec("", width);
    }

    /// <summary>A horizontal run of caps. Rows are stacked top to bottom inside a block.</summary>
    public sealed class KeyboardRowSpec
    {
        public readonly KeyCapSpec[] Caps;
        public KeyboardRowSpec(params KeyCapSpec[] caps) { Caps = caps; }

        /// <summary>Total width in cap units, spacers included.</summary>
        public float Units
        {
            get
            {
                float w = 0f;
                for (int i = 0; i < Caps.Length; i++) w += Caps[i].Width;
                return w;
            }
        }
    }

    /// <summary>The main block, the navigation cluster and the numpad are laid out side by
    /// side, each with its own row widths.</summary>
    public sealed class KeyboardBlockSpec
    {
        public readonly string Name;
        public readonly KeyboardRowSpec[] Rows;
        public KeyboardBlockSpec(string name, params KeyboardRowSpec[] rows)
        {
            Name = name; Rows = rows;
        }

        public float Units
        {
            get
            {
                float w = 0f;
                for (int i = 0; i < Rows.Length; i++) if (Rows[i].Units > w) w = Rows[i].Units;
                return w;
            }
        }
    }

    /// <summary>
    /// The shape of a keyboard, as data.
    ///
    /// <para>WHAT IT DELIBERATELY DOES NOT CONTAIN IS THE LEGENDS. A Spanish ISO board prints
    /// <c>ñ</c> where a US board prints <c>;</c>, and both are
    /// <c>&lt;Keyboard&gt;/semicolon</c> to Unity — the InputSystem keys by PHYSICAL position.
    /// So the cap text comes from <see cref="InputControl.displayName"/>, which asks the OS
    /// what this machine's layout actually prints, and falls back to
    /// <see cref="InputControlPaths"/>'s label only when there is no keyboard device (EditMode
    /// tests, a boot-time race). Hardcoding a Spanish legend table would be a guess that is
    /// wrong for every other layout and cannot be checked from here.</para>
    ///
    /// <para>Tall keys — the ISO Enter spanning two rows, the numpad's Plus and Enter — are
    /// drawn as ordinary-height WIDE keys instead. Two-row caps buy a photograph and cost a
    /// second layout pass with its own row-alignment bugs, and every key is still present
    /// exactly once either way.</para>
    /// </summary>
    public static class KeyboardLayoutModel
    {
        private static KeyCapSpec K(string control, float width = 1f) => new KeyCapSpec(control, width);
        private static KeyCapSpec Gap(float w = 0.5f) => KeyCapSpec.Gap(w);

        /// <summary>The three blocks, left to right.</summary>
        public static KeyboardBlockSpec[] Build(KeyboardLayoutKind kind)
        {
            return new[] { BuildMain(kind), BuildNavigation(), BuildNumpad() };
        }

        private static KeyboardBlockSpec BuildMain(KeyboardLayoutKind kind)
        {
            bool iso = kind == KeyboardLayoutKind.Iso;

            var function = new KeyboardRowSpec(
                K("escape"), Gap(1f),
                K("f1"), K("f2"), K("f3"), K("f4"), Gap(0.5f),
                K("f5"), K("f6"), K("f7"), K("f8"), Gap(0.5f),
                K("f9"), K("f10"), K("f11"), K("f12"));

            var digits = new KeyboardRowSpec(
                K("backquote"),
                K("1"), K("2"), K("3"), K("4"), K("5"),
                K("6"), K("7"), K("8"), K("9"), K("0"),
                K("minus"), K("equals"), K("backspace", 2f));

            var upper = new KeyboardRowSpec(
                K("tab", 1.5f),
                K("q"), K("w"), K("e"), K("r"), K("t"),
                K("y"), K("u"), K("i"), K("o"), K("p"),
                K("leftBracket"), K("rightBracket"), Gap(1.5f));

            // ISO puts backslash on the home row and gives Enter the leftover width; ANSI puts
            // backslash on the digit row's right end. Modelled the ISO way for both and the
            // width absorbs the difference, so no key is missing on either board.
            var home = new KeyboardRowSpec(
                K("capsLock", 1.75f),
                K("a"), K("s"), K("d"), K("f"), K("g"),
                K("h"), K("j"), K("k"), K("l"),
                K("semicolon"), K("quote"), K("backslash"),
                K("enter", iso ? 1.25f : 2.25f));

            var lower = iso
                ? new KeyboardRowSpec(
                    K("leftShift", 1.25f), K("OEM1"),
                    K("z"), K("x"), K("c"), K("v"), K("b"), K("n"), K("m"),
                    K("comma"), K("period"), K("slash"), K("rightShift", 2.75f))
                : new KeyboardRowSpec(
                    K("leftShift", 2.25f),
                    K("z"), K("x"), K("c"), K("v"), K("b"), K("n"), K("m"),
                    K("comma"), K("period"), K("slash"), K("rightShift", 2.75f));

            var bottom = new KeyboardRowSpec(
                K("leftCtrl", 1.25f), K("leftMeta", 1.25f), K("leftAlt", 1.25f),
                K("space", 6.25f),
                K("rightAlt", 1.25f), K("rightMeta", 1.25f),
                K("contextMenu", 1.25f), K("rightCtrl", 1.25f));

            return new KeyboardBlockSpec("main", function, digits, upper, home, lower, bottom);
        }

        private static KeyboardBlockSpec BuildNavigation()
        {
            return new KeyboardBlockSpec("nav",
                new KeyboardRowSpec(K("printScreen"), K("scrollLock"), K("pause")),
                new KeyboardRowSpec(K("insert"), K("home"), K("pageUp")),
                new KeyboardRowSpec(K("delete"), K("end"), K("pageDown")),
                new KeyboardRowSpec(Gap(1f), Gap(1f), Gap(1f)),
                new KeyboardRowSpec(Gap(1f), K("upArrow"), Gap(1f)),
                new KeyboardRowSpec(K("leftArrow"), K("downArrow"), K("rightArrow")));
        }

        private static KeyboardBlockSpec BuildNumpad()
        {
            return new KeyboardBlockSpec("numpad",
                new KeyboardRowSpec(Gap(1f), Gap(1f), Gap(1f), Gap(1f)),
                new KeyboardRowSpec(K("numLock"), K("numpadDivide"), K("numpadMultiply"), K("numpadMinus")),
                new KeyboardRowSpec(K("numpad7"), K("numpad8"), K("numpad9"), K("numpadPlus")),
                new KeyboardRowSpec(K("numpad4"), K("numpad5"), K("numpad6"), K("numpadEquals")),
                new KeyboardRowSpec(K("numpad1"), K("numpad2"), K("numpad3"), K("numpadEnter")),
                new KeyboardRowSpec(K("numpad0", 2f), K("numpadPeriod"), Gap(1f)));
        }

        /// <summary>
        /// What to print on a cap. Asks the live keyboard device first, so a Spanish ISO board
        /// shows <c>ñ</c> on the key Unity calls <c>semicolon</c>; falls back to the project's
        /// own label table when there is no device.
        /// </summary>
        public static string CapLabel(string controlName)
        {
            if (string.IsNullOrEmpty(controlName)) return "";

            var kb = Keyboard.current;
            if (kb != null)
            {
                var control = kb.TryGetChildControl(controlName);
                if (control != null && !string.IsNullOrEmpty(control.displayName))
                    return control.displayName;
            }

            return InputControlPaths.TryResolveControlName(controlName, out var e)
                ? e.Label
                : controlName;
        }

        /// <summary>Every control name the layout draws, for the audit that asks whether the
        /// drawn board can reach every key the project binds.</summary>
        public static IEnumerable<string> ControlNames(KeyboardLayoutKind kind)
        {
            foreach (var block in Build(kind))
                foreach (var row in block.Rows)
                    foreach (var cap in row.Caps)
                        if (!cap.IsSpacer) yield return cap.ControlName;
        }
    }
}
