using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Gameplay;
using Valkur.Gameplay.World.Worlds;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Phase 1 contract: GameplaySceneSetup.EnsureWorldManager must register
    /// an IWorldManager in the global ServiceLocator and activate the legacy
    /// base WorldDescriptor so every downstream step (TileOverlayPersistence,
    /// MapEditorManager, SaveService) can resolve "what is the active world"
    /// without wiring it ad-hoc.
    /// </summary>
    [TestFixture]
    public class GameplaySceneSetupWorldManagerWiringTests
    {
        private GameObject _setupGo;
        private GameplaySceneSetup _setup;

        [SetUp]
        public void SetUp()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            // Make sure no leftover IWorldManager from a previous fixture
            // pollutes the assertion below.
            ServiceLocator.Unregister<IWorldManager>();

            _setupGo = new GameObject("WiringTestSetup");
            _setup   = _setupGo.AddComponent<GameplaySceneSetup>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_setupGo != null) Object.DestroyImmediate(_setupGo);
            ServiceLocator.Unregister<IWorldManager>();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        private void InvokeEnsureWorldManager()
        {
            var m = typeof(GameplaySceneSetup).GetMethod(
                "EnsureWorldManager",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "EnsureWorldManager must exist as a private method on GameplaySceneSetup.");
            m.Invoke(_setup, null);
        }

        [Test]
        public void EnsureWorldManager_RegistersInServiceLocator()
        {
            Assert.IsNull(ServiceLocator.Get<IWorldManager>(),
                "Sanity: TearDown must have cleared the previous registration.");

            InvokeEnsureWorldManager();

            var mgr = ServiceLocator.Get<IWorldManager>();
            Assert.IsNotNull(mgr,
                "After EnsureWorldManager, downstream code must be able to resolve " +
                "an IWorldManager via the global ServiceLocator.");
        }

        [Test]
        public void EnsureWorldManager_ActivatesBaseWorld()
        {
            InvokeEnsureWorldManager();

            var mgr = ServiceLocator.Get<IWorldManager>();
            Assert.IsNotNull(mgr.Active,
                "EnsureWorldManager must Load AND Activate the legacy base " +
                "descriptor so downstream steps see a non-null Active.");
            Assert.AreEqual(WorldId.Base.Slug, mgr.Active.WorldId.Slug,
                "Active world's slug must be the legacy 'base' so byte-compat " +
                "with single-world boot is preserved.");
        }

        [Test]
        public void EnsureWorldManager_Idempotent()
        {
            InvokeEnsureWorldManager();
            var first = ServiceLocator.Get<IWorldManager>();

            InvokeEnsureWorldManager();
            var second = ServiceLocator.Get<IWorldManager>();

            Assert.AreSame(first, second,
                "Repeated EnsureWorldManager calls must reuse the same instance — " +
                "DevConsole reset / SaveService rehydration paths reentrancy depends " +
                "on this so registered services do not double-register.");
        }
    }
}
