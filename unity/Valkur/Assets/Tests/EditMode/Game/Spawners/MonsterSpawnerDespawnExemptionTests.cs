using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// <c>MonsterSpawner.ProcessDespawns</c> destroys anything more than <c>despawnRadius</c>
    /// (100 world units by default) from the player, and every spawn path fed that list — F5
    /// drags, the <c>spawn</c> console command, and spawner waves — with no exemption at all.
    /// <c>SpawnerTemplateData.persistent</c> had zero readers, so a vendor respawn template
    /// authored with <c>persistent = true</c> (every shipped one is) was destroyed exactly like
    /// any ordinary hostile the moment the player walked away from it.
    ///
    /// This pins <see cref="MonsterSpawner.IsExemptFromDespawn"/> — the predicate
    /// <c>ProcessDespawns</c> now consults — directly, rather than exercising the full sweep:
    /// <c>Object.Destroy</c>'s behaviour outside Play Mode is not something an EditMode fixture
    /// should depend on for a correctness assertion (see <c>SafeDestroy</c> /
    /// <c>EntitiesRuntimeEditor.ClearSection</c> for why the rest of the codebase routes around
    /// exactly that).
    /// </summary>
    [TestFixture]
    public class MonsterSpawnerDespawnExemptionTests
    {
        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        [Test]
        public void PersistentSpawnMarkerIsExempt()
        {
            var go = new GameObject("PersistentMonster");
            go.AddComponent<PersistentSpawnMarker>();

            Assert.IsTrue(MonsterSpawner.IsExemptFromDespawn(go),
                "A monster spawned from a persistent template (PersistentSpawnMarker) must be " +
                "exempt from the distance despawn sweep — vendors must not evaporate when the " +
                "player walks to the far side of the map.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void EditorPlacedEntityIsExempt()
        {
            var go = new GameObject("F5PlacedMonster");
            go.AddComponent<PersistedEntityInstance>().Initialize(null, "barbol");

            Assert.IsTrue(MonsterSpawner.IsExemptFromDespawn(go),
                "A monster placed by hand through F5 (PersistedEntityInstance) must be exempt " +
                "from the distance despawn sweep — a designer testing a placement should not " +
                "have it vanish while they walk around to look at it from another angle.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void OrdinaryMonsterIsNotExempt()
        {
            var go = new GameObject("OrdinaryMonster");

            Assert.IsFalse(MonsterSpawner.IsExemptFromDespawn(go),
                "A monster with neither marker must still be culled — the exemption must not " +
                "leak to every spawn path, only the two that ask for it.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NullIsNotExempt()
        {
            Assert.IsFalse(MonsterSpawner.IsExemptFromDespawn(null),
                "A destroyed/null reference must resolve false, not throw — ProcessDespawns " +
                "already handles the null case before consulting this predicate, but the " +
                "predicate itself must be safe to call directly too.");
        }

        [Test]
        public void SpawnEntityAttachesTheMarkerOnlyWhenPersistentIsRequested()
        {
            // SpriteRenderer + Health pre-attached, matching the minimal-but-safe prefab shape
            // CombatFeedbackHitFlashTests.ConfigureMonster_AttachesTheComponentThatFlashes
            // already established for driving EntitySetup.ConfigureMonster from an EditMode
            // test without depending on the rest of the monster-authoring pipeline.
            var prefab = new GameObject("MonsterPrefab");
            prefab.AddComponent<SpriteRenderer>();
            prefab.AddComponent<Health>();

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = "test_marker_dummy";
            def.displayName = "Test Marker Dummy";

            var spawnerGo = new GameObject("[MonsterSpawner]");
            var spawner = spawnerGo.AddComponent<MonsterSpawner>();
            spawner.Initialize(prefab);

            GameObject persistentSpawn = null;
            GameObject ordinarySpawn = null;
            try
            {
                persistentSpawn = spawner.SpawnEntity(def, Vector2.zero, persistent: true);
                ordinarySpawn = spawner.SpawnEntity(def, Vector2.one, persistent: false);

                Assert.IsNotNull(persistentSpawn, "SpawnEntity must still return the instantiated GameObject.");
                Assert.IsTrue(MonsterSpawner.IsExemptFromDespawn(persistentSpawn),
                    "persistent: true must attach PersistentSpawnMarker.");

                Assert.IsNotNull(ordinarySpawn);
                Assert.IsFalse(MonsterSpawner.IsExemptFromDespawn(ordinarySpawn),
                    "persistent: false (the default) must NOT attach the marker.");
            }
            finally
            {
                if (persistentSpawn != null) { EntityRegistry.UnregisterMonster(persistentSpawn); Object.DestroyImmediate(persistentSpawn); }
                if (ordinarySpawn != null)   { EntityRegistry.UnregisterMonster(ordinarySpawn);   Object.DestroyImmediate(ordinarySpawn); }
                Object.DestroyImmediate(spawnerGo);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(def);
            }
        }
    }
}
