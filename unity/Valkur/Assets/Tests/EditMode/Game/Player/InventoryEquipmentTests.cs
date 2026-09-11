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

        private ItemDefinition CreateItem(string id, bool stackable = false, int maxStack = 1,
                                          EquipSlot slot = EquipSlot.None, int level = 1)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.stackable = stackable;
            item.maxStack = maxStack;
            item.equipSlot = slot;
            item.levelRequirement = level;
            _assets.Add(item);
            return item;
        }

        private static int Idx(EquipmentSlotKind k) => (int)k;

        // ── TryDepositInEquipmentSlot: typed slots ─────────────────────────────

        [Test]
        public void TryDepositInEquipmentSlot_ItemInItsOwnSlot_IsAccepted()
        {
            var inv = CreateInventory();
            var helm = CreateItem("helm", slot: EquipSlot.Helmet);
            int placed = inv.TryDepositInEquipmentSlot(Idx(EquipmentSlotKind.Head), helm, 1);
            Assert.AreEqual(1, placed);
            Assert.AreEqual(helm, inv.EquipmentSlots[Idx(EquipmentSlotKind.Head)].Item);
        }

        [Test]
        public void TryDepositInEquipmentSlot_WrongSlot_IsRefused()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword", slot: EquipSlot.Weapon);
            int placed = inv.TryDepositInEquipmentSlot(Idx(EquipmentSlotKind.Head), sword, 1);
            Assert.AreEqual(0, placed, "A sword must not go on the head.");
            Assert.AreEqual(EquipRefusal.WrongSlot, inv.LastRefusal);
            Assert.IsTrue(inv.EquipmentSlots[Idx(EquipmentSlotKind.Head)].IsEmpty);
        }

        [Test]
        public void TryDepositInEquipmentSlot_NotWearable_IsRefused()
        {
            var inv = CreateInventory();
            var potion = CreateItem("potion", stackable: true, maxStack: 10);
            for (int i = 0; i < Inventory.EquipmentCapacity; i++)
                Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(i, potion, 1),
                    "A potion is not wearable in any slot (slot " + (EquipmentSlotKind)i + ").");
            Assert.AreEqual(EquipRefusal.NotWearable, inv.LastRefusal);
        }

        [Test]
        public void TryDepositInEquipmentSlot_NonStackable_PlacesOne()
        {
            var inv = CreateInventory();
            var weapon = CreateItem("weapon", slot: EquipSlot.Weapon);
            int placed = inv.TryDepositInEquipmentSlot(Idx(EquipmentSlotKind.Weapon), weapon, 5);
            Assert.AreEqual(1, placed);
            Assert.AreEqual(1, inv.EquipmentSlots[Idx(EquipmentSlotKind.Weapon)].Quantity);
        }

        [Test]
        public void TryDepositInEquipmentSlot_StackableEmptySlot_PlacesUpToMaxStack()
        {
            var inv = CreateInventory();
            var bolts = CreateItem("bolts", stackable: true, maxStack: 10, slot: EquipSlot.Offhand);
            int placed = inv.TryDepositInEquipmentSlot(Idx(EquipmentSlotKind.Offhand), bolts, 30);
            Assert.AreEqual(10, placed);
        }

        [Test]
        public void TryDepositInEquipmentSlot_SameItemStackable_StacksUpToRoom()
        {
            var inv = CreateInventory();
            var bolts = CreateItem("bolts", stackable: true, maxStack: 10, slot: EquipSlot.Offhand);
            int off = Idx(EquipmentSlotKind.Offhand);
            inv.TryDepositInEquipmentSlot(off, bolts, 6);
            int placed = inv.TryDepositInEquipmentSlot(off, bolts, 7);
            Assert.AreEqual(4, placed);
            Assert.AreEqual(10, inv.EquipmentSlots[off].Quantity);
        }

        [Test]
        public void TryDepositInEquipmentSlot_DifferentItem_Rejects()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword", slot: EquipSlot.Weapon);
            var bow = CreateItem("bow", slot: EquipSlot.Weapon);
            int w = Idx(EquipmentSlotKind.Weapon);
            inv.TryDepositInEquipmentSlot(w, sword, 1);
            int placed = inv.TryDepositInEquipmentSlot(w, bow, 1);
            Assert.AreEqual(0, placed);
            Assert.AreEqual(sword, inv.EquipmentSlots[w].Item);
        }

        [Test]
        public void TryDepositInEquipmentSlot_OutOfRange_ReturnsZero()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword", slot: EquipSlot.Weapon);
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
            var helm = CreateItem("helm", slot: EquipSlot.Helmet);
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(0, helm, 0));
        }

        [Test]
        public void TryDepositInEquipmentSlot_NegativeQty_ReturnsZero()
        {
            var inv = CreateInventory();
            var helm = CreateItem("helm", slot: EquipSlot.Helmet);
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(0, helm, -5));
        }

        /// <summary>
        /// The defect the typed slots exist for: nine untyped cells each accepted a weapon and
        /// EquipmentStatSource summed all nine — nine longswords were +162 melee damage.
        /// </summary>
        [Test]
        public void NineSwords_OnlyOneCanBeWorn()
        {
            var inv = CreateInventory();
            int worn = 0;
            for (int i = 0; i < Inventory.EquipmentCapacity; i++)
            {
                var sword = CreateItem("sword" + i, slot: EquipSlot.Weapon);
                worn += inv.TryDepositInEquipmentSlot(i, sword, 1);
            }
            Assert.AreEqual(1, worn, "Exactly one slot — the weapon slot — may hold a weapon.");
            var resolved = new ItemDefinition[EquipmentView.SLOT_COUNT];
            EquipmentView.Resolve(inv, resolved);
            int weapons = 0;
            foreach (var it in resolved) if (it != null && it.equipSlot == EquipSlot.Weapon) weapons++;
            Assert.AreEqual(1, weapons);
        }

        [Test]
        public void Aliases_LandInTheSameSlot()
        {
            Assert.AreEqual(EquipmentLayout.IndexFor(EquipSlot.Head), EquipmentLayout.IndexFor(EquipSlot.Helmet));
            Assert.AreEqual(EquipmentLayout.IndexFor(EquipSlot.Body), EquipmentLayout.IndexFor(EquipSlot.Chest));
            Assert.AreEqual(EquipmentLayout.IndexFor(EquipSlot.Offhand), EquipmentLayout.IndexFor(EquipSlot.Shield));
            Assert.AreEqual(EquipmentLayout.IndexFor(EquipSlot.Offhand), EquipmentLayout.IndexFor(EquipSlot.Book));
            Assert.AreEqual(EquipmentLayout.IndexFor(EquipSlot.Trinket), EquipmentLayout.IndexFor(EquipSlot.Accessory));
            Assert.AreEqual(-1, EquipmentLayout.IndexFor(EquipSlot.None));
        }

        /// <summary>Every EquipSlot value except None has a home, so no wearable item is unwearable.</summary>
        [Test]
        public void EveryEquipSlotValue_HasAHome()
        {
            foreach (EquipSlot v in System.Enum.GetValues(typeof(EquipSlot)))
            {
                if (v == EquipSlot.None) continue;
                int i = EquipmentLayout.IndexFor(v);
                Assert.That(i, Is.InRange(0, EquipmentLayout.Count - 1), v + " has no equipment slot.");
            }
        }

        // ── Level requirement ──────────────────────────────────────────────────

        [Test]
        public void LevelRequirement_RefusesUnderLevel()
        {
            var inv = CreateInventory();
            var xp = inv.gameObject.AddComponent<Valkur.Gameplay.Experience>();
            var blade = CreateItem("blade", slot: EquipSlot.Weapon, level: 5);
            Assert.Less(xp.Level, 5, "Fixture assumes a fresh character under level 5.");
            Assert.AreEqual(EquipRefusal.LevelTooLow, inv.CheckEquip(blade, Idx(EquipmentSlotKind.Weapon)));
            Assert.AreEqual(0, inv.TryDepositInEquipmentSlot(Idx(EquipmentSlotKind.Weapon), blade, 1));
        }

        [Test]
        public void LevelRequirement_OfOne_IsNoRequirement()
        {
            // levelRequirement defaults to 1 on every item while the model starts at level 0.
            var inv = CreateInventory();
            inv.gameObject.AddComponent<Valkur.Gameplay.Experience>();
            var stick = CreateItem("stick", slot: EquipSlot.Weapon, level: 1);
            Assert.AreEqual(EquipRefusal.None, inv.CheckEquip(stick, Idx(EquipmentSlotKind.Weapon)));
        }

        // ── Wearing and taking off ─────────────────────────────────────────────

        [Test]
        public void TryEquipFromBag_WearsIt_AndSwapsTheOldOneBack()
        {
            var inv = CreateInventory();
            var sword = CreateItem("sword", slot: EquipSlot.Weapon);
            var axe = CreateItem("axe", slot: EquipSlot.Weapon);
            inv.SetEquipmentSlot(Idx(EquipmentSlotKind.Weapon), axe, 1);
            inv.SetSlot(3, sword, 1);

            Assert.IsTrue(inv.TryEquipFromBag(3, out var why), why.ToString());
            Assert.AreEqual(sword, inv.EquipmentSlots[Idx(EquipmentSlotKind.Weapon)].Item);
            Assert.AreEqual(axe, inv.Slots[3].Item, "The old weapon goes back where the new one came from.");
        }

        [Test]
        public void TryEquipFromBag_NotWearable_Refuses()
        {
            var inv = CreateInventory();
            var ore = CreateItem("ore", stackable: true, maxStack: 20);
            inv.SetSlot(0, ore, 4);
            Assert.IsFalse(inv.TryEquipFromBag(0, out var why));
            Assert.AreEqual(EquipRefusal.NotWearable, why);
            Assert.AreEqual(ore, inv.Slots[0].Item);
        }

        [Test]
        public void TryUnequip_MovesToFirstFreeBagSlot()
        {
            var inv = CreateInventory(3);
            var helm = CreateItem("helm", slot: EquipSlot.Helmet);
            var rock = CreateItem("rock");
            inv.SetSlot(0, rock, 1);
            inv.SetEquipmentSlot(Idx(EquipmentSlotKind.Head), helm, 1);

            int landed = inv.TryUnequip(Idx(EquipmentSlotKind.Head));
            Assert.AreEqual(1, landed);
            Assert.AreEqual(helm, inv.Slots[1].Item);
            Assert.IsTrue(inv.EquipmentSlots[Idx(EquipmentSlotKind.Head)].IsEmpty);
        }

        [Test]
        public void TryUnequip_BagFull_RefusesAndKeepsItWorn()
        {
            var inv = CreateInventory(1);
            var helm = CreateItem("helm", slot: EquipSlot.Helmet);
            inv.SetSlot(0, CreateItem("rock"), 1);
            inv.SetEquipmentSlot(Idx(EquipmentSlotKind.Head), helm, 1);

            Assert.AreEqual(-1, inv.TryUnequip(Idx(EquipmentSlotKind.Head)));
            Assert.AreEqual(EquipRefusal.BagFull, inv.LastRefusal);
            Assert.AreEqual(helm, inv.EquipmentSlots[Idx(EquipmentSlotKind.Head)].Item);
        }

        // ── Restore: old saves stored nine untyped cells ──────────────────────

        [Test]
        public void RestoreEquipped_RehomesByKind_WhateverTheSavedIndex()
        {
            var inv = CreateInventory();
            var boots = CreateItem("boots", slot: EquipSlot.Boots);
            Assert.AreEqual(0, inv.RestoreEquipped(boots, 1));
            Assert.AreEqual(boots, inv.EquipmentSlots[Idx(EquipmentSlotKind.Boots)].Item);
        }

        [Test]
        public void RestoreEquipped_NotWearable_GoesToTheBag()
        {
            // A pre-typed save could hold a potion in the old "Helmet" cell.
            var inv = CreateInventory();
            var potion = CreateItem("potion", stackable: true, maxStack: 10);
            Assert.AreEqual(0, inv.RestoreEquipped(potion, 3));
            Assert.AreEqual(3, inv.GetItemCount(potion));
            foreach (var s in inv.EquipmentSlots) Assert.IsTrue(s.IsEmpty);
        }

        [Test]
        public void RestoreEquipped_SlotTaken_SecondGoesToTheBag()
        {
            var inv = CreateInventory();
            var a = CreateItem("a", slot: EquipSlot.Weapon);
            var b = CreateItem("b", slot: EquipSlot.Weapon);
            inv.RestoreEquipped(a, 1);
            inv.RestoreEquipped(b, 1);
            Assert.AreEqual(a, inv.EquipmentSlots[Idx(EquipmentSlotKind.Weapon)].Item);
            Assert.AreEqual(1, inv.GetItemCount(b));
        }

        // ── Split and sort ─────────────────────────────────────────────────────

        [Test]
        public void SplitStack_MovesPart_KeepsAtLeastOne()
        {
            var inv = CreateInventory();
            var arrows = CreateItem("arrows", stackable: true, maxStack: 50);
            inv.SetSlot(0, arrows, 10);
            Assert.AreEqual(5, inv.SplitStack(0, 1, 5));
            Assert.AreEqual(5, inv.Slots[0].Quantity);
            Assert.AreEqual(5, inv.Slots[1].Quantity);
            Assert.AreEqual(4, inv.SplitStack(0, 2, 99), "The source keeps one.");
            Assert.AreEqual(1, inv.Slots[0].Quantity);
        }

        [Test]
        public void SplitStack_OntoDifferentItem_Refuses()
        {
            var inv = CreateInventory();
            var arrows = CreateItem("arrows", stackable: true, maxStack: 50);
            inv.SetSlot(0, arrows, 10);
            inv.SetSlot(1, CreateItem("rock"), 1);
            Assert.AreEqual(0, inv.SplitStack(0, 1, 3));
            Assert.AreEqual(10, inv.Slots[0].Quantity);
        }

        [Test]
        public void SortBag_MergesStacks_PacksToTheFront_AndIsStable()
        {
            var inv = CreateInventory(6);
            var ore = CreateItem("ore", stackable: true, maxStack: 20);
            var sword = CreateItem("sword", slot: EquipSlot.Weapon);
            sword.rarity = ItemRarity.Rare;
            inv.SetSlot(4, ore, 5);
            inv.SetSlot(1, ore, 7);
            inv.SetSlot(5, sword, 1);

            Assert.IsTrue(inv.SortBag());
            Assert.AreEqual(sword, inv.Slots[0].Item, "Equipment sorts first.");
            Assert.AreEqual(ore, inv.Slots[1].Item);
            Assert.AreEqual(12, inv.Slots[1].Quantity, "Partial stacks merge.");
            for (int i = 2; i < 6; i++) Assert.IsTrue(inv.Slots[i].IsEmpty);
            Assert.IsFalse(inv.SortBag(), "Sorting a sorted bag moves nothing.");
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
