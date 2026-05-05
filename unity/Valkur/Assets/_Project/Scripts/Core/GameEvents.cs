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

        /// <summary>Player was resurrected by the DevConsole or a game mechanic. Signals the death screen to close.</summary>
        public static event Action OnPlayerResurrected;

        /// <summary>
        /// Canonical revive signal: fires after the death-sequence controller finishes the
        /// REVIVING phase (post-build banner hide, post-grayscale fade-out). Distinct from
        /// <see cref="OnPlayerResurrected"/>, which is the legacy signal used by the
        /// DevConsole resurrect command and the old DeathScreenUI. New subscribers (XP loss
        /// hook, audio, telemetry) should use this one.
        /// </summary>
        public static event Action OnPlayerRevived;

        /// <summary>
        /// The active run row in the profile DB has been closed (player exited to the
        /// main menu or loaded a different save). Distinct from <see cref="OnPlayerDied"/>:
        /// in the new spirit/altar flow, dying does NOT end the run — only an explicit
        /// session boundary does. ProfileTelemetrySystem listens for this to flush the
        /// run record.
        /// </summary>
        public static event Action OnRunEnded;

        // ── XP / Level Events ──

        /// <summary>XP gained by any entity. Args: (entity, amount)</summary>
        public static event Action<GameObject, int> OnXpGained;

        /// <summary>
        /// XP lost by any entity (e.g. death penalty). Args: (entity, amount).
        /// Always positive — the amount is the XP that was removed.
        /// </summary>
        public static event Action<GameObject, int> OnXpLost;

        /// <summary>Entity leveled up. Args: (entity, newLevel)</summary>
        public static event Action<GameObject, int> OnLevelUp;

        // ── Inventory Events ──

        /// <summary>Item picked up. Args: (collector, itemName, quantity)</summary>
        public static event Action<GameObject, string, int> OnItemPickedUp;

        /// <summary>Item consumed (used from inventory). Args: (consumer, itemId)</summary>
        public static event Action<GameObject, string> OnItemConsumed;

        // ── World Events ──

        /// <summary>The player crossed into a new zone. Args: (oldZone, newZone)</summary>
        public static event Action<string, string> OnZoneChanged;

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

        public static void FirePlayerResurrected()
        {
            OnPlayerResurrected?.Invoke();
        }

        public static void FirePlayerRevived()
        {
            OnPlayerRevived?.Invoke();
        }

        public static void FireRunEnded()
        {
            OnRunEnded?.Invoke();
        }

        public static void FireXpGained(GameObject entity, int amount)
        {
            OnXpGained?.Invoke(entity, amount);
        }

        public static void FireXpLost(GameObject entity, int amount)
        {
            OnXpLost?.Invoke(entity, amount);
        }

        public static void FireLevelUp(GameObject entity, int newLevel)
        {
            OnLevelUp?.Invoke(entity, newLevel);
        }

        public static void FireItemPickedUp(GameObject collector, string itemName, int quantity)
        {
            OnItemPickedUp?.Invoke(collector, itemName, quantity);
        }

        public static void FireItemConsumed(GameObject consumer, string itemId)
        {
            OnItemConsumed?.Invoke(consumer, itemId);
        }

        public static void FireZoneChanged(string oldZone, string newZone)
        {
            OnZoneChanged?.Invoke(oldZone, newZone);
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
            OnPlayerResurrected = null;
            OnPlayerRevived = null;
            OnRunEnded = null;
            OnXpGained = null;
            OnXpLost = null;
            OnLevelUp = null;
            OnItemPickedUp = null;
            OnItemConsumed = null;
            OnZoneChanged = null;
        }
    }
}
