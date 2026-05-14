using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;

namespace Valkur.Tests.PlayMode.Core
{
    /// <summary>
    /// PlayMode tests for <see cref="GameDirector"/> integration with
    /// <see cref="ServiceLocator"/>. The pure sync register/get/unregister/clear
    /// path lives in <c>EditMode/Game/Core/Services/ServiceLocatorTests.cs</c>;
    /// the three tests below stay in PlayMode because they require:
    ///   - <see cref="MonoBehaviour"/> Awake/OnDestroy to fire (registration hook).
    ///   - <see cref="Time.timeScale"/> mutation (Pause path).
    ///   - One real frame to elapse so destroy callbacks run before re-query.
    /// </summary>
    public class ServiceLocatorPlayTests
    {
        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
        }

        [UnityTest]
        public IEnumerator GameDirector_RegistersSelfInServiceLocator()
        {
            var go = new GameObject("GameDirector");
            go.AddComponent<GameDirector>();

            yield return null;

            var director = ServiceLocator.Get<GameDirector>();
            Assert.IsNotNull(director, "GameDirector should register itself in ServiceLocator.");
            Assert.AreEqual(GameDirector.Instance, director);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameDirector_UnregistersOnDestroy()
        {
            var go = new GameObject("GameDirector");
            go.AddComponent<GameDirector>();

            yield return null;
            Assert.IsNotNull(ServiceLocator.Get<GameDirector>());

            Object.Destroy(go);
            yield return null;

            Assert.IsNull(ServiceLocator.Get<GameDirector>(),
                "GameDirector should unregister from ServiceLocator on destroy.");
        }

        [UnityTest]
        public IEnumerator GameDirector_Pause_SetsTimeScale()
        {
            var go = new GameObject("GameDirector");
            var director = go.AddComponent<GameDirector>();

            yield return null;

            director.SetPaused(true);
            Assert.IsTrue(director.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);

            director.SetPaused(false);
            Assert.IsFalse(director.IsPaused);
            Assert.AreEqual(1f, Time.timeScale);

            Object.Destroy(go);
        }
    }
}
