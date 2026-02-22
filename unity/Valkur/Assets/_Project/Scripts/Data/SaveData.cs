using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Serializable inventory slot.
    /// Maps to Python's InventoryPlayerSchema slot: {item, quantity, stack_id}.
    /// </summary>
    [Serializable]
    public struct InventorySlotData
    {
        public string itemId;
        public int quantity;
        public string stackId;
    }

    /// <summary>
    /// Serializable player inventory.
    /// Maps to Python's InventoryPlayerSchema: {player_id, capacity, slots}.
    /// </summary>
    [Serializable]
    public class InventoryData
    {
        public string playerId;
        public int capacity;
        public List<InventorySlotData> slots = new List<InventorySlotData>();
        public string schemaVersion;
    }

    /// <summary>
    /// Serializable player save state.
    /// Maps to Python's ShutdownManager save output.
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        public string playerClass;
        public Vector2 position;
        public string currentZone;
        public int hp;
        public int maxHp;
        public float mana;
        public float maxMana;
        public int experience;
        public int level;
        public InventoryData inventory;
    }

    /// <summary>
    /// Serializable NPC memory entry.
    /// Maps to Python's NPC memory persisted by ShutdownManager.
    /// </summary>
    [Serializable]
    public struct NpcMemoryEntry
    {
        public string entityId;
        public string monsterKey;
        public Vector2 position;
        public int hp;
        public string fsmState;
        public string zone;
    }

    /// <summary>
    /// Root save file structure.
    /// Combines player state, NPC memory, and game metadata.
    /// </summary>
    [Serializable]
    public class GameSaveData
    {
        public string schemaVersion = "1.0";
        public string timestamp;
        public PlayerSaveData player;
        public List<NpcMemoryEntry> npcMemory = new List<NpcMemoryEntry>();
        public Dictionary<string, string> metadata = new Dictionary<string, string>();
    }
}
