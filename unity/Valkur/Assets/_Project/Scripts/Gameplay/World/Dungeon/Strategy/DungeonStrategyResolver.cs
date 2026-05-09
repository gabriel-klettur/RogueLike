using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.World.Dungeon.Strategy
{
    /// <summary>
    /// Lookup of <see cref="IDungeonStrategy"/> instances keyed by stable id.
    /// Strategies are registered at bootstrap (one per supported algorithm) and
    /// retrieved by id stored on each Map slot.
    /// </summary>
    public static class DungeonStrategyResolver
    {
        /// <summary>Default id used when a Map slot has no explicit strategy set.</summary>
        public const string DefaultStrategyId = "bsp";

        private static readonly Dictionary<string, IDungeonStrategy> _strategies =
            new Dictionary<string, IDungeonStrategy>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register or replace a strategy. Last write wins.</summary>
        public static void Register(IDungeonStrategy strategy)
        {
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));
            if (string.IsNullOrEmpty(strategy.Id))
                throw new ArgumentException("Strategy must have a non-empty Id.", nameof(strategy));

            _strategies[strategy.Id] = strategy;
        }

        /// <summary>
        /// Resolve a strategy by id (case-insensitive). Falls back to
        /// <see cref="DefaultStrategyId"/> when the requested id is unknown.
        /// Returns null only if no strategies have been registered yet.
        /// </summary>
        public static IDungeonStrategy Resolve(string id)
        {
            if (!string.IsNullOrEmpty(id) && _strategies.TryGetValue(id, out var strategy))
                return strategy;

            if (_strategies.TryGetValue(DefaultStrategyId, out var fallback))
                return fallback;

            return null;
        }

        /// <summary>Whether the given id has a registered strategy.</summary>
        public static bool IsRegistered(string id)
        {
            return !string.IsNullOrEmpty(id) && _strategies.ContainsKey(id);
        }

        /// <summary>Drop every registered strategy. Test hook.</summary>
        public static void ClearForTests()
        {
            _strategies.Clear();
        }

        /// <summary>Number of registered strategies. Test hook.</summary>
        public static int RegisteredCount => _strategies.Count;
    }
}
