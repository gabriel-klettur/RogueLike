using System;

namespace Valkur.Core.Services
{
    /// <summary>
    /// Scope-able service registry, alternative to the static <see cref="ServiceLocator"/>.
    ///
    /// Phase 0 introduces this interface so new managers can resolve their
    /// dependencies through an injectable handle instead of a global static.
    /// The default implementation, <see cref="GlobalServiceRegistry"/>, just
    /// adapts <see cref="ServiceLocator"/> — every legacy callsite keeps
    /// working unchanged.
    ///
    /// In Phase 1 (multi-world) and Phase 4 (MMO server with multiple
    /// dimensions per process), the static <see cref="ServiceLocator"/> is
    /// kept for genuinely global infrastructure (audio, input, settings),
    /// while gameplay code routes through an <see cref="IServiceRegistry"/>
    /// scoped per world / per shard. Replacing 37 callsites at that point
    /// is impossible; introducing the interface NOW makes the migration
    /// gradual and per-callsite.
    /// </summary>
    public interface IServiceRegistry
    {
        /// <summary>Register a service. Replaces any previous registration of the same type.</summary>
        void Register<T>(T service) where T : class;

        /// <summary>Remove a registered service.</summary>
        void Unregister<T>() where T : class;

        /// <summary>Resolve a service. Returns null if no registration exists.</summary>
        T Get<T>() where T : class;

        /// <summary>Resolve a service if registered. Returns false otherwise.</summary>
        bool TryGet<T>(out T service) where T : class;

        /// <summary>Drop every registration. Test fixtures and shutdown.</summary>
        void Clear();
    }
}
