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
        public static bool UndoPressed() => WithCtrl("Undo");

        /// <summary>Ctrl+Y.</summary>
        public static bool RedoPressed() => WithCtrl("Redo");

        /// <summary>Ctrl+S.</summary>
        public static bool SavePressed() => WithCtrl("Save");

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
            var descriptor = InputActionCatalog.Find(map, action);
            if (!InputContextPolicy.IsLive(descriptor)) return false;
            return InputBindingResolver.WasPerformedThisFrame(Resolve(map, action));
        }

        /// <summary>The held form of <see cref="Tool"/>.</summary>
        public static bool ToolHeld(string map, string action)
        {
            var descriptor = InputActionCatalog.Find(map, action);
            if (!InputContextPolicy.IsLive(descriptor)) return false;
            return InputBindingResolver.IsPressed(Resolve(map, action));
        }

        // ── Plumbing ─────────────────────────────────────────────────────────

        private static InputAction Resolve(string map, string action)
        {
            var asset = InputService.Instance?.Asset;
            var m = asset?.FindActionMap(map, throwIfNotFound: false);
            return m?.FindAction(action, throwIfNotFound: false);
        }

        private static bool Live(string action)
        {
            var descriptor = InputActionCatalog.Find(InputActionCatalog.MapEditorShared, action);
            return InputContextPolicy.IsLive(descriptor);
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

        /// <summary>
        /// A shortcut whose key is data and whose Ctrl is not. Ten editors read
        /// <c>IsCtrlHeld()</c> for gestures that are not shortcuts, so the modifier is asked
        /// separately rather than folded into a composite binding nobody could then reuse.
        /// </summary>
        private static bool WithCtrl(string action) =>
            KeyboardInputManager.IsCtrlHeld() && Shared(action);
    }
}
