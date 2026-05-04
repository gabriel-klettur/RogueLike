using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.WorldDrops;
using Valkur.Gameplay.Items;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.Items
{
    /// <summary>
    /// End-to-end coverage of the F7 Items Editor + persistence service stack:
    ///
    ///   • SpawnAt with a service registered records the drop on disk (repo).
    ///   • DeletePickup on a persistent drop removes the matching record.
    ///   • Quantity ± mirrors the new value through to the repo.
    ///   • Without a service registered the editor still works (ephemeral path).
    ///
    /// Uses the same reflection helpers as the existing Items tests so we stay
    /// consistent with their conventions.
    /// </summary>
    [TestFixture]
    public class ItemsRuntimeEditorPersistenceTests
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
            ServiceLocator.Register(_service);
        }

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

            // Singleton reset so repeat test runs don't see stale instances.
            ClearSingletonInstance<ItemsRuntimeEditor>();
            // Reset static event so prior subscribers can't leak forward.
            var evField = typeof(WorldPickup).GetField("OnDestroyed",
                BindingFlags.Static | BindingFlags.NonPublic);
            evField?.SetValue(null, null);
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        private static FieldInfo Field(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetField(object obj, string name, object value)
            => Field(obj, name)?.SetValue(obj, value);

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, args); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}");
        }

        private ItemDefinition AddItemToCatalog(string id, string displayName, float despawnTime = 0f)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = displayName;
            def.despawnTime = despawnTime;
            def.icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _runtimeAssets.Add(def.icon);
            _runtimeAssets.Add(def);
            _catalog.Upsert(def);
            return def;
        }

        private ItemsRuntimeEditor CreateActiveEditor()
        {
            ClearSingletonInstance<ItemsRuntimeEditor>();
            var go = new GameObject("TestItemsEditor");
            _scene.Add(go);
            var ed = go.AddComponent<ItemsRuntimeEditor>();
            Invoke(ed, "OnSingletonAwake");
            Invoke(ed, "Start");
            ed.Activate();
            return ed;
        }

        private void InjectCatalog(ItemsRuntimeEditor ed, params ItemDefinition[] items)
        {
            SetField(ed, "_allItems", items);
            Invoke(ed, "ApplyFilter");
            Invoke(ed, "RefreshPicker");
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void SpawnAt_WithServiceRegistered_RecordsDropInRepo()
        {
            var ed = CreateActiveEditor();
            var sword = AddItemToCatalog("sword", "Iron Sword", despawnTime: 0f);
            InjectCatalog(ed, sword);
            Invoke(ed, "SelectItem", "sword");

            Invoke(ed, "SpawnAt", new Vector3(5f, 7f, 0f));

            Assert.AreEqual(1, _service.Count, "Service must hold the drop after SpawnAt.");
            string raw = _repo.ReadRawJson(WorldId.Base);
            StringAssert.Contains("sword", raw);
            // JsonUtility pretty-print emits "quantity": 1 (with the space). Match either form.
            string compact = raw.Replace(" ", "");
            StringAssert.Contains("\"quantity\":1", compact);
        }

        [Test]
        public void SpawnAt_TtlInherited_FromItemDefinitionDespawnTime()
        {
            var ed = CreateActiveEditor();
            var potion = AddItemToCatalog("potion", "Healing Potion", despawnTime: 30f);
            InjectCatalog(ed, potion);
            Invoke(ed, "SelectItem", "potion");

            Invoke(ed, "SpawnAt", new Vector3(0f, 0f, 0f));

            ItemDropInstance only = null;
            foreach (var d in _service.All) { only = d; break; }
            Assert.IsNotNull(only);
            Assert.AreEqual(30f, only.despawnTtlSeconds,
                "Editor F7 must read the catalog's despawnTime as the default TTL.");
            Assert.IsFalse(only.IsInfinite);
        }

        [Test]
        public void SpawnAt_ItemWithZeroDespawnTime_IsInfinite()
        {
            var ed = CreateActiveEditor();
            var torch = AddItemToCatalog("torch", "Torch", despawnTime: 0f);
            InjectCatalog(ed, torch);
            Invoke(ed, "SelectItem", "torch");

            Invoke(ed, "SpawnAt", new Vector3(2f, 2f, 0f));

            ItemDropInstance only = null;
            foreach (var d in _service.All) { only = d; break; }
            Assert.IsNotNull(only);
            Assert.IsTrue(only.IsInfinite, "despawnTime=0 must produce an infinite drop.");
        }

        [Test]
        public void DeletePickup_OnPersistentDrop_RemovesFromRepo()
        {
            var ed = CreateActiveEditor();
            var coin = AddItemToCatalog("gold", "Gold");
            InjectCatalog(ed, coin);
            Invoke(ed, "SelectItem", "gold");
            Invoke(ed, "SpawnAt", new Vector3(1f, 1f, 0f));

            // Pull the live pickup the service spawned.
            ItemDropInstance only = null;
            foreach (var d in _service.All) { only = d; break; }
            Assert.IsNotNull(only);
            var live = _service.GetLivePickup(only.dropId);
            Assert.IsNotNull(live);

            Invoke(ed, "DeletePickup", live);

            Assert.AreEqual(0, _service.Count, "Service cache must be empty after DeletePickup.");
            // Re-loading from the repo must agree.
            var fresh = new ItemDropService(_repo, _catalog, WorldId.Base);
            try
            {
                Assert.AreEqual(0, fresh.LoadFromRepository());
            }
            finally { fresh.Dispose(); }
        }

        [Test]
        public void SelectItem_ClearsAnyPreviouslyActiveWorldInstance()
        {
            // Reproduces the bug where the Properties panel kept showing
            // "Instance" actions after the user selected a different item from
            // the catalog grid (because _selectedInstance was never cleared).
            var ed = CreateActiveEditor();
            var torch = AddItemToCatalog("torch", "Torch");
            var coin  = AddItemToCatalog("coin",  "Coin");
            InjectCatalog(ed, torch, coin);
            Invoke(ed, "SelectItem", "torch");
            Invoke(ed, "SpawnAt", new Vector3(1f, 1f, 0f));

            // Grab the spawned pickup and route through SetActiveInstance, the
            // same path a world click would take.
            ItemDropInstance only = null;
            foreach (var d in _service.All) { only = d; break; }
            var live = _service.GetLivePickup(only.dropId);
            ed.SetActiveInstance(live);
            Assert.IsNotNull(Field(ed, "_selectedInstance").GetValue(ed),
                "Sanity: clicking the world drop must select it.");

            // Now select a different catalog entry — the world instance must clear.
            Invoke(ed, "SelectItem", "coin");
            Assert.IsNull(Field(ed, "_selectedInstance").GetValue(ed),
                "Picking from the catalog grid must clear the previously selected world instance.");
        }

        [Test]
        public void Rehydrate_OnFreshSceneLoad_RecreatesPickupsFromRepo()
        {
            // Pre-populate the repo as if a previous session had saved drops.
            var def = AddItemToCatalog("rune", "Rune");
            var snapshot = new ItemDropInstance(
                "rune-saved", "rune", 7, new Vector2(4f, 5f),
                "lab", 0, 1L, 0f, ItemDropSource.Editor);
            // Manually flush a hand-built file to the repo via a temp service.
            var temp = new ItemDropService(_repo, _catalog, WorldId.Base);
            try
            {
                temp.RestorePersistent(snapshot);
                _scene.Add(temp.GetLivePickup("rune-saved").gameObject);
                temp.Flush();
            }
            finally { temp.Dispose(); }

            // New service simulates a fresh scene boot.
            ServiceLocator.Unregister<ItemDropService>();
            var fresh = new ItemDropService(_repo, _catalog, WorldId.Base);
            ServiceLocator.Register(fresh);
            try
            {
                int loaded = fresh.LoadFromRepository();
                int spawned = fresh.Rehydrate();
                Assert.AreEqual(1, loaded);
                Assert.AreEqual(1, spawned);
                var live = fresh.GetLivePickup("rune-saved");
                Assert.IsNotNull(live);
                _scene.Add(live.gameObject);
                Assert.AreEqual(7, live.Quantity);
                Assert.IsTrue(live.IsPersistent);
            }
            finally
            {
                ServiceLocator.Unregister<ItemDropService>();
                fresh.Dispose();
                // Re-register the original test service so [TearDown] can clean it.
                ServiceLocator.Register(_service);
            }
        }
    }
}
