using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    public class SaveDataTests
    {
        // --- InventoryData ---

        [Test]
        public void InventoryData_DefaultSlots_IsEmptyList()
        {
            var data = new InventoryData();
            Assert.IsNotNull(data.slots);
            Assert.AreEqual(0, data.slots.Count);
        }

        [Test]
        public void InventoryData_AddSlots_PersistsCorrectly()
        {
            var data = new InventoryData
            {
                playerId = "player1",
                capacity = 20,
                schemaVersion = "1.0"
            };
            data.slots.Add(new InventorySlotData { itemId = "sword", quantity = 1, stackId = "" });
            data.slots.Add(new InventorySlotData { itemId = "potion", quantity = 5, stackId = "stack_1" });

            Assert.AreEqual(2, data.slots.Count);
            Assert.AreEqual("sword", data.slots[0].itemId);
            Assert.AreEqual(5, data.slots[1].quantity);
        }

        // --- GameSaveData ---

        [Test]
        public void GameSaveData_DefaultValues_AreValid()
        {
            var save = new GameSaveData();
            Assert.AreEqual("1.0", save.schemaVersion);
            Assert.IsNotNull(save.npcMemory);
            Assert.IsNotNull(save.metadata);
            Assert.AreEqual(0, save.npcMemory.Count);
        }

        [Test]
        public void GameSaveData_JsonRoundtrip_PreservesData()
        {
            var save = new GameSaveData
            {
                schemaVersion = "1.0",
                timestamp = "2025-01-01T00:00:00",
                player = new PlayerSaveData
                {
                    playerClass = "dwarf",
                    position = new Vector2(10f, 20f),
                    currentZone = "Lobby",
                    hp = 80,
                    maxHp = 100,
                    experience = 500,
                    level = 3
                }
            };
            save.npcMemory.Add(new NpcMemoryEntry
            {
                entityId = "goblin_1",
                monsterKey = "goblin",
                position = new Vector2(5f, 5f),
                hp = 30,
                fsmState = "Patrol",
                zone = "Forest"
            });

            string json = JsonUtility.ToJson(save);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            var loaded = JsonUtility.FromJson<GameSaveData>(json);
            Assert.AreEqual("1.0", loaded.schemaVersion);
            Assert.AreEqual("dwarf", loaded.player.playerClass);
            Assert.AreEqual(80, loaded.player.hp);
            Assert.AreEqual(new Vector2(10f, 20f), loaded.player.position);
            Assert.AreEqual(1, loaded.npcMemory.Count);
            Assert.AreEqual("goblin_1", loaded.npcMemory[0].entityId);
            Assert.AreEqual("Patrol", loaded.npcMemory[0].fsmState);
        }

        // --- PlayerSaveData ---

        [Test]
        public void PlayerSaveData_AllFieldsSerialize()
        {
            var player = new PlayerSaveData
            {
                playerClass = "valkyrie",
                position = new Vector2(100f, 200f),
                currentZone = "Dungeon",
                hp = 150,
                maxHp = 200,
                mana = 50f,
                maxMana = 100f,
                experience = 1200,
                level = 5,
                inventory = new InventoryData
                {
                    playerId = "player1",
                    capacity = 20,
                    schemaVersion = "1.0"
                }
            };

            string json = JsonUtility.ToJson(player);
            var loaded = JsonUtility.FromJson<PlayerSaveData>(json);

            Assert.AreEqual("valkyrie", loaded.playerClass);
            Assert.AreEqual(new Vector2(100f, 200f), loaded.position);
            Assert.AreEqual("Dungeon", loaded.currentZone);
            Assert.AreEqual(150, loaded.hp);
            Assert.AreEqual(200, loaded.maxHp);
            Assert.AreEqual(50f, loaded.mana, 0.001f);
            Assert.AreEqual(100f, loaded.maxMana, 0.001f);
            Assert.AreEqual(1200, loaded.experience);
            Assert.AreEqual(5, loaded.level);
            Assert.IsNotNull(loaded.inventory);
            Assert.AreEqual(20, loaded.inventory.capacity);
        }

        // --- NpcMemoryEntry ---

        [Test]
        public void NpcMemoryEntry_SerializesCorrectly()
        {
            var entry = new NpcMemoryEntry
            {
                entityId = "skeleton_3",
                monsterKey = "skeleton",
                position = new Vector2(-10f, 30f),
                hp = 45,
                fsmState = "Chase",
                zone = "Crypt"
            };

            string json = JsonUtility.ToJson(entry);
            var loaded = JsonUtility.FromJson<NpcMemoryEntry>(json);

            Assert.AreEqual("skeleton_3", loaded.entityId);
            Assert.AreEqual("skeleton", loaded.monsterKey);
            Assert.AreEqual(new Vector2(-10f, 30f), loaded.position);
            Assert.AreEqual(45, loaded.hp);
            Assert.AreEqual("Chase", loaded.fsmState);
            Assert.AreEqual("Crypt", loaded.zone);
        }
    }
}
