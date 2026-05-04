using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.WorldDrops;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.WorldDrops
{
    /// <summary>
    /// Phase B coverage — verifies that the <see cref="ItemDropService"/>
    /// correctly routes records to the authoring repo (Editor / Quest /
    /// Unknown sources) vs. the run repo (Loot / PlayerDrop sources), and that
    /// loading merges both stores into a single in-memory cache.
    /// </summary>
    [TestFixture]
    public class ItemDropServiceScopeTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _runtimeAssets = new List<Object>();

        private ItemCatalog _catalog;
        private InMemoryItemDropRepository _authoring;
        private InMemoryItemDropRepository _run;
        private ItemDropService _service;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            _runtimeAssets.Add(_catalog);
            _authoring = new InMemoryItemDropRepository();
            _run       = new InMemoryItemDropRepository();
            _service   = new ItemDropService(_authoring, _run, _catalog, WorldId.Base);
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

        private ItemDefinition AddItem(string id)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = id;
            def.icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _runtimeAssets.Add(def.icon);
            _runtimeAssets.Add(def);
            _catalog.Upsert(def);
            return def;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [TestCase(ItemDropSource.Editor,   true)]
        [TestCase(ItemDropSource.Quest,    true)]
        [TestCase(ItemDropSource.Unknown,  true)]
        [TestCase(ItemDropSource.Loot,     false)]
        [TestCase(ItemDropSource.PlayerDrop, false)]
        public void IsAuthoringSource_PartitionsTheEnum(ItemDropSource source, bool expected)
        {
            Assert.AreEqual(expected, ItemDropService.IsAuthoringSource(source));
        }

        [Test]
        public void SpawnPersistent_WritesAuthoringRepoOnly()
        {
            var def = AddItem("torch");
            var inst = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "lobby", ItemDropSource.Editor);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);

            string authoring = _authoring.ReadRawJson(WorldId.Base);
            string run       = _run.ReadRawJson(WorldId.Base);
            StringAssert.Contains("torch", authoring);
            // Run repo should be empty (or hold an empty drops array).
            Assert.IsTrue(string.IsNullOrEmpty(run) || !run.Contains("torch"),
                "Authoring drop must NOT bleed into the run repo.");
        }

        [Test]
        public void SpawnGameplay_WritesRunRepoOnly()
        {
            var def = AddItem("gold");
            var inst = _service.SpawnGameplay(def, 7, Vector3.zero, 30f, "cave", ItemDropSource.Loot);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);

            string run       = _run.ReadRawJson(WorldId.Base);
            string authoring = _authoring.ReadRawJson(WorldId.Base);
            StringAssert.Contains("gold", run);
            Assert.IsTrue(string.IsNullOrEmpty(authoring) || !authoring.Contains("gold"),
                "Run drop must NOT bleed into the authoring repo.");
        }

        [Test]
        public void SpawnGameplay_ForcesNonAuthoringSource()
        {
            // If a caller hands SpawnGameplay an authoring-flavoured source by
            // mistake, the service must coerce it back to Loot so the routing
            // stays consistent.
            var def = AddItem("scroll");
            var inst = _service.SpawnGameplay(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);
            Assert.AreEqual(ItemDropSource.Loot, inst.Source);
        }

        [Test]
        public void SpawnPersistent_ForcesAuthoringSource()
        {
            var def = AddItem("rune");
            var inst = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Loot);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);
            Assert.AreEqual(ItemDropSource.Editor, inst.Source);
        }

        [Test]
        public void LoadFromRepository_MergesBothStores()
        {
            // Pre-populate both repos with hand-crafted JSON.
            _authoring.WriteRawJson(WorldId.Base,
                "{\"schemaVersion\":1,\"drops\":[" +
                "{\"dropId\":\"a1\",\"itemId\":\"x\",\"quantity\":1,\"sourceRaw\":1}" +
                "]}");
            _run.WriteRawJson(WorldId.Base,
                "{\"schemaVersion\":1,\"drops\":[" +
                "{\"dropId\":\"r1\",\"itemId\":\"y\",\"quantity\":2,\"sourceRaw\":2}" +
                "]}");

            int loaded = _service.LoadFromRepository();
            Assert.AreEqual(2, loaded);
            Assert.IsNotNull(_service.Get("a1"));
            Assert.IsNotNull(_service.Get("r1"));
            Assert.AreEqual(ItemDropSource.Editor, _service.Get("a1").Source);
            Assert.AreEqual(ItemDropSource.Loot,   _service.Get("r1").Source);
        }

        [Test]
        public void Flush_SeparatesAuthoringAndRunDropsByFile()
        {
            var def = AddItem("mixed");
            var auth = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            var loot = _service.SpawnGameplay (def, 9, Vector3.one,  0f, "", ItemDropSource.Loot);
            _scene.Add(_service.GetLivePickup(auth.dropId).gameObject);
            _scene.Add(_service.GetLivePickup(loot.dropId).gameObject);

            string authoring = _authoring.ReadRawJson(WorldId.Base);
            string run       = _run.ReadRawJson(WorldId.Base);

            StringAssert.Contains(auth.dropId, authoring);
            StringAssert.DoesNotContain(loot.dropId, authoring);

            StringAssert.Contains(loot.dropId, run);
            StringAssert.DoesNotContain(auth.dropId, run);
        }

        [Test]
        public void ClearRunDropsInMemory_KeepsAuthoringDrops()
        {
            var def = AddItem("survivor");
            var auth = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            var loot = _service.SpawnGameplay (def, 1, Vector3.one,  0f, "", ItemDropSource.Loot);
            _scene.Add(_service.GetLivePickup(auth.dropId).gameObject);
            // run drop will be destroyed by ClearRunDropsInMemory; track was for safety.

            _service.ClearRunDropsInMemory();

            Assert.IsNotNull(_service.Get(auth.dropId));
            Assert.IsNull(_service.Get(loot.dropId),
                "Run drops must be evicted; authoring drops must stay.");
        }

        [Test]
        public void SetRunRepository_RoutesNewGameplayDropsToTheNewFile()
        {
            // Simulate "player started a new run with a different runId".
            var newRun = new InMemoryItemDropRepository();
            _service.SetRunRepository(newRun);

            var def = AddItem("freshrun");
            var inst = _service.SpawnGameplay(def, 1, Vector3.zero, 0f, "", ItemDropSource.Loot);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);

            StringAssert.Contains("freshrun", newRun.ReadRawJson(WorldId.Base));
            string oldRun = _run.ReadRawJson(WorldId.Base);
            Assert.IsTrue(string.IsNullOrEmpty(oldRun) || !oldRun.Contains("freshrun"));
        }

        [Test]
        public void Constructor_RunRepoNullStillAcceptsAuthoringDrops()
        {
            // Defensive: a scene without a save folder yet (e.g. main menu)
            // should still let the F7 editor work against authoring data.
            _service.Dispose();
            _service = new ItemDropService(_authoring, runRepo: null, _catalog, WorldId.Base);

            var def = AddItem("authonly");
            var inst = _service.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
            _scene.Add(_service.GetLivePickup(inst.dropId).gameObject);
            StringAssert.Contains("authonly", _authoring.ReadRawJson(WorldId.Base));
        }
    }
}
