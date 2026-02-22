using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Collects current game state from live entities into a serializable GameSaveData.
    /// No IO — pure state extraction.
    /// </summary>
    public static class GameStateCollector
    {
        /// <summary>
        /// Snapshot the current game state into a GameSaveData instance.
        /// Returns null if no player is available.
        /// </summary>
        public static GameSaveData Collect()
        {
            var player = EntityRegistry.Player;
            if (player == null) return null;

            var data = new GameSaveData
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };

            data.player = CollectPlayerState(player);
            data.npcMemory = CollectNpcMemory();

            return data;
        }

        private static PlayerSaveData CollectPlayerState(GameObject player)
        {
            var health = player.GetComponent<Health>();
            var mana = player.GetComponent<Mana>();
            var experience = player.GetComponent<Experience>();
            var inventory = player.GetComponent<Inventory.Inventory>();

            var psd = new PlayerSaveData
            {
                position = (Vector2)player.transform.position,
                hp = health != null ? health.CurrentHp : 0,
                maxHp = health != null ? health.MaxHp : 0,
                mana = mana != null ? mana.CurrentMana : 0,
                maxMana = mana != null ? mana.MaxMana : 0,
                currentZone = "",
                experience = experience != null ? experience.TotalXp : 0,
                level = experience != null ? experience.Level : 1
            };

            if (inventory != null)
                psd.inventory = inventory.ToSaveData("player");

            return psd;
        }

        private static List<NpcMemoryEntry> CollectNpcMemory()
        {
            var memory = new List<NpcMemoryEntry>();
            var monsters = GameObject.FindGameObjectsWithTag("Monster");

            foreach (var monster in monsters)
            {
                var health = monster.GetComponent<Health>();
                if (health == null) continue;

                var brain = monster.GetComponent<FSMMonsterBrain>();
                string fsmState = brain != null ? brain.CurrentStateName : "";

                memory.Add(new NpcMemoryEntry
                {
                    entityId = monster.GetInstanceID().ToString(),
                    monsterKey = monster.name,
                    position = (Vector2)monster.transform.position,
                    hp = health.CurrentHp,
                    fsmState = fsmState,
                    zone = ""
                });
            }

            return memory;
        }
    }
}
