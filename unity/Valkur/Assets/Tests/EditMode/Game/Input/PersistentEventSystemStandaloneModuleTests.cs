using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Pins the contract that <see cref="PersistentEventSystem.ConfigureModule"/>
    /// installs <see cref="StandaloneInputModule"/> as the ACTIVE UI input module
    /// and keeps <see cref="InputSystemUIInputModule"/> disabled. This is the
    /// fix that gets <c>Button.OnClick</c> working when the new InputSystem
    /// package is dropping OS events — Standalone reads from the legacy
    /// <see cref="UnityEngine.Input"/> backend which never breaks that way.
    ///
    /// If a future refactor flips the modules' enabled flags or removes the
    /// Standalone install, every UI button in the menu chain stops responding
    /// to clicks under the bug. These tests catch that regression.
    /// </summary>
    [TestFixture]
    public class PersistentEventSystemStandaloneModuleTests
    {
        private GameObject _esGo;
        private EventSystem _eventSystem;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Boot InputService (Standalone module configuration reads from it).
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            InputService.Initialize();

            _esGo = new GameObject("[TestEventSystem]");
            _eventSystem = _esGo.AddComponent<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_esGo != null) Object.DestroyImmediate(_esGo);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void ConfigureModule_InstallsStandaloneInputModule()
        {
            PersistentEventSystem.ConfigureModule(_eventSystem);
            var standalone = _eventSystem.GetComponent<StandaloneInputModule>();
            Assert.IsNotNull(standalone,
                "ConfigureModule must add a StandaloneInputModule — it is the " +
                "module that delivers UI clicks via the legacy backend.");
        }

        [Test]
        public void ConfigureModule_StandaloneInputModuleIsEnabled()
        {
            PersistentEventSystem.ConfigureModule(_eventSystem);
            var standalone = _eventSystem.GetComponent<StandaloneInputModule>();
            Assert.IsNotNull(standalone);
            Assert.IsTrue(standalone.enabled,
                "StandaloneInputModule must be enabled — it is the active UI " +
                "input module that delivers Button.OnClick.");
        }

        [Test]
        public void ConfigureModule_InputSystemUIInputModuleIsDisabled()
        {
            PersistentEventSystem.ConfigureModule(_eventSystem);
            var newModule = _eventSystem.GetComponent<InputSystemUIInputModule>();
            // The new module may or may not be installed depending on test
            // ordering; the contract is that IF installed, it stays disabled.
            // Two enabled UI modules on one EventSystem fight over events.
            if (newModule != null)
                Assert.IsFalse(newModule.enabled,
                    "InputSystemUIInputModule must be DISABLED — Standalone takes " +
                    "precedence to guarantee clicks reach Button.OnClick under the " +
                    "Unity 2022.3 InputSystem-drops-events bug.");
        }

        [Test]
        public void ConfigureModule_EventSystemIsEnabled()
        {
            _eventSystem.enabled = false;
            PersistentEventSystem.ConfigureModule(_eventSystem);
            Assert.IsTrue(_eventSystem.enabled,
                "ConfigureModule must leave the EventSystem itself enabled.");
        }

        [Test]
        public void ConfigureModule_IsIdempotent()
        {
            PersistentEventSystem.ConfigureModule(_eventSystem);
            int standaloneCount1 = _eventSystem.GetComponents<StandaloneInputModule>().Length;
            int newModuleCount1 = _eventSystem.GetComponents<InputSystemUIInputModule>().Length;

            PersistentEventSystem.ConfigureModule(_eventSystem);
            int standaloneCount2 = _eventSystem.GetComponents<StandaloneInputModule>().Length;
            int newModuleCount2 = _eventSystem.GetComponents<InputSystemUIInputModule>().Length;

            Assert.AreEqual(standaloneCount1, standaloneCount2,
                "Idempotent: configuring twice must not add a second StandaloneInputModule.");
            Assert.AreEqual(newModuleCount1, newModuleCount2,
                "Idempotent: configuring twice must not add a second InputSystemUIInputModule.");
        }

        [Test]
        public void ConfigureModule_HandlesNullEventSystemGracefully()
        {
            Assert.DoesNotThrow(() => PersistentEventSystem.ConfigureModule(null));
        }
    }
}
