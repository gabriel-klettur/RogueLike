using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

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

            // Refuse to persist an uninitialized or dead player.
            // maxHp==0 means EntitySetup.ConfigurePlayer hasn't run yet.
            var healthCheck = player.GetComponent<Health>();
            if (healthCheck == null || healthCheck.MaxHp <= 0 || healthCheck.CurrentHp <= 0)
            {
                Debug.LogWarning("[GameStateCollector] Skipping save: player HP is invalid " +
                                 $"(hp={healthCheck?.CurrentHp}, maxHp={healthCheck?.MaxHp}).");
                return null;
            }

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
            var layerOccupant = player.GetComponent<VisualLayerOccupant>();
            var wallet = player.GetComponent<CurrencyWallet>();

            var psd = new PlayerSaveData
            {
                playerClass = PlayerSelectionState.SelectedPlayerKey,
                position = (Vector2)player.transform.position,
                hp = health != null ? health.CurrentHp : 0,
                maxHp = health != null ? health.MaxHp : 0,
                mana = mana != null ? mana.CurrentMana : 0,
                maxMana = mana != null ? mana.MaxMana : 0,
                currentZone = UnityEngine.Object.FindObjectOfType<ZoneManager>()?.CurrentZone ?? "",
                experience = experience != null ? experience.TotalXp : 0,
                level = experience != null ? experience.Level : 1,
                visualLayer = layerOccupant != null ? layerOccupant.CurrentVisualLayer : 0,

                // -1 when the player somehow has no wallet, which the restorer reads as
                // "this save says nothing about money" and leaves the balance untouched —
                // the same path a save written before coins were persisted takes.
                coins = wallet != null ? wallet.Coins : -1
            };

            if (inventory != null)
                psd.inventory = inventory.ToSaveData("player");

            // Talents and grimoire. Written through the component that owns them rather
            // than read off the trees, because the character's spent points are state the
            // trees know nothing about.
            var progression = player.GetComponent<PlayerProgression>();
            if (progression != null) progression.WriteTo(psd.progression);

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
