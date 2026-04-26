using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;

namespace Valkur.Tests.PlayMode.Core
{
    /// <summary>
    /// PlayMode tests for EntityRegistry: verifies registration, deregistration,
    /// and that subsystems using EntityRegistry (NPCSeparationSystem) work correctly.
    /// </summary>
    public class EntityRegistryPlayTests
    {
        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            EntityRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator RegisterPlayer_ThenAccess_ReturnsPlayer()
        {
            var playerGo = new GameObject("Player");
            EntityRegistry.RegisterPlayer(playerGo);

            yield return null;

            Assert.IsNotNull(EntityRegistry.Player);
            Assert.AreEqual(playerGo, EntityRegistry.Player);
            Assert.IsTrue(EntityRegistry.HasPlayer);
            Assert.IsNotNull(EntityRegistry.PlayerTransform);

            Object.Destroy(playerGo);
        }

        [UnityTest]
        public IEnumerator UnregisterPlayer_ClearsRef()
        {
            var playerGo = new GameObject("Player");
            EntityRegistry.RegisterPlayer(playerGo);

            yield return null;
            Assert.IsTrue(EntityRegistry.HasPlayer);

            EntityRegistry.UnregisterPlayer(playerGo);
            Assert.IsFalse(EntityRegistry.HasPlayer);
            Assert.IsNull(EntityRegistry.Player);

            Object.Destroy(playerGo);
        }

        [UnityTest]
        public IEnumerator RegisterMonsters_MaintainsList()
        {
            var m1 = new GameObject("Monster1");
            var m2 = new GameObject("Monster2");
            var m3 = new GameObject("Monster3");

            EntityRegistry.RegisterMonster(m1);
            EntityRegistry.RegisterMonster(m2);
            EntityRegistry.RegisterMonster(m3);

            yield return null;

            Assert.AreEqual(3, EntityRegistry.MonsterCount);
            Assert.Contains(m1, (System.Collections.ICollection)EntityRegistry.Monsters);
            Assert.Contains(m2, (System.Collections.ICollection)EntityRegistry.Monsters);
            Assert.Contains(m3, (System.Collections.ICollection)EntityRegistry.Monsters);

            EntityRegistry.UnregisterMonster(m2);
            Assert.AreEqual(2, EntityRegistry.MonsterCount);

            Object.Destroy(m1);
            Object.Destroy(m2);
            Object.Destroy(m3);
        }

        [UnityTest]
        public IEnumerator PurgeDestroyed_RemovesNullEntries()
        {
            var m1 = new GameObject("Monster1");
            var m2 = new GameObject("Monster2");

            EntityRegistry.RegisterMonster(m1);
            EntityRegistry.RegisterMonster(m2);

            Object.Destroy(m1);
            yield return null; // Allow destruction to process

            EntityRegistry.PurgeDestroyed();
            Assert.AreEqual(1, EntityRegistry.MonsterCount);

            Object.Destroy(m2);
        }

        [UnityTest]
        public IEnumerator DuplicateRegister_DoesNotAddTwice()
        {
            var m1 = new GameObject("Monster1");
            EntityRegistry.RegisterMonster(m1);
            EntityRegistry.RegisterMonster(m1);

            yield return null;

            Assert.AreEqual(1, EntityRegistry.MonsterCount);

            Object.Destroy(m1);
        }

        [UnityTest]
        public IEnumerator Clear_RemovesAll()
        {
            var playerGo = new GameObject("Player");
            var m1 = new GameObject("Monster1");
            var npc1 = new GameObject("NPC1");

            EntityRegistry.RegisterPlayer(playerGo);
            EntityRegistry.RegisterMonster(m1);
            EntityRegistry.RegisterNPC(npc1);

            yield return null;

            EntityRegistry.Clear();
            Assert.IsFalse(EntityRegistry.HasPlayer);
            Assert.AreEqual(0, EntityRegistry.MonsterCount);
            Assert.AreEqual(0, EntityRegistry.NPCs.Count);

            Object.Destroy(playerGo);
            Object.Destroy(m1);
            Object.Destroy(npc1);
        }
    }
}
