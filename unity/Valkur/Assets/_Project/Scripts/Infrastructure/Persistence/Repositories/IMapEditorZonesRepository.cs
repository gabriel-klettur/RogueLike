using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for <c>persistentDataPath/map_editor_zones.json</c>
    /// (the F11 map editor's per-user zone manifest). Decouples
    /// <c>MapEditorManager</c> from <see cref="System.IO"/> so tests can swap
    /// in an <see cref="InMemoryMapEditorZonesRepository"/> instead of
    /// touching the user's real persistentDataPath — the same anti-pattern
    /// that caused the real-file-overwrite bug fixed earlier this session.
    ///
    /// Atomic semantics: <see cref="WriteAtomic"/> uses a tmp file +
    /// <c>File.Replace</c> with a sidecar <c>.bak</c>. Reads transparently
    /// fall back to the sidecar when the primary is missing or unparseable
    /// (see <see cref="ReadWithSidecarFallback"/>) so a crash mid-write
    /// cannot strand the user without their saved zones.
    /// </summary>
    public interface IMapEditorZonesRepository
    {
        /// <summary>True iff a primary or sidecar file exists for the world.</summary>
        bool Exists(WorldId worldId);

        /// <summary>
        /// Read the manifest JSON, preferring the primary file. Falls back
        /// transparently to the sidecar <c>.bak</c> when the primary is
        /// missing or returns content the caller's parser rejects.
        /// </summary>
        /// <param name="recoveredFromSidecar">True iff the returned string
        /// came from the sidecar (caller can log / surface a warning).</param>
        string ReadWithSidecarFallback(WorldId worldId, out bool recoveredFromSidecar);

        /// <summary>
        /// Persist the manifest JSON atomically. On systems where
        /// <c>File.Replace</c> is supported (NTFS), tmp + replace produces a
        /// fresh <c>.bak</c> from the previous primary in a single OS step.
        /// </summary>
        void WriteAtomic(WorldId worldId, string json);
    }
}
