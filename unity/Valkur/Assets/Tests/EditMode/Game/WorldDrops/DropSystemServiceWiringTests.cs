using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.WorldDrops;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.WorldDrops
{
    /// <summary>
    /// Verifies the loot-drop call site (<see cref="DropSystem.SpawnDrop"/>)
    /// auto-routes to the active <see cref="ItemDropService"/> when one is
    /// registered, falling back to the legacy ephemeral path when not.
    /// </summary>
    [TestFixture]
    public class DropSystemServiceWiringTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _runtimeAssets = new List<Object>();

        private ItemCatalog _catalog;
        private InMemoryItemDropRepository _authoring;
        private InMemoryItemDropRepository _run;
        private ItemDropService _service;

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Unregister<ItemDropService>();
            _service?.Dispose();
            _service = null;
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _runtimeAssets) if (a != null) Object.DestroyImmediate(a);
            _runtimeAssets.Clear();
        }

        private ItemDefinition CreateItem(string id, float despawnTime = 0f)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = id;
            def.despawnTime = despawnTime;
            def.icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _runtimeAssets.Add(def.icon);
            _runtimeAssets.Add(def);
            // When a service is registered we must also register the item in
            // its catalog so the rehydrate path can resolve the dropped itemId
            // back to a definition.
            _catalog?.Upsert(def);
            return def;
        }

        private void RegisterService()
        {
            LogAssert.ignoreFailingMessages = true;
            _catalog   = ScriptableObject.CreateInstance<ItemCatalog>();
            _runtimeAssets.Add(_catalog);
            _authoring = new InMemoryItemDropRepository();
            _run       = new InMemoryItemDropRepository();
            _service   = new ItemDropService(_authoring, _run, _catalog, WorldId.Base);
            ServiceLocator.Register(_service);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void SpawnDrop_WithoutService_UsesEphemeralPath()
        {
            LogAssert.ignoreFailingMessages = true;
            // No service registered → DropSystem must fall back to a legacy
            // ephemeral pickup. That pickup has no dropId / no IsPersistent.
            var def = CreateItem("ephemeral");
            var pickup = DropSystem.SpawnDrop(def, 1, Vector3.zero);
            Assert.IsNotNull(pickup);
            _scene.Add(pickup.gameObject);
            Assert.IsFalse(pickup.IsPersistent);
            Assert.IsTrue(string.IsNullOrEmpty(pickup.DropId));
        }

        [Test]
        public void SpawnDrop_WithService_RecordsAsRunLoot()
        {
            RegisterService();
            var def = CreateItem("loot");
            var pickup = DropSystem.SpawnDrop(def, 3, new Vector3(2f, 3f, 0f));
            Assert.IsNotNull(pickup);
            _scene.Add(pickup.gameObject);

            Assert.IsTrue(pickup.IsPersistent);
            Assert.IsFalse(string.IsNullOrEmpty(pickup.DropId));
            Assert.AreEqual(ItemDropSource.Loot, pickup.Source);

            // Routed to the run repo, not authoring.
            string runJson = _run.ReadRawJson(WorldId.Base);
            string authJson = _authoring.ReadRawJson(WorldId.Base);
            StringAssert.Contains("loot", runJson);
            Assert.IsTrue(string.IsNullOrEmpty(authJson) || !authJson.Contains("loot"));
        }

        [Test]
        public void SpawnDrop_TtlInheritedFromDespawnTime()
        {
            RegisterService();
            var def = CreateItem("perishable", despawnTime: 45f);
            var pickup = DropSystem.SpawnDrop(def, 1, Vector3.zero);
            Assert.IsNotNull(pickup);
            _scene.Add(pickup.gameObject);

            Assert.AreEqual(45f, pickup.DespawnTtlSeconds);
            Assert.IsFalse(pickup.IsInfiniteTtl);
        }

        [Test]
        public void SpawnDrop_NullItem_ReturnsNull_NoSideEffects()
        {
            RegisterService();
            var pickup = DropSystem.SpawnDrop(null, 1, Vector3.zero);
            Assert.IsNull(pickup);
            Assert.AreEqual(0, _service.Count);
        }

        [Test]
        public void SpawnDrop_ZeroQuantity_ReturnsNull_NoSideEffects()
        {
            RegisterService();
            var def = CreateItem("zero");
            var pickup = DropSystem.SpawnDrop(def, 0, Vector3.zero);
            Assert.IsNull(pickup);
            Assert.AreEqual(0, _service.Count);
        }
    }
}
