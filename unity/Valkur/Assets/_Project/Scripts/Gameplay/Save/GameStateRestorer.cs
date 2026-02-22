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
            int damage = psd.maxHp - psd.hp;
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
            if (psd.inventory == null) return;

            var inventory = player.GetComponent<Inventory.Inventory>();
            if (inventory == null) return;

            inventory.Clear();
            inventory.Initialize(psd.inventory.capacity);
            Debug.Log($"[GameStateRestorer] Inventory structure restored. Slots: {psd.inventory.slots.Count}");
        }
    }
}
