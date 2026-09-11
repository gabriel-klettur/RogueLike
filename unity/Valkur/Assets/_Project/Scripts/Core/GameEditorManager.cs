using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Core
{
    /// <summary>
    /// Central editor exclusivity manager. Opening one runtime editor closes all others.
    /// Mirrors Python's open_editor_exclusive() / close_all_editors() from editors_common.py.
    /// </summary>
    public class GameEditorManager : SingletonMonoBehaviour<GameEditorManager>
    {
        public interface IGameEditor
        {
            string EditorName { get; }
            bool IsActive { get; }
            void Activate();
            void Deactivate();
        }

        /// <summary>
        /// Fired whenever any editor opens (true) or all editors close (false).
        /// Subscribers in Valkur.UI can react without a Gameplay dependency.
        /// </summary>
        public static event Action<bool> OnEditorStateChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEditorStateEvent()
        {
            OnEditorStateChanged = null;
        }

        private readonly List<IGameEditor> _registered = new List<IGameEditor>();
        private IGameEditor _activeEditor;

        public IGameEditor ActiveEditor => _activeEditor;
        public bool AnyEditorActive => _activeEditor != null;

        /// <summary>
        /// Every editor that has called <see cref="Register"/> and not yet <see cref="Unregister"/>.
        /// Read-only view over the exclusivity roster itself (not merely the active one) —
        /// e.g. a launcher panel that wants to grey out entries for editors not yet booted,
        /// or a diagnostic that wants to assert exactly N editors registered this boot.
        /// </summary>
        public IReadOnlyList<IGameEditor> RegisteredEditors => _registered;

        /// <summary>
        /// Returns the existing instance, or creates a new GameObject hosting one if missing.
        /// Use this from any editor's Awake/Start to guarantee the manager exists at runtime.
        /// </summary>
        public static GameEditorManager EnsureInstance()
        {
            if (HasInstance) return Instance;
            var go = new GameObject("[GameEditorManager]");
            return go.AddComponent<GameEditorManager>();
        }

        public void Register(IGameEditor editor)
        {
            if (editor == null || _registered.Contains(editor)) return;
            _registered.Add(editor);
        }

        public void Unregister(IGameEditor editor)
        {
            if (editor == null) return;
            if (_activeEditor == editor) _activeEditor = null;
            // A destroyed editor cannot be returned to, and a dangling pointer to one would be
            // reopened as a MissingReference the next time Escape was pressed.
            if (ReferenceEquals(_returnTo, editor)) _returnTo = null;
            _registered.Remove(editor);
        }

        /// <summary>
        /// Where Escape should go back to, once, instead of opening the launcher.
        ///
        /// <para>It exists because an editor can now be opened FROM another editor — the
        /// Selection tool double-clicks a building and lands in the Buildings editor — and the
        /// author expects Escape to undo that step rather than to dump them at the launcher
        /// with their selection stranded behind it.</para>
        ///
        /// <para>IT IS CONSUMED ONCE and armed only by the overload that takes it, so a stale
        /// target cannot exist: any other <see cref="OpenExclusive(IGameEditor)"/> clears it.
        /// A return pointer that survived an unrelated editor change would send Escape
        /// somewhere surprising minutes later, which is worse than not having one.</para>
        /// </summary>
        private IGameEditor _returnTo;

        /// <summary>The armed return target, or null. Read by the launcher and the tests.</summary>
        public IGameEditor ReturnTarget => _returnTo;

        /// <summary>
        /// Opens the target editor exclusively — closes any other active editor first.
        ///
        /// <para>Clears any armed return target. Opening an editor by any route other than the
        /// two-argument overload means the author is somewhere new on purpose, and the old
        /// "go back to" no longer describes anywhere they have been.</para>
        /// </summary>
        public void OpenExclusive(IGameEditor target)
        {
            _returnTo = null;
            OpenExclusiveInternal(target);
        }

        /// <summary>
        /// Open <paramref name="target"/> and remember that Escape should return to
        /// <paramref name="returnTo"/> rather than open the launcher.
        ///
        /// <para>THE TARGET IS ARMED AFTER THE SWITCH, and the order is the whole of why this
        /// works. Opening tears down the previous editor, whose <c>Deactivate</c> calls
        /// <see cref="NotifyDeactivated"/> — which clears the trail, correctly, because an
        /// editor closing itself has ended one. Arming first means arming a pointer that the
        /// very next line erases, and the feature would simply never fire. Same shape as the
        /// shield having to <c>Track</c> before it claims invincibility.</para>
        /// </summary>
        public void OpenExclusive(IGameEditor target, IGameEditor returnTo)
        {
            if (target == null) return;
            OpenExclusiveInternal(target);
            // Returning to the editor just opened would make Escape a no-op that reads as a
            // frozen key, so that pairing is refused rather than armed.
            _returnTo = ReferenceEquals(target, returnTo) ? null : returnTo;
        }

        /// <summary>
        /// Take the armed return target, if there is one. Consumed: Escape goes back exactly
        /// once, and a second press behaves as it always did.
        /// </summary>
        public bool TryConsumeReturnTarget(out IGameEditor back)
        {
            back = _returnTo;
            _returnTo = null;
            // A target unregistered while it was armed (a scene teardown) must not be reopened.
            if (back != null && !_registered.Contains(back)) back = null;
            return back != null;
        }

        private void OpenExclusiveInternal(IGameEditor target)
        {
            if (target == null) return;

            if (_activeEditor != null && _activeEditor != target)
            {
                CaptureWorkspace(_activeEditor);
                _activeEditor.Deactivate();
            }

            _activeEditor = target;
            target.Activate();
            RestoreWorkspace(target);
            OnEditorStateChanged?.Invoke(true);
        }

        /// <summary>
        /// Toggle the target editor: if active, close it; otherwise open exclusively.
        /// </summary>
        public void ToggleExclusive(IGameEditor target)
        {
            if (target == null) return;

            if (_activeEditor == target && target.IsActive)
            {
                CaptureWorkspace(target);
                target.Deactivate();
                _activeEditor = null;
                _returnTo = null;   // closing to gameplay ends the trail
                OnEditorStateChanged?.Invoke(false);
            }
            else
            {
                OpenExclusive(target);
            }
        }

        /// <summary>
        /// Close all editors.
        /// </summary>
        public void CloseAll()
        {
            _returnTo = null;
            if (_activeEditor != null)
            {
                CaptureWorkspace(_activeEditor);
                _activeEditor.Deactivate();
                _activeEditor = null;
                OnEditorStateChanged?.Invoke(false);
            }
        }

        public void NotifyDeactivated(IGameEditor editor)
        {
            if (_activeEditor == editor)
            {
                // An editor that closed itself reaches the manager only here, so this is
                // the capture point for that path. Deduplicated against the pre-Deactivate
                // capture — see CaptureWorkspace.
                CaptureWorkspace(editor);
                _activeEditor = null;
                _returnTo = null;   // an editor that closed itself ends the trail too
                OnEditorStateChanged?.Invoke(false);
            }
        }

        // ── Workspace persistence ───────────────────────────────────────────────
        //
        // This manager is the ONE seam every editor open and close already passes through,
        // which is why the workspace layer hooks here and nowhere else. Core may reference
        // nothing, so the service is resolved through ServiceLocator and every call no-ops
        // when it is absent — that is what keeps this manager working in the many tests
        // that never install the layer.

        /// <summary>
        /// The editor whose workspace was captured for the close currently in progress.
        ///
        /// A close reaches this manager along two paths — the caller capturing before it
        /// calls <c>Deactivate</c>, and the editor itself calling
        /// <see cref="NotifyDeactivated"/> after it already deactivated — and BOTH fire for
        /// an editor that closes itself. A second capture is not harmless: an editor is
        /// free to clear its own transient state in Deactivate, and the Tile Editor does
        /// exactly that (<c>_state.SelectedCellPos = null</c>). Capturing again afterwards
        /// writes that null over the selection the first pass had correctly recorded.
        /// </summary>
        private IGameEditor _capturedOnClose;

        private void RestoreWorkspace(IGameEditor editor)
        {
            if (editor == null) return;

            // Opening the same editor again re-arms its capture.
            if (ReferenceEquals(_capturedOnClose, editor)) _capturedOnClose = null;

            ServiceLocator.Get<IEditorWorkspaceService>()?.RestoreOnOpen(editor);
        }

        private void CaptureWorkspace(IGameEditor editor)
        {
            if (editor == null) return;
            if (ReferenceEquals(_capturedOnClose, editor)) return;

            _capturedOnClose = editor;
            ServiceLocator.Get<IEditorWorkspaceService>()?.CaptureOnClose(editor);
        }
    }
}
