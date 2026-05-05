using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Persistence;

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
    public class InventoryData : IVersioned
    {
        public string playerId;
        public int capacity;
        public List<InventorySlotData> slots = new List<InventorySlotData>();
        // Paper-doll 3×3 equipment slots. Empty in saves from before the
        // equipment storage refactor; restored by id 0..8 (row-major).
        public List<InventorySlotData> equipmentSlots = new List<InventorySlotData>();
        public string schemaVersion;

        // IVersioned: delegates to the existing JsonUtility-serialized field
        // so adopting the interface does not change the on-disk shape and
        // lets MigrationChain<InventoryData> drive any future inventory
        // schema bump without re-reading the field by reflection.
        string IVersioned.SchemaVersion
        {
            get => schemaVersion;
            set => schemaVersion = value;
        }
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
    /// Serializable key-value pair for metadata storage.
    /// Replaces Dictionary&lt;string,string&gt; which JsonUtility cannot serialize.
    /// </summary>
    [Serializable]
    public struct SerializableKeyValue
    {
        public string key;
        public string value;

        public SerializableKeyValue(string key, string value)
        {
            this.key = key;
            this.value = value;
        }
    }

    /// <summary>
    /// Lightweight position-only checkpoint for crash-safe position persistence.
    /// Written every few seconds during gameplay; separate from full save files.
    /// </summary>
    [Serializable]
    public class PositionCheckpointData
    {
        public float x;
        public float y;
        public string zone;
        public string timestamp;
    }

    /// <summary>
    /// Root save file structure.
    /// Combines player state, NPC memory, and game metadata.
    /// </summary>
    [Serializable]
    public class GameSaveData : IVersioned
    {
        public string schemaVersion = "1.0";
        public string timestamp;
        public PlayerSaveData player;
        public List<NpcMemoryEntry> npcMemory = new List<NpcMemoryEntry>();
        public List<SerializableKeyValue> metadata = new List<SerializableKeyValue>();

        // IVersioned: delegates to the existing JsonUtility-serialized field
        // so adding the interface is a non-breaking change for save files
        // already on disk.
        string IVersioned.SchemaVersion
        {
            get => schemaVersion;
            set => schemaVersion = value;
        }

        public string GetMeta(string key, string defaultValue = "")
        {
            for (int i = 0; i < metadata.Count; i++)
                if (metadata[i].key == key) return metadata[i].value;
            return defaultValue;
        }

        public void SetMeta(string key, string value)
        {
            for (int i = 0; i < metadata.Count; i++)
            {
                if (metadata[i].key == key)
                {
                    metadata[i] = new SerializableKeyValue(key, value);
                    return;
                }
            }
            metadata.Add(new SerializableKeyValue(key, value));
        }
    }
}
