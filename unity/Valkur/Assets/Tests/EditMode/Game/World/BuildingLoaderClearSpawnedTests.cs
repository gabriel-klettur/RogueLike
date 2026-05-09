using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the Map-Editor multi-map regression where buildings placed in one
    /// slot leaked into every other slot.
    ///
    /// Root cause: <see cref="BuildingsRuntimeEditor"/>'s placement / fill /
    /// erase-undo paths instantiate <see cref="BuildingObject"/> directly under
    /// the scene-level "BuildingsRoot" parent without going through
    /// <see cref="BuildingLoader"/>, so they never landed in
    /// <c>_spawnedBuildings</c>. A subsequent map-slot switch called
    /// <see cref="BuildingLoader.ClearSpawned"/> which only iterated that list
    /// and left the orphans alive — and the next save (which serialises via
    /// <c>FindObjectsOfType&lt;BuildingObject&gt;()</c>) wrote them into the
    /// new slot's JSON.
    ///
    /// Two contracts pinned here:
    ///   1. <see cref="BuildingLoader.ClearSpawned"/> destroys orphan
    ///      <see cref="BuildingObject"/> instances parented under
    ///      <c>_buildingsRoot</c>, even when they were never registered.
    ///   2. <see cref="BuildingLoader.RegisterPlacedBuilding"/> adds an
    ///      externally-created instance into the tracked list so
    ///      <see cref="BuildingLoader.SpawnedBuildings"/> reflects reality and
    ///      consumers (ResurrectionZoneAutoBinder, GameplaySceneSetup) see it.
    /// </summary>
    [TestFixture]
    public class BuildingLoaderClearSpawnedTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scene.Count; i++)
                if (_scene[i] != null) Object.DestroyImmediate(_scene[i]);
            _scene.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Factories ────────────────────────────────────────────────────────

        private BuildingLoader CreateLoaderWithRoot(out Transform root)
        {
            var loaderGo = new GameObject("BuildingLoader");
            _scene.Add(loaderGo);
            var loader = loaderGo.AddComponent<BuildingLoader>();

            // Disable auto-load — we don't want Start() to read the live
            // StreamingAssets file during the test.
            SetPrivateField(loader, "_autoLoad", false);

            var rootGo = new GameObject("BuildingsRoot");
            _scene.Add(rootGo);
            root = rootGo.transform;
            SetPrivateField(loader, "_buildingsRoot", root);

            return loader;
        }

        private BuildingObject CreateOrphanBuilding(Transform parent, int instanceId)
        {
            var go = new GameObject($"Building_{instanceId}_test");
            go.transform.SetParent(parent, worldPositionStays: false);
            _scene.Add(go);
            var b = go.AddComponent<BuildingObject>();
            b.InstanceId = instanceId;
            return b;
        }

        // ── Tests ───────────────────────────────────────────────────────────

        [Test]
        public void ClearSpawned_DestroysOrphansUnderBuildingsRoot()
        {
            var loader = CreateLoaderWithRoot(out var root);
            var orphan = CreateOrphanBuilding(root, instanceId: 99);
            Assert.IsNotNull(orphan, "Pre-condition: orphan exists.");
            Assert.AreEqual(0, loader.SpawnedBuildings.Count,
                "Pre-condition: orphan was never registered.");

            loader.ClearSpawned();

            // DestroyImmediate would have nulled the reference synchronously;
            // Destroy() defers, so the BuildingObject still answers != null
            // until the deferred frame. The robust check is asking the parent
            // for live BuildingObjects.
            var remaining = root.GetComponentsInChildren<BuildingObject>(true);
            int alive = 0;
            for (int i = 0; i < remaining.Length; i++)
                if (remaining[i] != null) alive++;
            Assert.AreEqual(0, alive,
                "ClearSpawned must destroy orphan BuildingObjects under _buildingsRoot " +
                "— without this, slot switches leak buildings between maps.");
        }

        [Test]
        public void ClearSpawned_LeavesUnrelatedSiblingsAlone()
        {
            // Defensive guarantee: the orphan sweep is scoped to _buildingsRoot.
            // Nothing else in the scene should be touched.
            var loader = CreateLoaderWithRoot(out var root);

            var unrelated = new GameObject("UnrelatedSibling");
            _scene.Add(unrelated);

            loader.ClearSpawned();

            Assert.IsNotNull(unrelated,
                "ClearSpawned must not touch GameObjects outside _buildingsRoot.");
        }

        [Test]
        public void RegisterPlacedBuilding_AddsToSpawnedBuildingsList()
        {
            var loader = CreateLoaderWithRoot(out var root);
            var b = CreateOrphanBuilding(root, instanceId: 1);

            loader.RegisterPlacedBuilding(b);

            Assert.AreEqual(1, loader.SpawnedBuildings.Count,
                "Register must add the building to the tracked list.");
            Assert.AreSame(b, loader.SpawnedBuildings[0],
                "Tracked list must contain the registered instance.");
        }

        [Test]
        public void RegisterPlacedBuilding_IsIdempotent()
        {
            var loader = CreateLoaderWithRoot(out var root);
            var b = CreateOrphanBuilding(root, instanceId: 1);

            loader.RegisterPlacedBuilding(b);
            loader.RegisterPlacedBuilding(b);

            Assert.AreEqual(1, loader.SpawnedBuildings.Count,
                "Register must not duplicate a building that's already tracked.");
        }

        [Test]
        public void RegisterPlacedBuilding_NullArgument_IsNoOp()
        {
            var loader = CreateLoaderWithRoot(out _);

            loader.RegisterPlacedBuilding(null);

            Assert.AreEqual(0, loader.SpawnedBuildings.Count,
                "Register must silently ignore null so callers don't have to null-guard.");
        }

        // ── Reflection helper ────────────────────────────────────────────────

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var f = target.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            f.SetValue(target, value);
        }
    }
}
