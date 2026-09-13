using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// The curtain: drops in one call, lifts over a stated time, never eats a click, and runs
    /// its callback exactly once at full black.
    /// </summary>
    [TestFixture]
    public class ScreenFadeTests
    {
        [TearDown] public void Cleanup() => ScreenFade.DestroyForTests();

        [Test]
        public void CoverAndReveal_IsBlackNow_AndLiftsOverTheStatedTime()
        {
            ScreenFade.CoverAndReveal(0.5f);
            var f = ScreenFade.Instance;
            Assert.That(f.Alpha, Is.EqualTo(1f));
            Assert.IsTrue(ScreenFade.IsCovering);

            f.Tick(0.25f);
            Assert.That(f.Alpha, Is.EqualTo(0.5f).Within(1e-4f));
            f.Tick(0.3f);
            Assert.That(f.Alpha, Is.EqualTo(0f));
            Assert.IsFalse(ScreenFade.IsCovering);
        }

        [Test]
        public void FadeOut_RunsItsCallbackOnce_AtFullBlack()
        {
            int calls = 0;
            ScreenFade.FadeOut(0.2f, () => calls++);
            var f = ScreenFade.Instance;
            f.Tick(0.1f);
            Assert.That(calls, Is.EqualTo(0), "Half way down is not covered.");
            f.Tick(0.1f);
            Assert.That(calls, Is.EqualTo(1));
            f.Tick(1f);
            Assert.That(calls, Is.EqualTo(1), "Once.");
        }

        [Test]
        public void TheCurtain_NeverEatsAClick()
        {
            var f = ScreenFade.Instance;
            var image = f.GetComponentInChildren<Image>(true);
            Assert.IsNotNull(image);
            Assert.IsFalse(image.raycastTarget, "A fade that swallowed the click that caused it would break the door.");
            Assert.IsNull(f.GetComponent<GraphicRaycaster>());
            Assert.That(f.GetComponent<Canvas>().sortingOrder, Is.LessThan(9999), "Under the loading screen.");
        }
    }
}
