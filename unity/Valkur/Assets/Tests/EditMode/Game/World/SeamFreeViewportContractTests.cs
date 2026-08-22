using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// End-to-end contract for "the tilemap must never show seam lines".
    ///
    /// The seam is not owned by any single class — it emerges from a chain, and
    /// every previous fix guarded exactly one link, which is why it kept coming
    /// back wearing a different orientation:
    ///
    ///   window size
    ///     -> <see cref="AspectRatioEnforcer.ComputeViewport"/>  (integer box, exact ratio)
    ///     -> <c>Camera.aspect</c>
    ///     -> <see cref="CameraSetup.SnapOrthoSize"/>            (whole pixels per texel, Y)
    ///     -> world-units-per-screen-pixel on BOTH axes
    ///     -> <c>CameraPixelSnap</c>                             (camera on the pixel lattice)
    ///     -> tile quad edges land on whole pixels -> no gap -> no seam
    ///
    /// Bug history (do not regress):
    ///   - 2026-05-16: fractional <c>pixelRect</c> (819.5 px tall) -> HORIZONTAL
    ///     line. Fixed by rounding the rect to whole pixels.
    ///   - 2026-08-22: whole pixels but the WRONG RATIO — 1366x768 gave a
    ///     1366x682 viewport, aspect 2.002933. <c>SnapOrthoSize</c> only
    ///     guarantees whole pixels per texel vertically; X inherits that purely
    ///     through <c>Camera.aspect</c>, so the 0.3% error walked tile edges
    ///     onto half pixels across the width and the black background showed
    ///     through as VERTICAL lines. Fixed by quantising the viewport to
    ///     k*p by k*q from an exact integer ratio.
    ///
    /// This fixture asserts the COMPOSITION, not the links. A test that
    /// exercises only one half of a round trip proves nothing — the same lesson
    /// the spawner coordinate-space drift incident wrote down.
    /// </summary>
    [TestFixture]
    public class SeamFreeViewportContractTests
    {
        private const int SnapPPU   = 16;  // CameraSetup.snapPPU
        private const int AssetsPPU = 32;  // CameraSetup.assetsPPU (tiles)

        /// <summary>Window sizes: shipped presets, common desktops, historical breakers, edges.</summary>
        private static readonly int[][] Windows =
        {
            new[] { 1280,  640 }, new[] { 1600,  800 }, new[] { 1920,  960 },
            new[] { 2560, 1280 }, new[] { 3840, 1920 },
            new[] { 1366,  768 }, new[] { 1920, 1080 }, new[] { 2560, 1440 },
            new[] { 1280,  720 }, new[] { 1920, 1200 }, new[] { 1440,  900 },
            new[] { 1680, 1050 }, new[] { 1024,  768 }, new[] { 1552,  773 },
            new[] { 1553,  773 }, new[] {  801,  401 },
        };

        /// <summary>Zoom requests spread across the playable range, deliberately off-rung.</summary>
        private static readonly float[] RequestedOrthos =
        { 2.0f, 3.3f, 4.7f, 6.0f, 7.5f, 9.1f, 12.4f, 18.0f, 24.9f };

        private static System.Collections.IEnumerable WindowCases()
        {
            foreach (var w in Windows)
                yield return new TestCaseData(w[0], w[1]).SetName($"{w[0]}x{w[1]}");
        }

        // ────────────────────────────────────────────────────────────────────
        // The invariant that actually decides whether a seam can exist
        // ────────────────────────────────────────────────────────────────────

        [TestCaseSource(nameof(WindowCases))]
        public void WorldUnitsPerScreenPixel_MatchOnBothAxes_AtEveryZoomLevel(int sw, int sh)
        {
            var box = AspectRatioEnforcer.ComputeViewport(sw, sh, 2, 1);

            foreach (float requested in RequestedOrthos)
            {
                float ortho = CameraSetup.SnapOrthoSize(requested, box.height, SnapPPU);

                // Y is solved from pixelHeight. X is whatever Camera.aspect says
                // — and the rest of the pipeline assumes that aspect is the
                // TARGET, not the measured box. If the box drifts off 2:1 the
                // two disagree, and that disagreement IS the seam.
                float wppY = ortho * 2f / box.height;
                float wppX = ortho * 2f * DisplaySettings.TargetAspect / box.width;

                Assert.AreEqual(wppY, wppX,
                    $"{sw}x{sh} (viewport {box.width}x{box.height}), ortho {ortho:F6}: " +
                    $"world-units-per-pixel differ between axes (X {wppX:F10}, Y {wppY:F10}). " +
                    "Tile quad edges land mid-pixel horizontally and the black camera " +
                    "background shows through as vertical seam lines.");
            }
        }

        [TestCaseSource(nameof(WindowCases))]
        public void OneTileCoversAWholeNumberOfScreenPixels(int sw, int sh)
        {
            var box = AspectRatioEnforcer.ComputeViewport(sw, sh, 2, 1);

            foreach (float requested in RequestedOrthos)
            {
                float ortho = CameraSetup.SnapOrthoSize(requested, box.height, SnapPPU);

                // Above the top rung the snap deliberately passes the request
                // through so the in-game editors can zoom out panoramically; at
                // that scale the whole scene aliases together and the seam is
                // moot. Only assert inside the snapped range.
                if (ortho > box.height / (2f * SnapPPU)) continue;

                // A tile is one world unit (Grid.cellSize = 1).
                float pixelsPerTile = box.height / (2f * ortho);
                Assert.AreEqual(Mathf.Round(pixelsPerTile), pixelsPerTile, 1e-4f,
                    $"{sw}x{sh}, ortho {ortho:F6}: one tile spans {pixelsPerTile:F4} screen pixels. " +
                    "A fractional tile width walks every quad edge onto a half pixel — " +
                    "that is the gap the black background shows through.");
            }
        }

        /// <summary>
        /// Pins the trade-off measured on 2026-08-22 so nobody "fixes" it back.
        ///
        /// With snapPPU 16 against 32-PPU tile art, one texel covers N/2 screen
        /// pixels — half-integer on odd rungs. That looks alarming and is
        /// harmless: a tile is 32 texels, so its EDGES stay whole (32 x 2.5 = 80
        /// px) and only texel widths inside the tile alternate 2/3 px, uniform
        /// tile to tile and stable because CameraPixelSnap pins the camera to
        /// the pixel lattice. Rendering the live tilemap at 1600x800 found zero
        /// near-black columns at ortho 8.3333 / 6.25 / 5.0 / 4.1667.
        ///
        /// The set of ortho values giving integer 32-PPU texels is exactly
        /// pixelHeight/(64m) — i.e. snapPPU = 32 — which halves the zoom ladder
        /// and puts a 2x jump at the top. Dense ladder or integer texels; there
        /// is no third option, and the seam depends on neither.
        /// </summary>
        [TestCaseSource(nameof(WindowCases))]
        public void TexelWidthMayBeHalfAPixel_ButTileEdgesNeverAre(int sw, int sh)
        {
            var box = AspectRatioEnforcer.ComputeViewport(sw, sh, 2, 1);

            foreach (float requested in RequestedOrthos)
            {
                float ortho = CameraSetup.SnapOrthoSize(requested, box.height, SnapPPU);
                if (ortho > box.height / (2f * SnapPPU)) continue;

                float pixelsPerTexel = box.height / (2f * AssetsPPU * ortho);
                Assert.AreEqual(Mathf.Round(pixelsPerTexel * 2f), pixelsPerTexel * 2f, 1e-4f,
                    $"{sw}x{sh}, ortho {ortho:F6}: {pixelsPerTexel:F4} px per texel is not a " +
                    "multiple of 0.5. The snap ladder should only ever produce N/2.");

                float pixelsPerTile = pixelsPerTexel * AssetsPPU;
                Assert.AreEqual(Mathf.Round(pixelsPerTile), pixelsPerTile, 1e-4f,
                    $"{sw}x{sh}, ortho {ortho:F6}: tile edge at {pixelsPerTile:F4} px is fractional.");
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Source-level guards on the two constants the chain is calibrated to
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void CameraSetup_KeepsTheCalibratedPpuConstants()
        {
            string path = Path.Combine(Application.dataPath,
                "_Project", "Scripts", "Gameplay", "World", "Setup", "CameraSetup.cs");
            Assert.IsTrue(File.Exists(path), $"Production script not found at {path}");
            string src = File.ReadAllText(path);

            Assert.IsTrue(Regex.IsMatch(src, @"private\s+int\s+snapPPU\s*=\s*16\s*;"),
                "CameraSetup.snapPPU must stay 16. Raising it to 32 buys integer 32-PPU " +
                "texels at the cost of halving the zoom ladder (~12 rungs -> ~6, with a 2x " +
                "jump at the top). Measured 2026-08-22: half-pixel texels open no gaps, so " +
                "the trade is all cost. Read the comment above the field before changing this.");

            Assert.IsTrue(Regex.IsMatch(src, @"private\s+int\s+assetsPPU\s*=\s*32\s*;"),
                "CameraSetup.assetsPPU must match the tile PPU (32). The seam math in this " +
                "fixture and in SnapOrthoSize is calibrated to it.");
        }

        [Test]
        public void AspectRatioEnforcer_TargetMatchesDisplaySettings()
        {
            // Two files carry the 2:1 decision — the component's serialized
            // defaults and the preset list. If they drift apart, every shipped
            // preset silently starts letterboxing.
            string path = Path.Combine(Application.dataPath,
                "_Project", "Scripts", "Core", "AspectRatioEnforcer.cs");
            Assert.IsTrue(File.Exists(path), $"Production script not found at {path}");
            string src = File.ReadAllText(path);

            Assert.IsTrue(Regex.IsMatch(src, @"targetAspectWidth\s*=\s*2f\s*;"),
                "AspectRatioEnforcer.targetAspectWidth must stay 2 to match " +
                "DisplaySettings.TargetAspect and the shipped preset list.");
            Assert.IsTrue(Regex.IsMatch(src, @"targetAspectHeight\s*=\s*1f\s*;"),
                "AspectRatioEnforcer.targetAspectHeight must stay 1 to match " +
                "DisplaySettings.TargetAspect and the shipped preset list.");
            Assert.AreEqual(2f, DisplaySettings.TargetAspect,
                "DisplaySettings.TargetAspect drifted from the enforcer's serialized target.");
        }

        [Test]
        public void CameraBackgroundStaysBlack_SoAResidualSeamIsInvisible()
        {
            // Last line of defence: the seam reveals whatever sits behind the
            // tile mesh. Against black it is invisible even if one ever opens;
            // against Unity's default cyan it is a bright line.
            string path = Path.Combine(Application.dataPath,
                "_Project", "Scripts", "Gameplay", "World", "Setup", "CameraSetup.cs");
            string src = File.ReadAllText(path);

            Assert.IsTrue(Regex.IsMatch(src, @"forceSafeBackgroundColor\s*=\s*true\s*;"),
                "CameraSetup.forceSafeBackgroundColor must stay true.");
            Assert.IsTrue(Regex.IsMatch(src, @"safeBackgroundColor\s*=\s*Color\.black\s*;"),
                "CameraSetup.safeBackgroundColor must stay black.");
        }
    }
}
