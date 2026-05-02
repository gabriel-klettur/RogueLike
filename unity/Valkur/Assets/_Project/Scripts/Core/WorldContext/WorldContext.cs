using Valkur.Core.Coordinates;
using Valkur.Core.Services;

namespace Valkur.Core.WorldContext
{
    /// <summary>
    /// Default <see cref="IWorldContext"/> implementation: a <see cref="WorldId"/>
    /// plus an <see cref="IServiceRegistry"/>. Two pre-built instances cover
    /// every Phase-0 callsite:
    ///
    /// <list type="bullet">
    ///   <item><see cref="Global"/> — <see cref="WorldId.Base"/> + the
    ///   <see cref="GlobalServiceRegistry"/>. Use this anywhere the legacy
    ///   <see cref="ServiceLocator"/> would have been used.</item>
    ///   <item><see cref="Scoped"/> — for tests or future per-world scopes;
    ///   each call returns an isolated registry.</item>
    /// </list>
    /// </summary>
    public sealed class WorldContext : IWorldContext
    {
        public WorldId WorldId { get; }
        public IServiceRegistry Services { get; }

        public WorldContext(WorldId worldId, IServiceRegistry services)
        {
            WorldId = worldId;
            Services = services ?? new ScopedServiceRegistry();
        }

        /// <summary>The single context every legacy callsite resolves to during Phase 0.</summary>
        public static readonly WorldContext Global =
            new WorldContext(WorldId.Base, GlobalServiceRegistry.Instance);

        /// <summary>Builds an isolated context: useful for unit tests that need a
        /// clean registry without polluting the global one.</summary>
        public static WorldContext Scoped(WorldId? worldId = null)
            => new WorldContext(worldId ?? WorldId.Base, new ScopedServiceRegistry());
    }
}
