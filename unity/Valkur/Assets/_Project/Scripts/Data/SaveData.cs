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

        /// <summary>
        /// Coins in the player's <c>CurrencyWallet</c>.
        ///
        /// <para>Absent from every save written before this field existed, and -1 is how that
        /// is told apart from a player who is genuinely broke. JsonUtility runs field
        /// initialisers before overwriting what the JSON carries, so a legacy save arrives
        /// here as -1 and the restorer leaves the wallet alone; a save written since arrives
        /// as a real balance, zero included.</para>
        ///
        /// <para>A plain <c>0</c> default would have been silently destructive in the other
        /// direction: every legacy save would restore as "you have no money", which is
        /// indistinguishable from the bug this field fixes.</para>
        /// </summary>
        public int coins = -1;

        // Current visual layer index (0=Ground, 4=WallsBottom, 8=OverheadDetails)
        // for the per-visual-layer collisions pipeline (M1.5 foundation, M2 runtime).
        // Default 0 ensures legacy saves load with the player on Ground — JsonUtility
        // tolerates missing fields, so pre-feature saves never see a regression.
        public int visualLayer = 0;

        /// <summary>
        /// Talents and grimoire. Never null — JsonUtility runs field initialisers before
        /// applying the JSON, so a save written before progression existed arrives here as
        /// an EMPTY document rather than as null, and restores as "a character who has
        /// spent nothing", which is exactly what such a save describes.
        /// </summary>
        public ProgressionSaveData progression = new ProgressionSaveData();
    }

    /// <summary>
    /// The persisted half of a character's progression.
    ///
    /// It lives in <c>Valkur.Data</c> rather than as a nested type on the two runtime
    /// components because the save layer may not reference <c>Valkur.Gameplay</c>, and
    /// because one shared document is the only way the two halves cannot drift into
    /// different on-disk shapes.
    ///
    /// Ranks are a list PARALLEL to <see cref="skillIds"/> rather than a list of pairs:
    /// JsonUtility serializes a list of primitives and refuses a dictionary, and a flat
    /// list survives every serializer this project has used.
    /// </summary>
    [Serializable]
    public class ProgressionSaveData
    {
        public List<string> skillIds = new List<string>();
        public List<int> skillRanks = new List<int>();
        public int skillPoints;
        public int skillPointsSpent;

        public List<string> grimoireNodeIds = new List<string>();
        public int arcanePoints;
        public int arcanePointsSpent;

        /// <summary>True when this document says nothing — a legacy save, or a character
        /// who has genuinely spent nothing. The two are indistinguishable and should be
        /// treated the same way.</summary>
        public bool IsEmpty =>
            (skillIds == null || skillIds.Count == 0) &&
            (grimoireNodeIds == null || grimoireNodeIds.Count == 0) &&
            skillPoints == 0 && arcanePoints == 0 &&
            skillPointsSpent == 0 && arcanePointsSpent == 0;
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
    /// Lightweight crash-recovery snapshot of the player's position + zone.
    ///
    /// NOTE: this duplicates the information stored in
    /// <see cref="PlayerSaveData.position"/> + <see cref="PlayerSaveData.currentZone"/>
    /// inside the full <see cref="GameSaveData"/>. The duplication exists so
    /// the gameplay scene's spawn step can read a 30-byte JSON without
    /// having to parse the entire save document (full saves grow with NPC
    /// memory + inventory, parsing them on every spawn would add latency to
    /// loading screens).
    ///
    /// The two are kept in lockstep: <c>SaveService.WriteAutosaveToDisk</c>
    /// calls <c>SavePositionCheckpoint</c> on every successful write, so the
    /// checkpoint always reflects the same position as the most recent full
    /// save tick. Full state restoration (HP, mana, XP, inventory, NPCs)
    /// still requires loading the full save via <c>SaveService.Load</c> —
    /// the checkpoint alone restores position only.
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
