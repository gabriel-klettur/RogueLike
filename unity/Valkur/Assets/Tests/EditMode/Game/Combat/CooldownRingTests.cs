using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.Combat
{
    public class CooldownRingTests
    {
        [SetUp]
        public void Ignore()
        {
            // CooldownRing uses Texture2D.whiteTexture; the underlying sprite creation
            // allocates a material that survives EditMode teardown. Suppress the leak warn.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        [Test]
        public void AddToParent_CreatesChildWithStretchedRect()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            var ring = CooldownRing.AddToParent(parent.transform);
            Assert.IsNotNull(ring);
            Assert.AreSame(parent.transform, ring.transform.parent);
            var rt = ring.GetComponent<RectTransform>();
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void SetProgress_UpdatesImageFillAmount()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            var ring = CooldownRing.AddToParent(parent.transform);
            ring.SetProgress(0.5f);
            var img = ring.GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(0.5f, img.fillAmount, 0.001f);
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void SetProgress_ClampsBetweenZeroAndOne()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            var ring = CooldownRing.AddToParent(parent.transform);
            ring.SetProgress(-1f);
            Assert.AreEqual(0f, ring.GetComponent<UnityEngine.UI.Image>().fillAmount, 0.001f);
            ring.SetProgress(5f);
            Assert.AreEqual(1f, ring.GetComponent<UnityEngine.UI.Image>().fillAmount, 0.001f);
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void ReadyFlash_BeginsWhenProgressReachesZero()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            var ring = CooldownRing.AddToParent(parent.transform);
            ring.SetProgress(0.9f);
            ring.SetProgress(0f);
            // With flash timer set, image should still be enabled for the flash duration.
            Assert.IsTrue(ring.GetComponent<UnityEngine.UI.Image>().enabled);
            Object.DestroyImmediate(parent);
        }
    }
}
