using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="XpFeedbackSystem"/>: XP-gained on the player records
    /// the amount + entity, level-up on the player records the formatted
    /// toast string, NPCs are filtered out by default, and zero / null
    /// inputs are no-ops.
    /// </summary>
    [TestFixture]
    public class XpFeedbackSystemTests
    {
        private GameObject _systemGo;
        private XpFeedbackSystem _system;
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();

            _systemGo = new GameObject("XpFeedbackSystem");
            _system = _systemGo.AddComponent<XpFeedbackSystem>();

            // Force OnEnable in EditMode (AddComponent doesn't always fire it).
            var onEnable = typeof(XpFeedbackSystem).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);
            onEnable.Invoke(_system, null);

            _player = new GameObject("Player");
            _player.tag = "Player";
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemGo != null) Object.DestroyImmediate(_systemGo);
            if (_player != null) Object.DestroyImmediate(_player);
            GameEvents.Clear();
        }

        [Test]
        public void PlayerXpGained_RecordsAmountAndEntity()
        {
            GameEvents.FireXpGained(_player, 25);

            Assert.AreEqual(25, _system.LastXpShown);
            Assert.AreSame(_player, _system.LastXpEntity);
        }

        [Test]
        public void NpcXpGained_IsFiltered_WhenPlayerOnly()
        {
            var npc = new GameObject("NPC");
            try
            {
                GameEvents.FireXpGained(npc, 10);
                Assert.AreEqual(0, _system.LastXpShown,
                    "Default config is playerOnly=true — NPC XP must be ignored.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        [Test]
        public void ZeroAmount_IsNoOp()
        {
            GameEvents.FireXpGained(_player, 0);
            Assert.AreEqual(0, _system.LastXpShown);
            Assert.IsNull(_system.LastXpEntity);
        }

        [Test]
        public void PlayerLevelUp_FormatsToastWithLevel()
        {
            GameEvents.FireLevelUp(_player, 7);

            Assert.AreEqual(7, _system.LastToastedLevel);
            StringAssert.Contains("LEVEL UP", _system.LastToastMessage);
            StringAssert.Contains("7", _system.LastToastMessage);
        }

        [Test]
        public void NpcLevelUp_IsFiltered_WhenPlayerOnly()
        {
            var npc = new GameObject("NPC");
            try
            {
                GameEvents.FireLevelUp(npc, 9);
                Assert.AreEqual(-1, _system.LastToastedLevel,
                    "NPC level-ups must not produce a player-facing toast.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        [Test]
        public void XpGained_FloatingNumberSpawned_OnPlayerSpawner()
        {
            var spawner = _player.AddComponent<FloatingDamageSpawner>();
            int before = spawner.SpawnedCount;

            GameEvents.FireXpGained(_player, 12);

            Assert.AreEqual(before + 1, spawner.SpawnedCount,
                "OnXpGained must drive the player's FloatingDamageSpawner.");
            StringAssert.Contains("+12", spawner.LastSpawnedText);
            StringAssert.Contains("XP", spawner.LastSpawnedText);
        }

        [Test]
        public void NullEntity_IsNoOp()
        {
            GameEvents.FireXpGained(null, 5);
            GameEvents.FireLevelUp(null, 5);

            Assert.AreEqual(0, _system.LastXpShown);
            Assert.AreEqual(-1, _system.LastToastedLevel);
        }
    }
}
