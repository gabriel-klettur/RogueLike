namespace Valkur.Core.Editors
{
    /// <summary>
    /// The single owner of "put the editor back the way it was".
    ///
    /// Reached from <see cref="GameEditorManager"/> — the one seam every editor open and
    /// close already passes through. ONE hook, not sixteen: a second call site anywhere
    /// else is how this layer would start disagreeing with itself.
    ///
    /// Core may reference nothing, so the manager resolves this through
    /// <see cref="ServiceLocator"/> and no-ops when it is absent — which keeps the manager
    /// working in every test that never installs the layer.
    /// </summary>
    public interface IEditorWorkspaceService
    {
        /// <summary>
        /// The editor is opening. Restores panel geometry, visibility and editor state.
        ///
        /// Restoration is DEFERRED a frame: editors build their UI lazily on first Activate,
        /// and <c>DraggablePanel</c> normalizes its anchors one frame after enable. Applying
        /// geometry before either has happened writes onto a rect that is about to be
        /// overwritten. Safe to call for an editor that does not implement
        /// <see cref="IProvidesWorkspaceState"/> — it simply has nothing to restore yet.
        /// </summary>
        void RestoreOnOpen(GameEditorManager.IGameEditor editor);

        /// <summary>
        /// The editor is closing. Captures panel geometry and editor state, then persists.
        /// Runs synchronously — the panels are still alive at this point and will be
        /// <c>SetActive(false)</c> immediately after.
        /// </summary>
        void CaptureOnClose(GameEditorManager.IGameEditor editor);

        /// <summary>
        /// Forgets one editor's workspace and returns its panels to their default docks.
        /// The escape hatch for an author whose layout ended up somewhere unusable.
        /// </summary>
        void ResetWorkspace(GameEditorManager.IGameEditor editor);
    }
}
