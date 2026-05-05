using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Tests covering fixed-array semantics: slot count after Initialize, slot
    /// stability after Remove, AddItem first-empty placement, and
    /// PredictAddTargetSlot.
    /// </summary>
    public class InventoryFixedArrayTests
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

        // ── Constants ──────────────────────────────────────────────────────────

        [Test]
        public void DefaultBagCapacity_Is25()
        {
            Assert.AreEqual(25, Inventory.DefaultBagCapacity);
        }

        [Test]
        public void EquipmentCapacity_Is9()
        {
            Assert.AreEqual(9, Inventory.EquipmentCapacity);
        }

        // ── Initialize fixed-array semantics ───────────────────────────────────

        [Test]
        public void Initialize_SlotsCount_EqualsCapacity()
        {
            var inv = CreateInventory(10);
            Assert.AreEqual(10, inv.Slots.Count,
                "Slots.Count must equal capacity immediately after Initialize.");
        }

        [Test]
        public void Initialize_EquipmentSlotsCount_EqualsEquipmentCapacity()
        {
            var inv = CreateInventory(8);
            Assert.AreEqual(Inventory.EquipmentCapacity, inv.EquipmentSlots.Count,
                "EquipmentSlots.Count must always equal EquipmentCapacity.");
        }

        [Test]
        public void Initialize_AllSlotsStart_Empty()
        {
            var inv = CreateInventory(5);
            for (int i = 0; i < inv.Slots.Count; i++)
                Assert.IsTrue(inv.Slots[i].IsEmpty, $"Slot {i} should be empty after Initialize.");
        }

        [Test]
        public void Initialize_AllEquipSlotsStart_Empty()
        {
            var inv = CreateInventory(5);
            for (int i = 0; i < inv.EquipmentSlots.Count; i++)
                Assert.IsTrue(inv.EquipmentSlots[i].IsEmpty,
                    $"EquipmentSlot {i} should be empty after Initialize.");
        }

        // ── Slot stability after Remove ────────────────────────────────────────

        [Test]
        public void RemoveEntireStack_SlotBecomesEmpty_NotGone()
        {
            // Fill slot 0 with a sword, slot 1 stays empty, slot 2 with a shield.
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            var shield = CreateItem("shield");

            inv.AddItem(sword);    // -> slot 0
            inv.AddItem(shield);   // -> slot 1

            // Remove entire sword stack.
            inv.RemoveItem(sword, 1);

            // The list length must not change.
            Assert.AreEqual(5, inv.Slots.Count,
                "Removing a stack must not shrink the fixed-size slot list.");

            // Slot 0 must be empty, not filled by shield shifting down.
            Assert.IsTrue(inv.Slots[0].IsEmpty,
                "Slot 0 should be empty after the only item in it is removed.");

            // Shield must still be at slot 1 (index unchanged).
            Assert.IsFalse(inv.Slots[1].IsEmpty,
                "Slot 1 should still contain shield — indices must not shift.");
            Assert.AreEqual(shield, inv.Slots[1].Item);
        }

        [Test]
        public void RemovePartialStack_LeavesSlotAtSameIndex()
        {
            var inv = CreateInventory(5);
            var potion = CreateItem("potion", stackable: true, maxStack: 10);

            inv.AddItem(potion, 7);   // -> slot 0

            inv.RemoveItem(potion, 3);

            Assert.AreEqual(5, inv.Slots.Count);
            Assert.IsFalse(inv.Slots[0].IsEmpty);
            Assert.AreEqual(4, inv.Slots[0].Quantity);
        }

        // ── AddItem first-empty placement ──────────────────────────────────────

        [Test]
        public void AddItem_PlacesAtFirstEmptyIndex_WhenNoPartialStack()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            var potion = CreateItem("potion");

            // Put sword in slot 0, potion in slot 1.
            inv.AddItem(sword);
            inv.AddItem(potion);

            // Remove sword, making slot 0 empty again.
            inv.RemoveItem(sword, 1);

            // Now add a shield — should go to slot 0 (first empty).
            var shield = CreateItem("shield");
            int overflow = inv.AddItem(shield);

            Assert.AreEqual(0, overflow);
            Assert.IsFalse(inv.Slots[0].IsEmpty,
                "After Remove vacates slot 0, the next AddItem should fill slot 0.");
            Assert.AreEqual(shield, inv.Slots[0].Item);
        }

        [Test]
        public void AddItem_StackablePartial_FillsExistingSlotBeforeNewOne()
        {
            var inv = CreateInventory(5);
            var arrow = CreateItem("arrow", stackable: true, maxStack: 10);

            inv.AddItem(arrow, 5);  // slot 0 gets 5
            inv.AddItem(arrow, 3);  // should stack into slot 0 (up to 10), not use slot 1

            Assert.AreEqual(8, inv.Slots[0].Quantity);
            Assert.IsTrue(inv.Slots[1].IsEmpty,
                "The second AddItem should stack, not spill into slot 1.");
        }

        // ── PredictAddTargetSlot ───────────────────────────────────────────────

        [Test]
        public void PredictAddTargetSlot_Stackable_WithPartial_ReturnsPartialIndex()
        {
            var inv = CreateInventory(5);
            var arrow = CreateItem("arrow", stackable: true, maxStack: 10);
            inv.AddItem(arrow, 5);  // partial in slot 0

            int predicted = inv.PredictAddTargetSlot(arrow, 1);
            Assert.AreEqual(0, predicted,
                "Should predict the existing partial stack's slot.");
        }

        [Test]
        public void PredictAddTargetSlot_Stackable_NoPartial_ReturnsFirstEmpty()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            var arrow = CreateItem("arrow", stackable: true, maxStack: 10);

            inv.AddItem(sword);  // fills slot 0

            int predicted = inv.PredictAddTargetSlot(arrow, 1);
            Assert.AreEqual(1, predicted,
                "No partial for stackable; first empty is slot 1.");
        }

        [Test]
        public void PredictAddTargetSlot_NonStackable_ReturnsFirstEmpty()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            inv.AddItem(sword);  // fills slot 0

            var shield = CreateItem("shield");
            int predicted = inv.PredictAddTargetSlot(shield, 1);
            Assert.AreEqual(1, predicted,
                "Non-stackable: first empty is slot 1.");
        }

        [Test]
        public void PredictAddTargetSlot_FullInventory_ReturnsMinusOne()
        {
            var inv = CreateInventory(2);
            var sword = CreateItem("sword");
            var shield = CreateItem("shield");
            inv.AddItem(sword);
            inv.AddItem(shield);

            Assert.AreEqual(-1, inv.PredictAddTargetSlot(CreateItem("gem"), 1));
        }

        [Test]
        public void PredictAddTargetSlot_NullItem_ReturnsMinusOne()
        {
            var inv = CreateInventory(5);
            Assert.AreEqual(-1, inv.PredictAddTargetSlot(null, 1));
        }

        [Test]
        public void PredictAddTargetSlot_ZeroQuantity_ReturnsMinusOne()
        {
            var inv = CreateInventory(5);
            var arrow = CreateItem("arrow", stackable: true, maxStack: 10);
            Assert.AreEqual(-1, inv.PredictAddTargetSlot(arrow, 0));
        }
    }
}
