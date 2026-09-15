using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spawners;
namespace Valkur.Tests.EditMode.Gameplay.Spawners
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

        /// <summary>
        /// Author the spawn area and hand it to the placement.
        ///
        /// <para>These tests used to mutate <c>_template</c> AFTER <c>Initialize</c> and
        /// expect the live spawner to follow — which is exactly the coupling copy-on-place
        /// removed. A placement snapshots its preset when it is made and reads its own
        /// <see cref="SpawnerInstanceConfig"/> from then on, so the preset edit has to be
        /// re-applied to reach it. Three of these went red on that and were right to: the
        /// measured values were the CONFIG defaults, not the values the test had set.</para>
        /// </summary>
        private void Arm(int spawnRadius, SpawnerShape shape)
        {
            _template.spawnRadius  = spawnRadius;
            _template.spawnerShape = shape;
            _instance.ApplyConfig(SpawnerInstanceConfig.SnapshotOf(_template));
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
            Arm(0, SpawnerShape.Square);
            var result = Clamp(new Vector2(500f, 500f));
            Assert.AreEqual(new Vector2(500f, 500f), result,
                "spawnRadius <= 0 must mean unbounded — reproduces the exact pre-fix " +
                "behaviour so every shipped template (all authored with spreadRadius well " +
                "under spawnRadius) is unaffected.");
        }

        [Test]
        public void CircleShapeClampsMagnitudeToRadius()
        {
            Arm(10, SpawnerShape.Circle);
            var result = Clamp(new Vector2(20f, 0f));
            Assert.AreEqual(10f, result.magnitude, 0.001f,
                "A Circle-shaped area must clamp the offset's magnitude to spawnRadius.");
        }

        [Test]
        public void CircleShapeLeavesInBoundsOffsetsUntouched()
        {
            Arm(10, SpawnerShape.Circle);
            var offset = new Vector2(3f, 4f); // magnitude 5, within 10
            Assert.AreEqual(offset, Clamp(offset));
        }

        [Test]
        public void SquareShapeClampsEachAxisIndependently()
        {
            Arm(10, SpawnerShape.Square);
            var result = Clamp(new Vector2(20f, -20f));
            Assert.AreEqual(new Vector2(10f, -10f), result);
        }
    }
}
