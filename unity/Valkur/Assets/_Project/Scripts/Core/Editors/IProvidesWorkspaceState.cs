using UnityEngine;

namespace Valkur.Core.Editors
{
    /// <summary>
    /// Implemented by a runtime editor that has state worth remembering beyond its panel
    /// geometry — active mode, active tab, search text, hidden table columns, camera zoom,
    /// active layer, and the live selection.
    ///
    /// OPTIONAL on purpose. Panel layout is captured generically off <c>DraggablePanel</c>
    /// and costs an editor nothing; only the editor-specific half needs an implementation.
    /// Push work into the generic half wherever the choice exists — anything captured
    /// generically is free for all sixteen editors, anything here is paid sixteen times.
    /// </summary>
    public interface IProvidesWorkspaceState
    {
        /// <summary>
        /// Root of this editor's UI hierarchy — the object whose children the service walks
        /// to find <c>DraggablePanel</c>s.
        ///
        /// It lives on this optional interface rather than on
        /// <see cref="GameEditorManager.IGameEditor"/> so adopting the layer stays opt-in:
        /// adding a member to IGameEditor would break all sixteen editors at once, and the
        /// roadmap's Phase 1 ships the layer without touching any of them.
        ///
        /// May be null before the editor has lazily built its UI; the service tolerates that
        /// and retries on the next open.
        /// </summary>
        Transform WorkspaceRoot { get; }

        /// <summary>
        /// Write this editor's own state into the workspace. Called as the editor closes,
        /// AFTER panel geometry has been captured.
        /// </summary>
        void CaptureWorkspace(EditorWorkspace workspace);

        /// <summary>
        /// Read this editor's own state back. Called as the editor opens, AFTER panel
        /// geometry has been applied.
        ///
        /// Must tolerate every value being absent or stale: validate each against its own
        /// live domain (does that category still exist? is that layer index still in
        /// range?) and fall back to this editor's default, silently.
        /// </summary>
        void RestoreWorkspace(EditorWorkspace workspace);
    }
}
