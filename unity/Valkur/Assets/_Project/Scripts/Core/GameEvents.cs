using System;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Global static event bus for cross-cutting game events.
    /// Allows any system to react to combat, XP, and item events without direct coupling.
    /// Producers fire events; consumers subscribe/unsubscribe as needed.
    /// </summary>
    public static class GameEvents
    {
        // ── Combat Events ──

        /// <summary>Any entity took damage. Args: (victim, attacker, amount)</summary>
        public static event Action<GameObject, GameObject, int> OnEntityDamaged;

        /// <summary>Any entity died. Args: (victim, killer)</summary>
        public static event Action<GameObject, GameObject> OnEntityDied;

        /// <summary>An entity hit another entity. Args: (attacker, victim, damage)</summary>
        public static event Action<GameObject, GameObject, int> OnHitDealt;

        // ── Player-Specific Events ──

        /// <summary>Player took damage. Args: (amount, currentHp, maxHp)</summary>
        public static event Action<int, int, int> OnPlayerDamaged;

        /// <summary>Player died.</summary>
        public static event Action OnPlayerDied;

        // ── XP / Level Events ──

        /// <summary>XP gained by any entity. Args: (entity, amount)</summary>
        public static event Action<GameObject, int> OnXpGained;

        /// <summary>Entity leveled up. Args: (entity, newLevel)</summary>
        public static event Action<GameObject, int> OnLevelUp;

        // ── Inventory Events ──

        /// <summary>Item picked up. Args: (collector, itemName, quantity)</summary>
        public static event Action<GameObject, string, int> OnItemPickedUp;

        // ── Fire Methods ──

        public static void FireEntityDamaged(GameObject victim, GameObject attacker, int amount)
        {
            OnEntityDamaged?.Invoke(victim, attacker, amount);
        }

        public static void FireEntityDied(GameObject victim, GameObject killer)
        {
            OnEntityDied?.Invoke(victim, killer);
        }

        public static void FireHitDealt(GameObject attacker, GameObject victim, int damage)
        {
            OnHitDealt?.Invoke(attacker, victim, damage);
        }

        public static void FirePlayerDamaged(int amount, int currentHp, int maxHp)
        {
            OnPlayerDamaged?.Invoke(amount, currentHp, maxHp);
        }

        public static void FirePlayerDied()
        {
            OnPlayerDied?.Invoke();
        }

        public static void FireXpGained(GameObject entity, int amount)
        {
            OnXpGained?.Invoke(entity, amount);
        }

        public static void FireLevelUp(GameObject entity, int newLevel)
        {
            OnLevelUp?.Invoke(entity, newLevel);
        }

        public static void FireItemPickedUp(GameObject collector, string itemName, int quantity)
        {
            OnItemPickedUp?.Invoke(collector, itemName, quantity);
        }

        /// <summary>
        /// Clear all subscribers. Call on scene unload or domain reload to prevent leaks.
        /// </summary>
        public static void Clear()
        {
            OnEntityDamaged = null;
            OnEntityDied = null;
            OnHitDealt = null;
            OnPlayerDamaged = null;
            OnPlayerDied = null;
            OnXpGained = null;
            OnLevelUp = null;
            OnItemPickedUp = null;
        }
    }
}
