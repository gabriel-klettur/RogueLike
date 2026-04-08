using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// Vendor NPC that sells/buys items.
    /// Maps to Python's vendor NPC system with buy/sell price support.
    /// </summary>
    [RequireComponent(typeof(NPCInteractable))]
    public class VendorNPC : MonoBehaviour
    {
        [Header("Shop")]
        [SerializeField] private List<ShopEntry> shopInventory = new List<ShopEntry>();
        [SerializeField] private float buyPriceMultiplier = 1.0f;
        [SerializeField] private float sellPriceMultiplier = 0.5f;

        private NPCInteractable _interactable;

        [System.Serializable]
        public struct ShopEntry
        {
            public ItemDefinition item;
            public int stock;
            public int priceOverride;
        }

        public IReadOnlyList<ShopEntry> ShopInventory => shopInventory;
        public float BuyMultiplier => buyPriceMultiplier;
        public float SellMultiplier => sellPriceMultiplier;

        private void Awake()
        {
            _interactable = GetComponent<NPCInteractable>();
        }

        private void OnEnable()
        {
            _interactable.OnInteract += HandleInteract;
        }

        private void OnDisable()
        {
            _interactable.OnInteract -= HandleInteract;
        }

        private void HandleInteract(NPCInteractable npc)
        {
            var playerGo = Valkur.Core.EntityRegistry.PlayerTransform?.gameObject;
            if (playerGo == null)
            {
                Debug.LogWarning("[VendorNPC] No player found to open shop.");
                return;
            }

            var shopUI = VendorShopUI.Instance;
            if (shopUI == null)
            {
                Debug.LogWarning("[VendorNPC] VendorShopUI singleton not found in scene.");
                return;
            }

            var playerInventory = playerGo.GetComponent<Inventory.Inventory>();
            var playerWallet = playerGo.GetComponent<CurrencyWallet>();
            shopUI.OpenShop(this, playerInventory, playerWallet);
        }

        public int GetBuyPrice(ItemDefinition item)
        {
            foreach (var entry in shopInventory)
            {
                if (entry.item == item && entry.priceOverride > 0)
                    return entry.priceOverride;
            }
            return Mathf.RoundToInt(item.buyPrice * buyPriceMultiplier);
        }

        public int GetSellPrice(ItemDefinition item)
        {
            if (item == null) return 0;
            int basePrice = item.sellPrice > 0 ? item.sellPrice : item.buyPrice;
            return Mathf.RoundToInt(basePrice * sellPriceMultiplier);
        }

        public bool TryBuyItem(ItemDefinition item, Inventory.Inventory playerInventory, ref int playerGold)
        {
            int price = GetBuyPrice(item);
            if (playerGold < price) return false;
            if (playerInventory.IsFull) return false;

            // Check stock
            for (int i = 0; i < shopInventory.Count; i++)
            {
                if (shopInventory[i].item == item && shopInventory[i].stock > 0)
                {
                    var entry = shopInventory[i];
                    entry.stock--;
                    shopInventory[i] = entry;

                    playerGold -= price;
                    playerInventory.AddItem(item);
                    return true;
                }
            }
            return false;
        }

        public bool TrySellItem(ItemDefinition item, Inventory.Inventory playerInventory, ref int playerGold)
        {
            if (!playerInventory.HasItem(item)) return false;

            int price = GetSellPrice(item);
            playerInventory.RemoveItem(item);
            playerGold += price;
            return true;
        }

        /// <summary>Buy an item using a CurrencyWallet. Automatically handles refunds on failure.</summary>
        public bool TryBuyItem(ItemDefinition item, Inventory.Inventory playerInventory, CurrencyWallet wallet)
        {
            int price = GetBuyPrice(item);
            if (!wallet.TrySpend(price)) return false;

            if (playerInventory.IsFull)
            {
                wallet.Add(price);
                return false;
            }

            for (int i = 0; i < shopInventory.Count; i++)
            {
                if (shopInventory[i].item == item && shopInventory[i].stock > 0)
                {
                    var entry = shopInventory[i];
                    entry.stock--;
                    shopInventory[i] = entry;
                    playerInventory.AddItem(item);
                    return true;
                }
            }

            wallet.Add(price); // refund — item not in stock
            return false;
        }

        /// <summary>Sell an item to this vendor using a CurrencyWallet.</summary>
        public bool TrySellItem(ItemDefinition item, Inventory.Inventory playerInventory, CurrencyWallet wallet)
        {
            if (!playerInventory.HasItem(item)) return false;
            int price = GetSellPrice(item);
            playerInventory.RemoveItem(item);
            wallet.Add(price);
            return true;
        }
    }
}
