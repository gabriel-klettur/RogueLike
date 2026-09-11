using System.Globalization;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.NPC;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Applies a GameSaveData snapshot back to live entities.
    /// No IO — pure state restoration.
    /// </summary>
    public static class GameStateRestorer
    {
        /// <summary>
        /// Restore game state from save data onto the current player and world.
        /// </summary>
        public static void Restore(GameSaveData data)
        {
            // Before the player guard: the market is WORLD state and a save whose player
            // block is missing or malformed should still put the economy back rather than
            // silently reseeding it, which would move every price in the world.
            RestoreMarket(data);

            if (data.player == null) return;

            if (!string.IsNullOrWhiteSpace(data.player.playerClass))
                PlayerSelectionState.SetSelectedPlayer(data.player.playerClass);

            var player = EntityRegistry.Player;
            if (player == null)
            {
                Debug.LogWarning("[GameStateRestorer] No player found to restore state.");
                return;
            }

            // Resolved BEFORE any stat restore, because it changes what RestoreHealth may do: a
            // save taken in spirit form legitimately carries hp 0, and the historical guard bumps
            // any 0 to full — which would revive the player on load and hand back the run.
            bool wasSpirit = DeathStateSave.TryRead(data, out Vector3 corpsePosition);

            RestorePosition(player, data.player);
            // Progression FIRST among the stat-bearing restores. It rebuilds the Level,
            // Skill and Grimoire stat layers, and PlayerStats pushes the resolved max HP
            // and max mana into Health and Mana as it does — so restoring those two before
            // it would have their saved values immediately overwritten by the recompute.
            RestoreProgression(player, data.player);
            RestoreHealth(player, data.player, wasSpirit);
            RestoreMana(player, data.player);
            RestoreExperience(player, data.player);
            RestoreCoins(player, data.player);
            RestoreInventory(player, data.player);
            RestoreVisualLayer(player, data.player);

            // After experience, coins and the bag: a restored quest re-begins its
            // objectives, and the polled ones (Collect / EarnCoins / ReachLevel) read the
            // live player on their first tick. Restoring them earlier would measure a
            // half-built character.
            RestoreQuests(data.player);

            // LAST, after every stat and the inventory are back: entering spirit form spawns a
            // corpse and swaps the collider mask, and doing that before RestoreInventory would put
            // the swap in the middle of a rebuild it has no reason to be inside.
            if (wasSpirit) RestoreSpirit(corpsePosition);

            Debug.Log($"[GameStateRestorer] Player state restored: pos={data.player.position}, " +
                      $"HP={data.player.hp}/{data.player.maxHp}, " +
                      $"Mana={data.player.mana}/{data.player.maxMana}, " +
                      $"XP={data.player.experience}, Lv={data.player.level}, " +
                      $"Coins={data.player.coins}, " +
                      $"VisualLayer={data.player.visualLayer}");
        }

        /// <summary>
        /// Puts the economic cycle back where the save left it.
        ///
        /// <para>Both halves matter and they fail differently. Losing the SEED reshapes the
        /// cycle — different phase lengths, so a player who learned this run's rhythm is
        /// reading a market that no longer exists. Losing the DAY rewinds it to Boom, which
        /// is worse: it is not noticeable, it is not random, and it means the Peak can be
        /// farmed by reloading.</para>
        ///
        /// <para>A save that carries neither key predates this layer. It is restored as
        /// <c>(0, 0)</c>, which <c>MarketService.RestoreFrom</c> reads as "keep the seed you
        /// derived, start the clock" — a fresh market rather than a refusal, because refusing
        /// would leave the service holding whatever the previous save had loaded.</para>
        /// </summary>
        private static void RestoreMarket(GameSaveData data)
        {
            if (data == null || !MarketService.HasInstance) return;

            int seed = ParseMeta(data, MarketService.SeedMetaKey);
            int day = ParseMeta(data, MarketService.DayMetaKey);
            string source = data.GetMeta(MarketService.SourceMetaKey, "");

            MarketService.Instance.RestoreFrom(seed, day, source);
        }

        /// <summary>
        /// Reads one integer out of the metadata bag, invariantly. Anything unparseable reads
        /// as 0 — the same answer as absent, which is the right one: a corrupted market key is
        /// not worth refusing a whole save load over.
        /// </summary>
        private static int ParseMeta(GameSaveData data, string key)
        {
            string raw = data.GetMeta(key, "");
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : 0;
        }

        /// <summary>
        /// Put the player where the save says — unless the save says somewhere this world does
        /// not have.
        ///
        /// <para>A save written while the player stood inside an interior carries that room's
        /// own coordinate, which is off the map here. It is detectable because the same save
        /// carries the zone it was measured in, and an interior's label is its overlay file
        /// rather than a zone in the database. Leaving the player where the spawn put them is
        /// the recoverable answer; the void is not.</para>
        ///
        /// <para>This is what heals a save poisoned before the write-side guard existed, so it
        /// is not redundant with it — see <see cref="PlayerPositionPersistence"/>.</para>
        /// </summary>
        private static void RestorePosition(GameObject player, PlayerSaveData psd)
        {
            var zones = UnityEngine.Object.FindObjectOfType<ZoneManager>();
            System.Func<string, bool> known = zones == null
                ? (System.Func<string, bool>)null
                : (name => zones.TryGetZone(name, out _));

            if (!PlayerPositionPersistence.IsUsableSpawn(psd.currentZone, known, out string refusal))
            {
                Debug.LogWarning($"[GameStateRestorer] Keeping the spawn position: the save records " +
                                 $"({psd.position.x:F2}, {psd.position.y:F2}) but {refusal}");
                return;
            }

            player.transform.position = new Vector3(psd.position.x, psd.position.y, 0f);
        }

        /// <summary>
        /// Put the player back into spirit form after loading a save taken while dead.
        ///
        /// <para>Silent when no controller exists — an EditMode fixture restoring a save has no
        /// death flow, and refusing the whole load over it would be worse than loading a player
        /// who is simply alive.</para>
        /// </summary>
        private static void RestoreSpirit(Vector3 corpsePosition)
        {
            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller == null)
            {
                Debug.LogWarning("[GameStateRestorer] Save says the player was a spirit, but no " +
                                 "DeathSequenceController is registered — loaded as alive.");
                return;
            }
            controller.RestoreSpiritState(corpsePosition);
        }

        private static void RestoreHealth(GameObject player, PlayerSaveData psd, bool wasSpirit)
        {
            var health = player.GetComponent<Health>();
            if (health == null) return;

            // Guard: a save with hp==0 usually means the write raced a death and the state is
            // junk, so it is restored to full rather than loading a corpse. The ONE exception is a
            // save that explicitly says the player was a spirit — there the 0 is the recorded
            // truth, and bumping it is what made dying free for anyone willing to reload.
            int safeHp = (psd.hp > 0) ? psd.hp : (wasSpirit ? 0 : psd.maxHp);

            // Use the (max, current) overload so OnDamaged / EntityDamaged
            // events DON'T fire. Going through TakeDamage(delta) here used
            // to play the hurt SFX + hit-flash the instant the run started,
            // because CombatAudioSystem treats every OnDamaged as a real hit.
            health.Initialize(psd.maxHp, safeHp);
        }

        private static void RestoreMana(GameObject player, PlayerSaveData psd)
        {
            var mana = player.GetComponent<Mana>();
            if (mana == null || psd.maxMana <= 0) return;

            int maxMana = Mathf.RoundToInt(psd.maxMana);
            int curMana = Mathf.RoundToInt(psd.mana);

            // Same rationale as RestoreHealth: bypass TryConsume so future
            // OnManaConsumed subscribers (spell-cost SFX, etc.) don't fire
            // a fake consume event on game load.
            mana.Initialize(maxMana, curMana, 2f);
        }

        /// <summary>
        /// Puts the coin balance back.
        ///
        /// <para>Coins were collected by pickups and by selling to vendors, and then thrown
        /// away on every load: nothing in the save pipeline mentioned CurrencyWallet, and
        /// its Awake resets the balance to <c>startingCoins</c> each session. A player could
        /// sell an inventory to Gatita and have the money gone by the next launch.</para>
        ///
        /// <para>A negative balance means the save predates this field. Restoring 0 for
        /// those would look exactly like the bug, so they are left at whatever the wallet
        /// started with.</para>
        /// </summary>
        private static void RestoreCoins(GameObject player, PlayerSaveData psd)
        {
            if (psd.coins < 0) return;

            var wallet = player.GetComponent<CurrencyWallet>();
            if (wallet != null) wallet.SetBalance(psd.coins);
        }

        /// <summary>
        /// Rehydrates talents, grimoire and both point balances, then rebuilds every stat
        /// layer that depends on them and re-syncs the spell book to what the character
        /// knows.
        ///
        /// A save written before progression existed carries an EMPTY document rather than
        /// null (JsonUtility runs field initialisers first). PlayerProgression treats that
        /// case as a migration rather than as data — see RestoreFrom — because reading it
        /// literally would zero both point balances and destroy the grant the character was
        /// given moments earlier at spawn.
        /// </summary>
        private static void RestoreProgression(GameObject player, PlayerSaveData psd)
        {
            var progression = player.GetComponent<PlayerProgression>();
            if (progression == null) return;

            progression.RestoreFrom(psd.progression, Mathf.Max(1, psd.level));
        }

        /// <summary>
        /// Rebuilds the quest log: which quests are open, how far along each one is, and
        /// which are done.
        ///
        /// <para>Runs AFTER progression and experience, and that order is load-bearing
        /// rather than tidy. Restoring an active quest re-BEGINS its objectives, and the
        /// polled ones read the live player the moment they tick — a ReachLevel objective
        /// restored before <c>RestoreExperience</c> would measure a level-1 character and
        /// report the quest as barely started until the next poll corrected it.</para>
        ///
        /// <para>A save with no quest document restores an empty log rather than warning:
        /// every save written before this layer existed is exactly that, and it describes
        /// a character who has accepted nothing.</para>
        /// </summary>
        private static void RestoreQuests(PlayerSaveData psd)
        {
            var service = Quests.QuestService.Instance;
            var manager = service != null
                ? service.Manager
                : UnityEngine.Object.FindObjectOfType<Quests.QuestManager>();
            if (manager == null) return;

            var catalog = service != null
                ? service.Definitions
                : System.Array.Empty<Valkur.Data.QuestDefinition>();

            manager.ReadFrom(psd.quests, catalog);
        }

        private static void RestoreExperience(GameObject player, PlayerSaveData psd)
        {
            var experience = player.GetComponent<Experience>();
            if (experience != null)
                experience.Initialize(psd.experience, psd.level);
        }

        private static void RestoreInventory(GameObject player, PlayerSaveData psd)
        {
            Debug.Log($"[GameStateRestorer] RestoreInventory ENTER player={player?.name} psd.inventory={(psd.inventory == null ? "NULL" : "OK")}");
            if (psd.inventory == null) return;

            var inventory = player.GetComponent<Inventory.Inventory>();
            if (inventory == null)
            {
                Debug.LogWarning($"[GameStateRestorer] Player '{player.name}' has no Inventory component — abort.");
                return;
            }

            // Initialize already clears + resizes; no need for a separate Clear().
            // Force capacity to at least the current default so the bag UI never
            // shows dead cells when an old save reloads with a smaller capacity.
            int restoredCapacity = Mathf.Max(psd.inventory.capacity,
                                             Inventory.Inventory.DefaultBagCapacity);
            inventory.Initialize(restoredCapacity);

            var slots = psd.inventory.slots;
            if (slots == null || slots.Count == 0)
            {
                Debug.Log("[GameStateRestorer] Inventory restored (empty).");
                return;
            }

            // Re-hydrate items by resolving ids through the canonical ItemCatalog.
            // Without it we have no way to map a saved string id back to the live
            // ItemDefinition asset, so we fail loud rather than silently drop items.
            if (!ServiceLocator.TryGet<ItemCatalog>(out var catalog) || catalog == null)
            {
                Debug.LogWarning("[GameStateRestorer] No ItemCatalog registered — inventory items cannot be resolved and will be lost on this load.");
                return;
            }

            // Index-aligned restore: each saved entry's position in the list IS
            // its visual slot. Old saves (schema 1.0, compact list shorter than
            // capacity) still load correctly because the i-th compact entry
            // becomes the i-th visual slot — same outcome as the previous
            // AddItem-based restore for those payloads.
            int restored = 0;
            int missing  = 0;
            int max = Mathf.Min(slots.Count, psd.inventory.capacity);
            for (int i = 0; i < max; i++)
            {
                var slot = slots[i];
                if (string.IsNullOrEmpty(slot.itemId) || slot.quantity <= 0) continue;

                var def = catalog.GetById(slot.itemId);
                if (def == null)
                {
                    Debug.LogWarning($"[GameStateRestorer] Saved itemId '{slot.itemId}' (slot {i}) not found in ItemCatalog — slot dropped.");
                    missing++;
                    continue;
                }

                inventory.SetSlot(i, def, slot.quantity);
                Debug.Log($"[GameStateRestorer] SetSlot bag[{i}] = {slot.itemId} x{slot.quantity}");
                restored++;
            }

            int equipRestored = 0;
            var equipSlots = psd.inventory.equipmentSlots;
            if (equipSlots != null)
            {
                int eqMax = Mathf.Min(equipSlots.Count, Inventory.Inventory.EquipmentCapacity);
                for (int i = 0; i < eqMax; i++)
                {
                    var slot = equipSlots[i];
                    if (string.IsNullOrEmpty(slot.itemId) || slot.quantity <= 0) continue;
                    var def = catalog.GetById(slot.itemId);
                    if (def == null)
                    {
                        Debug.LogWarning($"[GameStateRestorer] Saved equipment itemId '{slot.itemId}' (slot {i}) not found in ItemCatalog — slot dropped.");
                        missing++;
                        continue;
                    }
                    inventory.SetEquipmentSlot(i, def, slot.quantity);
                    equipRestored++;
                }
            }

            Debug.Log($"[GameStateRestorer] Inventory restored: {restored} bag stack(s), {equipRestored} equipment slot(s)" +
                      (missing > 0 ? $", {missing} missing" : "") +
                      $" (capacity={psd.inventory.capacity}).");

            // Sanity probe: confirm what the live Inventory component actually
            // holds *after* we finished writing. If this list is empty but the
            // log above said "restored: N", a downstream system is wiping the
            // inventory between Restore and the next UI refresh.
            int live = 0;
            for (int i = 0; i < inventory.Slots.Count; i++)
                if (!inventory.Slots[i].IsEmpty) live++;
            Debug.Log($"[GameStateRestorer] Live Inventory probe: {live} non-empty bag slot(s) on '{player.name}'.");
        }

        /// <summary>
        /// Restore the player's current visual layer from <paramref name="psd"/>'s
        /// <c>visualLayer</c> field. Routes through
        /// <see cref="VisualLayerOccupant.SetVisualLayer(int)"/> so listeners (panel
        /// readouts now, per-layer Physics2D includeLayers in M2) get the same
        /// <c>OnLayerChanged</c> event they would on a gameplay-driven flip.
        /// </summary>
        private static void RestoreVisualLayer(GameObject player, PlayerSaveData psd)
        {
            var occupant = player.GetComponent<VisualLayerOccupant>();
            if (occupant == null) return;
            // Setter clamps to [MinLayer..MaxLayer] and no-ops if the value didn't
            // change, so a fresh-spawn player on layer 0 reloading a layer-0 save
            // skips the event entirely — cheaper and matches "no real transition".
            occupant.SetVisualLayer(psd.visualLayer);
        }
    }
}
