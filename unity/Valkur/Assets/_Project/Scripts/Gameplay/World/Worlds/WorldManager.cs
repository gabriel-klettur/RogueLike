using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Core.Services;
using Valkur.Core.WorldContext;
using Valkur.Data;

namespace Valkur.Gameplay.World.Worlds
{
    /// <summary>
    /// Phase 1 default <see cref="IWorldManager"/>. Pure POCO — does not
    /// instantiate Unity scenes or tilemaps; orchestrating the actual scene
    /// load is the responsibility of <c>BootstrapPipeline</c> (or the
    /// legacy <c>GameplaySceneSetup</c> until that migration lands).
    ///
    /// Phase 1 scope:
    ///   - Holds N <see cref="IWorldContext"/> by <see cref="WorldId"/>.
    ///   - Tracks one Active world.
    ///   - LoadWorldAsync is synchronous for now (returns Task.CompletedTask):
    ///     the async surface is reserved for chunk streaming in Phase 2 and
    ///     the network handshake in Phase 4.
    ///
    /// Out of scope (deferred):
    ///   - Driving Unity scene swap / tilemap rebuild — done by
    ///     BootstrapPipeline when migrated.
    ///   - Save folder per world — SaveFileManager will key on
    ///     Active.WorldId once it adopts the manager.
    ///   - Cinemachine bounds reset — done by the scene-swap step.
    /// </summary>
    public sealed class WorldManager : IWorldManager
    {
        private readonly Dictionary<WorldId, IWorldContext> _loaded
            = new Dictionary<WorldId, IWorldContext>();

        public IWorldContext Active { get; private set; }

        public IReadOnlyCollection<IWorldContext> Loaded => _loaded.Values;

        public event Action<IWorldContext, IWorldContext> ActiveWorldChanged;

        public Task<IWorldContext> LoadWorldAsync(WorldDescriptor descriptor, CancellationToken ct = default)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (descriptor.Config == null)
                throw new InvalidOperationException(
                    $"WorldDescriptor '{descriptor.name}' has no WorldConfig — cannot load.");

            ct.ThrowIfCancellationRequested();

            var id = descriptor.Id;
            if (_loaded.TryGetValue(id, out var existing))
                return Task.FromResult(existing);

            // Each loaded world gets its own scoped registry so a service
            // registered for world A does not leak into world B. Cross-world
            // infrastructure (audio, input, settings) keeps living in the
            // global ServiceLocator.
            var ctx = new Valkur.Core.WorldContext.WorldContext(id, new ScopedServiceRegistry());
            _loaded[id] = ctx;
            return Task.FromResult<IWorldContext>(ctx);
        }

        public Task ActivateAsync(WorldId worldId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_loaded.TryGetValue(worldId, out var target))
                throw new InvalidOperationException(
                    $"WorldManager.ActivateAsync({worldId}): world is not loaded. Call LoadWorldAsync first.");

            if (Active == target) return Task.CompletedTask;

            var previous = Active;
            Active = target;
            ActiveWorldChanged?.Invoke(previous, target);
            return Task.CompletedTask;
        }

        public async Task<IWorldContext> LoadAndActivateAsync(WorldDescriptor descriptor, CancellationToken ct = default)
        {
            var ctx = await LoadWorldAsync(descriptor, ct).ConfigureAwait(false);
            await ActivateAsync(ctx.WorldId, ct).ConfigureAwait(false);
            return ctx;
        }

        public Task<bool> UnloadWorldAsync(WorldId worldId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Active != null && Active.WorldId.Equals(worldId))
            {
                Debug.LogWarning($"[WorldManager] Cannot unload active world {worldId}. " +
                                 "Activate a different world first.");
                return Task.FromResult(false);
            }
            return Task.FromResult(_loaded.Remove(worldId));
        }
    }
}
