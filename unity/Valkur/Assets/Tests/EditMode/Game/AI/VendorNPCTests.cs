using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.NPC;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.AI
{
    public class VendorNPCTests
    {
        private ItemDefinition CreateItem(string id, int buyPrice = 100, int sellPrice = 50)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.buyPrice = buyPrice;
            item.sellPrice = sellPrice;
            item.stackable = false;
            item.maxStack = 1;
            return item;
        }

        // --- Price Calculations ---

        [Test]
        public void GetBuyPrice_UsesMultiplier()
        {
            var go = new GameObject("Vendor");
            go.AddComponent<NPCInteractable>();
            var vendor = go.AddComponent<VendorNPC>();
            var item = CreateItem("sword", buyPrice: 100);

            // Default buyPriceMultiplier = 1.0
            int price = vendor.GetBuyPrice(item);
            Assert.AreEqual(100, price);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void GetSellPrice_UsesMultiplier()
        {
            var go = new GameObject("Vendor");
            go.AddComponent<NPCInteractable>();
            var vendor = go.AddComponent<VendorNPC>();
            var item = CreateItem("sword", buyPrice: 100, sellPrice: 60);

            // Default sellPriceMultiplier = 0.5
            int price = vendor.GetSellPrice(item);
            Assert.AreEqual(30, price); // 60 * 0.5
            Object.DestroyImmediate(go);
        }

        [Test]
        public void GetSellPrice_NullItem_ReturnsZero()
        {
            var go = new GameObject("Vendor");
            go.AddComponent<NPCInteractable>();
            var vendor = go.AddComponent<VendorNPC>();
            Assert.AreEqual(0, vendor.GetSellPrice(null));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void GetSellPrice_ZeroSellPrice_UsesBuyPrice()
        {
            var go = new GameObject("Vendor");
            go.AddComponent<NPCInteractable>();
            var vendor = go.AddComponent<VendorNPC>();
            var item = CreateItem("gem", buyPrice: 200, sellPrice: 0);

            // sellPrice=0 -> falls back to buyPrice * sellMultiplier
            int price = vendor.GetSellPrice(item);
            Assert.AreEqual(100, price); // 200 * 0.5
            Object.DestroyImmediate(go);
        }

        // --- Buy/Sell Transactions ---

        [Test]
        public void TrySellItem_PlayerHasItem_Succeeds()
        {
            var vendorGo = new GameObject("Vendor");
            vendorGo.AddComponent<NPCInteractable>();
            var vendor = vendorGo.AddComponent<VendorNPC>();

            var invGo = new GameObject("PlayerInv");
            var inv = invGo.AddComponent<Inventory>();
            inv.Initialize(10);

            var item = CreateItem("sword", buyPrice: 100, sellPrice: 80);
            inv.AddItem(item);

            int gold = 0;
            bool result = vendor.TrySellItem(item, inv, ref gold);
            Assert.IsTrue(result);
            Assert.AreEqual(40, gold); // 80 * 0.5
            Assert.AreEqual(0, inv.GetItemCount(item));

            Object.DestroyImmediate(vendorGo);
            Object.DestroyImmediate(invGo);
        }

        [Test]
        public void TrySellItem_PlayerDoesNotHaveItem_Fails()
        {
            var vendorGo = new GameObject("Vendor");
            vendorGo.AddComponent<NPCInteractable>();
            var vendor = vendorGo.AddComponent<VendorNPC>();

            var invGo = new GameObject("PlayerInv");
            var inv = invGo.AddComponent<Inventory>();
            inv.Initialize(10);

            var item = CreateItem("sword");
            int gold = 50;
            bool result = vendor.TrySellItem(item, inv, ref gold);
            Assert.IsFalse(result);
            Assert.AreEqual(50, gold);

            Object.DestroyImmediate(vendorGo);
            Object.DestroyImmediate(invGo);
        }
    }
}
