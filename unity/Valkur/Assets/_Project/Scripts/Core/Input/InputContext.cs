using System;
using UnityEngine;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Which contexts an action is live in.
    ///
    /// <para>This replaced a War/Peace-only mask. The two play postures are not the whole
    /// story: while a runtime editor is open the postures do not apply AT ALL — the editor
    /// owns the keyboard and the mouse, and gameplay input is frozen by
    /// <c>IsGameplayInputSuspended</c>. So an editor is a context in its own right, not a
    /// third stance.</para>
    /// </summary>
    [Flags]
    public enum InputContextMask
    {
        None = 0,

        /// <summary>Live while playing, in the War posture.</summary>
        War = 1 << 0,

        /// <summary>Live while playing, in the Peace posture. Refused for anything that
        /// reaches the damage path — see <see cref="InputContextPolicy"/>.</summary>
        Peace = 1 << 1,

        /// <summary>
        /// Live inside a runtime editor. On its own it means EVERY editor — the shared verbs
        /// (undo, redo, save, close, delete, select, drag-select, zoom, pan). Paired with a
        /// descriptor's <see cref="InputActionDescriptor.OwnerEditor"/> it means that editor
        /// only, which is where each editor's own tools live.
        /// </summary>
        Editors = 1 << 2,

        Gameplay   = War | Peace,
        Everywhere = War | Peace | Editors,
    }

    /// <summary>
    /// The vocabulary of context ids, and the single answer to "which one is live right now".
    ///
    /// <para>WHY A STRING ID. Editors register themselves at runtime and are named by
    /// <c>GameEditorManager.IGameEditor.EditorName</c>; an enum would have to be edited once
    /// per editor, and CLAUDE.md already records what that positional tax costs when
    /// <c>AnimState</c> pays it four times over. A string keyed the same way the editor
    /// registry keys itself cannot drift from it.</para>
    ///
    /// <para>WHY THE ACTIVE EDITOR IS QUERIED RATHER THAN PUSHED. <c>GameEditorManager</c>
    /// also lives in <c>Valkur.Core</c>, and it writes <c>_activeEditor</c> from six places —
    /// open, close, unregister, close-all, deactivate-notify. Pushing from all six is the
    /// shape that drifts the first time somebody adds a seventh, and the failure would be
    /// silent: input would answer for an editor that is no longer open. Reading the one field
    /// that already IS the answer cannot drift. <see cref="SetActiveEditorOverride"/> exists
    /// for tests, which have no manager.</para>
    /// </summary>
    public static class InputContexts
    {
        public const string War   = "gameplay/war";
        public const string Peace = "gameplay/peace";

        /// <summary>Prefix for an editor context. <c>editor/Tile</c>, <c>editor/Buildings</c>.</summary>
        public const string EditorPrefix = "editor/";

        /// <summary>
        /// "Any editor" — the context that carries the SHARED verbs and no editor's own tools.
        ///
        /// <para>It exists because twelve of the sixteen editors own no tools at all, so a tab
        /// per editor showed twelve identical boards and buried the four that differ. This one
        /// answers "what do select, zoom, undo, save and close do inside an editor", which is
        /// the question those twelve were being asked. It needs no special case in
        /// <see cref="InputContextPolicy.IsLive"/>: <see cref="EditorNameOf"/> returns "*",
        /// which matches no <c>OwnerEditor</c>, so every owned tool falls out on its own.</para>
        ///
        /// <para>It is never <see cref="Current"/> — the live context always names the editor
        /// that is actually open. This is a VIEW, and only the Controls editor uses it.</para>
        /// </summary>
        public const string EditorsAny = EditorPrefix + "*";

        /// <summary>Test-only override. Null means "ask the manager", which is what production
        /// always does.</summary>
        private static string _activeEditorOverride;
        private static bool _hasOverride;

        /// <summary>
        /// Raised after <see cref="Current"/> changes — which, in practice, only
        /// <see cref="SetActiveEditorOverride"/> does, because <see cref="Current"/> is COMPUTED
        /// from the editor registry and the posture rather than pushed.
        ///
        /// <para>It has NO subscribers, and the comment used to claim two. Kept because the
        /// test override raises it and a future prompt overlay is the obvious caller; what it
        /// must not do is name readers it does not have. Anything that needs the live context
        /// today asks <see cref="Current"/>, which cannot go stale.</para>
        /// </summary>
        public static event Action<string> OnChanged;

        /// <summary>The context id for an editor by its <c>EditorName</c>.</summary>
        public static string ForEditor(string editorName) =>
            string.IsNullOrEmpty(editorName) ? null : EditorPrefix + editorName;

        public static bool IsEditor(string contextId) =>
            !string.IsNullOrEmpty(contextId) &&
            contextId.StartsWith(EditorPrefix, StringComparison.OrdinalIgnoreCase);

        public static bool IsGameplay(string contextId) =>
            string.Equals(contextId, War, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(contextId, Peace, StringComparison.OrdinalIgnoreCase);

        /// <summary>The editor name inside an editor context id, or null.</summary>
        public static string EditorNameOf(string contextId) =>
            IsEditor(contextId) ? contextId.Substring(EditorPrefix.Length) : null;

        /// <summary>The editor that currently owns input, or null while playing.</summary>
        public static string ActiveEditor
        {
            get
            {
                if (_hasOverride) return _activeEditorOverride;
                if (!GameEditorManager.HasInstance) return null;
                var active = GameEditorManager.Instance.ActiveEditor;
                return active?.EditorName;
            }
        }

        /// <summary>
        /// The live context. An open editor wins over the posture unconditionally — that is
        /// the whole rule the user stated and the one the runtime already obeyed before the
        /// configuration layer knew about it.
        /// </summary>
        public static string Current
        {
            get
            {
                var editor = ActiveEditor;
                return !string.IsNullOrEmpty(editor)
                    ? EditorPrefix + editor
                    : (PlayerStance.IsPeace ? Peace : War);
            }
        }

        /// <summary>
        /// Test hook: pretend an editor is open without a <c>GameEditorManager</c> in the
        /// scene. Pass null to pretend gameplay. Call <see cref="ClearActiveEditorOverride"/>
        /// in teardown — with Domain Reload off, an override left set would tell every later
        /// fixture in the session that an editor is open.
        /// </summary>
        public static void SetActiveEditorOverride(string editorName)
        {
            _hasOverride = true;
            _activeEditorOverride = string.IsNullOrEmpty(editorName) ? null : editorName;
            OnChanged?.Invoke(Current);
        }

        public static void ClearActiveEditorOverride()
        {
            _hasOverride = false;
            _activeEditorOverride = null;
        }

        /// <summary>Human label for a context, for the Controls editor's selector.</summary>
        public static string Label(string contextId)
        {
            if (string.Equals(contextId, War, StringComparison.OrdinalIgnoreCase))   return "Guerra";
            if (string.Equals(contextId, Peace, StringComparison.OrdinalIgnoreCase)) return "Paz";
            if (string.Equals(contextId, EditorsAny, StringComparison.Ordinal)) return "Editores";
            var editor = EditorNameOf(contextId);
            return editor ?? contextId ?? "";
        }

        /// <summary>The mask bit a context id belongs to.</summary>
        public static InputContextMask MaskOf(string contextId)
        {
            if (string.Equals(contextId, War, StringComparison.OrdinalIgnoreCase))   return InputContextMask.War;
            if (string.Equals(contextId, Peace, StringComparison.OrdinalIgnoreCase)) return InputContextMask.Peace;
            if (IsEditor(contextId)) return InputContextMask.Editors;
            return InputContextMask.None;
        }

        /// <summary>
        /// Domain Reload is OFF, so both the active editor and the subscriber list survive into
        /// the next Play session — the second carrying delegates that point at destroyed
        /// panels, the first claiming an editor is open in a scene that has just been loaded.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _activeEditorOverride = null;
            _hasOverride = false;
            OnChanged = null;
        }

        /// <summary>Test hook — an event cannot be cleared from outside the declaring class,
        /// the same reason <see cref="PlayerStance.ResetForTests"/> exists.</summary>
        public static void ResetForTests()
        {
            _activeEditorOverride = null;
            _hasOverride = false;
            OnChanged = null;
        }
    }
}
