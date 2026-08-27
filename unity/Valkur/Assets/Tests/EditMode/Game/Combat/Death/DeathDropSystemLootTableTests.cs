using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// Pins <see cref="DeathDropSystem"/>'s loot-table roll — the fix for audit item 3
    /// ("no monster can drop loot"; <c>.github/ENTITIES_FSM_PVM_AUDIT.md</c>): before this,
    /// <see cref="MonsterDefinition"/> had no loot field at all and <see cref="LootTable.Roll"/>
    /// had zero non-test callers. <see cref="MonsterDefinition.lootTable"/> is now rolled
    /// alongside the existing inventory + XP-orb paths in <c>HandleEntityDied</c>, gated to
    /// hostile monsters (<c>stats.faction == "EVIL"</c>, or unset) so a NEUTRAL vendor's table
    /// never fires through ordinary combat death.
    /// </summary>
    [TestFixture]
    public class DeathDropSystemLootTableTests
    {
        private GameObject _systemGo;
        private DeathDropSystem _system;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            GameEvents.Clear();

            _systemGo = new GameObject("DeathDropSystem");
            _system = _systemGo.AddComponent<DeathDropSystem>();
            ForceOnEnable(_system);

            // Clean any lingering pickups from prior tests.
            foreach (var pickup in Object.FindObjectsOfType<WorldPickup>())
                Object.DestroyImmediate(pickup.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemGo != null) Object.DestroyImmediate(_systemGo);
            foreach (var pickup in Object.FindObjectsOfType<WorldPickup>())
                Object.DestroyImmediate(pickup.gameObject);
            GameEvents.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static void ForceOnEnable(MonoBehaviour mb)
        {
            var method = mb.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(mb, null);
        }

        private static int CountPickups() => Object.FindObjectsOfType<WorldPickup>().Length;

        private static ItemDefinition MakeItem(string id)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.rarity = ItemRarity.Common;
            return item;
        }

        /// <summary>Single-entry table at 100% drop chance — always rolls the given item.</summary>
        private static LootTable MakeGuaranteedTable(ItemDefinition item)
        {
            var table = ScriptableObject.CreateInstance<LootTable>();
            table.EditorSetEntries(new[] { new LootTable.Entry { item = item, weight = 1 } });
            table.EditorSetDropChance(1000);
            return table;
        }

        /// <summary>
        /// Builds a bare FSM-monster GameObject (Rigidbody2D + Health + FSMMonsterBrain,
        /// no Awake/Start lifecycle run) and injects a MonsterDefinition with the given
        /// faction and lootTable via reflection — same pattern as
        /// KillCountObjectiveTests.MakeMonster, chosen so the test never has to spin up
        /// FSMMonsterBrain.Initialize's full StateMachine / FSMRuntimeFactory machinery.
        /// </summary>
        private static GameObject MakeMonster(string monsterKey, string faction, LootTable lootTable)
        {
            var go = new GameObject("Monster_" + monsterKey);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<Health>();
            var brain = go.AddComponent<FSMMonsterBrain>();

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = monsterKey;
            def.displayName = monsterKey;
            def.stats = new EntityStats { faction = faction };
            def.lootTable = lootTable;

            var field = typeof(FSMMonsterBrain).GetField("definition",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(brain, def);
            return go;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void NoLootTable_DropsNothingExtra()
        {
            var victim = MakeMonster("barbol_test", "EVIL", lootTable: null);
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(victim, null);

                Assert.AreEqual(before, CountPickups(),
                    "A MonsterDefinition with lootTable == null must not spawn a ground pickup.");
            }
            finally { Object.DestroyImmediate(victim); }
        }

        [Test]
        public void LootTable_ProducesDrop()
        {
            var item = MakeItem("test_loot_drop");
            var table = MakeGuaranteedTable(item);
            var victim = MakeMonster("barbol_test", "EVIL", table);
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(victim, null);

                Assert.AreEqual(before + 1, CountPickups(),
                    "A hostile monster with a guaranteed-drop lootTable must spawn exactly one pickup.");

                var pickup = Object.FindObjectOfType<WorldPickup>();
                Assert.IsNotNull(pickup, "The spawned drop must be a WorldPickup.");
                Assert.AreSame(item, pickup.Item, "The spawned pickup must carry the rolled item.");
            }
            finally
            {
                Object.DestroyImmediate(victim);
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void FriendlyVendor_LootTableNeverRolls()
        {
            // NEUTRAL is how every shipped vendor is authored (vendor_alchemist_valeria.asset
            // et al.). Even a guaranteed-drop table must not fire through ordinary combat death.
            var item = MakeItem("vendor_would_drop");
            var table = MakeGuaranteedTable(item);
            var victim = MakeMonster("vendor_test", "NEUTRAL", table);
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(victim, null);

                Assert.AreEqual(before, CountPickups(),
                    "A NEUTRAL (friendly/vendor) monster must never roll its lootTable, " +
                    "even if one happens to be assigned.");
            }
            finally
            {
                Object.DestroyImmediate(victim);
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void EmptyFaction_DefaultsToHostile_AndRolls()
        {
            // Mirrors NPCRespawnSystem.HostileFaction: an unauthored faction string
            // defaults to hostile, so legacy monster assets that predate the faction
            // field still drop their loot table.
            var item = MakeItem("legacy_mob_drop");
            var table = MakeGuaranteedTable(item);
            var victim = MakeMonster("legacy_mob", faction: "", lootTable: table);
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(victim, null);

                Assert.AreEqual(before + 1, CountPickups(),
                    "An unset faction must default to hostile and still roll the loot table.");
            }
            finally
            {
                Object.DestroyImmediate(victim);
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void Player_NeverRollsLootTable()
        {
            // Belt-and-suspenders on top of the top-of-method Player tag guard —
            // even if a MonsterDefinition were (mis)attached to the player, death
            // must route through PlayerDeathDropSystem, never this table roll.
            var item = MakeItem("player_would_never_drop");
            var table = MakeGuaranteedTable(item);
            var player = MakeMonster("player", "EVIL", table);
            player.tag = "Player";
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(player, null);

                Assert.AreEqual(before, CountPickups(),
                    "Player deaths must never roll a monster lootTable through DeathDropSystem.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
            }
        }
    }
}
