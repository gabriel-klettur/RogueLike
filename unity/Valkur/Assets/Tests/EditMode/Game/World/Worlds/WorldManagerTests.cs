using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Core.WorldContext;
using Valkur.Data;
using Valkur.Gameplay.World.Worlds;

namespace Valkur.Tests.EditMode.Game.World.Worlds
{
    /// <summary>
    /// Pins the Phase 1 multi-world contract. Every assertion here is a guard
    /// for what Phase 4 (MMO server hosting multiple shards in one process)
    /// will rely on later.
    /// </summary>
    [TestFixture]
    public class WorldManagerTests
    {
        // NUnit 3.5 in Unity does not expose Assert.ThrowsAsync. Block-on
        // synchronously instead — the manager's tasks complete inline anyway.
        private static System.Exception RunAndCaptureException(System.Func<Task> action)
        {
            try { action().GetAwaiter().GetResult(); return null; }
            catch (System.Exception ex) { return ex; }
        }

        private static WorldDescriptor MakeDescriptor(string slug)
        {
            var cfg = WorldConfig.CreateLegacyFallback();
            cfg.name = "cfg-" + slug;
            // WorldConfig.DimensionSlug field is private; use the legacy
            // fallback's slug ("base") for the WorldConfig.CreateLegacyFallback
            // case, then patch the descriptor's slug independently.
            var d = ScriptableObject.CreateInstance<WorldDescriptor>();
            d.name = "desc-" + slug;
            // Wire config + slug via reflection (descriptor fields are private).
            typeof(WorldDescriptor)
                .GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(d, cfg);
            typeof(WorldDescriptor)
                .GetField("slug", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(d, slug);
            return d;
        }

        private static void Cleanup(WorldDescriptor d)
        {
            if (d != null && d.Config != null) Object.DestroyImmediate(d.Config);
            if (d != null) Object.DestroyImmediate(d);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void NewManager_HasNoActiveAndNoLoadedWorlds()
        {
            var mgr = new WorldManager();
            Assert.IsNull(mgr.Active);
            Assert.AreEqual(0, mgr.Loaded.Count);
        }

        [Test]
        public void LoadWorldAsync_NullDescriptor_Throws()
        {
            var mgr = new WorldManager();
            var ex  = RunAndCaptureException(() => mgr.LoadWorldAsync(null));
            Assert.IsInstanceOf<System.ArgumentNullException>(ex);
        }

        [Test]
        public void LoadWorldAsync_DescriptorWithoutConfig_Throws()
        {
            var mgr = new WorldManager();
            var d   = ScriptableObject.CreateInstance<WorldDescriptor>(); // no config wired
            try
            {
                var ex = RunAndCaptureException(() => mgr.LoadWorldAsync(d));
                Assert.IsInstanceOf<System.InvalidOperationException>(ex,
                    "A descriptor without a WorldConfig must NOT load — it would " +
                    "later poison the chunk size / seed wiring of every consumer.");
            }
            finally { Object.DestroyImmediate(d); }
        }

        [Test]
        public void LoadWorldAsync_AddsContextToLoadedSet()
        {
            var mgr = new WorldManager();
            var d   = MakeDescriptor("alpha");
            try
            {
                var ctx = mgr.LoadWorldAsync(d).GetAwaiter().GetResult();
                Assert.IsNotNull(ctx);
                Assert.AreEqual(1, mgr.Loaded.Count);
                Assert.AreEqual(d.Id, ctx.WorldId);
            }
            finally { Cleanup(d); }
        }

        [Test]
        public void LoadWorldAsync_Idempotent_ReturnsSameContext()
        {
            var mgr = new WorldManager();
            var d   = MakeDescriptor("alpha");
            try
            {
                var a = mgr.LoadWorldAsync(d).GetAwaiter().GetResult();
                var b = mgr.LoadWorldAsync(d).GetAwaiter().GetResult();
                Assert.AreSame(a, b,
                    "Calling LoadWorldAsync twice for the same descriptor must " +
                    "return the cached context, not allocate a fresh registry " +
                    "(any service registered between the two calls would otherwise vanish).");
                Assert.AreEqual(1, mgr.Loaded.Count);
            }
            finally { Cleanup(d); }
        }

        [Test]
        public void ActivateAsync_NotLoaded_Throws()
        {
            var mgr = new WorldManager();
            var ex  = RunAndCaptureException(() => mgr.ActivateAsync(WorldId.Base));
            Assert.IsInstanceOf<System.InvalidOperationException>(ex);
        }

        [Test]
        public void Activate_FiresActiveWorldChangedWithCorrectArgs()
        {
            var mgr = new WorldManager();
            var d   = MakeDescriptor("alpha");
            try
            {
                IWorldContext seenOld = null, seenNew = null;
                int fires = 0;
                mgr.ActiveWorldChanged += (oldCtx, newCtx) =>
                { seenOld = oldCtx; seenNew = newCtx; fires++; };

                var ctx = mgr.LoadAndActivateAsync(d).GetAwaiter().GetResult();

                Assert.AreEqual(1, fires);
                Assert.IsNull(seenOld, "First activation reports null oldContext.");
                Assert.AreSame(ctx, seenNew);
                Assert.AreSame(ctx, mgr.Active);
            }
            finally { Cleanup(d); }
        }

        [Test]
        public void Activate_SwitchBetweenWorlds_FiresEventOnce()
        {
            var mgr = new WorldManager();
            var a   = MakeDescriptor("alpha");
            var b   = MakeDescriptor("beta");
            try
            {
                int fires = 0;
                mgr.LoadAndActivateAsync(a).GetAwaiter().GetResult();
                mgr.LoadWorldAsync(b).GetAwaiter().GetResult();

                IWorldContext seenOld = null;
                mgr.ActiveWorldChanged += (oldCtx, _) => { seenOld = oldCtx; fires++; };

                mgr.ActivateAsync(b.Id).GetAwaiter().GetResult();

                Assert.AreEqual(1, fires);
                Assert.IsNotNull(seenOld, "Switch from a -> b must report a non-null oldContext.");
                Assert.AreEqual(a.Id, seenOld.WorldId);
                Assert.AreEqual(b.Id, mgr.Active.WorldId);
            }
            finally { Cleanup(a); Cleanup(b); }
        }

        [Test]
        public void Activate_SameWorldTwice_NoExtraEvent()
        {
            var mgr = new WorldManager();
            var d   = MakeDescriptor("alpha");
            try
            {
                mgr.LoadAndActivateAsync(d).GetAwaiter().GetResult();
                int fires = 0;
                mgr.ActiveWorldChanged += (_, __) => fires++;
                mgr.ActivateAsync(d.Id).GetAwaiter().GetResult();
                Assert.AreEqual(0, fires,
                    "Activating the already-active world is a no-op: subscribers " +
                    "should NOT see a redundant event.");
            }
            finally { Cleanup(d); }
        }

        [Test]
        public void UnloadActiveWorld_Refused()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var mgr = new WorldManager();
            var d   = MakeDescriptor("alpha");
            try
            {
                mgr.LoadAndActivateAsync(d).GetAwaiter().GetResult();
                bool unloaded = mgr.UnloadWorldAsync(d.Id).GetAwaiter().GetResult();
                Assert.IsFalse(unloaded);
                Assert.AreEqual(d.Id, mgr.Active.WorldId,
                    "Active world must remain loaded after a refused unload.");
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                Cleanup(d);
            }
        }

        [Test]
        public void UnloadInactiveWorld_Releases()
        {
            var mgr = new WorldManager();
            var a   = MakeDescriptor("alpha");
            var b   = MakeDescriptor("beta");
            try
            {
                mgr.LoadAndActivateAsync(a).GetAwaiter().GetResult();
                mgr.LoadWorldAsync(b).GetAwaiter().GetResult();
                bool unloaded = mgr.UnloadWorldAsync(b.Id).GetAwaiter().GetResult();
                Assert.IsTrue(unloaded);
                Assert.AreEqual(1, mgr.Loaded.Count);
            }
            finally { Cleanup(a); Cleanup(b); }
        }

        [Test]
        public void LoadedWorlds_HaveIsolatedRegistries()
        {
            var mgr = new WorldManager();
            var a   = MakeDescriptor("alpha");
            var b   = MakeDescriptor("beta");
            try
            {
                var ctxA = mgr.LoadWorldAsync(a).GetAwaiter().GetResult();
                var ctxB = mgr.LoadWorldAsync(b).GetAwaiter().GetResult();

                ctxA.Services.Register<TestService>(new TestService { Tag = "A" });
                Assert.IsFalse(ctxB.Services.TryGet<TestService>(out _),
                    "Each world must own its own registry — Phase 4 sharding " +
                    "depends on this isolation.");
            }
            finally { Cleanup(a); Cleanup(b); }
        }

        public class TestService { public string Tag; }
    }
}
