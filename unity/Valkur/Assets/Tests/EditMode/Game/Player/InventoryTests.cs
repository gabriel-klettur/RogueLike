using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.Player
{
    public class InventoryTests
    {
        private Inventory CreateInventory(int capacity = 5)
        {
            var go = new GameObject("TestInventory");
            var inv = go.AddComponent<Inventory>();
            inv.Initialize(capacity);
            return inv;
        }

        private ItemDefinition CreateItem(string id, bool stackable = false, int maxStack = 1)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.stackable = stackable;
            item.maxStack = maxStack;
            item.buyPrice = 10;
            item.sellPrice = 5;
            return item;
        }

        private void Cleanup(Inventory inv)
        {
            Object.DestroyImmediate(inv.gameObject);
        }

        // --- Basic Add/Remove ---

        [Test]
        public void AddItem_SingleNonStackable_OccupiesOneSlot()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("sword");
            int overflow = inv.AddItem(item);
            Assert.AreEqual(0, overflow);
            Assert.AreEqual(1, inv.UsedSlots);
            Assert.AreEqual(1, inv.GetItemCount(item));
            Cleanup(inv);
        }

        [Test]
        public void AddItem_Stackable_StacksInSameSlot()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 3);
            inv.AddItem(item, 4);
            Assert.AreEqual(1, inv.UsedSlots);
            Assert.AreEqual(7, inv.GetItemCount(item));
            Cleanup(inv);
        }

        [Test]
        public void AddItem_Stackable_OverflowsToNewSlot()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("arrow", stackable: true, maxStack: 5);
            inv.AddItem(item, 8);
            Assert.AreEqual(2, inv.UsedSlots);
            Assert.AreEqual(8, inv.GetItemCount(item));
            Cleanup(inv);
        }

        [Test]
        public void AddItem_FullInventory_ReturnsOverflow()
        {
            var inv = CreateInventory(2);
            var item = CreateItem("gem");
            inv.AddItem(item, 1);
            inv.AddItem(item, 1);
            int overflow = inv.AddItem(item, 1);
            Assert.AreEqual(1, overflow);
            Assert.AreEqual(2, inv.UsedSlots);
            Cleanup(inv);
        }

        [Test]
        public void AddItem_NullItem_ReturnsFullQuantity()
        {
            var inv = CreateInventory(5);
            int overflow = inv.AddItem(null, 3);
            Assert.AreEqual(3, overflow);
            Assert.AreEqual(0, inv.UsedSlots);
            Cleanup(inv);
        }

        [Test]
        public void AddItem_ZeroQuantity_ReturnsZero()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("sword");
            int overflow = inv.AddItem(item, 0);
            Assert.AreEqual(0, overflow);
            Assert.AreEqual(0, inv.UsedSlots);
            Cleanup(inv);
        }

        [Test]
        public void RemoveItem_ReducesCount()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 5);
            int removed = inv.RemoveItem(item, 3);
            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, inv.GetItemCount(item));
            Cleanup(inv);
        }

        [Test]
        public void RemoveItem_EntireStack_RemovesSlot()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 5);
            inv.RemoveItem(item, 5);
            Assert.AreEqual(0, inv.UsedSlots);
            Assert.AreEqual(0, inv.GetItemCount(item));
            Cleanup(inv);
        }

        [Test]
        public void RemoveItem_MoreThanAvailable_RemovesOnlyAvailable()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 3);
            int removed = inv.RemoveItem(item, 10);
            Assert.AreEqual(3, removed);
            Assert.AreEqual(0, inv.UsedSlots);
            Cleanup(inv);
        }

        [Test]
        public void RemoveItem_ItemNotInInventory_ReturnsZero()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("sword");
            int removed = inv.RemoveItem(item, 1);
            Assert.AreEqual(0, removed);
            Cleanup(inv);
        }

        // --- HasItem ---

        [Test]
        public void HasItem_ReturnsTrueWhenSufficient()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 5);
            Assert.IsTrue(inv.HasItem(item, 5));
            Assert.IsTrue(inv.HasItem(item, 1));
            Cleanup(inv);
        }

        [Test]
        public void HasItem_ReturnsFalseWhenInsufficient()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 2);
            Assert.IsFalse(inv.HasItem(item, 3));
            Cleanup(inv);
        }

        [Test]
        public void HasItem_NullItem_ReturnsFalse()
        {
            var inv = CreateInventory(5);
            Assert.IsFalse(inv.HasItem(null));
            Cleanup(inv);
        }

        // --- IsFull ---

        [Test]
        public void IsFull_ReturnsTrueWhenCapacityReached()
        {
            var inv = CreateInventory(2);
            var item = CreateItem("gem");
            inv.AddItem(item, 1);
            Assert.IsFalse(inv.IsFull);
            inv.AddItem(item, 1);
            Assert.IsTrue(inv.IsFull);
            Cleanup(inv);
        }

        // --- Clear ---

        [Test]
        public void Clear_RemovesAllItems()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 8);
            inv.Clear();
            Assert.AreEqual(0, inv.UsedSlots);
            Assert.AreEqual(0, inv.GetItemCount(item));
            Cleanup(inv);
        }

        // --- Events ---

        [Test]
        public void OnInventoryChanged_FiresOnAdd()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("sword");
            bool fired = false;
            inv.OnInventoryChanged += () => fired = true;
            inv.AddItem(item);
            Assert.IsTrue(fired);
            Cleanup(inv);
        }

        [Test]
        public void OnInventoryChanged_FiresOnRemove()
        {
            var inv = CreateInventory(5);
            var item = CreateItem("sword");
            inv.AddItem(item);
            bool fired = false;
            inv.OnInventoryChanged += () => fired = true;
            inv.RemoveItem(item);
            Assert.IsTrue(fired);
            Cleanup(inv);
        }

        // --- Serialization ---

        [Test]
        public void ToSaveData_ProducesCorrectStructure()
        {
            var inv = CreateInventory(10);
            var item = CreateItem("potion", stackable: true, maxStack: 10);
            inv.AddItem(item, 7);
            var data = inv.ToSaveData("player1");
            Assert.AreEqual("player1", data.playerId);
            Assert.AreEqual(10, data.capacity);
            Assert.AreEqual(1, data.slots.Count);
            Assert.AreEqual("potion", data.slots[0].itemId);
            Assert.AreEqual(7, data.slots[0].quantity);
            Cleanup(inv);
        }

        // --- Multiple item types ---

        [Test]
        public void MultipleItemTypes_TrackSeparately()
        {
            var inv = CreateInventory(10);
            var sword = CreateItem("sword");
            var shield = CreateItem("shield");
            var potion = CreateItem("potion", stackable: true, maxStack: 5);
            inv.AddItem(sword);
            inv.AddItem(shield);
            inv.AddItem(potion, 3);
            Assert.AreEqual(3, inv.UsedSlots);
            Assert.AreEqual(1, inv.GetItemCount(sword));
            Assert.AreEqual(1, inv.GetItemCount(shield));
            Assert.AreEqual(3, inv.GetItemCount(potion));
            Cleanup(inv);
        }
    }
}
