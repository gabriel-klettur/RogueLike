using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for the <c>buildings_instances.json</c> file
    /// (placement of every BuildingObject in a world). Bridges the legacy
    /// hand-written JSON IO inside <c>BuildingLoader</c> /
    /// <c>BuildingsRuntimeEditor</c> to a swap-able backend.
    ///
    /// The repo deliberately works at <b>raw JSON</b> granularity rather than
    /// at parsed-list level: the production parser (<c>MiniJsonRuntime</c>)
    /// lives in <c>Valkur.Gameplay</c>, and pulling it into Infrastructure
    /// would invert the assembly graph. Callers (BuildingLoader,
    /// BuildingsRuntimeEditor) keep ownership of parsing / serialisation;
    /// the repo isolates them from the actual file path / IO mechanism.
    ///
    /// Phase 0 introduces the contract; Phase 1 (multi-world) gets the
    /// per-world file layout for free because every method already takes a
    /// <see cref="WorldId"/>.
    /// </summary>
    public interface IBuildingInstanceRepository
    {
        /// <summary>True iff a building-instances file exists for the given world.</summary>
        bool Exists(WorldId worldId);

        /// <summary>Read the raw instances JSON. Returns null when the file is missing.</summary>
        string ReadRawJson(WorldId worldId);

        /// <summary>Persist the raw instances JSON for the given world. Implementations
        /// write atomically (tmp + replace) so a crash mid-write cannot truncate the
        /// previous content.</summary>
        void WriteRawJson(WorldId worldId, string json);
    }
}
