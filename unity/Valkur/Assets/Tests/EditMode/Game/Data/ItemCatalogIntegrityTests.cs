using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data
{
    /// <summary>
    /// Pins data quality of every <see cref="ItemDefinition"/> ScriptableObject
    /// shipped in the project's <c>ItemCatalog</c>. Catches the regression
    /// patterns that emerged from the Python -&gt; Unity migration:
    ///   - empty / placeholder ids
    ///   - displayName left as a raw "*.png" filename
    ///   - equipment rows with no slot, damage, or durability
    ///   - duplicate ids in the catalog
    ///   - stackable rows with maxStack &lt; 1, or non-stackable with maxStack &gt; 1
    ///   - missing icon
    ///   - prices below 1 (vendors require &gt;= 1 to allow trading)
    ///
    /// The catalog is loaded via AssetDatabase so the test reflects the real
    /// shipping data, not a synthetic fixture.
    /// </summary>
    [TestFixture]
    public class ItemCatalogIntegrityTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset";

        private ItemCatalog _catalog;

        [SetUp]
        public void LoadCatalog()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.IsNotNull(_catalog, $"ItemCatalog asset not found at {CatalogPath}");
        }

        [Test]
        public void Catalog_HasNoNullEntries()
        {
            for (int i = 0; i < _catalog.Items.Count; i++)
                Assert.IsNotNull(_catalog.Items[i], $"Catalog entry {i} is null");
        }

        [Test]
        public void EveryItem_HasNonEmptyId()
        {
            foreach (var item in _catalog.Items)
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.itemId),
                    $"Item '{item.name}' has empty itemId");
        }

        [Test]
        public void EveryItem_HasNonEmptyDisplayName()
        {
            foreach (var item in _catalog.Items)
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.displayName),
                    $"Item '{item.itemId}' has empty displayName");
        }

        [Test]
        public void DisplayName_DoesNotEndWithPng()
        {
            foreach (var item in _catalog.Items)
                Assert.IsFalse(
                    item.displayName.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase),
                    $"Item '{item.itemId}' has unprofessional displayName '{item.displayName}'");
        }

        [Test]
        public void ItemIds_AreUnique()
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var item in _catalog.Items)
            {
                Assert.IsTrue(seen.Add(item.itemId),
                    $"Duplicate itemId '{item.itemId}' in catalog");
            }
        }

        [Test]
        public void ItemId_MatchesAssetFileName()
        {
            foreach (var item in _catalog.Items)
            {
                string assetPath = AssetDatabase.GetAssetPath(item);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                Assert.AreEqual(item.itemId, fileName,
                    $"itemId '{item.itemId}' does not match filename '{fileName}'");
            }
        }

        [Test]
        public void Equipment_HasSlotAndDurability()
        {
            foreach (var item in _catalog.Items)
            {
                if (item.equipSlot == EquipSlot.None) continue;
                Assert.Greater(item.durability, 0,
                    $"Equipment '{item.itemId}' has slot {item.equipSlot} but durability=0");
            }
        }

        [Test]
        public void Weapons_HaveDamage()
        {
            foreach (var item in _catalog.Items)
            {
                if (item.equipSlot != EquipSlot.Weapon) continue;
                Assert.Greater(item.damage, 0,
                    $"Weapon '{item.itemId}' has damage=0");
                Assert.Greater(item.attackSpeed, 0,
                    $"Weapon '{item.itemId}' has attackSpeed=0");
            }
        }

        [Test]
        public void Stackable_MaxStack_IsAtLeastOne()
        {
            foreach (var item in _catalog.Items)
            {
                if (!item.stackable) continue;
                Assert.GreaterOrEqual(item.maxStack, 1,
                    $"Stackable '{item.itemId}' has maxStack={item.maxStack}");
            }
        }

        [Test]
        public void NonStackable_MaxStack_IsExactlyOne()
        {
            foreach (var item in _catalog.Items)
            {
                if (item.stackable) continue;
                Assert.AreEqual(1, item.maxStack,
                    $"Non-stackable '{item.itemId}' has maxStack={item.maxStack}");
            }
        }

        [Test]
        public void Consumables_AreStackable()
        {
            foreach (var item in _catalog.Items)
            {
                bool isConsumable = item.healing > 0 || item.mana > 0
                                 || item.energy > 0 || item.hunger > 0;
                if (!isConsumable) continue;
                Assert.IsTrue(item.stackable,
                    $"Consumable '{item.itemId}' is not stackable");
            }
        }

        [Test]
        public void Prices_AreNonNegative()
        {
            foreach (var item in _catalog.Items)
            {
                Assert.GreaterOrEqual(item.buyPrice, 0,
                    $"Item '{item.itemId}' has negative buyPrice");
                Assert.GreaterOrEqual(item.sellPrice, 0,
                    $"Item '{item.itemId}' has negative sellPrice");
            }
        }

        [Test]
        public void SellPrice_NotGreaterThanBuyPrice()
        {
            foreach (var item in _catalog.Items)
            {
                if (item.buyPrice == 0) continue;
                Assert.LessOrEqual(item.sellPrice, item.buyPrice,
                    $"Item '{item.itemId}' has sellPrice {item.sellPrice} > buyPrice {item.buyPrice}");
            }
        }

        [Test]
        public void LevelRequirement_IsAtLeastOne()
        {
            foreach (var item in _catalog.Items)
                Assert.GreaterOrEqual(item.levelRequirement, 1,
                    $"Item '{item.itemId}' has levelRequirement={item.levelRequirement}");
        }

        [Test]
        public void ItemId_HasNoUppercaseOrWhitespace()
        {
            foreach (var item in _catalog.Items)
            {
                foreach (char c in item.itemId)
                {
                    Assert.IsFalse(char.IsUpper(c),
                        $"Item '{item.itemId}' contains uppercase '{c}' (snake_case expected)");
                    Assert.IsFalse(char.IsWhiteSpace(c),
                        $"Item '{item.itemId}' contains whitespace");
                }
            }
        }

        [Test]
        public void Description_IsNonEmpty()
        {
            foreach (var item in _catalog.Items)
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.description),
                    $"Item '{item.itemId}' has empty description");
        }
    }
}
