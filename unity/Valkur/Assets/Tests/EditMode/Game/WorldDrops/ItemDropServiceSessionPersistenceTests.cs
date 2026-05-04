using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.WorldDrops;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.WorldDrops
{
    /// <summary>
    /// Reproduces the "items don't persist across game open / close" bug.
    ///
    /// Two runtime conditions matter:
    ///  1. Domain Reload OFF (Valkur default) keeps every static field alive
    ///     across Play stop/start. A stale <see cref="ItemDropService"/> in
    ///     <see cref="ServiceLocator"/> would short-circuit the rehydrate path.
    ///  2. A fresh process restart (build) loses every static, so the new
    ///     service must read from disk via the same repo path.
    ///
    /// Both paths are verified here with an in-memory repo standing in for the
    /// disk file.
    /// </summary>
    [TestFixture]
    public class ItemDropServiceSessionPersistenceTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _runtimeAssets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Unregister<ItemDropService>();
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _runtimeAssets) if (a != null) Object.DestroyImmediate(a);
            _runtimeAssets.Clear();
        }

        private (ItemCatalog catalog, ItemDefinition def) BuildCatalog(string itemId)
        {
            var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            _runtimeAssets.Add(catalog);
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = itemId;
            def.displayName = itemId;
            def.icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _runtimeAssets.Add(def.icon);
            _runtimeAssets.Add(def);
            catalog.Upsert(def);
            return (catalog, def);
        }

        // ── Bug reproductions ─────────────────────────────────────────────────

        [Test]
        public void DropPlacedInPreviousSession_RehydratesOnNextBoot()
        {
            LogAssert.ignoreFailingMessages = true;
            var (catalog, def) = BuildCatalog("torch");
            var repo = new InMemoryItemDropRepository();

            // ── "Session 1": player places a torch via the editor ─────────────
            var sessionA = new ItemDropService(repo, catalog, WorldId.Base);
            try
            {
                var inst = sessionA.SpawnPersistent(def, 1, new Vector3(2f, 3f, 0f),
                    despawnTtlSeconds: 0f, zoneId: "lobby", source: ItemDropSource.Editor);
                _scene.Add(sessionA.GetLivePickup(inst.dropId).gameObject);
                Assert.AreEqual(1, sessionA.Count);
            }
            finally { sessionA.Dispose(); }
            // Scene unloads; pickup destroyed via DestroyImmediate so the cache is empty.
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();

            // ── "Session 2": fresh service, same repo ────────────────────────
            var sessionB = new ItemDropService(repo, catalog, WorldId.Base);
            try
            {
                int loaded = sessionB.LoadFromRepository();
                int spawned = sessionB.Rehydrate();
                Assert.AreEqual(1, loaded, "Disk record placed in session 1 must be re-read.");
                Assert.AreEqual(1, spawned, "A fresh WorldPickup must spawn for the persisted drop.");

                // Track the rehydrated pickup so TearDown cleans it.
                foreach (var inst in sessionB.All)
                {
                    var live = sessionB.GetLivePickup(inst.dropId);
                    Assert.IsNotNull(live);
                    Assert.AreEqual("torch", live.Item.itemId);
                    Assert.AreEqual(new Vector3(2f, 3f, 0f), live.transform.position);
                    _scene.Add(live.gameObject);
                }
            }
            finally { sessionB.Dispose(); }
        }

        [Test]
        public void ResetForPlayMode_DropsStaleServiceFromLocator()
        {
            LogAssert.ignoreFailingMessages = true;
            var (catalog, _) = BuildCatalog("any");
            var repo = new InMemoryItemDropRepository();

            // Pretend Play 1 left a service registered.
            var stale = new ItemDropService(repo, catalog, WorldId.Base);
            ServiceLocator.Register(stale);
            Assert.IsTrue(ServiceLocator.TryGet<ItemDropService>(out _));

            // Drive the reset hook directly. Under [RuntimeInitializeOnLoadMethod]
            // Unity invokes this on every Play; tests just call the method by name.
            var hook = typeof(ItemDropService).GetMethod("ResetForPlayMode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(hook, "ResetForPlayMode static reset hook must exist on ItemDropService.");
            hook.Invoke(null, null);

            Assert.IsFalse(ServiceLocator.TryGet<ItemDropService>(out _),
                "Stale service must be dropped so GameplaySceneSetup rebuilds it from disk.");
        }

        [Test]
        public void Rehydrate_PreservesDropIdAcrossSessions()
        {
            // The dropId is the cross-session correlation key. If it changes
            // on rehydrate, undo / redo / vendor stock counters would lose
            // their reference between Plays.
            LogAssert.ignoreFailingMessages = true;
            var (catalog, def) = BuildCatalog("rune");
            var repo = new InMemoryItemDropRepository();

            string firstSessionDropId;
            var sessionA = new ItemDropService(repo, catalog, WorldId.Base);
            try
            {
                var inst = sessionA.SpawnPersistent(def, 1, Vector3.zero, 0f, "", ItemDropSource.Editor);
                _scene.Add(sessionA.GetLivePickup(inst.dropId).gameObject);
                firstSessionDropId = inst.dropId;
            }
            finally { sessionA.Dispose(); }
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();

            var sessionB = new ItemDropService(repo, catalog, WorldId.Base);
            try
            {
                sessionB.LoadFromRepository();
                sessionB.Rehydrate();

                Assert.IsNotNull(sessionB.Get(firstSessionDropId),
                    "The same dropId must be visible after reboot.");
                var live = sessionB.GetLivePickup(firstSessionDropId);
                Assert.IsNotNull(live);
                Assert.AreEqual(firstSessionDropId, live.DropId);
                _scene.Add(live.gameObject);
            }
            finally { sessionB.Dispose(); }
        }
    }
}
