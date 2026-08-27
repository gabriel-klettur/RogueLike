using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for <c>StreamingAssets/Entities/entities_instances.json</c> —
    /// monsters placed through the Entities runtime editor (F5) that must survive a Stop.
    ///
    /// Deliberately its OWN record family rather than piggybacking on
    /// <c>ISpawnerInstanceRepository</c>: an F5 placement is an already-materialised entity a
    /// designer put down to fight, not a spawn recipe with waves, a trigger type and a cooldown.
    /// Every other placement editor (Buildings, Lights, Particles, Spawners) already owns its
    /// own instance file for the same reason — one editor's anti-wipe guard and per-slot routing
    /// must never depend on another editor's save happening to run first.
    ///
    /// Read by the Entities editor's own boot-time loader (not a separate
    /// <c>EntityInstanceLoader</c> component — the editor already exists as a scene-wide
    /// singleton regardless of whether F5 is open, so it is the natural single owner of both
    /// halves of this round trip); written by <c>EntitiesRuntimeEditor</c>.
    /// </summary>
    public interface IEntityInstanceRepository
    {
        bool Exists(WorldId worldId);
        string ReadRawJson(WorldId worldId);
        void WriteRawJson(WorldId worldId, string json);
    }
}
