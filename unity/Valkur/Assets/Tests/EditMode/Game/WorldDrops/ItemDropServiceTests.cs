using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.WorldDrops;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.WorldDrops
{
    /// <summary>
    /// Integration coverage for <see cref="ItemDropService"/>: the orchestrator
    /// that bridges the in-memory drop cache, the file/in-memory repository,
    /// and the live <see cref="WorldPickup"/> GameObjects in the scene.
    ///
    /// These tests run against an <see cref="InMemoryItemDropRepository"/>
    /// so disk I/O is never touched.
    /// </summary>
    [TestFixture]
    public class ItemDropServiceTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _runtimeAssets = new List<Object>();

        private ItemCatalog _catalog;
        private InMemoryItemDropRepository _repo;
        private ItemDropService _service;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            _runtimeAssets.Add(_catalog);
            _repo = new InMemoryItemDropRepository();
            _service = new ItemDropService(_repo, _catalog, WorldId.Base);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _runtimeAssets) if (a != null) Object.DestroyImmediate(a);
            _runtimeAssets.Clear();
        }

        // ── Fixture helpers ────────────────────────────────────────────────────

        private ItemDefinition AddItemToCatalog(string id, string displayName, float despawnTime = 0f)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = displayName;
            def.despawnTime = despawnTime;
            // Real sprite so ComputeWorldScale doesn't trip over a null bounds.
            def.icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _runtimeAssets.Add(def.icon);
            _runtimeAssets.Add(def);
            _catalog.Upsert(def);
            return def;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void SpawnPersistent_AddsToCacheAndPersistsThroughRepo()
        {
            var def = AddItemToCatalog("sword", "Iron Sword");
            var instance = _service.SpawnPersistent(def, 1, new Vector3(2f, 3f, 0f),
                despawnTtlSeconds: 0f, zoneId: "lobby", source: ItemDropSource.Editor);

            Assert.IsNotNull(instance);
            Assert.AreEqual(1, _service.Count);
            Assert.AreEqual("sword", instance.itemId);
            Assert.IsTrue(instance.IsInfinite);
            Assert.IsNotNull(_service.GetLivePickup(instance.dropId));
            // Track for cleanup
            _scene.Add(_service.GetLivePickup(instance.dropId).gameObject);

            // Repo round-trip: re-load into a fresh service must surface the same drop.
            string json = _repo.ReadRawJson(WorldId.Base);
            Assert.IsFalse(string.IsNullOrEmpty(json));
            StringAssert.Contains("sword", json);
            StringAssert.Contains(instance.dropId, json);
        }

        [Test]
        public void SpawnPersistent_NullCatalogEntryDoesNotSpawnPickup()
        {
            // ItemId not in catalog → service refuses gracefully, no GameObject leaks.
            var orphan = ScriptableObject.CreateInstance<ItemDefinition>();
            orphan.itemId = "ghost";
            orphan.displayName = "Ghost";
            _runtimeAssets.Add(orphan);

            var instance = _service.SpawnPersistent(orphan, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            // The instance still gets cached (caller may add it to the catalog later)
            Assert.IsNotNull(instance);
            Assert.IsNull(_service.GetLivePickup(instance.dropId),
                "No pickup should be spawned when the itemId can't be resolved.");
        }

        [Test]
        public void RemoveByDropId_DropsFromCacheAndKillsLivePickup()
        {
            var def = AddItemToCatalog("potion", "Healing Potion");
            var inst = _service.SpawnPersistent(def, 2, Vector3.zero, 0f, "", ItemDropSource.Editor);
            var live = _service.GetLivePickup(inst.dropId);
            Assert.IsNotNull(live);
            _scene.Add(live.gameObject);

            bool removed = _service.RemoveByDropId(inst.dropId);
            Assert.IsTrue(removed);
            Assert.AreEqual(0, _service.Count);
            Assert.IsNull(_service.Get(inst.dropId));
            Assert.IsTrue(live == null,
                "RemoveByDropId must destroy the live pickup so the world reflects the cache.");

            // Re-loading from the repo must show the entry is gone.
            var freshService = new ItemDropService(_repo, _catalog, WorldId.Base);
            try
            {
                int loaded = freshService.LoadFromRepository();
                Assert.AreEqual(0, loaded);
            }
            finally { freshService.Dispose(); }
        }

        [Test]
        public void UpdateQuantity_PersistsNewValue()
        {
            var def = AddItemToCatalog("gold", "Gold");
            var inst = _service.SpawnPersistent(def, 5, Vector3.zero, 0f, "", ItemDropSource.Editor);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);

            Assert.IsTrue(_service.UpdateQuantity(inst.dropId, 17));
            Assert.AreEqual(17, _service.Get(inst.dropId).quantity);

            // Read repo and validate via the JSON wrapper.
            var fresh = new ItemDropService(_repo, _catalog, WorldId.Base);
            try
            {
                fresh.LoadFromRepository();
                Assert.AreEqual(17, fresh.Get(inst.dropId).quantity);
            }
            finally { fresh.Dispose(); }
        }

        [Test]
        public void UpdateQuantity_UnknownDropId_ReturnsFalse()
        {
            Assert.IsFalse(_service.UpdateQuantity("nope", 1));
        }

        [Test]
        public void Rehydrate_IsIdempotent()
        {
            var def = AddItemToCatalog("torch", "Torch");
            var inst = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);

            // Calling Rehydrate again must not double-spawn.
            int spawnedAgain = _service.Rehydrate();
            Assert.AreEqual(0, spawnedAgain);
            Assert.IsNotNull(_service.GetLivePickup(inst.dropId));
        }

        [Test]
        public void LoadFromRepository_IgnoresEntriesMissingDropIdOrItemId()
        {
            // Hand-craft a JSON payload with two valid + two invalid records.
            var raw = "{\"schemaVersion\":1,\"drops\":[" +
                "{\"dropId\":\"x\",\"itemId\":\"y\",\"quantity\":1}," +
                "{\"dropId\":\"\",\"itemId\":\"y\",\"quantity\":1}," +
                "{\"dropId\":\"x2\",\"itemId\":\"\",\"quantity\":1}," +
                "{\"dropId\":\"x3\",\"itemId\":\"y\",\"quantity\":1}" +
                "]}";
            _repo.WriteRawJson(WorldId.Base, raw);
            int loaded = _service.LoadFromRepository();
            Assert.AreEqual(2, loaded, "Records lacking either dropId or itemId must be skipped.");
        }

        [Test]
        public void RestorePersistent_PreservesDropId()
        {
            var def = AddItemToCatalog("scroll", "Scroll");
            var snapshot = new ItemDropInstance(
                "fixed-id-42", "scroll", 1, new Vector2(7f, 5f),
                "lab", 0, 1L, 0f, ItemDropSource.Editor);

            _service.RestorePersistent(snapshot);
            Assert.IsNotNull(_service.Get("fixed-id-42"));
            Assert.IsNotNull(_service.GetLivePickup("fixed-id-42"));
            _scene.Add(_service.GetLivePickup("fixed-id-42").gameObject);
        }

        [Test]
        public void Dispose_UnsubscribesFromOnDestroyed()
        {
            // After Dispose, destroying a pickup must NOT call back into the
            // (already torn-down) service. We assert this indirectly by spawning,
            // disposing, then destroying — the test simply needs to NOT crash.
            var def = AddItemToCatalog("ring", "Ring");
            var inst = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            var live = _service.GetLivePickup(inst.dropId);
            Assert.IsNotNull(live);
            _scene.Add(live.gameObject);

            _service.Dispose();
            _service = null;

            Assert.DoesNotThrow(() => Object.DestroyImmediate(live.gameObject));
        }

        [Test]
        public void FlushOnEveryChange_OffSkipsRepoUntilFlushCalled()
        {
            _service.FlushOnEveryChange = false;

            var def = AddItemToCatalog("gem", "Gem");
            var inst = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);

            // Repo still empty until we flush manually.
            Assert.IsTrue(string.IsNullOrEmpty(_repo.ReadRawJson(WorldId.Base)));
            _service.Flush();
            Assert.IsFalse(string.IsNullOrEmpty(_repo.ReadRawJson(WorldId.Base)));
        }
    }
}
