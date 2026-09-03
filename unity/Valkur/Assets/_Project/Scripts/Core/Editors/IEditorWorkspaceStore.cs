namespace Valkur.Core.Editors
{
    /// <summary>
    /// Reads and writes one <see cref="EditorWorkspace"/> per editor.
    ///
    /// Deliberately NOT PlayerPrefs. On Windows that is the registry: no schema version, no
    /// backup, no atomic write, and a practical size cap per entry. This project already has
    /// the good pattern (atomic write + rotating backups) and already keeps per-machine user
    /// state — saves, profile.json — under <c>Application.persistentDataPath</c>. A panel
    /// layout is a personal preference of one machine, not project data, so it stays out of
    /// git and out of everyone else's diffs.
    /// </summary>
    public interface IEditorWorkspaceStore
    {
        /// <summary>
        /// The stored workspace for <paramref name="editorName"/>, or null when there is
        /// none, the file is unreadable, or its schema version is unknown to this build.
        /// Never throws and never returns a partially-read document.
        /// </summary>
        EditorWorkspace Load(string editorName);

        /// <summary>Persists the workspace. A failed write is reported, never thrown.</summary>
        void Save(EditorWorkspace workspace);

        /// <summary>Drops the stored workspace — the "reset layout" action, and test teardown.</summary>
        void Delete(string editorName);
    }
}
