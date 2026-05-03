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
        private const int   CoinChunkSize     = 25;

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

            int spawned = 0;
            int remaining = total;
            while (remaining > 0)
            {
                int chunk = Mathf.Min(CoinChunkSize, remaining);
                remaining -= chunk;

                Vector2 offset = Random.insideUnitCircle * CoinScatterRadius;
                Vector3 pos = deathPos + new Vector3(offset.x, offset.y, 0f);
                SpawnCoinPickup(pos, chunk);
                spawned++;
            }

            if (spawned > 0)
                Debug.Log($"[PlayerDeathDropSystem] Dropped {total} coin(s) across {spawned} pile(s).");
        }

        private static void SpawnCoinPickup(Vector3 position, int amount)
        {
            var go = new GameObject($"CoinDrop_{amount}");
            int pickupLayer = LayerMask.NameToLayer("Pickup");
            if (pickupLayer >= 0) go.layer = pickupLayer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CoinSpriteFactory.GetOrCreate();
            sr.color = new Color(1f, 0.85f, 0.25f, 1f);
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;

            go.AddComponent<CircleCollider2D>(); // CoinPickup's RequireComponent
            var coin = go.AddComponent<CoinPickup>();
            coin.Initialize(amount, position);
        }

        private static class CoinSpriteFactory
        {
            private static Sprite s_Sprite;

            public static Sprite GetOrCreate()
            {
                if (s_Sprite != null) return s_Sprite;
                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "CoinDropPlaceholder",
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color[64];
                Color gold = new Color(1f, 0.85f, 0.2f, 1f);
                Color empty = new Color(0f, 0f, 0f, 0f);
                // Round-ish disc inside an 8×8 grid.
                for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    float dx = x - 3.5f;
                    float dy = y - 3.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * 8 + x] = r <= 3.5f ? gold : empty;
                }
                tex.SetPixels(pixels);
                tex.Apply(false);
                s_Sprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 16f);
                s_Sprite.name = "CoinDropPlaceholderSprite";
                s_Sprite.hideFlags = HideFlags.DontSave;
                return s_Sprite;
            }

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStatics() => s_Sprite = null;
        }
    }
}
