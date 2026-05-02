namespace Valkur.Core.Services
{
    /// <summary>
    /// Adapter that exposes the legacy global <see cref="ServiceLocator"/>
    /// through the <see cref="IServiceRegistry"/> interface. New managers
    /// take an <see cref="IServiceRegistry"/> in their constructor / Awake
    /// and existing code that calls <see cref="ServiceLocator"/> directly
    /// keeps working — both views observe the same underlying dictionary.
    ///
    /// The single instance lives behind <see cref="Instance"/>; treat it as
    /// an immutable handle, not a singleton to extend.
    /// </summary>
    public sealed class GlobalServiceRegistry : IServiceRegistry
    {
        public static readonly GlobalServiceRegistry Instance = new GlobalServiceRegistry();

        private GlobalServiceRegistry() { }

        public void Register<T>(T service) where T : class   => ServiceLocator.Register(service);
        public void Unregister<T>() where T : class          => ServiceLocator.Unregister<T>();
        public T Get<T>() where T : class                    => ServiceLocator.Get<T>();
        public bool TryGet<T>(out T service) where T : class => ServiceLocator.TryGet(out service);
        public void Clear()                                  => ServiceLocator.Clear();
    }

    /// <summary>
    /// Lightweight standalone <see cref="IServiceRegistry"/> backed by a private
    /// dictionary. Useful for unit tests that need to inject mocks without
    /// polluting the global <see cref="ServiceLocator"/>, and for Phase 4
    /// per-world / per-shard scopes that must not leak across boundaries.
    /// </summary>
    public sealed class ScopedServiceRegistry : IServiceRegistry
    {
        private readonly System.Collections.Generic.Dictionary<System.Type, object> _services
            = new System.Collections.Generic.Dictionary<System.Type, object>();

        public void Register<T>(T service) where T : class => _services[typeof(T)] = service;
        public void Unregister<T>() where T : class        => _services.Remove(typeof(T));

        public T Get<T>() where T : class
        {
            _services.TryGetValue(typeof(T), out var obj);
            return obj as T;
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var obj))
            {
                service = obj as T;
                return service != null;
            }
            service = null;
            return false;
        }

        public void Clear() => _services.Clear();
    }
}
