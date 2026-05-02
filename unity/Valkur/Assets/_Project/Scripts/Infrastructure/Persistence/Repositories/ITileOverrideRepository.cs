using System.Collections.Generic;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for per-zone tile-edit overlay files. Phase 0 of
    /// the multi-world refactor introduces this interface so:
    ///
    ///  • Tests can swap in <see cref="InMemoryTileOverrideRepository"/> and
    ///    exercise tile-editor flows without ever touching
    ///    <see cref="UnityEngine.Application.persistentDataPath"/> — the
    ///    same path the user's real save file lives in. The Move-based
    ///    test helpers that overwrote the user's file (the bug fixed
    ///    earlier in this session) become impossible by construction.
    ///  • Phase 4 (MMO) can plug in a <c>RemoteTileOverrideRepository</c>
    ///    that round-trips through an authoritative server without any of
    ///    the editor / runtime code knowing the difference.
    ///  • The <see cref="WorldId"/> on every method is the seam where a
    ///    multi-world world layout lands without churn through every
    ///    callsite — even though today it always resolves to
    ///    <see cref="WorldId.Base"/>.
    ///
    /// Method semantics intentionally mirror the legacy static API on
    /// <c>TileOverlayPersistence</c> so existing call paths can adopt this
    /// interface mechanically.
    /// </summary>
    public interface ITileOverrideRepository
    {
        /// <summary>True iff a saved overlay exists for the given zone in the given world.</summary>
        bool Exists(WorldId worldId, string zoneName);

        /// <summary>Read the raw overlay JSON. Returns null when the file is missing.</summary>
        string Read(WorldId worldId, string zoneName);

        /// <summary>Persist the given overlay JSON for the given zone. Implementations
        /// are expected to write atomically (tmp + replace) so a crash mid-write
        /// cannot truncate the file.</summary>
        void Write(WorldId worldId, string zoneName, string overlayJson);

        /// <summary>Delete the saved overlay. Returns true if a file was removed,
        /// false when there was nothing to delete (not an error).</summary>
        bool Delete(WorldId worldId, string zoneName);

        /// <summary>Move the overlay from <paramref name="fromZoneName"/> to
        /// <paramref name="toZoneName"/>. Returns true on success or no-op (no
        /// source file). Returns false if a destination file already exists
        /// (the implementation must NOT silently overwrite — caller decides).</summary>
        bool Rename(WorldId worldId, string fromZoneName, string toZoneName);

        /// <summary>Enumerate the zone names that currently have an overlay file
        /// in the given world. Order is not guaranteed.</summary>
        IEnumerable<string> ListAvailableZones(WorldId worldId);
    }
}
