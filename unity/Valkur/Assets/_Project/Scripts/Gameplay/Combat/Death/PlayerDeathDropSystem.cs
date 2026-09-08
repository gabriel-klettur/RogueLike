using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Handles loot scatter when the player dies in the spirit/altar flow.
    ///
    /// <para>Mirrors <see cref="DeathDropSystem"/>, but always operates on the Player, has no
    /// questId filter, and also empties the <see cref="CurrencyWallet"/> as separated coin
    /// pickups. Invoked synchronously from <see cref="DeathSequenceController"/>'s coroutine
    /// rather than subscribing to <c>GameEvents.OnPlayerDied</c>, so the controller can guarantee
    /// the drops happen before the spirit transition — otherwise picking the corpse-position items
    /// would race with the spirit's own movement.</para>
    ///
    /// <para><b>What it drops is now a decision, not a constant.</b> <c>dropInventory</c>,
    /// <c>dropCoins</c> and <c>coinLossFraction</c> live on <see cref="DeathTuning"/>, because
    /// "how much does dying cost" is the balance question of the whole subsystem and it was
    /// hard-coded at "everything". Everything is a defensible answer and it must be a CHOSEN one:
    /// a total wipe on a bad pull is what makes a player stop playing rather than try again.</para>
    ///
    /// <para>Everything it spawns is registered with <see cref="DeathLitter"/>, so the next death
    /// can sweep it. Before that, drops accumulated forever.</para>
    /// </summary>
    public static class PlayerDeathDropSystem
    {
        private const float ItemScatterRadius = 1.5f;
        private const float CoinScatterRadius = 1.2f;

        public static void DropEverything(GameObject player)
        {
            if (player == null) return;
            Vector3 deathPos = player.transform.position;
            var tuning = DeathTuning.Active;

            if (tuning.dropInventory) DropInventory(player, deathPos);
            if (tuning.dropCoins) DropCurrency(player, deathPos, tuning.coinLossFraction);
        }

        private static void DropInventory(GameObject player, Vector3 deathPos)
        {
            var inventory = player.GetComponent<Inventory.Inventory>();
            if (inventory == null || inventory.UsedSlots == 0) return;

            // Snapshot the slots before clearing — the spawn loop must not see the inventory
            // mutating mid-iteration.
            int slotCount = inventory.Slots.Count;
            var snapshot = new (ItemDefinition item, int qty)[slotCount];
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
                if (pickup != null)
                {
                    pickup.gameObject.SetActive(true);
                    DeathLitter.Track(pickup);
                }
                dropped++;
            }

            inventory.Clear();

            if (dropped > 0)
                Debug.Log($"[PlayerDeathDropSystem] Dropped {dropped} inventory stack(s) at {deathPos}.");
        }

        /// <summary>
        /// Spill the purse.
        ///
        /// <para><paramref name="lossFraction"/> below 1 leaves the player a cushion. It is
        /// rounded with <c>FloorToInt</c> deliberately: at a 0.9 fraction on 5 coins, rounding UP
        /// would take all five and the cushion would silently not exist for small purses, which is
        /// exactly where a cushion matters.</para>
        /// </summary>
        private static void DropCurrency(GameObject player, Vector3 deathPos, float lossFraction)
        {
            var wallet = player.GetComponent<CurrencyWallet>();
            if (wallet == null || wallet.Coins <= 0) return;

            int held = wallet.Coins;
            int lost = Mathf.Clamp(Mathf.FloorToInt(held * Mathf.Clamp01(lossFraction)), 0, held);
            if (lost <= 0) return;

            wallet.SetBalance(held - lost);

            // The shell (layer, sprite, collider, sorting) and the chunking both live in
            // CoinDropSpawner, which DeathDropSystem's monster reward also goes through — a purse
            // spilled on death and a reward minted on a kill must look identical on the ground,
            // and two copies of that shell is how they stop being.
            int spawned = CoinDropSpawner.Spill(lost, deathPos, CoinScatterRadius, DeathLitter.Track);

            if (spawned > 0)
                Debug.Log($"[PlayerDeathDropSystem] Dropped {lost} of {held} coin(s) across {spawned} pile(s).");
        }
    }
}
