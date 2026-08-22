using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// Contract for the Options → Video resolution list.
    ///
    /// The whole point of a curated list is that every entry is a size the
    /// camera can render without a tilemap seam. A seam appears when one art
    /// texel doesn't cover a whole number of screen pixels;
    /// <c>CameraSetup.SnapOrthoSize</c> guarantees that vertically by solving
    /// ortho size from <c>pixelHeight</c>, and the horizontal axis inherits the
    /// guarantee only when <c>pixelWidth / pixelHeight</c> is EXACTLY the
    /// target aspect. So: any preset that isn't exactly 2:1 is a shipped
    /// seam, and this fixture is what stops one being added by hand.
    /// </summary>
    [TestFixture]
    public class DisplaySettingsTests
    {
        [Test]
        public void Presets_FirstEntryIsNative()
        {
            Assert.IsTrue(DisplaySettings.Presets.Length > 1,
                "Presets must offer Native plus at least one fixed size.");
            Assert.IsTrue(DisplaySettings.Presets[0].IsNative,
                "Index 0 must be the Native entry — GameSettings defaults to 0x0 and " +
                "IndexOf() falls back to 0 for anything it doesn't recognise.");
        }

        [Test]
        public void Presets_EveryFixedSizeIsExactlyTargetAspect()
        {
            for (int i = 1; i < DisplaySettings.Presets.Length; i++)
            {
                var p = DisplaySettings.Presets[i];
                Assert.IsFalse(p.IsNative, $"Preset {i} ({p.Label}) must be a concrete size.");
                Assert.IsTrue(DisplaySettings.IsSeamSafe(p.Width, p.Height),
                    $"Preset {i} ({p.Label}) is not exactly {DisplaySettings.TargetAspect}:1. " +
                    "A non-exact ratio makes Camera.aspect drift from the ortho snap's " +
                    "vertical guarantee, which is what draws vertical seam lines across " +
                    "the tilemap.");

                // Bit-exact float division, not an epsilon compare — the drift
                // that produced the bug was 0.0029, well inside any tolerance
                // someone would have picked.
                Assert.AreEqual(DisplaySettings.TargetAspect, (float)p.Width / p.Height,
                    $"Preset {i} ({p.Label}) width/height must equal the target aspect exactly.");
            }
        }

        [Test]
        public void Presets_HaveNoDuplicates()
        {
            for (int i = 0; i < DisplaySettings.Presets.Length; i++)
                for (int j = i + 1; j < DisplaySettings.Presets.Length; j++)
                    Assert.IsFalse(
                        DisplaySettings.Presets[i].Width == DisplaySettings.Presets[j].Width &&
                        DisplaySettings.Presets[i].Height == DisplaySettings.Presets[j].Height,
                        $"Presets {i} and {j} are the same size — IndexOf() would never " +
                        "return the second one, so it would be unselectable.");
        }

        [Test]
        public void IndexOf_RoundTripsEveryPreset()
        {
            for (int i = 0; i < DisplaySettings.Presets.Length; i++)
            {
                var p = DisplaySettings.Presets[i];
                Assert.AreEqual(i, DisplaySettings.IndexOf(p.Width, p.Height),
                    $"Preset {i} ({p.Label}) must round-trip through IndexOf.");
            }
        }

        [Test]
        public void IndexOf_UnknownSizeFallsBackToNative()
        {
            // A settings.json written by an older build (or hand-edited) must
            // degrade to Native rather than resizing the window to a size the
            // camera can't render seam-free.
            Assert.AreEqual(0, DisplaySettings.IndexOf(1366, 768));
            Assert.AreEqual(0, DisplaySettings.IndexOf(-1, -1));
        }

        [Test]
        public void IsSeamSafe_RejectsOffRatioSizes()
        {
            Assert.IsFalse(DisplaySettings.IsSeamSafe(1366, 768));
            Assert.IsFalse(DisplaySettings.IsSeamSafe(1920, 1080));
            Assert.IsFalse(DisplaySettings.IsSeamSafe(0, 0));
            Assert.IsTrue(DisplaySettings.IsSeamSafe(1920, 960));
        }

        [Test]
        public void WindowModeLabels_CoverEveryEnumValue()
        {
            var values = (WindowMode[])System.Enum.GetValues(typeof(WindowMode));
            Assert.AreEqual(values.Length, DisplaySettings.WindowModeLabels.Length,
                "Every WindowMode needs a label — the Video panel cycles by index " +
                "over WindowModeLabels and casts that index straight to WindowMode.");
            foreach (var v in values)
                Assert.IsFalse(string.IsNullOrEmpty(DisplaySettings.WindowModeLabel(v)),
                    $"WindowMode.{v} has no label.");
        }

        [Test]
        public void GameSettings_DefaultsToNativeWindowed()
        {
            var fresh = new GameSettings();
            Assert.AreEqual(0, fresh.resolutionWidth);
            Assert.AreEqual(0, fresh.resolutionHeight);
            Assert.AreEqual(WindowMode.Windowed, fresh.windowMode,
                "A first-run player must get a window, not an exclusive-fullscreen " +
                "mode change before they've seen the menu.");
        }

        [Test]
        public void GameSettings_ResetToDefaults_ClearsDisplayChoice()
        {
            var s = new GameSettings
            {
                resolutionWidth  = 2560,
                resolutionHeight = 1280,
                windowMode       = WindowMode.ExclusiveFullscreen,
            };
            s.ResetToDefaults();
            Assert.AreEqual(0, s.resolutionWidth);
            Assert.AreEqual(0, s.resolutionHeight);
            Assert.AreEqual(WindowMode.Windowed, s.windowMode);
        }

        [Test]
        public void Apply_IsANoOpInTheEditor()
        {
            // Screen.SetResolution can't move the Game View, and calling it
            // anyway shifts Screen.width for a frame, which makes the aspect
            // enforcer and the ortho snap thrash. Guard is asserted by the
            // absence of any Screen change — plus this pins that Apply never
            // throws on a settings object with a size the presets don't hold.
            int w = Screen.width, h = Screen.height;
            var s = new GameSettings { resolutionWidth = 1366, resolutionHeight = 768 };
            Assert.DoesNotThrow(() => DisplaySettings.Apply(s));
            Assert.DoesNotThrow(() => DisplaySettings.Apply(null));
            Assert.AreEqual(w, Screen.width);
            Assert.AreEqual(h, Screen.height);
        }
    }
}
