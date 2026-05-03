using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// PlayerDeathDropSystem.DropEverything must:
    ///   1. Empty <see cref="Inventory"/> entirely (questId is NOT a filter).
    ///   2. Empty <see cref="CurrencyWallet"/> entirely.
    /// We don't assert on the exact spawned <c>WorldPickup</c> count because
    /// <see cref="DropSystem.SpawnDrop"/> creates real GameObjects with
    /// SpriteRenderers + colliders that would clutter the test scene; the
    /// inventory + wallet contracts above are sufficient to prove the
    /// behaviour-of-record.
    /// </summary>
    public class PlayerDeathDropSystemTests
    {
        private GameObject _player;
        private Inventory _inventory;
        private CurrencyWallet _wallet;

        [SetUp]
        public void Setup()
        {
            _player = new GameObject("Player");
            _player.tag = "Player";
            _inventory = _player.AddComponent<Inventory>();
            _inventory.Initialize(20);
            _wallet = _player.AddComponent<CurrencyWallet>();
            _wallet.Add(150);
        }

        [TearDown]
        public void Teardown()
        {
            if (_player != null) Object.DestroyImmediate(_player);
            // World pickups spawned by DropSystem leak into the scene — clean
            // them up so subsequent tests start fresh.
            foreach (var go in Object.FindObjectsOfType<WorldPickup>())
                Object.DestroyImmediate(go.gameObject);
        }

        [Test]
        public void DropEverything_EmptiesInventory()
        {
            var sword = ScriptableObject.CreateInstance<ItemDefinition>();
            sword.itemId = "test_sword";
            sword.displayName = "Test Sword";
            sword.stackable = false;
            sword.maxStack = 1;
            _inventory.AddItem(sword, 1);

            var potion = ScriptableObject.CreateInstance<ItemDefinition>();
            potion.itemId = "test_potion";
            potion.displayName = "Test Potion";
            potion.stackable = true;
            potion.maxStack = 99;
            _inventory.AddItem(potion, 5);

            Assert.AreEqual(2, _inventory.UsedSlots, "Sanity check: inventory should hold 2 stacks pre-death.");

            PlayerDeathDropSystem.DropEverything(_player);

            Assert.AreEqual(0, _inventory.UsedSlots, "Inventory must be empty after death drop.");
        }

        [Test]
        public void DropEverything_EmptiesWallet()
        {
            Assert.AreEqual(150, _wallet.Coins);
            PlayerDeathDropSystem.DropEverything(_player);
            Assert.AreEqual(0, _wallet.Coins);
        }

        [Test]
        public void DropEverything_HandlesEmptyInventoryAndZeroWallet()
        {
            _wallet.SetBalance(0);
            Assert.DoesNotThrow(() => PlayerDeathDropSystem.DropEverything(_player));
            Assert.AreEqual(0, _inventory.UsedSlots);
            Assert.AreEqual(0, _wallet.Coins);
        }
    }
}
