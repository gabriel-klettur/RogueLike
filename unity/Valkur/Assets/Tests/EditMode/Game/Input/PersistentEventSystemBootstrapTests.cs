using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Regression net for "There can be only one active Event System." — one console
    /// error on every boot.
    ///
    /// <see cref="RuntimeInputBootstrap"/> runs at <c>BeforeSceneLoad</c>, when the first
    /// scene's objects have NOT awoken yet. Creating an EventSystem there guarantees a
    /// duplicate the instant a scene that ships its own (MainMenu.unity) runs
    /// <c>OnEnable</c> and registers with UIElementsRuntimeUtility.
    ///
    /// Two halves of the fix are locked down here:
    ///   • <c>Ensure(createIfMissing: false)</c> mints nothing when the scene has none yet,
    ///     so the sceneLoaded pass can adopt the scene's instead.
    ///   • <c>RemoveDuplicates</c> DISABLES a duplicate before destroying it. Destroy is
    ///     deferred to end-of-frame, so without the explicit disable the duplicate is
    ///     still registered when the persistent one is re-enabled on the next line.
    /// </summary>
    [TestFixture]
    public class PersistentEventSystemBootstrapTests
    {
        [SetUp]
        public void SetUp() => DestroyEveryEventSystem();

        [TearDown]
        public void TearDown() => DestroyEveryEventSystem();

        private static void DestroyEveryEventSystem()
        {
            PersistentEventSystem.ResetForTests();
            var all = Object.FindObjectsOfType<EventSystem>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null) Object.DestroyImmediate(all[i].gameObject);
            }
        }

        [Test]
        public void Ensure_WithoutCreate_MintsNothingWhenSceneHasNone()
        {
            var result = PersistentEventSystem.Ensure(createIfMissing: false);

            Assert.IsNull(result,
                "Ensure(createIfMissing: false) must not mint an EventSystem — that is the " +
                "BeforeSceneLoad path, and creating one there produces the boot duplicate.");
            Assert.AreEqual(0, Object.FindObjectsOfType<EventSystem>().Length,
                "No EventSystem may exist in the scene after a non-creating Ensure.");
        }

        [Test]
        public void Ensure_WithoutCreate_StillAdoptsASceneShippedEventSystem()
        {
            // MainMenu.unity ships its own. The boot pass must adopt it rather than
            // ignore it — otherwise nothing configures its input module.
            var scene = new GameObject("SceneEventSystem").AddComponent<EventSystem>();

            var result = PersistentEventSystem.Ensure(createIfMissing: false);

            Assert.AreSame(scene, result, "A scene-shipped EventSystem must be adopted.");
        }

        [Test]
        public void Ensure_WithCreate_MintsExactlyOne()
        {
            var result = PersistentEventSystem.Ensure(createIfMissing: true);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, Object.FindObjectsOfType<EventSystem>().Length);
        }

        [Test]
        public void ConfigureModule_CollapsesDuplicatesAndLeavesExactlyOneEnabled()
        {
            var keep = new GameObject("[PersistentEventSystem]").AddComponent<EventSystem>();
            var duplicateA = new GameObject("SceneEventSystemA").AddComponent<EventSystem>();
            var duplicateB = new GameObject("SceneEventSystemB").AddComponent<EventSystem>();

            PersistentEventSystem.ConfigureModule(keep);

            // DestroyImmediate is used outside play mode, so the duplicates are gone
            // synchronously; the Unity == overload reports the destroyed refs as null.
            Assert.IsTrue(duplicateA == null, "Duplicate A must be removed.");
            Assert.IsTrue(duplicateB == null, "Duplicate B must be removed.");

            var survivors = Object.FindObjectsOfType<EventSystem>();
            Assert.AreEqual(1, survivors.Length, "Exactly one EventSystem may survive.");
            Assert.AreSame(keep, survivors[0]);
            Assert.IsTrue(keep.enabled, "The surviving EventSystem must end up enabled.");
        }
    }
}
