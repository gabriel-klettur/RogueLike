using System;
using System.Collections.Generic;

namespace Valkur.Core
{
    /// <summary>
    /// Minimal service locator for decoupling cross-assembly dependencies.
    /// Services register themselves on initialization; consumers resolve by interface.
    /// Lives in Core so all layers can access it without circular references.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class
        {
            _services[typeof(T)] = service;
        }

        public static void Unregister<T>() where T : class
        {
            _services.Remove(typeof(T));
        }

        public static T Get<T>() where T : class
        {
            _services.TryGetValue(typeof(T), out var service);
            return service as T;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var obj))
            {
                service = obj as T;
                return service != null;
            }
            service = null;
            return false;
        }

        /// <summary>
        /// Clear all registered services. Call on application quit or domain reload.
        /// </summary>
        public static void Clear()
        {
            _services.Clear();
        }
    }
}
