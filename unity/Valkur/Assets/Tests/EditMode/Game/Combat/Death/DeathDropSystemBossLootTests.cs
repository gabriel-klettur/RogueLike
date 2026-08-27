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
    /// Pins <see cref="DeathDropSystem"/>'s roll of <see cref="BossDefinition.bossLoot"/> —
    /// the audit's Phase-4 "item 3" caveat: <c>MonsterDefinition.lootTable</c> was already
    /// rolled, but <c>bossLoot</c> had no reader anywhere, because nothing attached a
    /// <see cref="BossConfigurator"/> to a live boss until <c>EntitySetup.ConfigureBoss</c>
    /// shipped. Mirrors <see cref="DeathDropSystemLootTableTests"/>'s fixture shape.
    /// </summary>
    [TestFixture]
    public class DeathDropSystemBossLootTests
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

        // ── Helpers (mirrors DeathDropSystemLootTableTests) ──────────────────────

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

        private static LootTable MakeGuaranteedTable(ItemDefinition item)
        {
            var table = ScriptableObject.CreateInstance<LootTable>();
            table.EditorSetEntries(new[] { new LootTable.Entry { item = item, weight = 1 } });
            table.EditorSetDropChance(1000);
            return table;
        }

        /// <summary>
        /// Builds a bare FSM-monster GameObject and, when <paramref name="bossLoot"/> is
        /// non-null, attaches a real <see cref="BossConfigurator"/> (which chain-requires
        /// <see cref="BossPhaseController"/>, itself requiring the <see cref="Health"/> already
        /// added below) carrying a <see cref="BossDefinition"/> naming that table. No lifecycle
        /// method is invoked on the configurator — <c>DeathDropSystem</c> only ever reads its
        /// public <c>Definition</c> property, so Awake/Start never need to run for this test.
        /// </summary>
        private static GameObject MakeMonster(string monsterKey, string faction,
            LootTable monsterLoot, LootTable bossLoot)
        {
            var go = new GameObject("Monster_" + monsterKey);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<Health>();
            var brain = go.AddComponent<FSMMonsterBrain>();

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = monsterKey;
            def.displayName = monsterKey;
            def.stats = new EntityStats { faction = faction };
            def.lootTable = monsterLoot;

            var field = typeof(FSMMonsterBrain).GetField("definition",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(brain, def);

            if (bossLoot != null)
            {
                var bossDef = ScriptableObject.CreateInstance<BossDefinition>();
                bossDef.bossLoot = bossLoot;
                var configurator = go.AddComponent<BossConfigurator>();
                configurator.SetDefinition(bossDef);
            }

            return go;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void BossWithBossLoot_DropsIt()
        {
            var item = MakeItem("boss_guaranteed_drop");
            var bossLoot = MakeGuaranteedTable(item);
            var victim = MakeMonster("barbol_boss_test", "EVIL", monsterLoot: null, bossLoot: bossLoot);
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(victim, null);

                Assert.AreEqual(before + 1, CountPickups(),
                    "A boss whose BossDefinition names a bossLoot table must spawn a pickup " +
                    "from it — before this fix, bossLoot had no reader anywhere.");

                var pickup = Object.FindObjectOfType<WorldPickup>();
                Assert.IsNotNull(pickup);
                Assert.AreSame(item, pickup.Item);
            }
            finally
            {
                Object.DestroyImmediate(victim);
                Object.DestroyImmediate(bossLoot);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void BossLoot_StacksWithTheMonstersOwnLootTable()
        {
            // BossDefinition.bossLoot's own doc comment: "bosses can drop guaranteed items
            // via this table AND the regular monster drop pool simultaneously."
            var monsterItem = MakeItem("monster_pool_drop");
            var bossItem = MakeItem("boss_guaranteed_drop_2");
            var monsterTable = MakeGuaranteedTable(monsterItem);
            var bossTable = MakeGuaranteedTable(bossItem);
            var victim = MakeMonster("barbol_boss_test2", "EVIL", monsterTable, bossTable);
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(victim, null);

                Assert.AreEqual(before + 2, CountPickups(),
                    "A boss must roll BOTH its own MonsterDefinition.lootTable and its " +
                    "BossDefinition.bossLoot — they stack, they don't replace each other.");
            }
            finally
            {
                Object.DestroyImmediate(victim);
                Object.DestroyImmediate(monsterTable);
                Object.DestroyImmediate(bossTable);
                Object.DestroyImmediate(monsterItem);
                Object.DestroyImmediate(bossItem);
            }
        }

        [Test]
        public void PlainMonster_NoBossConfigurator_BossLootNeverRolls()
        {
            // A monster with no BossConfigurator component at all (the overwhelming majority
            // of the bestiary) must never attempt the boss-loot path — GetComponent returns
            // null and TryDropBossLootTable no-ops.
            var victim = MakeMonster("barbol_plain", "EVIL", monsterLoot: null, bossLoot: null);
            try
            {
                int before = CountPickups();
                Assert.DoesNotThrow(() => GameEvents.FireEntityDied(victim, null));

                Assert.AreEqual(before, CountPickups(),
                    "A plain (non-boss) monster must not drop anything extra.");
            }
            finally { Object.DestroyImmediate(victim); }
        }

        [Test]
        public void FriendlyBoss_BossLootNeverRolls()
        {
            // Mirrors DeathDropSystemLootTableTests.FriendlyVendor_LootTableNeverRolls — the
            // hostile-faction gate applies identically to the boss-loot path so an authored
            // BossDefinition can never be used to bypass "vendors never drop loot".
            var item = MakeItem("boss_would_never_drop");
            var bossLoot = MakeGuaranteedTable(item);
            var victim = MakeMonster("boss_vendor_test", "NEUTRAL", monsterLoot: null, bossLoot: bossLoot);
            try
            {
                int before = CountPickups();
                GameEvents.FireEntityDied(victim, null);

                Assert.AreEqual(before, CountPickups(),
                    "A NEUTRAL boss must never roll bossLoot through ordinary combat death.");
            }
            finally
            {
                Object.DestroyImmediate(victim);
                Object.DestroyImmediate(bossLoot);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void BossConfiguratorWithNoDefinition_DoesNotThrow()
        {
            // A BossConfigurator can exist with no BossDefinition assigned yet (mid-authoring
            // in the Boss Editor's preview sandbox, or a stray component). Definition is then
            // null, not the BossDefinition's bossLoot field — must not NRE.
            var victim = MakeMonster("boss_no_def", "EVIL", monsterLoot: null, bossLoot: null);
            victim.AddComponent<BossConfigurator>(); // definition left unset
            try
            {
                Assert.DoesNotThrow(() => GameEvents.FireEntityDied(victim, null));
            }
            finally { Object.DestroyImmediate(victim); }
        }
    }
}
