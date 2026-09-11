using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;
using Valkur.Data;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.NPC;
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

            // Refuse to persist an UNINITIALIZED player: maxHp==0 means EntitySetup.ConfigurePlayer
            // has not run yet, and a save written there restores a character with no stats.
            var healthCheck = player.GetComponent<Health>();
            if (healthCheck == null || healthCheck.MaxHp <= 0)
            {
                Debug.LogWarning("[GameStateCollector] Skipping save: player HP is invalid " +
                                 $"(hp={healthCheck?.CurrentHp}, maxHp={healthCheck?.MaxHp}).");
                return null;
            }

            // A DEAD player is a different case and used to be refused with it. That refusal is
            // what made death free: the newest save was always a pre-death one, so dying, quitting
            // and loading gave back the inventory, the coins and the XP. It is only allowed through
            // when DeathTuning.persistDeathState is on AND the flow can be recorded, so turning the
            // setting off restores the historical behaviour exactly.
            var deathController = ServiceLocator.Get<DeathSequenceController>();
            if (healthCheck.CurrentHp <= 0 && !DeathStateSave.ShouldPersist(deathController))
            {
                Debug.LogWarning("[GameStateCollector] Skipping save: player is dead and " +
                                 "DeathTuning.persistDeathState is off.");
                return null;
            }

            var data = new GameSaveData
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };

            data.player = CollectPlayerState(player);
            data.npcMemory = CollectNpcMemory();
            CollectMarketState(data);
            DeathStateSave.Collect(data, deathController);

            return data;
        }

        /// <summary>
        /// Writes the economic cycle's two integers into the metadata bag.
        ///
        /// <para>The bag rather than a typed field on <see cref="GameSaveData"/>, and that is
        /// a deliberate call: this is WORLD state, not player state, so it does not belong on
        /// <see cref="PlayerSaveData"/> where every other saved number lives; it is exactly
        /// two ints; and the bag is already the project's answer for run-level facts
        /// (<c>run_id</c>, <c>run_ordinal</c>). Going through it means no schema-version bump
        /// and no migration path for saves that predate the market — they simply carry no
        /// market keys, and <c>MarketService.RestoreFrom</c> reads that as a fresh one.</para>
        ///
        /// <para>Skipped entirely when no market is running, rather than writing zeros: a
        /// seed of 0 is the "nobody set one" sentinel, and persisting it would make every
        /// save taken in a market-less scene share one economy on the next load.</para>
        /// </summary>
        private static void CollectMarketState(GameSaveData data)
        {
            if (!MarketService.HasInstance) return;
            var market = MarketService.Instance;

            data.SetMeta(MarketService.SeedMetaKey,
                market.Seed.ToString(CultureInfo.InvariantCulture));
            data.SetMeta(MarketService.DayMetaKey,
                market.Day.ToString(CultureInfo.InvariantCulture));
            data.SetMeta(MarketService.SourceMetaKey, market.SeedSource);
        }

        /// <summary>
        /// Where to record the player, and under which zone.
        ///
        /// <para>Routed through <c>SaveService</c> so this and the position checkpoint answer
        /// with one rule — see <see cref="PlayerPositionPersistence"/>. Without a live service
        /// (an EditMode fixture collecting a save) it falls back to the transform, which is
        /// what this method always did and is correct whenever no transition is in flight.</para>
        ///
        /// <para>The interior position is still written when nothing better is known, because
        /// a save document must carry SOME position. It carries the interior's zone label with
        /// it, which is exactly what the restore side refuses to spawn on.</para>
        /// </summary>
        private static Vector2 ResolvePlayerPosition(GameObject player, out string zone)
        {
            Vector2 live     = (Vector2)player.transform.position;
            string  liveZone = UnityEngine.Object.FindObjectOfType<ZoneManager>()?.CurrentZone ?? "";

            // HasInstance, never a bare Instance: the singleton accessor is not something to
            // touch from a collector that also runs in fixtures with no service in the scene.
            var service = Valkur.Gameplay.SaveService.HasInstance
                        ? Valkur.Gameplay.SaveService.Instance : null;
            if (service == null)
            {
                zone = liveZone;
                return live;
            }

            // The LIVE values go in and the answer comes back: outside a transition it is the
            // same pair, so the snapshot still captures the transform at the moment of the call.
            var record = service.ResolvePersistablePlayerPosition(live, liveZone);
            zone = record.Zone ?? "";
            return record.Position;
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
                // NOT the live transform. An interior is its own grid loaded at the origin, so
                // a save taken inside one records a room-local coordinate that is off the map
                // in the base world — measured, the player block came back as (11, -8). The
                // service answers the same question the position checkpoint asks, so the two
                // writers of the player position cannot disagree.
                position = ResolvePlayerPosition(player, out string playerZone),
                hp = health != null ? health.CurrentHp : 0,
                maxHp = health != null ? health.MaxHp : 0,
                mana = mana != null ? mana.CurrentMana : 0,
                maxMana = mana != null ? mana.MaxMana : 0,
                // Whatever zone the position above belongs to, which inside an interior is the
                // one the player walked in FROM. Keeping the live zone beside a base-world
                // position would label it with a room it is not in — and the restore side
                // reads exactly that label to decide whether a position is spawnable.
                currentZone = playerZone,
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

            // The quest log. Read off the manager rather than off the player, because
            // the manager is the only thing that knows how far along an open quest is —
            // the player carries the consequences (xp, coins, items) and none of the
            // state. Absent manager writes an empty document, which restores as "this
            // character has accepted nothing" rather than as a warning.
            var quests = Quests.QuestService.Instance != null
                ? Quests.QuestService.Instance.Manager
                : UnityEngine.Object.FindObjectOfType<Quests.QuestManager>();
            if (quests != null) quests.WriteTo(psd.quests);

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
