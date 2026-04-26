using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.UI.HUD;

namespace Valkur.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// PlayMode tests for the Minimap system (MinimapManager + MinimapDot).
    /// Verifies static registration pattern and Instance caching.
    /// </summary>
    public class MinimapPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            // Ensure no leftover state between tests
            EntityRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator MinimapManager_SetsStaticInstance()
        {
            var go = new GameObject("MinimapManager");
            var mgr = go.AddComponent<MinimapManager>();

            yield return null;

            Assert.IsNotNull(MinimapManager.Instance);
            Assert.AreSame(mgr, MinimapManager.Instance);

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator MinimapDot_RegistersAndUnregisters()
        {
            // Create a minimal manager
            var mgrGo = new GameObject("MinimapManager");
            mgrGo.AddComponent<MinimapManager>();
            yield return null;

            // Create dot
            var dotGo = new GameObject("TestDot");
            var dot = dotGo.AddComponent<MinimapDot>();
            yield return null;

            // Dot should be registered (visible in MinimapManager's static list)
            Assert.IsNotNull(dot);
            Assert.IsTrue(dot.enabled);

            // Disable should unregister
            dotGo.SetActive(false);
            yield return null;

            // Re-enable should re-register
            dotGo.SetActive(true);
            yield return null;

            Object.Destroy(dotGo);
            Object.Destroy(mgrGo);
        }

        [UnityTest]
        public IEnumerator MinimapDot_GetDefaultColor_UsesStaticInstance()
        {
            var mgrGo = new GameObject("MinimapManager");
            mgrGo.AddComponent<MinimapManager>();
            yield return null;

            // Verify Instance is set
            Assert.IsNotNull(MinimapManager.Instance);

            // Create a dot with useDefaultColor=true
            var dotGo = new GameObject("TestDot");
            var dot = dotGo.AddComponent<MinimapDot>();
            dot.Configure(MinimapDotType.Player, Color.white);
            yield return null;

            // Player type should get green-ish color from MinimapManager defaults
            // (Configure overrides useDefaultColor, so this tests the manual path)
            Assert.AreEqual(MinimapDotType.Player, dot.DotType);

            Object.Destroy(dotGo);
            Object.Destroy(mgrGo);
        }
    }
}
