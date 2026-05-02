using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Valkur.Core.Coordinates;
using Valkur.Core.WorldContext;
using Valkur.Data;

namespace Valkur.Gameplay.World.Worlds
{
    /// <summary>
    /// Phase 1 contract. Manages the lifecycle of one or more
    /// <see cref="IWorldContext"/> instances (one per loaded world), routes
    /// portal transitions across worlds, and exposes the currently active
    /// world to UI / gameplay code.
    ///
    /// Implementation guarantees:
    ///   - Exactly one world is "active" on the client at any time. Loading
    ///     a different world must unload the active one (Phase 1 keeps it
    ///     simple; Phase 4 / MMO server can hold N active simultaneously).
    ///   - <see cref="LoadWorldAsync"/> is idempotent for an already-loaded
    ///     world: re-issuing the call returns the existing context.
    ///   - Switching worlds emits <see cref="ActiveWorldChanged"/> AFTER the
    ///     new context is fully wired so subscribers can re-resolve their
    ///     dependencies through it.
    /// </summary>
    public interface IWorldManager
    {
        /// <summary>The currently active world's context, or null when no
        /// world has been loaded yet.</summary>
        IWorldContext Active { get; }

        /// <summary>Read-only snapshot of every world currently held in
        /// memory (loaded but possibly inactive). For Phase 1 single-world
        /// always has at most one entry; the surface is multi-aware so
        /// Phase 4 doesn't break callers.</summary>
        IReadOnlyCollection<IWorldContext> Loaded { get; }

        /// <summary>Fired AFTER the active world swap completes so listeners
        /// can re-resolve their per-world handles. Args: (oldContext, newContext).
        /// Null oldContext = first activation.</summary>
        event Action<IWorldContext, IWorldContext> ActiveWorldChanged;

        /// <summary>Load (or return the cached instance of) the world
        /// described by <paramref name="descriptor"/>. Does NOT activate
        /// it — call <see cref="ActivateAsync"/> to make it the active
        /// world. Idempotent.</summary>
        Task<IWorldContext> LoadWorldAsync(WorldDescriptor descriptor, CancellationToken ct = default);

        /// <summary>Switch the active world to one previously loaded via
        /// <see cref="LoadWorldAsync"/>. Emits <see cref="ActiveWorldChanged"/>
        /// on success.</summary>
        Task ActivateAsync(WorldId worldId, CancellationToken ct = default);

        /// <summary>Convenience: load + activate in one call.</summary>
        Task<IWorldContext> LoadAndActivateAsync(WorldDescriptor descriptor, CancellationToken ct = default);

        /// <summary>Unload the world identified by <paramref name="worldId"/>.
        /// Refuses to unload the active world — caller must Activate a different
        /// world first. Returns true iff the world was loaded and is now released.</summary>
        Task<bool> UnloadWorldAsync(WorldId worldId, CancellationToken ct = default);
    }
}
