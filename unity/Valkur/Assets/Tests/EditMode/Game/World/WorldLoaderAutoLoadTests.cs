using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Guards the double-world-load fix.
    ///
    /// <see cref="WorldLoader"/> and <see cref="ZoneDatabaseLoader"/> both load
    /// themselves from Start(). GameplaySceneSetup also builds them with AddComponent
    /// and then loads explicitly — so Start() fired a frame later and did the whole
    /// thing again: roughly 248,000 redundant SetTile calls, 6.9 MB of re-parsed JSON,
    /// a second 31 MB Resources.LoadAll of the tile sprites and a second collision
    /// bake, on every single Play.
    ///
    /// The contract these tests pin down is narrow and load-bearing:
    ///   • the self-load stays ON by default, so a loader dropped into a scene by hand
    ///     still works with no code;
    ///   • SetAutoLoad(false) makes Start() a no-op, so a caller can own the timing.
    ///
    /// Start() is invoked directly here rather than waited for: Unity does not run it
    /// on AddComponent in edit mode, and these tests must never let the real load run
    /// (it would paint tilemaps and read StreamingAssets from a test).
    /// </summary>
    [TestFixture]
    public class WorldLoaderAutoLoadTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private T CreateDetached<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<T>();
        }

        private static bool ReadAutoLoad(object target)
        {
            var f = target.GetType().GetField("_autoLoad",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "_autoLoad field not found — renamed? SetAutoLoad must follow it.");
            return (bool)f.GetValue(target);
        }

        private static void InvokeStart(object target)
        {
            var m = target.GetType().GetMethod("Start",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(m, "Start() not found — the auto-load guard lives there.");
            m.Invoke(target, null);
        }

        // ── WorldLoader ──────────────────────────────────────────────────────────

        [Test]
        public void WorldLoader_AutoLoad_IsOnByDefault()
        {
            var loader = CreateDetached<WorldLoader>("AutoLoadDefault_World");

            Assert.IsTrue(ReadAutoLoad(loader),
                "A WorldLoader placed in a scene by hand must still load itself. Only the " +
                "bootstrap, which loads explicitly, is allowed to turn this off.");
        }

        [Test]
        public void WorldLoader_SetAutoLoadFalse_MakesStartANoOp()
        {
            var loader = CreateDetached<WorldLoader>("AutoLoadOff_World");
            loader.SetAutoLoad(false);

            InvokeStart(loader);

            Assert.AreEqual(0, loader.OverlaysLoaded,
                "Start() must not load once the caller has taken ownership — this is the " +
                "second, redundant world build the fix removes.");
            Assert.AreEqual(0, loader.CollisionsLoaded);
        }

        [Test]
        public void WorldLoader_SetAutoLoad_RoundTrips()
        {
            var loader = CreateDetached<WorldLoader>("AutoLoadRoundTrip_World");

            loader.SetAutoLoad(false);
            Assert.IsFalse(ReadAutoLoad(loader));

            loader.SetAutoLoad(true);
            Assert.IsTrue(ReadAutoLoad(loader), "The setter must not be one-way.");
        }

        // ── ZoneDatabaseLoader ───────────────────────────────────────────────────

        [Test]
        public void ZoneDatabaseLoader_AutoLoad_IsOnByDefault()
        {
            var loader = CreateDetached<ZoneDatabaseLoader>("AutoLoadDefault_Zones");

            Assert.IsTrue(ReadAutoLoad(loader));
        }

        [Test]
        public void ZoneDatabaseLoader_SetAutoLoadFalse_MakesStartANoOp()
        {
            var loader = CreateDetached<ZoneDatabaseLoader>("AutoLoadOff_Zones");
            loader.SetAutoLoad(false);

            Assert.DoesNotThrow(() => InvokeStart(loader),
                "With auto-load off, Start() must return before it needs a ZoneManager — " +
                "which is exactly why it is safe to call from a test at all.");

            Assert.AreEqual(0, loader.WorldOriginX,
                "Nothing may have been read from disk.");
            Assert.AreEqual(0, loader.WorldOriginY);
        }

        [Test]
        public void ZoneDatabaseLoader_SetAutoLoad_RoundTrips()
        {
            var loader = CreateDetached<ZoneDatabaseLoader>("AutoLoadRoundTrip_Zones");

            loader.SetAutoLoad(false);
            Assert.IsFalse(ReadAutoLoad(loader));

            loader.SetAutoLoad(true);
            Assert.IsTrue(ReadAutoLoad(loader));
        }

        // ── The bootstrap must actually use it ───────────────────────────────────

        [Test]
        public void BothLoaders_ExposeSetAutoLoad_SoTheBootstrapCanDisableIt()
        {
            // Cheap contract check: if either setter is renamed or removed, the
            // bootstrap's call site breaks at compile time — but a signature change
            // (say, to a property) would compile there and silently reintroduce the
            // double load elsewhere.
            foreach (var t in new[] { typeof(WorldLoader), typeof(ZoneDatabaseLoader) })
            {
                var m = t.GetMethod("SetAutoLoad", new[] { typeof(bool) });
                Assert.IsNotNull(m, $"{t.Name}.SetAutoLoad(bool) is the seam the bootstrap depends on.");
                Assert.IsTrue(m.IsPublic);
            }
        }
    }
}
