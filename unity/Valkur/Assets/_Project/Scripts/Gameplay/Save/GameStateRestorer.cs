using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Applies a GameSaveData snapshot back to live entities.
    /// No IO — pure state restoration.
    /// </summary>
    public static class GameStateRestorer
    {
        /// <summary>
        /// Restore game state from save data onto the current player and world.
        /// </summary>
        public static void Restore(GameSaveData data)
        {
            if (data.player == null) return;

            if (!string.IsNullOrWhiteSpace(data.player.playerClass))
                PlayerSelectionState.SetSelectedPlayer(data.player.playerClass);

            var player = EntityRegistry.Player;
            if (player == null)
            {
                Debug.LogWarning("[GameStateRestorer] No player found to restore state.");
                return;
            }

            RestorePosition(player, data.player);
            RestoreHealth(player, data.player);
            RestoreMana(player, data.player);
            RestoreExperience(player, data.player);
            RestoreInventory(player, data.player);

            Debug.Log($"[GameStateRestorer] Player state restored: pos={data.player.position}, " +
                      $"HP={data.player.hp}/{data.player.maxHp}, " +
                      $"Mana={data.player.mana}/{data.player.maxMana}, " +
                      $"XP={data.player.experience}, Lv={data.player.level}");
        }

        private static void RestorePosition(GameObject player, PlayerSaveData psd)
        {
            player.transform.position = new Vector3(psd.position.x, psd.position.y, 0f);
        }

        private static void RestoreHealth(GameObject player, PlayerSaveData psd)
        {
            var health = player.GetComponent<Health>();
            if (health == null) return;

            health.Initialize(psd.maxHp);
            // Guard: a save with hp==0 means the player died before saving.
            // Restore to full health so the player is never loaded in a dead state.
            int safeHp = (psd.hp > 0) ? psd.hp : psd.maxHp;
            int damage = psd.maxHp - safeHp;
            if (damage > 0)
                health.TakeDamage(damage);
        }

        private static void RestoreMana(GameObject player, PlayerSaveData psd)
        {
            var mana = player.GetComponent<Mana>();
            if (mana == null || psd.maxMana <= 0) return;

            mana.Initialize(Mathf.RoundToInt(psd.maxMana), 2f);
            int manaToConsume = Mathf.RoundToInt(psd.maxMana - psd.mana);
            if (manaToConsume > 0)
                mana.TryConsume(manaToConsume);
        }

        private static void RestoreExperience(GameObject player, PlayerSaveData psd)
        {
            var experience = player.GetComponent<Experience>();
            if (experience != null)
                experience.Initialize(psd.experience, psd.level);
        }

        private static void RestoreInventory(GameObject player, PlayerSaveData psd)
        {
            Debug.Log($"[GameStateRestorer] RestoreInventory ENTER player={player?.name} psd.inventory={(psd.inventory == null ? "NULL" : "OK")}");
            if (psd.inventory == null) return;

            var inventory = player.GetComponent<Inventory.Inventory>();
            if (inventory == null)
            {
                Debug.LogWarning($"[GameStateRestorer] Player '{player.name}' has no Inventory component — abort.");
                return;
            }

            // Initialize already clears + resizes; no need for a separate Clear().
            // Force capacity to at least the current default so the bag UI never
            // shows dead cells when an old save reloads with a smaller capacity.
            int restoredCapacity = Mathf.Max(psd.inventory.capacity,
                                             Inventory.Inventory.DefaultBagCapacity);
            inventory.Initialize(restoredCapacity);

            var slots = psd.inventory.slots;
            if (slots == null || slots.Count == 0)
            {
                Debug.Log("[GameStateRestorer] Inventory restored (empty).");
                return;
            }

            // Re-hydrate items by resolving ids through the canonical ItemCatalog.
            // Without it we have no way to map a saved string id back to the live
            // ItemDefinition asset, so we fail loud rather than silently drop items.
            if (!ServiceLocator.TryGet<ItemCatalog>(out var catalog) || catalog == null)
            {
                Debug.LogWarning("[GameStateRestorer] No ItemCatalog registered — inventory items cannot be resolved and will be lost on this load.");
                return;
            }

            // Index-aligned restore: each saved entry's position in the list IS
            // its visual slot. Old saves (schema 1.0, compact list shorter than
            // capacity) still load correctly because the i-th compact entry
            // becomes the i-th visual slot — same outcome as the previous
            // AddItem-based restore for those payloads.
            int restored = 0;
            int missing  = 0;
            int max = Mathf.Min(slots.Count, psd.inventory.capacity);
            for (int i = 0; i < max; i++)
            {
                var slot = slots[i];
                if (string.IsNullOrEmpty(slot.itemId) || slot.quantity <= 0) continue;

                var def = catalog.GetById(slot.itemId);
                if (def == null)
                {
                    Debug.LogWarning($"[GameStateRestorer] Saved itemId '{slot.itemId}' (slot {i}) not found in ItemCatalog — slot dropped.");
                    missing++;
                    continue;
                }

                inventory.SetSlot(i, def, slot.quantity);
                Debug.Log($"[GameStateRestorer] SetSlot bag[{i}] = {slot.itemId} x{slot.quantity}");
                restored++;
            }

            int equipRestored = 0;
            var equipSlots = psd.inventory.equipmentSlots;
            if (equipSlots != null)
            {
                int eqMax = Mathf.Min(equipSlots.Count, Inventory.Inventory.EquipmentCapacity);
                for (int i = 0; i < eqMax; i++)
                {
                    var slot = equipSlots[i];
                    if (string.IsNullOrEmpty(slot.itemId) || slot.quantity <= 0) continue;
                    var def = catalog.GetById(slot.itemId);
                    if (def == null)
                    {
                        Debug.LogWarning($"[GameStateRestorer] Saved equipment itemId '{slot.itemId}' (slot {i}) not found in ItemCatalog — slot dropped.");
                        missing++;
                        continue;
                    }
                    inventory.SetEquipmentSlot(i, def, slot.quantity);
                    equipRestored++;
                }
            }

            Debug.Log($"[GameStateRestorer] Inventory restored: {restored} bag stack(s), {equipRestored} equipment slot(s)" +
                      (missing > 0 ? $", {missing} missing" : "") +
                      $" (capacity={psd.inventory.capacity}).");

            // Sanity probe: confirm what the live Inventory component actually
            // holds *after* we finished writing. If this list is empty but the
            // log above said "restored: N", a downstream system is wiping the
            // inventory between Restore and the next UI refresh.
            int live = 0;
            for (int i = 0; i < inventory.Slots.Count; i++)
                if (!inventory.Slots[i].IsEmpty) live++;
            Debug.Log($"[GameStateRestorer] Live Inventory probe: {live} non-empty bag slot(s) on '{player.name}'.");
        }
    }
}
