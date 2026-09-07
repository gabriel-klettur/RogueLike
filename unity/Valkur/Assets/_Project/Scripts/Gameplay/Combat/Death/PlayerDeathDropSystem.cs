using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Handles loot scatter when the player dies in the spirit/altar flow.
    /// Mirrors <see cref="DeathDropSystem"/>, but always operates on the
    /// Player tag, drops <em>everything</em> (no questId filter), and also
    /// empties the <see cref="CurrencyWallet"/> as separated coin pickups.
    ///
    /// The body marker is spawned by <see cref="DeathSequenceController"/>;
    /// this system is invoked synchronously from the controller's coroutine
    /// instead of subscribing to <c>GameEvents.OnPlayerDied</c> directly,
    /// so the controller can guarantee drops happen before the spirit
    /// transition (otherwise picking the corpse-position items would race
    /// with the spirit's own movement).
    /// </summary>
    public static class PlayerDeathDropSystem
    {
        private const float ItemScatterRadius = 1.5f;
        private const float CoinScatterRadius = 1.2f;

        public static void DropEverything(GameObject player)
        {
            if (player == null) return;
            Vector3 deathPos = player.transform.position;

            DropInventory(player, deathPos);
            DropCurrency(player, deathPos);
        }

        private static void DropInventory(GameObject player, Vector3 deathPos)
        {
            var inventory = player.GetComponent<Inventory.Inventory>();
            if (inventory == null || inventory.UsedSlots == 0) return;

            // Snapshot the slots before clearing — the spawn loop must not see
            // the inventory mutating mid-iteration.
            int slotCount = inventory.Slots.Count;
            var snapshot = new (Valkur.Data.ItemDefinition item, int qty)[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                var s = inventory.Slots[i];
                snapshot[i] = (s.Item, s.Quantity);
            }

            int dropped = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i];
                if (entry.item == null || entry.qty <= 0) continue;

                Vector2 offset = Random.insideUnitCircle * ItemScatterRadius;
                Vector3 dropPos = deathPos + new Vector3(offset.x, offset.y, 0f);

                var pickup = DropSystem.SpawnDrop(entry.item, entry.qty, dropPos);
                // Persist until the player can come back for them — the corpse
                // and items live until revive (DeathSequenceController despawns
                // the corpse, but does not clean up dropped items).
                if (pickup != null)
                    pickup.gameObject.SetActive(true);
                dropped++;
            }

            inventory.Clear();

            if (dropped > 0)
                Debug.Log($"[PlayerDeathDropSystem] Dropped {dropped} inventory stack(s) at {deathPos}.");
        }

        private static void DropCurrency(GameObject player, Vector3 deathPos)
        {
            var wallet = player.GetComponent<CurrencyWallet>();
            if (wallet == null || wallet.Coins <= 0) return;

            int total = wallet.Coins;
            wallet.SetBalance(0);

            // The shell (layer, sprite, collider, sorting) and the chunking both live in
            // CoinDropSpawner, which DeathDropSystem's monster reward also goes through —
            // a purse spilled on death and a reward minted on a kill must look identical
            // on the ground, and two copies of that shell is how they stop being.
            int spawned = CoinDropSpawner.Spill(total, deathPos, CoinScatterRadius);

            if (spawned > 0)
                Debug.Log($"[PlayerDeathDropSystem] Dropped {total} coin(s) across {spawned} pile(s).");
        }
    }
}
