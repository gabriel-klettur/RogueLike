using Valkur.Core.Coordinates;
using Valkur.Core.Services;

namespace Valkur.Core.WorldContext
{
    /// <summary>
    /// Per-world handle that gameplay code resolves its dependencies through.
    /// Today every callsite uses <see cref="GlobalContext"/> (WorldId.Base +
    /// the global <see cref="ServiceLocator"/>); Phase 1 lets a <c>WorldManager</c>
    /// hand out one context per loaded world, and Phase 4 lets a dedicated
    /// server hold N contexts simultaneously (one per dimension shard).
    ///
    /// The point of having this interface NOW, while there is only one world,
    /// is that every new manager / loader written from this point on accepts
    /// an <see cref="IWorldContext"/> in its API. When multi-world arrives,
    /// the work is "wire a different context", not "rewrite hundreds of
    /// callsites that assumed a global singleton".
    ///
    /// Cache the resolved values in <c>Awake</c>; never resolve per-frame.
    /// </summary>
    public interface IWorldContext
    {
        /// <summary>Identity of the world this context represents.</summary>
        WorldId WorldId { get; }

        /// <summary>Service registry scoped to this world. Loaders/repositories
        /// for this world live here. Cross-world infrastructure (audio, input,
        /// settings) lives in the global <see cref="ServiceLocator"/>.</summary>
        IServiceRegistry Services { get; }
    }
}
