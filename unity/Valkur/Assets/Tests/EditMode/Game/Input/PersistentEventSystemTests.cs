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
    /// Verifies <see cref="PersistentEventSystem"/> ends up with exactly one
    /// EventSystem in the scene at all times, wired to <see cref="InputService"/>'s
    /// UI map.
    ///
    /// This guards two related regressions:
    ///   1. Two simultaneous EventSystems on scene reload (the persistent one +
    ///      the one a scene shipped). Only one wins, often the broken one.
    ///   2. The InputSystemUIInputModule keeping stale references to actions
    ///      that point at <c>fileID:0</c> (the MainMenu.unity bug from earlier).
    /// </summary>
    [TestFixture]
    public class PersistentEventSystemTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Mouse.current == null)    InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();

            // Wipe any leftover EventSystems from prior tests so each test starts clean.
            foreach (var es in Object.FindObjectsOfType<EventSystem>())
                Object.DestroyImmediate(es.gameObject);

            PersistentEventSystem.ResetForTests();
            InputService.ResetForTests();
            InputService.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            PersistentEventSystem.ResetForTests();
            InputService.ResetForTests();
            foreach (var es in Object.FindObjectsOfType<EventSystem>())
                Object.DestroyImmediate(es.gameObject);
            LogAssert.ignoreFailingMessages = false;
        }

        // ─── Singleton contract ─────────────────────────────────────────────────

        [Test]
        public void Ensure_FromEmptyScene_CreatesOneEventSystem()
        {
            var es = PersistentEventSystem.Ensure();
            Assert.IsNotNull(es);
            Assert.AreSame(es, PersistentEventSystem.Instance);

            var all = Object.FindObjectsOfType<EventSystem>();
            Assert.AreEqual(1, all.Length, "Ensure on an empty scene must produce exactly one EventSystem.");
        }

        [Test]
        public void Ensure_AdoptsExistingSceneEventSystem_InsteadOfDuplicating()
        {
            // Simulate a scene-shipped EventSystem (the legacy MainMenu pattern).
            var sceneGo = new GameObject("SceneEventSystem");
            sceneGo.AddComponent<EventSystem>();

            var es = PersistentEventSystem.Ensure();

            Assert.AreSame(sceneGo.GetComponent<EventSystem>(), es,
                "Ensure must adopt a scene-shipped EventSystem rather than spawning a parallel one.");

            var all = Object.FindObjectsOfType<EventSystem>();
            Assert.AreEqual(1, all.Length, "After adoption the scene must still contain exactly one EventSystem.");
        }

        // ─── Duplicate cleanup on subsequent calls ─────────────────────────────

        [Test]
        public void Ensure_CalledTwice_RemovesDuplicateEventSystem_OnSecondCall()
        {
            // First Ensure produces the singleton.
            var first = PersistentEventSystem.Ensure();

            // A scene loads carrying its own EventSystem (regression scenario).
            var stowaway = new GameObject("StowawayEventSystem");
            stowaway.AddComponent<EventSystem>();
            Assert.AreEqual(2, Object.FindObjectsOfType<EventSystem>().Length,
                "Sanity: we should momentarily have two EventSystems before Ensure cleans up.");

            // Second Ensure must collapse back to one.
            var second = PersistentEventSystem.Ensure();
            Assert.AreSame(first, second);

            var all = Object.FindObjectsOfType<EventSystem>();
            Assert.AreEqual(1, all.Length,
                "Ensure on subsequent calls MUST destroy duplicates introduced by scene loads — otherwise UI clicks dispatch to the wrong (un-wired) EventSystem.");
        }

        // ─── InputSystemUIInputModule wiring ───────────────────────────────────

        [Test]
        public void Ensure_InputModule_HasInputServiceWiring()
        {
            var es = PersistentEventSystem.Ensure();
            var module = es.GetComponent<InputSystemUIInputModule>();
            Assert.IsNotNull(module, "Ensure must add an InputSystemUIInputModule to the EventSystem GameObject.");

            var ui = InputService.Instance.UI;
            Assert.IsNotNull(module.point);
            Assert.AreSame(ui.Point, module.point.action,
                "module.point must reference InputService.UI.Point so EventSystem cursor events come from the canonical asset.");
            Assert.AreSame(ui.Click, module.leftClick.action);
            Assert.AreSame(ui.RightClick, module.rightClick.action);
            Assert.AreSame(ui.MiddleClick, module.middleClick.action);
            Assert.AreSame(ui.ScrollWheel, module.scrollWheel.action);
            Assert.AreSame(ui.Navigate, module.move.action);
            Assert.AreSame(ui.Submit, module.submit.action);
            Assert.AreSame(ui.Cancel, module.cancel.action);
        }

        [Test]
        public void Ensure_KeepsStandaloneInputModuleAsActiveUIModule()
        {
            // Earlier versions of PersistentEventSystem stripped StandaloneInputModule
            // in favor of InputSystemUIInputModule. That regressed UI clicks under the
            // recurring Unity 2022.3 InputSystem-drops-events bug, so the policy is
            // now reversed: Standalone is kept enabled (reads UnityEngine.Input which
            // never breaks), and the new module is installed but disabled. See
            // PersistentEventSystem.ConfigureModule for the rationale.
            PersistentEventSystem.Ensure();

            var es = PersistentEventSystem.Instance;
            Assert.IsNotNull(es);
            var standalone = es.GetComponent<StandaloneInputModule>();
            Assert.IsNotNull(standalone, "StandaloneInputModule must be present and active.");
            Assert.IsTrue(standalone.enabled);
        }

        // ─── Idempotence ────────────────────────────────────────────────────────

        [Test]
        public void Ensure_CalledManyTimes_AlwaysReturnsSameInstance()
        {
            var a = PersistentEventSystem.Ensure();
            var b = PersistentEventSystem.Ensure();
            var c = PersistentEventSystem.Ensure();
            Assert.AreSame(a, b);
            Assert.AreSame(b, c);
            Assert.AreEqual(1, Object.FindObjectsOfType<EventSystem>().Length);
        }
    }
}
