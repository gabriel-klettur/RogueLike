using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core.Registry
{
    /// <summary>
    /// EditMode tests for the static <see cref="EntityRegistry"/>.
    ///
    /// Migrated from <c>PlayMode/Core/EntityRegistryPlayTests.cs</c>:
    /// <see cref="EntityRegistry"/> is a static class with no MonoBehaviour
    /// lifecycle and no <see cref="Time"/> dependency. The original used
    /// <c>yield return null</c> to let <see cref="Object.Destroy"/> drain;
    /// EditMode uses <see cref="Object.DestroyImmediate"/> instead so the
    /// same purge semantics fire synchronously.
    /// </summary>
    [TestFixture]
    public class EntityRegistryTests
    {
        [SetUp]
        public void SetUp() => EntityRegistry.Clear();

        [TearDown]
        public void TearDown() => EntityRegistry.Clear();

        [Test]
        public void RegisterPlayer_ThenAccess_ReturnsPlayer()
        {
            var playerGo = new GameObject("Player");
            try
            {
                EntityRegistry.RegisterPlayer(playerGo);

                Assert.IsNotNull(EntityRegistry.Player);
                Assert.AreEqual(playerGo, EntityRegistry.Player);
                Assert.IsTrue(EntityRegistry.HasPlayer);
                Assert.IsNotNull(EntityRegistry.PlayerTransform);
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
            }
        }

        [Test]
        public void UnregisterPlayer_ClearsRef()
        {
            var playerGo = new GameObject("Player");
            try
            {
                EntityRegistry.RegisterPlayer(playerGo);
                Assert.IsTrue(EntityRegistry.HasPlayer);

                EntityRegistry.UnregisterPlayer(playerGo);
                Assert.IsFalse(EntityRegistry.HasPlayer);
                Assert.IsNull(EntityRegistry.Player);
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
            }
        }

        [Test]
        public void RegisterMonsters_MaintainsList()
        {
            var m1 = new GameObject("Monster1");
            var m2 = new GameObject("Monster2");
            var m3 = new GameObject("Monster3");
            try
            {
                EntityRegistry.RegisterMonster(m1);
                EntityRegistry.RegisterMonster(m2);
                EntityRegistry.RegisterMonster(m3);

                Assert.AreEqual(3, EntityRegistry.MonsterCount);
                Assert.Contains(m1, (System.Collections.ICollection)EntityRegistry.Monsters);
                Assert.Contains(m2, (System.Collections.ICollection)EntityRegistry.Monsters);
                Assert.Contains(m3, (System.Collections.ICollection)EntityRegistry.Monsters);

                EntityRegistry.UnregisterMonster(m2);
                Assert.AreEqual(2, EntityRegistry.MonsterCount);
            }
            finally
            {
                Object.DestroyImmediate(m1);
                Object.DestroyImmediate(m2);
                Object.DestroyImmediate(m3);
            }
        }

        [Test]
        public void PurgeDestroyed_RemovesNullEntries()
        {
            var m1 = new GameObject("Monster1");
            var m2 = new GameObject("Monster2");
            try
            {
                EntityRegistry.RegisterMonster(m1);
                EntityRegistry.RegisterMonster(m2);

                // DestroyImmediate makes the Unity null sentinel propagate
                // synchronously — the original PlayMode test needed a frame
                // yield for Object.Destroy to drain.
                Object.DestroyImmediate(m1);
                m1 = null;

                EntityRegistry.PurgeDestroyed();
                Assert.AreEqual(1, EntityRegistry.MonsterCount);
            }
            finally
            {
                if (m1 != null) Object.DestroyImmediate(m1);
                Object.DestroyImmediate(m2);
            }
        }

        [Test]
        public void DuplicateRegister_DoesNotAddTwice()
        {
            var m1 = new GameObject("Monster1");
            try
            {
                EntityRegistry.RegisterMonster(m1);
                EntityRegistry.RegisterMonster(m1);

                Assert.AreEqual(1, EntityRegistry.MonsterCount);
            }
            finally
            {
                Object.DestroyImmediate(m1);
            }
        }

        [Test]
        public void Clear_RemovesAll()
        {
            var playerGo = new GameObject("Player");
            var m1 = new GameObject("Monster1");
            var npc1 = new GameObject("NPC1");
            try
            {
                EntityRegistry.RegisterPlayer(playerGo);
                EntityRegistry.RegisterMonster(m1);
                EntityRegistry.RegisterNPC(npc1);

                EntityRegistry.Clear();

                Assert.IsFalse(EntityRegistry.HasPlayer);
                Assert.AreEqual(0, EntityRegistry.MonsterCount);
                Assert.AreEqual(0, EntityRegistry.NPCs.Count);
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(m1);
                Object.DestroyImmediate(npc1);
            }
        }
    }
}
