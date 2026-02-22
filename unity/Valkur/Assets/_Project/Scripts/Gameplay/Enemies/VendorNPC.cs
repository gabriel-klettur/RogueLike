using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

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
            // TODO: Open shop UI
            Debug.Log($"[VendorNPC] Shop opened: {npc.NPCName} with {shopInventory.Count} items");
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
    }
}
