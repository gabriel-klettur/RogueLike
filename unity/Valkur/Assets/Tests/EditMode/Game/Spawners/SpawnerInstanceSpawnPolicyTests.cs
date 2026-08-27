using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// <see cref="SpawnerInstance.ClampToSpawnArea"/> pins <c>spawnRadius</c>/<c>spawnerShape</c>
    /// (drawn as a gizmo and nothing else before this pass). Reflected into directly, the same
    /// technique <c>SpawnerDeleteFromPropertiesTests</c> already uses for
    /// <c>SpawnerEditorManager</c>'s private fields.
    /// </summary>
    [TestFixture]
    public class SpawnerInstanceClampToSpawnAreaTests
    {
        private GameObject _go;
        private SpawnerInstance _instance;
        private SpawnerTemplateData _template;

        [SetUp]
        public void SetUp()
        {
            _template = ScriptableObject.CreateInstance<SpawnerTemplateData>();
            _template.templateId = "test_clamp_template";

            _go = new GameObject("TestSpawner");
            _instance = _go.AddComponent<SpawnerInstance>();
            _instance.Initialize(_template, "test_instance", "Lobby", spawner: null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_template != null) Object.DestroyImmediate(_template);
        }

        private Vector2 Clamp(Vector2 offset)
        {
            var method = typeof(SpawnerInstance).GetMethod("ClampToSpawnArea",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "ClampToSpawnArea not found via reflection — has it been renamed?");
            return (Vector2)method.Invoke(_instance, new object[] { offset });
        }

        [Test]
        public void ZeroSpawnRadiusIsUnbounded()
        {
            _template.spawnRadius = 0;
            var result = Clamp(new Vector2(500f, 500f));
            Assert.AreEqual(new Vector2(500f, 500f), result,
                "spawnRadius <= 0 must mean unbounded — reproduces the exact pre-fix " +
                "behaviour so every shipped template (all authored with spreadRadius well " +
                "under spawnRadius) is unaffected.");
        }

        [Test]
        public void CircleShapeClampsMagnitudeToRadius()
        {
            _template.spawnRadius = 10;
            _template.spawnerShape = SpawnerShape.Circle;
            var result = Clamp(new Vector2(20f, 0f));
            Assert.AreEqual(10f, result.magnitude, 0.001f,
                "A Circle-shaped area must clamp the offset's magnitude to spawnRadius.");
        }

        [Test]
        public void CircleShapeLeavesInBoundsOffsetsUntouched()
        {
            _template.spawnRadius = 10;
            _template.spawnerShape = SpawnerShape.Circle;
            var offset = new Vector2(3f, 4f); // magnitude 5, within 10
            Assert.AreEqual(offset, Clamp(offset));
        }

        [Test]
        public void SquareShapeClampsEachAxisIndependently()
        {
            _template.spawnRadius = 10;
            _template.spawnerShape = SpawnerShape.Square;
            var result = Clamp(new Vector2(20f, -20f));
            Assert.AreEqual(new Vector2(10f, -10f), result);
        }
    }

    /// <summary>
    /// <c>spawnMode</c> (Periodic vs Burst) was authored on every shipped
    /// <see cref="SpawnerTemplateData"/> but never branched on. Pins the fix directly against
    /// <c>SpawnerInstance.UpdateActive</c>, reflected into since it is private and exercising
    /// the branch through <c>MonoBehaviour.Update()</c> would require pumping real frames in an
    /// EditMode fixture.
    /// </summary>
    [TestFixture]
    public class SpawnerInstanceSpawnModeTests
    {
        private const string TestKey = "test_spawn_policy_dummy";

        private GameObject _prefab;
        private MonsterDefinition _def;
        private MonsterCatalog _catalog;
        private GameObject _spawnerGo;
        private MonsterSpawner _spawner;
        private GameObject _instanceGo;
        private SpawnerInstance _instance;
        private SpawnerTemplateData _template;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // SpriteRenderer + Health pre-attached — the minimal prefab shape
            // CombatFeedbackHitFlashTests already proved safe for driving
            // EntitySetup.ConfigureMonster (which MonsterSpawner.SpawnEntity calls)
            // from an EditMode test.
            _prefab = new GameObject("MonsterPrefab");
            _prefab.AddComponent<SpriteRenderer>();
            _prefab.AddComponent<Health>();

            _def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _def.monsterKey = TestKey;
            _def.displayName = "Test Spawn Policy Dummy";

            _catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            _catalog.UpsertDefinition(_def);

            _spawnerGo = new GameObject("[MonsterSpawner]");
            _spawner = _spawnerGo.AddComponent<MonsterSpawner>();
            _spawner.Initialize(_prefab, _catalog);

            _template = ScriptableObject.CreateInstance<SpawnerTemplateData>();
            _template.templateId = "test_spawn_policy_template";
            // Auto + autoStart so Initialize enters Active immediately without needing a
            // live player/proximity check.
            _template.triggerType = TriggerType.Auto;
            _template.autoStart = true;
            _template.cooldownSeconds = 0f;
            _template.waves = new System.Collections.Generic.List<WaveDefinition>
            {
                new WaveDefinition
                {
                    spawns = new System.Collections.Generic.List<WaveSpawnEntry>
                    {
                        new WaveSpawnEntry { entityId = TestKey, count = 1, spreadRadius = 0f },
                        new WaveSpawnEntry { entityId = TestKey, count = 1, spreadRadius = 0f },
                    }
                }
            };

            _instanceGo = new GameObject("TestSpawnerInstance");
            _instance = _instanceGo.AddComponent<SpawnerInstance>();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Every entity a wave spawns is tracked in SpawnerInstance's own private
            // _activeEntities — reflect it out so cleanup is precise (unregister + destroy
            // exactly what THIS fixture created) rather than pattern-matching names against
            // whatever else EntityRegistry happens to hold in the same domain-reload session.
            var activeField = typeof(SpawnerInstance).GetField("_activeEntities",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (activeField?.GetValue(_instance) is System.Collections.Generic.List<GameObject> active)
            {
                foreach (var m in active)
                {
                    if (m == null) continue;
                    EntityRegistry.UnregisterMonster(m);
                    Object.DestroyImmediate(m);
                }
            }

            if (_instanceGo != null) Object.DestroyImmediate(_instanceGo);
            if (_spawnerGo != null) Object.DestroyImmediate(_spawnerGo);
            if (_prefab != null) Object.DestroyImmediate(_prefab);
            if (_def != null) Object.DestroyImmediate(_def);
            if (_catalog != null) Object.DestroyImmediate(_catalog);
            if (_template != null) Object.DestroyImmediate(_template);
        }

        private void InvokeUpdateActive()
        {
            var method = typeof(SpawnerInstance).GetMethod("UpdateActive",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "UpdateActive not found via reflection — has it been renamed?");
            method.Invoke(_instance, null);
        }

        [Test]
        public void PeriodicSpawnsOneWaveEntryPerTick()
        {
            _template.spawnMode = SpawnMode.Periodic;
            _instance.Initialize(_template, "periodic_instance", "Lobby", _spawner);

            InvokeUpdateActive();
            Assert.AreEqual(1, _instance.ActiveEntityCount,
                "Periodic must spawn exactly one wave entry on the first tick, not the " +
                "whole two-entry wave at once.");

            InvokeUpdateActive();
            Assert.AreEqual(2, _instance.ActiveEntityCount,
                "The second tick must spawn the remaining entry.");
        }

        [Test]
        public void BurstSpawnsTheWholeWaveInOneTick()
        {
            _template.spawnMode = SpawnMode.Burst;
            _instance.Initialize(_template, "burst_instance", "Lobby", _spawner);

            InvokeUpdateActive();
            Assert.AreEqual(2, _instance.ActiveEntityCount,
                "Burst must spawn every entry in the wave on a single tick — this is the " +
                "behaviour every shipped template relied on before spawnMode was wired, " +
                "and it must still be reachable from data.");
        }
    }
}
