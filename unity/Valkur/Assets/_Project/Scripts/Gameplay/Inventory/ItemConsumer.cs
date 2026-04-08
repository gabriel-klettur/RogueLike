using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
// Mana lives in Valkur.Gameplay (same assembly, different sub-namespace)
using ManaCmp = Valkur.Gameplay.Mana;

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
        private FloatingDamageSpawner      _floatingText;

        // ── Events ────────────────────────────────────────────────────
        /// <summary>Fired after a successful consume. Args: (item)</summary>
        public static System.Action<ItemDefinition> OnItemConsumed;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            _inventory    = GetComponent<Inventory>();
            _health       = GetComponent<Health>();
            _mana         = GetComponent<ManaCmp>();
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

            // Energy and Hunger are stored in ItemDefinition for future subsystems.
            // Log so designers know the value is being read but not yet applied.
            if (item.energy != 0)
                Debug.Log($"[ItemConsumer] '{item.displayName}' energy={item.energy} (no Energy component yet).");

            if (item.hunger != 0)
                Debug.Log($"[ItemConsumer] '{item.displayName}' hunger={item.hunger} (no Hunger component yet).");

            // Timed stat buff
            if (item.duration > 0 && !string.IsNullOrEmpty(item.buffStat))
                StartCoroutine(ApplyTimedBuff(item.buffStat, item.buffValue, item.duration));
        }

        private IEnumerator ApplyTimedBuff(string stat, float value, float duration)
        {
            // Loose stat-buff routing. Extend when dedicated stat components exist.
            Debug.Log($"[ItemConsumer] Buff +{value} to '{stat}' for {duration}s on {gameObject.name}");
            // TODO: integrate with a StatComponent when implemented.
            yield return new WaitForSeconds(duration);
            Debug.Log($"[ItemConsumer] Buff '{stat}' expired on {gameObject.name}");
        }
    }
}
