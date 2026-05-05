using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// Pins the regression that <see cref="DeathDropSystem"/> spawns an XP
    /// orb for *every* dead NPC, regardless of inventory presence. Before
    /// the fix, entities without an Inventory component (barbols, all FSM
    /// monsters) returned early and never granted XP.
    /// </summary>
    [TestFixture]
    public class DeathDropSystemXpOrbTests
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

            // Clean any lingering XP orbs from prior tests.
            foreach (var orb in Object.FindObjectsOfType<XpOrb>())
                Object.DestroyImmediate(orb.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemGo != null) Object.DestroyImmediate(_systemGo);
            foreach (var orb in Object.FindObjectsOfType<XpOrb>())
                Object.DestroyImmediate(orb.gameObject);
            GameEvents.Clear();
        }

        private static void ForceOnEnable(MonoBehaviour mb)
        {
            var method = mb.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(mb, null);
        }

        private static int CountOrbs() => Object.FindObjectsOfType<XpOrb>().Length;

        [Test]
        public void NpcWithoutInventory_DropsXpOrb()
        {
            // Barbol-like NPC: no Inventory, only Health. Before the fix the
            // system returned early at the inventory null check.
            var npc = new GameObject("Barbol");
            try
            {
                var hp = npc.AddComponent<Health>();
                hp.Initialize(100);

                int before = CountOrbs();
                GameEvents.FireEntityDied(npc, null);

                Assert.AreEqual(before + 1, CountOrbs(),
                    "Inventory-less NPC must still drop an XP orb on death.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        [Test]
        public void NpcWithEmptyInventory_DropsXpOrb()
        {
            var npc = new GameObject("EmptyBag");
            try
            {
                var hp = npc.AddComponent<Health>();
                hp.Initialize(50);
                npc.AddComponent<Valkur.Gameplay.Inventory.Inventory>().Initialize(8);

                int before = CountOrbs();
                GameEvents.FireEntityDied(npc, null);

                Assert.AreEqual(before + 1, CountOrbs(),
                    "Empty inventory is not a reason to skip the XP orb.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        [Test]
        public void Player_DoesNotDropOrb()
        {
            var player = new GameObject("Player");
            player.tag = "Player";
            try
            {
                var hp = player.AddComponent<Health>();
                hp.Initialize(100);

                int before = CountOrbs();
                GameEvents.FireEntityDied(player, null);

                Assert.AreEqual(before, CountOrbs(),
                    "Player deaths are routed through PlayerDeathDropSystem and " +
                    "must not produce an XP orb here.");
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void NullVictim_IsNoOp()
        {
            int before = CountOrbs();
            Assert.DoesNotThrow(() => GameEvents.FireEntityDied(null, null));
            Assert.AreEqual(before, CountOrbs());
        }

        [Test]
        public void OrbCarriesEstimatedXpValue()
        {
            // hp=50 → maxHp/5 fallback = 10 (no MonsterDefinition on the GO).
            var npc = new GameObject("Mob");
            try
            {
                var hp = npc.AddComponent<Health>();
                hp.Initialize(50);

                GameEvents.FireEntityDied(npc, null);

                var orb = Object.FindObjectOfType<XpOrb>();
                Assert.IsNotNull(orb, "An orb must have been spawned.");
                Assert.AreEqual(10, orb.XpValue,
                    "Without a MonsterDefinition the heuristic must use maxHp/5 = 10.");
            }
            finally { Object.DestroyImmediate(npc); }
        }
    }
}
