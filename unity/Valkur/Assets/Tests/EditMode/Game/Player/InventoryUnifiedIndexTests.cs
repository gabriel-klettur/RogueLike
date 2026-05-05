using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Tests for the unified index-space helpers:
    ///   IsEquipmentIndex, GetSlotByIndex, TryDepositInIndex, MoveSlotByIndex.
    ///
    /// Unified indices:
    ///   [0, Capacity)                    → bag
    ///   [Capacity, Capacity+9)           → equipment (9 slots)
    /// </summary>
    public class InventoryUnifiedIndexTests
    {
        private const int Cap = 10;  // small capacity for legibility

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

        private Inventory CreateInventory(int capacity = Cap)
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

        // ── IsEquipmentIndex ──────────────────────────────────────────────────

        [Test]
        public void IsEquipmentIndex_BagLastIndex_ReturnsFalse()
        {
            var inv = CreateInventory(Cap);
            // Last bag index is Cap-1.
            Assert.IsFalse(inv.IsEquipmentIndex(Cap - 1),
                "Cap-1 is still in the bag range.");
        }

        [Test]
        public void IsEquipmentIndex_FirstEquipIndex_ReturnsTrue()
        {
            var inv = CreateInventory(Cap);
            // First equipment index is Capacity+0 = Cap.
            Assert.IsTrue(inv.IsEquipmentIndex(Cap),
                "Cap is the first equipment unified index.");
        }

        [Test]
        public void IsEquipmentIndex_LastEquipIndex_ReturnsTrue()
        {
            var inv = CreateInventory(Cap);
            Assert.IsTrue(inv.IsEquipmentIndex(Cap + Inventory.EquipmentCapacity - 1),
                "Cap + EquipmentCapacity - 1 is the last equipment index.");
        }

        [Test]
        public void IsEquipmentIndex_BeyondEquip_ReturnsFalse()
        {
            var inv = CreateInventory(Cap);
            Assert.IsFalse(inv.IsEquipmentIndex(Cap + Inventory.EquipmentCapacity),
                "Index at Cap+EquipmentCapacity is out of range.");
        }

        [Test]
        public void IsEquipmentIndex_NegativeIndex_ReturnsFalse()
        {
            var inv = CreateInventory(Cap);
            Assert.IsFalse(inv.IsEquipmentIndex(-1));
        }

        // ── GetSlotByIndex ────────────────────────────────────────────────────

        [Test]
        public void GetSlotByIndex_BagIndex_ReturnsBagSlot()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            inv.SetSlot(3, sword, 1);

            var slot = inv.GetSlotByIndex(3);
            Assert.IsFalse(slot.IsEmpty);
            Assert.AreEqual(sword, slot.Item);
        }

        [Test]
        public void GetSlotByIndex_EquipIndex_ReturnsEquipSlot()
        {
            var inv = CreateInventory(Cap);
            var armor = CreateItem("armor");
            inv.SetEquipmentSlot(5, armor, 1);

            // Unified index for equipment slot 5 = Cap + 5.
            var slot = inv.GetSlotByIndex(Cap + 5);
            Assert.IsFalse(slot.IsEmpty);
            Assert.AreEqual(armor, slot.Item);
        }

        [Test]
        public void GetSlotByIndex_OutOfRange_ReturnsDefault()
        {
            var inv = CreateInventory(Cap);
            // Beyond equipment range.
            var slot = inv.GetSlotByIndex(Cap + Inventory.EquipmentCapacity);
            Assert.IsTrue(slot.IsEmpty);

            // Negative.
            slot = inv.GetSlotByIndex(-1);
            Assert.IsTrue(slot.IsEmpty);
        }

        // ── TryDepositInIndex ─────────────────────────────────────────────────

        [Test]
        public void TryDepositInIndex_BagRange_RoutesToBagSlot()
        {
            var inv = CreateInventory(Cap);
            var potion = CreateItem("potion", stackable: true, maxStack: 10);
            int placed = inv.TryDepositInIndex(4, potion, 3);
            Assert.AreEqual(3, placed);
            Assert.AreEqual(potion, inv.Slots[4].Item);
        }

        [Test]
        public void TryDepositInIndex_EquipRange_RoutesToEquipSlot()
        {
            var inv = CreateInventory(Cap);
            var shield = CreateItem("shield");
            // Equipment slot 5 → unified index Cap + 5.
            int placed = inv.TryDepositInIndex(Cap + 5, shield, 1);
            Assert.AreEqual(1, placed);
            Assert.AreEqual(shield, inv.EquipmentSlots[5].Item);
        }

        [Test]
        public void TryDepositInIndex_NegativeIndex_ReturnsZero()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            Assert.AreEqual(0, inv.TryDepositInIndex(-1, sword, 1));
        }

        [Test]
        public void TryDepositInIndex_BeyondBothRanges_ReturnsZero()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            int oob = Cap + Inventory.EquipmentCapacity + 99;
            Assert.AreEqual(0, inv.TryDepositInIndex(oob, sword, 1));
        }

        // ── MoveSlotByIndex ───────────────────────────────────────────────────

        [Test]
        public void MoveSlotByIndex_BagToBag_DifferentItems_Swaps()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            var shield = CreateItem("shield");
            inv.SetSlot(0, sword, 1);
            inv.SetSlot(1, shield, 1);

            bool moved = inv.MoveSlotByIndex(0, 1);

            Assert.IsTrue(moved);
            Assert.AreEqual(shield, inv.Slots[0].Item);
            Assert.AreEqual(sword, inv.Slots[1].Item);
        }

        [Test]
        public void MoveSlotByIndex_BagToBag_SameStackableItem_MergesRespectingMaxStack()
        {
            var inv = CreateInventory(Cap);
            var arrow = CreateItem("arrow", stackable: true, maxStack: 10);
            inv.SetSlot(0, arrow, 4);
            inv.SetSlot(1, arrow, 3);

            bool moved = inv.MoveSlotByIndex(0, 1);

            Assert.IsTrue(moved);
            // src=0 had 4, dst=1 had 3 → merge: dst gets 7, src gets 0 (emptied)
            Assert.AreEqual(7, inv.Slots[1].Quantity);
            Assert.IsTrue(inv.Slots[0].IsEmpty);
        }

        [Test]
        public void MoveSlotByIndex_BagToBag_StackMerge_MaxStackOverflow_SrcKeepsRemainder()
        {
            var inv = CreateInventory(Cap);
            var arrow = CreateItem("arrow", stackable: true, maxStack: 10);
            inv.SetSlot(0, arrow, 8);   // src
            inv.SetSlot(1, arrow, 6);   // dst (only 4 room)

            bool moved = inv.MoveSlotByIndex(0, 1);

            Assert.IsTrue(moved);
            Assert.AreEqual(10, inv.Slots[1].Quantity, "dst hits maxStack cap.");
            Assert.AreEqual(4, inv.Slots[0].Quantity, "src keeps remaining 4.");
        }

        [Test]
        public void MoveSlotByIndex_BagToBag_EmptyDst_MovesWholeStack()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            inv.SetSlot(2, sword, 1);

            bool moved = inv.MoveSlotByIndex(2, 7);

            Assert.IsTrue(moved);
            Assert.IsTrue(inv.Slots[2].IsEmpty);
            Assert.AreEqual(sword, inv.Slots[7].Item);
        }

        [Test]
        public void MoveSlotByIndex_BagToEquip_Swaps()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            var helm = CreateItem("helm");
            inv.SetSlot(0, sword, 1);
            inv.SetEquipmentSlot(0, helm, 1);

            // Equipment slot 0 → unified index Cap + 0 = Cap.
            bool moved = inv.MoveSlotByIndex(0, Cap);

            Assert.IsTrue(moved);
            Assert.AreEqual(helm, inv.Slots[0].Item,
                "Bag slot 0 should now contain what was in equipment slot 0.");
            Assert.AreEqual(sword, inv.EquipmentSlots[0].Item,
                "Equipment slot 0 should now contain what was in bag slot 0.");
        }

        [Test]
        public void MoveSlotByIndex_EquipToBag_Swaps()
        {
            var inv = CreateInventory(Cap);
            var shield = CreateItem("shield");
            inv.SetEquipmentSlot(2, shield, 1);

            bool moved = inv.MoveSlotByIndex(Cap + 2, 5);

            Assert.IsTrue(moved);
            Assert.IsTrue(inv.EquipmentSlots[2].IsEmpty);
            Assert.AreEqual(shield, inv.Slots[5].Item);
        }

        [Test]
        public void MoveSlotByIndex_EquipToEquip_Swaps()
        {
            var inv = CreateInventory(Cap);
            var ring = CreateItem("ring");
            var amulet = CreateItem("amulet");
            inv.SetEquipmentSlot(0, ring, 1);
            inv.SetEquipmentSlot(8, amulet, 1);

            bool moved = inv.MoveSlotByIndex(Cap + 0, Cap + 8);

            Assert.IsTrue(moved);
            Assert.AreEqual(amulet, inv.EquipmentSlots[0].Item);
            Assert.AreEqual(ring, inv.EquipmentSlots[8].Item);
        }

        [Test]
        public void MoveSlotByIndex_SrcEmpty_ReturnsFalse()
        {
            var inv = CreateInventory(Cap);
            bool moved = inv.MoveSlotByIndex(0, 1);
            Assert.IsFalse(moved);
        }

        [Test]
        public void MoveSlotByIndex_SrcEqualsDst_ReturnsFalse()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            inv.SetSlot(3, sword, 1);
            bool moved = inv.MoveSlotByIndex(3, 3);
            Assert.IsFalse(moved);
        }

        [Test]
        public void MoveSlotByIndex_SrcOutOfBounds_ReturnsFalse()
        {
            var inv = CreateInventory(Cap);
            bool moved = inv.MoveSlotByIndex(-1, 0);
            Assert.IsFalse(moved);
        }

        [Test]
        public void MoveSlotByIndex_DstOutOfBounds_ReturnsFalse()
        {
            var inv = CreateInventory(Cap);
            var sword = CreateItem("sword");
            inv.SetSlot(0, sword, 1);
            int oob = Cap + Inventory.EquipmentCapacity + 5;
            bool moved = inv.MoveSlotByIndex(0, oob);
            Assert.IsFalse(moved);
        }
    }
}
