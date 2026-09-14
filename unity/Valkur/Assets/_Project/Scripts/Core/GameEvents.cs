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

        /// <summary>
        /// A craft succeeded. Args: (recipeId, resultItemId, resultQuantity).
        /// Fired from <c>CraftingService.TryCraft</c> AFTER the result is placed in
        /// the bag and the profession xp is granted — so a listener that reads the
        /// inventory sees the finished state, and a craft that rolled back because
        /// the bag was full never fires at all.
        ///
        /// <para>Primitive payload keeps <c>Valkur.Core</c> free of
        /// <c>Valkur.Data</c>, the same rule <c>OnSpellCast</c> follows.</para>
        /// </summary>
        public static event Action<string, string, int> OnItemCrafted;

        // ── Social Events ──

        /// <summary>
        /// The player opened a conversation with a character. Args: (personaId).
        /// Fired once per OPEN, not per message — a Talk objective means "go and
        /// see them", and a per-message event would tick it for chatting with
        /// somebody the player was already standing in front of.
        /// </summary>
        public static event Action<string> OnNpcConversed;

        // ── Spell Events ──

        /// <summary>
        /// A spell finished executing (after its <c>ISpellExecutor</c> ran and the
        /// cooldown timer was set on the caster's <c>SpellCaster</c>). Args:
        /// (caster, spellKey, displayName, cooldownDuration). Fired exactly once
        /// per cast attempt — Prepare → Channel transitions do not re-fire it.
        /// Primitive payload (no <c>SpellDefinition</c> reference) keeps the
        /// <c>Valkur.Core</c> assembly free of <c>Valkur.Data</c> dependencies.
        /// Subscribed by the action bar (<c>SpellBarHUD</c>), which filters
        /// on <c>caster</c> identity to light the slot of the spell that was cast.
        /// </summary>
        public static event Action<GameObject, string, string, float> OnSpellCast;

        // ── World Events ──

        /// <summary>The player crossed into a new zone. Args: (oldZone, newZone)</summary>
        public static event Action<string, string> OnZoneChanged;

        // ── Dungeon Room Events (Udemy strategy) ──

        /// <summary>
        /// The player entered a different dungeon room. Args:
        /// (roomId, bounds in world tile coords, entrance tile, isClearedOfEnemies).
        /// Primitive payload keeps Valkur.Core free of Gameplay dependencies — see
        /// the note on OnSpellCast for the rationale.
        /// </summary>
        public static event Action<string, RectInt, Vector2Int, bool> OnRoomChanged;

        /// <summary>
        /// All enemies of a dungeon room have been defeated. Args: (roomId).
        /// Listened to by InstantiatedRoom to unlock doors and by audio to swap
        /// from battle to ambient music.
        /// </summary>
        public static event Action<string> OnRoomEnemiesDefeated;

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

        public static void FireItemCrafted(string recipeId, string resultItemId, int resultQuantity)
        {
            OnItemCrafted?.Invoke(recipeId, resultItemId, resultQuantity);
        }

        public static void FireNpcConversed(string personaId)
        {
            OnNpcConversed?.Invoke(personaId);
        }

        public static void FireZoneChanged(string oldZone, string newZone)
        {
            OnZoneChanged?.Invoke(oldZone, newZone);
        }

        public static void FireSpellCast(GameObject caster, string spellKey, string displayName, float cooldownDuration)
        {
            OnSpellCast?.Invoke(caster, spellKey, displayName, cooldownDuration);
        }

        public static void FireRoomChanged(string roomId, RectInt bounds, Vector2Int entrance, bool isClearedOfEnemies)
        {
            OnRoomChanged?.Invoke(roomId, bounds, entrance, isClearedOfEnemies);
        }

        public static void FireRoomEnemiesDefeated(string roomId)
        {
            OnRoomEnemiesDefeated?.Invoke(roomId);
        }

        // ── Gathering Events ──

        /// <summary>
        /// A gatherer produced goods from a world node. Args: (gatherer, skillKey, itemId, quantity).
        /// Raised when the stack is actually on the ground, so a listener never counts a yield the
        /// drop system refused. skillKey is empty for nodes that train no skill.
        /// </summary>
        public static event Action<GameObject, string, string, int> OnResourceGathered;

        /// <summary>A node was finished — a tree felled. Args: (worker, profileName, position).</summary>
        public static event Action<GameObject, string, Vector2> OnNodeFelled;

        /// <summary>A gathering skill moved. Args: (owner, skillKey, newTenths).</summary>
        public static event Action<GameObject, string, int> OnSkillChanged;

        public static void FireResourceGathered(GameObject gatherer, string skillKey, string itemId, int quantity)
        {
            OnResourceGathered?.Invoke(gatherer, skillKey ?? string.Empty, itemId, quantity);
        }

        public static void FireNodeFelled(GameObject worker, string profileName, Vector2 position)
        {
            OnNodeFelled?.Invoke(worker, profileName, position);
        }

        public static void FireSkillChanged(GameObject owner, string skillKey, int tenths)
        {
            OnSkillChanged?.Invoke(owner, skillKey, tenths);
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
            OnItemCrafted = null;
            OnNpcConversed = null;
            OnZoneChanged = null;
            OnSpellCast = null;
            OnRoomChanged = null;
            OnRoomEnemiesDefeated = null;
            OnResourceGathered = null;
            OnNodeFelled = null;
            OnSkillChanged = null;
        }

        // ── Domain Reload OFF reset ─────────────────────────────────────────
        // With "Enter Play Mode → Disable Domain Reload" enabled, every
        // static event delegate above survives across Play Mode entries.
        // EditMode test fixtures that subscribe a MonoBehaviour to e.g.
        // OnZoneChanged then leak the GameObject (TearDown destroys the
        // GO but the static delegate keeps the dead component alive enough
        // that a subsequent Play Mode FireZoneChanged hits its handler).
        // See `.github/incidents/RUN_TWIN_SAVE.md` (the x12 recurrence on
        // 2026-05-09 was caused by 11 leaked SaveService instances each
        // writing to its own runId folder when ZoneManager fired the first
        // Lobby→Alpha transition). Wiping all subscribers at
        // SubsystemRegistration restores the single-Domain-Reload contract.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSubscribersOnPlayModeEnter()
        {
            Clear();
        }
    }
}
