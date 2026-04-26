using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UI.Loading;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Regression tests for the loading screen progress bar.
    ///
    /// Key regressions prevented:
    ///   - Bar appearing 100% full from the start regardless of fillAmount.
    ///     Root cause: an Image with Type.Filled but no sprite assigned renders
    ///     as a solid rect ignoring fillAmount. BuildUI() MUST set _barFill.sprite.
    ///   - ApplyProgress() not propagating values to fillAmount/percentage label.
    ///   - Percentage label not staying in sync with fillAmount (must always
    ///     reflect the same value to within 1%).
    /// </summary>
    [TestFixture]
    public class LoadingScreenControllerTests
    {
        private GameObject _go;
        private LoadingScreenController _ctrl;

        [SetUp]
        public void SetUp()
        {
            _go   = new GameObject("TestLoadingScreen");
            _ctrl = _go.AddComponent<LoadingScreenController>();
            // Build the UI hierarchy without going through Show() / Start()
            // (Start subscribes static callbacks and starts coroutines we don't
            //  want in EditMode tests).
            InvokePrivate("BuildUI");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private void InvokePrivate(string methodName)
        {
            var m = typeof(LoadingScreenController).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(_ctrl, null);
        }

        private void InvokeApplyProgress(float p)
        {
            var m = typeof(LoadingScreenController).GetMethod("ApplyProgress",
                BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(_ctrl, new object[] { p });
        }

        private T GetField<T>(string name)
        {
            var f = typeof(LoadingScreenController).GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (T)f.GetValue(_ctrl) : default;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// CRITICAL: Image.Type.Filled requires a sprite. Without one, the bar
        /// renders as a solid rect ignoring fillAmount and looks 100% full.
        /// </summary>
        [Test]
        public void BarFillImage_HasSpriteAssigned_AfterBuildUI()
        {
            var bar = GetField<Image>("_barFill");
            Assert.IsNotNull(bar, "_barFill Image must be created by BuildUI");
            Assert.IsNotNull(bar.sprite,
                "_barFill.sprite MUST be non-null. Image.Type.Filled without a " +
                "sprite renders as a solid rect that ignores fillAmount, making " +
                "the bar appear 100% full from the start.");
        }

        [Test]
        public void BarFillImage_IsFilledHorizontalLeftToRight()
        {
            var bar = GetField<Image>("_barFill");
            Assert.IsNotNull(bar);
            Assert.AreEqual(Image.Type.Filled, bar.type,
                "_barFill must use Image.Type.Filled so fillAmount drives the visual");
            Assert.AreEqual((int)Image.FillMethod.Horizontal, (int)bar.fillMethod,
                "Bar must fill horizontally");
            Assert.AreEqual(0, bar.fillOrigin, "Bar must fill left-to-right (origin = 0)");
        }

        [Test]
        public void BarFillImage_StartsAtZero()
        {
            var bar = GetField<Image>("_barFill");
            Assert.IsNotNull(bar);
            Assert.AreEqual(0f, bar.fillAmount, 0.001f,
                "Bar must start empty (fillAmount = 0). If this fails the bar is " +
                "showing as full from the very first frame.");
        }

        [TestCase(0.00f)]
        [TestCase(0.25f)]
        [TestCase(0.40f)]
        [TestCase(0.66f)]
        [TestCase(1.00f)]
        public void ApplyProgress_DrivesBarFillAndPercentLabel(float progress)
        {
            InvokeApplyProgress(progress);

            var bar  = GetField<Image>("_barFill");
            var pct  = GetField<TextMeshProUGUI>("_pctText");

            Assert.AreEqual(progress, bar.fillAmount, 0.001f,
                $"fillAmount must equal applied progress {progress}");

            int expectedPct = Mathf.RoundToInt(progress * 100f);
            Assert.AreEqual($"{expectedPct}%", pct.text,
                "Percentage label must mirror the fill amount");
        }

        [TestCase(-0.5f, 0f)]
        [TestCase( 1.5f, 1f)]
        [TestCase( 2.0f, 1f)]
        public void ApplyProgress_ClampsValuesToZeroOne(float input, float expected)
        {
            InvokeApplyProgress(input);
            var bar = GetField<Image>("_barFill");
            Assert.AreEqual(expected, bar.fillAmount, 0.001f,
                "ApplyProgress must clamp out-of-range inputs to [0,1]");
        }

        /// <summary>
        /// Walking the bar from 0% to 100% in increments must produce a strictly
        /// non-decreasing fill (and the percentage label must always agree).
        /// </summary>
        [Test]
        public void ApplyProgress_MonotonicallyFillsBarAcrossSweep()
        {
            var bar = GetField<Image>("_barFill");
            var pct = GetField<TextMeshProUGUI>("_pctText");

            float previous = -1f;
            for (int i = 0; i <= 10; i++)
            {
                float p = i / 10f;
                InvokeApplyProgress(p);

                Assert.GreaterOrEqual(bar.fillAmount, previous,
                    $"Bar fill must never decrease as progress advances (step {i})");
                Assert.AreEqual(Mathf.RoundToInt(p * 100f) + "%", pct.text,
                    $"Label must agree with fill at step {i}");
                previous = bar.fillAmount;
            }
        }
    }
}
