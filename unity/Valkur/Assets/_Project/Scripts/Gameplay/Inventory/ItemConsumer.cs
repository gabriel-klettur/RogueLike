using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
// Mana lives in Valkur.Gameplay (same assembly, different sub-namespace)
using ManaCmp = Valkur.Gameplay.Mana;
using Valkur.Gameplay.Player;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Handles consuming items from the player's inventory.
    /// Maps Python's ConsumeSystem (roguelike_game/game_systems/consume_system.py).
    ///
    /// Applies: healing, mana restoration, energy, hunger.
    /// Supports timed buff via buffStat/buffValue/duration.
    ///
    /// Usage:
    ///   itemConsumer.TryConsume(itemDef);
    /// </summary>
    [RequireComponent(typeof(Valkur.Gameplay.Inventory.Inventory))]
    public class ItemConsumer : MonoBehaviour
    {
        // ── Dependencies (resolved in Awake) ──────────────────────────
        private Inventory                  _inventory;
        private Health                     _health;
        private ManaCmp                    _mana;
        private Energy                     _energy;
        private Hunger                     _hunger;
        private FloatingDamageSpawner      _floatingText;

        // ── Events ────────────────────────────────────────────────────
        /// <summary>Fired after a successful consume. Args: (item)</summary>
        /// <summary>
        /// Subscribers from the previous Play session would otherwise still be attached.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEventOnPlayModeEnter()
        {
            OnItemConsumed = null;
        }

        public static event System.Action<ItemDefinition> OnItemConsumed;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            _inventory    = GetComponent<Inventory>();
            _health       = GetComponent<Health>();
            _mana         = GetComponent<ManaCmp>();
            _energy       = GetComponent<Energy>();
            _hunger       = GetComponent<Hunger>();
            _floatingText = GetComponentInChildren<FloatingDamageSpawner>(true);
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Tries to consume one unit of the given item from the inventory.
        /// Returns true on success.
        /// </summary>
        public bool TryConsume(ItemDefinition item)
        {
            if (item == null) return false;
            if (_inventory == null)
            {
                Debug.LogError($"[ItemConsumer] No Inventory component on {gameObject.name}.");
                return false;
            }

            int removed = _inventory.RemoveItem(item, 1);
            if (removed <= 0)
            {
                Debug.Log($"[ItemConsumer] Cannot consume '{item.displayName}': not in inventory.");
                return false;
            }

            ApplyEffects(item);
            GameEvents.FireItemConsumed(gameObject, item.itemId);
            OnItemConsumed?.Invoke(item);
            return true;
        }

        // ── Effects ───────────────────────────────────────────────────

        private void ApplyEffects(ItemDefinition item)
        {
            // Healing
            if (item.healing > 0 && _health != null)
            {
                _health.Heal(Mathf.RoundToInt(item.healing));
                _floatingText?.ShowHeal(Mathf.RoundToInt(item.healing));
            }

            // Mana
            if (item.mana > 0 && _mana != null)
                _mana.Restore(Mathf.RoundToInt(item.mana));

            // Energy
            if (item.energy != 0 && _energy != null)
                _energy.Restore(Mathf.RoundToInt(item.energy));

            // Hunger
            if (item.hunger != 0 && _hunger != null)
                _hunger.Feed(Mathf.RoundToInt(item.hunger));

            // Timed stat buff
            ApplyTimedBuff(item);
        }

        /// <summary>
        /// Hands the item's timed modifiers to <see cref="TimedBuffSource"/>, which owns the
        /// Buff stat layer.
        ///
        /// This used to be a coroutine that logged twice and changed nothing — an honest
        /// placeholder waiting for a stat component that did not exist yet. The buff key is
        /// the ITEM ID, so drinking a second flask of the same potion refreshes its timer
        /// instead of stacking a duplicate, and two different potions stack normally.
        /// </summary>
        private void ApplyTimedBuff(ItemDefinition item)
        {
            if (item == null || item.duration <= 0f) return;

            var buffs = GetComponent<TimedBuffSource>();
            if (buffs == null) return;   // monsters and test rigs have no stat store

            _buffScratch.Clear();
            if (item.buffModifiers != null && item.buffModifiers.Length > 0)
                _buffScratch.AddRange(item.buffModifiers);

            // Legacy string field. Kept working where it can be understood rather than
            // silently ignored, and warned about exactly once per item where it cannot.
            if (!string.IsNullOrWhiteSpace(item.buffStat))
            {
                if (Valkur.Data.StatCatalog.TryParse(item.buffStat, out var stat))
                {
                    _buffScratch.Add(Valkur.Data.StatModifier.Flat(stat, item.buffValue));
                }
                else if (_warnedLegacyBuffStats.Add(item.itemId ?? item.buffStat))
                {
                    Debug.LogWarning($"[ItemConsumer] Item '{item.displayName}' has legacy " +
                                     $"buffStat '{item.buffStat}', which names no StatKind. " +
                                     "Author buffModifiers instead — this half of the item " +
                                     "does nothing.");
                }
            }

            if (_buffScratch.Count == 0) return;

            string key = !string.IsNullOrEmpty(item.itemId) ? item.itemId : item.displayName;
            buffs.Apply(key, _buffScratch, item.duration);
        }

        private readonly System.Collections.Generic.List<Valkur.Data.StatModifier> _buffScratch =
            new System.Collections.Generic.List<Valkur.Data.StatModifier>(4);

        // Static so the warning is once per SESSION rather than once per consumer, and
        // reset with the domain because Domain Reload is off.
        private static readonly System.Collections.Generic.HashSet<string> _warnedLegacyBuffStats =
            new System.Collections.Generic.HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLegacyBuffWarnings() => _warnedLegacyBuffStats.Clear();
    }
}
