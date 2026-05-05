using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Tests for equipment storage (TryDepositInEquipmentSlot, SetEquipmentSlot),
    /// and EquipmentView.Resolve regression guard against the old auto-mirror.
    /// </summary>
    public class InventoryEquipmentTests
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

        // ── TryDepositInEquipmentSlot ──────────────────────────────────────────

        [Test]
        public void TryDepositInEquipmentSlot_EmptySlot_AcceptsAnyItem()
        {
            var inv = CreateInventory();
            var weapon = CreateItem("sword");
            int placed = inv.TryDepositInEquipmentSlot(0, weapon, 1);
            Assert.AreEqual(1, placed);
            Assert.AreEqual(weapon, inv.EquipmentSlots[0].Item);
        }

        [Test]
        public void TryDepositInEquipmentSlot_NonStackable_PlacesOne()
        {
            var inv = CreateInventory();
            var weapon = CreateItem("sword");
            int placed = inv.TryDepositInEquipmentSlot(2, weapon, 5);
            // Non-stackable: at most 1 placed regardless of qty requested.
            Assert.AreEqual(1, placed);
            Assert.AreEqual(1, inv.EquipmentSlots[2].Quantity);
        }

        [Test]
        public void TryDepositInEquipmentSlot_StackableEmptySlot_PlacesUpToMaxStack()
        {
            var inv = CreateInventory();
            var arrow = CreateItem("arrow", stackable: true, maxStack: 20);
            int placed = inv.TryDepositInEquipmentSlot(1, arrow, 30);
            Assert.AreEqual(20, placed, "Should clamp to maxStack.");
            Assert.AreEqual(20, inv.EquipmentSlots[1].Quantity);
        }

        [Test]
        public void TryDepositInEquipmentSlot_SameItemStackable_StacksUpToRoom()
        {
            var inv = CreateInventory();
            var arrow = CreateItem("arrow", stackable: true, maxStack: 10);
            inv.TryDepositInEquipmentSlot(0, arrow, 6);  // slot now has 6
            int placed = inv.TryDepositInEquipmentSlot(0, arrow, 7);  // 4 room left
            Assert.AreEqual(4, placed, "Should only stack into remaining room.");
            Assert.AreEqual(10, inv.EquipmentSlots[0].Quantity);
        }

        [Test]
        public void TryDepositInEquipmentSlot_SameItemStackable_Partial_WhenQtyFitsExactly()
        {
            var inv = CreateInventory();
            var gem = CreateItem("gem", stackable: true, maxStack: 5);
            inv.TryDepositInEquipmentSlot(3, gem, 3);
            int placed = inv.TryDepositInEquipmentSlot(3, gem, 2);
            Assert.AreEqual(2, placed);
            Assert.AreEqual(5, inv.EquipmentSlots[3].Quantity);
        }

        [Test]
        public void TryDepositInEquipmentSlot_DifferentItem_Rejects()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            var bow = CreateItem("bow");
            inv.TryDepositInEquipmentSlot(0, sword, 1);
            int placed = inv.TryDepositInEquipmentSlot(0, bow, 1);
            Assert.AreEqual(0, placed, "Different item in non-empty slot must be rejected.");
            Assert.AreEqual(sword, inv.EquipmentSlots[0].Item, "Slot must remain unchanged.");
        }

        [Test]
        public void TryDepositInEquipmentSlot_OutOfRange_ReturnsZero()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(-1, sword, 1));
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(Inventory.EquipmentCapacity, sword, 1));
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(99, sword, 1));
        }

        [Test]
        public void TryDepositInEquipmentSlot_NullItem_ReturnsZero()
        {
            var inv = CreateInventory();
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(0, null, 1));
        }

        [Test]
        public void TryDepositInEquipmentSlot_ZeroQty_ReturnsZero()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(0, sword, 0));
        }

        [Test]
        public void TryDepositInEquipmentSlot_NegativeQty_ReturnsZero()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(0, sword, -5));
        }

        // ── SetEquipmentSlot ───────────────────────────────────────────────────

        [Test]
        public void SetEquipmentSlot_DirectWrite_OverridesExistingContent()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            var bow = CreateItem("bow");
            inv.TryDepositInEquipmentSlot(0, sword, 1);
            inv.SetEquipmentSlot(0, bow, 1);
            Assert.AreEqual(bow, inv.EquipmentSlots[0].Item,
                "SetEquipmentSlot must bypass rules and overwrite.");
        }

        [Test]
        public void SetEquipmentSlot_NullItem_ClearsSlot()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            inv.TryDepositInEquipmentSlot(0, sword, 1);
            inv.SetEquipmentSlot(0, null, 1);
            Assert.IsTrue(inv.EquipmentSlots[0].IsEmpty,
                "Null item via SetEquipmentSlot must produce an empty slot.");
        }

        [Test]
        public void SetEquipmentSlot_ZeroQty_ClearsSlot()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            inv.TryDepositInEquipmentSlot(0, sword, 1);
            inv.SetEquipmentSlot(0, sword, 0);
            Assert.IsTrue(inv.EquipmentSlots[0].IsEmpty,
                "qty=0 via SetEquipmentSlot must produce an empty slot.");
        }

        [Test]
        public void SetEquipmentSlot_OutOfRange_IsSilentNoOp()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword");
            Assert.DoesNotThrow(() => inv.SetEquipmentSlot(-1, sword, 1));
            Assert.DoesNotThrow(() => inv.SetEquipmentSlot(Inventory.EquipmentCapacity, sword, 1));
            Assert.DoesNotThrow(() => inv.SetEquipmentSlot(99, sword, 1));
        }

        // ── TryDepositInSlot (bag) – SetSlot ──────────────────────────────────

        [Test]
        public void SetSlot_DirectWrite_OverridesExistingContent()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            var bow = CreateItem("bow");
            inv.AddItem(sword);
            inv.SetSlot(0, bow, 1);
            Assert.AreEqual(bow, inv.Slots[0].Item);
        }

        [Test]
        public void SetSlot_NullItem_ClearsSlot()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            inv.AddItem(sword);
            inv.SetSlot(0, null, 1);
            Assert.IsTrue(inv.Slots[0].IsEmpty);
        }

        [Test]
        public void SetSlot_ZeroQty_ClearsSlot()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            inv.AddItem(sword);
            inv.SetSlot(0, sword, 0);
            Assert.IsTrue(inv.Slots[0].IsEmpty);
        }

        [Test]
        public void SetSlot_OutOfRange_IsSilentNoOp()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            Assert.DoesNotThrow(() => inv.SetSlot(-1, sword, 1));
            Assert.DoesNotThrow(() => inv.SetSlot(5, sword, 1));
            Assert.DoesNotThrow(() => inv.SetSlot(99, sword, 1));
        }

        // ── TryDepositInSlot (bag) ─────────────────────────────────────────────

        [Test]
        public void TryDepositInSlot_EmptySlot_AcceptsAnyItem()
        {
            var inv = CreateInventory(5);
            var shield = CreateItem("shield");
            int placed = inv.TryDepositInSlot(2, shield, 1);
            Assert.AreEqual(1, placed);
            Assert.AreEqual(shield, inv.Slots[2].Item);
        }

        [Test]
        public void TryDepositInSlot_SameStackable_StacksUpToRoom()
        {
            var inv = CreateInventory(5);
            var potion = CreateItem("potion", stackable: true, maxStack: 10);
            inv.TryDepositInSlot(0, potion, 7);
            int placed = inv.TryDepositInSlot(0, potion, 5);
            Assert.AreEqual(3, placed, "Only 3 remaining room.");
            Assert.AreEqual(10, inv.Slots[0].Quantity);
        }

        [Test]
        public void TryDepositInSlot_DifferentItem_Rejects()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            var axe = CreateItem("axe");
            inv.TryDepositInSlot(0, sword, 1);
            int placed = inv.TryDepositInSlot(0, axe, 1);
            Assert.AreEqual(0, placed);
            Assert.AreEqual(sword, inv.Slots[0].Item);
        }

        [Test]
        public void TryDepositInSlot_OutOfRange_ReturnsZero()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            Assert.AreEqual(0, inv.TryDepositInSlot(-1, sword, 1));
            Assert.AreEqual(0, inv.TryDepositInSlot(5, sword, 1));
        }

        [Test]
        public void TryDepositInSlot_NullItem_ReturnsZero()
        {
            var inv = CreateInventory(5);
            Assert.AreEqual(0, inv.TryDepositInSlot(0, null, 1));
        }

        [Test]
        public void TryDepositInSlot_ZeroOrNegativeQty_ReturnsZero()
        {
            var inv = CreateInventory(5);
            var sword = CreateItem("sword");
            Assert.AreEqual(0, inv.TryDepositInSlot(0, sword, 0));
            Assert.AreEqual(0, inv.TryDepositInSlot(0, sword, -3));
        }

        // ── EquipmentView regression guard ─────────────────────────────────────

        [Test]
        public void EquipmentView_Resolve_DoesNotReflectBagItems()
        {
            // The old code auto-mirrored bag items into equipment slots.
            // This test pins that a weapon added only to the bag must NOT
            // appear in the equipment view.
            var inv = CreateInventory(5);
            var weapon = CreateItem("sword");
            inv.AddItem(weapon);  // goes to bag, NOT equipment

            var dest = new ItemDefinition[EquipmentView.SLOT_COUNT];
            EquipmentView.Resolve(inv, dest);

            for (int i = 0; i < dest.Length; i++)
            {
                Assert.IsNull(dest[i],
                    $"Equipment slot {i} must be null — bag items must not auto-mirror to equipment.");
            }
        }

        [Test]
        public void EquipmentView_Resolve_ReflectsEquipmentSlotItems()
        {
            var inv = CreateInventory(5);
            var weapon = CreateItem("sword");
            inv.SetEquipmentSlot(4, weapon, 1);  // place directly into equipment slot 4

            var dest = new ItemDefinition[EquipmentView.SLOT_COUNT];
            EquipmentView.Resolve(inv, dest);

            Assert.AreEqual(weapon, dest[4],
                "Equipment slot 4 must reflect item placed via SetEquipmentSlot.");
            for (int i = 0; i < dest.Length; i++)
            {
                if (i != 4)
                    Assert.IsNull(dest[i], $"Equipment slot {i} should be null.");
            }
        }

        [Test]
        public void EquipmentView_Resolve_NullInventory_AllNull()
        {
            var dest = new ItemDefinition[EquipmentView.SLOT_COUNT];
            // Pre-fill with non-null to make sure Resolve clears them.
            var dummy = CreateItem("dummy");
            for (int i = 0; i < dest.Length; i++) dest[i] = dummy;

            EquipmentView.Resolve(null, dest);

            for (int i = 0; i < dest.Length; i++)
                Assert.IsNull(dest[i], $"Slot {i} should be null for null inventory.");
        }

        [Test]
        public void EquipmentView_Resolve_NullDest_DoesNotThrow()
        {
            var inv = CreateInventory(5);
            Assert.DoesNotThrow(() => EquipmentView.Resolve(inv, null));
        }
    }
}
