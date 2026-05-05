using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Tests for ToSaveData schema 2.0 and round-trip fidelity.
    /// GameStateRestorer requires an EntityRegistry.Player GameObject and a
    /// ServiceLocator-registered ItemCatalog — both unavailable in EditMode
    /// without a Scene. The restorer test (item 11) is therefore skipped;
    /// instead we validate the inverse manually via SetSlot / SetEquipmentSlot,
    /// which is exactly what GameStateRestorer calls internally.
    /// </summary>
    public class InventoryPersistenceTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _assets)
                if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();
        }

        private Inventory CreateInventory(int capacity = 5)
        {
            var go = new GameObject("TestInventory");
            _scene.Add(go);
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
            _assets.Add(item);
            return item;
        }

        // ── ToSaveData schema 2.0 ──────────────────────────────────────────────

        [Test]
        public void ToSaveData_SlotCount_AlwaysEqualsCapacity()
        {
            var inv = CreateInventory(8);
            var data = inv.ToSaveData("p1");
            Assert.AreEqual(8, data.slots.Count,
                "data.slots.Count must equal capacity even with no items.");
        }

        [Test]
        public void ToSaveData_EquipmentSlotCount_AlwaysEqualsEquipmentCapacity()
        {
            var inv = CreateInventory(5);
            var data = inv.ToSaveData("p1");
            Assert.AreEqual(Inventory.EquipmentCapacity, data.equipmentSlots.Count,
                "data.equipmentSlots.Count must always equal EquipmentCapacity (9).");
        }

        [Test]
        public void ToSaveData_EmptySlots_SerialiseAsEmptyIdAndZeroQty()
        {
            var inv = CreateInventory(5);
            var data = inv.ToSaveData("p1");
            foreach (var slot in data.slots)
            {
                Assert.AreEqual("", slot.itemId,
                    "Empty bag slot must serialise with itemId=\"\".");
                Assert.AreEqual(0, slot.quantity,
                    "Empty bag slot must serialise with quantity=0.");
            }
        }

        [Test]
        public void ToSaveData_EmptyEquipSlots_SerialiseAsEmptyIdAndZeroQty()
        {
            var inv = CreateInventory(5);
            var data = inv.ToSaveData("p1");
            foreach (var slot in data.equipmentSlots)
            {
                Assert.AreEqual("", slot.itemId);
                Assert.AreEqual(0, slot.quantity);
            }
        }

        [Test]
        public void ToSaveData_ItemAtIndex3_SerializesAtIndex3()
        {
            var inv = CreateInventory(5);
            var potion = CreateItem("potion", stackable: true, maxStack: 10);
            inv.SetSlot(3, potion, 7);

            var data = inv.ToSaveData("p1");
            Assert.AreEqual("potion", data.slots[3].itemId);
            Assert.AreEqual(7, data.slots[3].quantity);
            // Surrounding slots must be empty.
            Assert.AreEqual("", data.slots[0].itemId);
            Assert.AreEqual("", data.slots[4].itemId);
        }

        [Test]
        public void ToSaveData_EquipItem_SerializesInEquipmentSlots()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            inv.SetEquipmentSlot(4, sword, 1);

            var data = inv.ToSaveData("p1");
            Assert.AreEqual("sword", data.equipmentSlots[4].itemId);
            Assert.AreEqual(1, data.equipmentSlots[4].quantity);
            // Other equip slots empty.
            Assert.AreEqual("", data.equipmentSlots[0].itemId);
        }

        [Test]
        public void ToSaveData_PlayerId_PreservedInData()
        {
            var inv = CreateInventory(5);
            var data = inv.ToSaveData("hero-42");
            Assert.AreEqual("hero-42", data.playerId);
        }

        [Test]
        public void ToSaveData_Capacity_MatchesInitializeArg()
        {
            var inv = CreateInventory(12);
            var data = inv.ToSaveData("p1");
            Assert.AreEqual(12, data.capacity);
        }

        [Test]
        public void ToSaveData_SchemaVersion_Is2_0()
        {
            var inv = CreateInventory(5);
            var data = inv.ToSaveData("p1");
            Assert.AreEqual("2.0", data.schemaVersion);
        }

        // ── Round-trip via SetSlot / SetEquipmentSlot ─────────────────────────

        [Test]
        public void RoundTrip_BagAndEquipment_PreservesLayoutAtSameIndices()
        {
            // Arrange: source inventory with items at specific indices.
            const int capacity = 10;
            var src = CreateInventory(capacity);
            var sword = CreateItem("sword");
            var potion = CreateItem("potion", stackable: true, maxStack: 20);
            var ring = CreateItem("ring");

            src.SetSlot(0, sword, 1);
            src.SetSlot(7, potion, 12);
            src.SetEquipmentSlot(3, ring, 1);

            // Act: serialise.
            var data = src.ToSaveData("p1");

            // Create a fresh inventory and restore manually (same as
            // GameStateRestorer does after finding items in the catalog).
            var dst = CreateInventory(capacity);
            for (int i = 0; i < data.slots.Count; i++)
            {
                var s = data.slots[i];
                if (!string.IsNullOrEmpty(s.itemId) && s.quantity > 0)
                {
                    // In production, the catalog resolves itemId → ItemDefinition.
                    // Here we map manually.
                    ItemDefinition def = s.itemId == "sword" ? sword
                                      : s.itemId == "potion" ? potion
                                      : s.itemId == "ring" ? ring : null;
                    if (def != null) dst.SetSlot(i, def, s.quantity);
                }
            }
            for (int i = 0; i < data.equipmentSlots.Count; i++)
            {
                var s = data.equipmentSlots[i];
                if (!string.IsNullOrEmpty(s.itemId) && s.quantity > 0)
                {
                    ItemDefinition def = s.itemId == "ring" ? ring : null;
                    if (def != null) dst.SetEquipmentSlot(i, def, s.quantity);
                }
            }

            // Assert: visual indices preserved.
            Assert.AreEqual(sword, dst.Slots[0].Item, "Bag slot 0 must hold sword.");
            Assert.AreEqual(1, dst.Slots[0].Quantity);

            Assert.IsTrue(dst.Slots[1].IsEmpty, "Slot 1 must remain empty.");

            Assert.AreEqual(potion, dst.Slots[7].Item, "Bag slot 7 must hold potion.");
            Assert.AreEqual(12, dst.Slots[7].Quantity);

            Assert.AreEqual(ring, dst.EquipmentSlots[3].Item,
                "Equipment slot 3 must hold ring.");
            Assert.IsTrue(dst.EquipmentSlots[0].IsEmpty,
                "Equipment slot 0 must remain empty.");

            // Layout dimensions.
            Assert.AreEqual(capacity, dst.Slots.Count);
            Assert.AreEqual(Inventory.EquipmentCapacity, dst.EquipmentSlots.Count);
        }

        [Test]
        public void RoundTrip_EmptyInventory_ProducesAllEmptyAfterRestore()
        {
            var src = CreateInventory(5);
            var data = src.ToSaveData("p1");

            var dst = CreateInventory(5);
            // Nothing to restore — all slots stay at default.
            for (int i = 0; i < data.slots.Count; i++)
                Assert.IsTrue(dst.Slots[i].IsEmpty, $"Bag slot {i} should be empty.");
            for (int i = 0; i < data.equipmentSlots.Count; i++)
                Assert.IsTrue(dst.EquipmentSlots[i].IsEmpty,
                    $"Equipment slot {i} should be empty.");
        }

        [Test]
        public void RoundTrip_CapacityBump_OldSaveCapacity20_RestoresToDefaultCapacity25()
        {
            // Simulate the GameStateRestorer bump:
            // Old saves may store capacity=20; the restorer forces at least
            // DefaultBagCapacity (25) on Initialize.
            const int oldCapacity = 20;
            var src = CreateInventory(oldCapacity);
            var sword = CreateItem("sword");
            src.SetSlot(0, sword, 1);
            src.SetSlot(5, sword, 1);
            src.SetSlot(12, sword, 1);

            var data = src.ToSaveData("p1");
            Assert.AreEqual(oldCapacity, data.capacity,
                "The data must store the original capacity so the restorer can read it.");

            // Simulated restorer bump.
            int restoredCapacity = Mathf.Max(data.capacity, Inventory.DefaultBagCapacity);
            Assert.AreEqual(25, restoredCapacity,
                "Capacity should be bumped to DefaultBagCapacity (25).");

            var dst = CreateInventory(restoredCapacity);
            Assert.AreEqual(25, dst.Slots.Count,
                "Restored inventory must have 25 slots.");
            Assert.AreEqual(25, dst.Capacity);

            // Restore items at their original visual indices.
            int max = Mathf.Min(data.slots.Count, oldCapacity);
            for (int i = 0; i < max; i++)
            {
                var s = data.slots[i];
                if (!string.IsNullOrEmpty(s.itemId) && s.quantity > 0)
                    dst.SetSlot(i, sword, s.quantity);
            }

            Assert.AreEqual(sword, dst.Slots[0].Item);
            Assert.AreEqual(sword, dst.Slots[5].Item);
            Assert.AreEqual(sword, dst.Slots[12].Item);
            Assert.IsTrue(dst.Slots[1].IsEmpty);
        }
    }
}
