using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.World.Worlds;

namespace Valkur.Tests.EditMode.Game.World.Worlds
{
    /// <summary>
    /// Phase 1 contract: WorldPortal hands off to IWorldManager and never
    /// touches scene state directly. Tests drive the activation flow through
    /// the public <see cref="WorldPortal.ActivateForTest"/> entry point so
    /// the OnTrigger / coroutine plumbing does not require a live PlayMode.
    /// </summary>
    [TestFixture]
    public class WorldPortalTests
    {
        private GameObject _portalGo;
        private WorldPortal _portal;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _portalGo = new GameObject("PortalTest");
            // BoxCollider2D satisfies the [RequireComponent(typeof(Collider2D))].
            _portalGo.AddComponent<BoxCollider2D>();
            _portal = _portalGo.AddComponent<WorldPortal>();
            // Bypass MonoBehaviour.Awake (which sets isTrigger but is not
            // strictly required for these tests).
        }

        [TearDown]
        public void TearDown()
        {
            if (_portalGo != null) Object.DestroyImmediate(_portalGo);
            LogAssert.ignoreFailingMessages = false;
        }

        private static void SetField(object obj, string name, object value)
            => obj.GetType()
                  .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                  .SetValue(obj, value);

        private static WorldDescriptor MakeDescriptor(string slug, Vector2Int defaultSpawn)
        {
            var cfg = WorldConfig.CreateLegacyFallback();
            var d   = ScriptableObject.CreateInstance<WorldDescriptor>();
            typeof(WorldDescriptor)
                .GetField("config", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(d, cfg);
            typeof(WorldDescriptor)
                .GetField("slug", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(d, slug);
            typeof(WorldDescriptor)
                .GetField("defaultSpawnTile", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(d, defaultSpawn);
            return d;
        }

        private static void Cleanup(WorldDescriptor d)
        {
            if (d != null && d.Config != null) Object.DestroyImmediate(d.Config);
            if (d != null) Object.DestroyImmediate(d);
        }

        // Drive the activation coroutine to completion synchronously.
        private static void Drive(IEnumerator routine)
        {
            int safety = 1000;
            while (routine.MoveNext() && safety-- > 0)
            {
                if (routine.Current is IEnumerator inner) Drive(inner);
            }
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void ResolveSpawnTile_Override_BeatsDescriptorDefault()
        {
            var d = MakeDescriptor("alt", new Vector2Int(10, 10));
            try
            {
                SetField(_portal, "destinationWorld",   d);
                SetField(_portal, "spawnTileOverride", new Vector2Int(50, 50));
                Assert.AreEqual(new Vector2Int(50, 50), _portal.ResolveSpawnTile(),
                    "Inspector override must take priority over descriptor default.");
            }
            finally { Cleanup(d); }
        }

        [Test]
        public void ResolveSpawnTile_Default_FallsBackToDescriptor()
        {
            var d = MakeDescriptor("alt", new Vector2Int(123, 456));
            try
            {
                SetField(_portal, "destinationWorld",   d);
                SetField(_portal, "spawnTileOverride", Vector2Int.zero);
                Assert.AreEqual(new Vector2Int(123, 456), _portal.ResolveSpawnTile());
            }
            finally { Cleanup(d); }
        }

        [Test]
        public void Activate_DelegatesToWorldManager_ActivatesDestination()
        {
            var d = MakeDescriptor("alt", new Vector2Int(50, 50));
            try
            {
                SetField(_portal, "destinationWorld",  d);
                SetField(_portal, "activationDelay",   0f);
                var mgr = new WorldManager();

                Drive(_portal.ActivateForTest(mgr));

                Assert.IsNotNull(mgr.Active);
                Assert.AreEqual(d.Id, mgr.Active.WorldId,
                    "Portal must hand off to IWorldManager.LoadAndActivateAsync " +
                    "with its configured destination.");
            }
            finally { Cleanup(d); }
        }

        [Test]
        public void Activate_NoDestination_LogsError()
        {
            // destinationWorld unset; activation must bail without crashing.
            // The portal logs an error in this case — declare it expected so
            // the test runner does not treat it as an unhandled message.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("destinationWorld is not set"));

            SetField(_portal, "activationDelay", 0f);
            var mgr = new WorldManager();
            Assert.DoesNotThrow(() => Drive(_portal.ActivateForTest(mgr)));
            Assert.IsNull(mgr.Active,
                "With no destination, the portal must not activate any world.");
        }
    }
}
