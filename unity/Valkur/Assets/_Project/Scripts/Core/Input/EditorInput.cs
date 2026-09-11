using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// The verbs every runtime editor shares, as one API.
    ///
    /// <para>WHY IT EXISTS. Undo, redo, save, close, delete, select, pan and zoom mean the
    /// same thing in all sixteen editors and must therefore behave identically in all of
    /// them — and before this they were 85 raw <see cref="KeyboardInputManager"/> /
    /// <see cref="MouseInputManager"/> calls spread over 48 files. "The same everywhere" was a
    /// convention maintained by hand, drift was invisible, and none of it could be
    /// reconfigured because the keys were literals. Each verb is one action now, in the
    /// <c>EditorShared</c> map, bound once.</para>
    ///
    /// <para>EVERY READ IS CONTEXT-GATED. A shared verb answers only while an editor actually
    /// owns input — <see cref="InputContexts.Current"/> resolving to an editor — so Ctrl+Z
    /// during play cannot reach an editor that merely exists in the scene. An editor's OWN
    /// tools go through <see cref="Tool"/> instead, which additionally checks that the tool
    /// belongs to the editor that is open.</para>
    ///
    /// <para>Ctrl and Shift are still read as HELD MODIFIERS through
    /// <see cref="KeyboardInputManager"/> rather than baked into these bindings. A composite
    /// with a modifier is expressible, but ten editors read <c>IsCtrlHeld()</c> as a state for
    /// things that are not shortcuts at all (Ctrl-drag, Ctrl-click), so the modifier stays a
    /// separate question from the key.</para>
    /// </summary>
    public static class EditorInput
    {
        /// <summary>True while a runtime editor owns input. Every read below is already gated
        /// on it; exposed because editors ask the same question for their own reasons.</summary>
        public static bool AnyEditorActive => InputContexts.ActiveEditor != null;

        // ── Shared verbs ─────────────────────────────────────────────────────

        /// <summary>Ctrl+Z. The Ctrl half is a held modifier, not part of the binding.</summary>
        public static bool UndoPressed() => Shared("Undo");

        /// <summary>Ctrl+Y.</summary>
        public static bool RedoPressed() => Shared("Redo");

        /// <summary>Ctrl+S.</summary>
        public static bool SavePressed() => Shared("Save");

        /// <summary>The editor's own close. Escape by default, and it stays on
        /// <see cref="InputBlocker"/>'s always-allowed list whatever it is bound to.</summary>
        public static bool ClosePressed() => Shared("Close");

        public static bool DeletePressed() => Shared("Delete");

        public static bool SelectPressed()  => SharedPressed("Select");
        public static bool SelectHeld()     => SharedHeld("Select");
        public static bool SelectReleased() => SharedReleased("Select");

        public static bool PanHeld()        => SharedHeld("PanDrag");
        public static bool PanPressed()     => SharedPressed("PanDrag");
        public static bool PanReleased()    => SharedReleased("PanDrag");

        public static bool ZoomInPressed()  => Shared("ZoomIn");
        public static bool ZoomOutPressed() => Shared("ZoomOut");

        /// <summary>Show/hide the outlines of everything this editor has placed. Shared
        /// because Particles and Spawners had the same verb on the same key, written twice.
        /// A one-shot toggle, not a held modifier.</summary>
        public static bool ToggleOutlinesPressed() => Shared("ToggleOutlines");

        // ── One editor's own tool ────────────────────────────────────────────

        /// <summary>
        /// Was this editor's own tool triggered this frame? Answers false unless THAT editor
        /// is the one currently open, which is what keeps one editor's tools out of another's
        /// keyboard even when the two share a key — and sharing is expected, because each
        /// editor gets the whole board to itself.
        /// </summary>
        public static bool Tool(string map, string action)
        {
            if (!ToolLive(map, action)) return false;
            return InputBindingResolver.WasPerformedThisFrame(Resolve(map, action));
        }

        /// <summary>The held form of <see cref="Tool"/>.</summary>
        public static bool ToolHeld(string map, string action)
        {
            if (!ToolLive(map, action)) return false;
            return InputBindingResolver.IsPressed(Resolve(map, action));
        }

        /// <summary>
        /// Is this editor's own tool answerable right now — the right editor open, and the
        /// right modifier state?
        ///
        /// <para>THE MODIFIER IS MATCHED, NOT REFUSED. Every shared shortcut in this project is
        /// Ctrl+key with the Ctrl living in C# rather than in the binding, so a bare key and a
        /// Ctrl+key were competing for the same press. Measured on the shipped asset:
        /// <c>EditorShared/Save</c> and <c>Editor.Tile/ToolSelect</c> are both on <c>s</c>, so
        /// Ctrl+S in the Tile editor saved the map AND switched the active tool to Select,
        /// every time, in silence.</para>
        ///
        /// <para>This used to be a blanket <c>if (IsCtrlHeld()) return false;</c>, which
        /// separates the two presses and ALSO makes a Ctrl tool unreachable — so the Tile
        /// editor's Ctrl+C / Ctrl+X / Ctrl+V, which are read as
        /// <c>ctrl &amp;&amp; EditorInput.Tool(...)</c>, could not fire at any time. Three
        /// buttons in the Select panel did the same job, so the dead half looked like a
        /// preference rather than a defect. It reads the descriptor's own
        /// <see cref="InputActionDescriptor.RequiresCtrl"/> now — the same field
        /// <see cref="Live"/> reads for the shared verbs and
        /// <see cref="InputConflictScanner"/> reads to decide a shared key is not a double
        /// fire, so the three cannot disagree about which press a tool wants.</para>
        /// </summary>
        private static bool ToolLive(string map, string action)
        {
            var descriptor = InputActionCatalog.Find(map, action);
            if (!InputContextPolicy.IsLive(descriptor)) return false;
            return descriptor.RequiresCtrl == KeyboardInputManager.IsCtrlHeld();
        }

        // ── Plumbing ─────────────────────────────────────────────────────────

        private static InputAction Resolve(string map, string action)
        {
            var asset = InputService.Instance?.Asset;
            var m = asset?.FindActionMap(map, throwIfNotFound: false);
            return m?.FindAction(action, throwIfNotFound: false);
        }

        /// <summary>
        /// Is this shared verb answerable right now — the right context, and the right
        /// modifier state?
        ///
        /// <para>The Ctrl half comes from the DESCRIPTOR rather than from a list here. It used
        /// to be a private <c>WithCtrl</c> wrapper naming undo, redo and save, which meant the
        /// conflict scanner had no way to know that <c>EditorShared/Save</c> and
        /// <c>Editor.Tile/ToolSelect</c> — both on <c>s</c> — are told apart by a modifier.
        /// One fact, one place.</para>
        /// </summary>
        private static bool Live(string action)
        {
            var descriptor = InputActionCatalog.Find(InputActionCatalog.MapEditorShared, action);
            if (!InputContextPolicy.IsLive(descriptor)) return false;
            return descriptor.RequiresCtrl == KeyboardInputManager.IsCtrlHeld();
        }

        private static bool Shared(string action) =>
            Live(action) &&
            InputBindingResolver.WasPerformedThisFrame(
                Resolve(InputActionCatalog.MapEditorShared, action));

        private static bool SharedPressed(string action) => Shared(action);

        private static bool SharedHeld(string action) =>
            Live(action) &&
            InputBindingResolver.IsPressed(Resolve(InputActionCatalog.MapEditorShared, action));

        private static bool SharedReleased(string action) =>
            Live(action) &&
            InputBindingResolver.WasReleasedThisFrame(
                Resolve(InputActionCatalog.MapEditorShared, action));

    }
}
