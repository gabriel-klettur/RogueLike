using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// Regression guard for the tilemap "horizontal seam line" Game-View
    /// composite drift bug.
    ///
    /// Bug history (do not regress):
    ///   - 2026-05-16: A thin horizontal line appeared across the tilemap
    ///     in Game View (visible in the editor's Game tab but NOT in
    ///     screenshots captured directly from the camera). Diagnostic via
    ///     Unity MCP confirmed:
    ///       * <c>Camera.pixelRect.height</c> was 819.5 — a HALF pixel.
    ///       * The fractional pixelRect height arose from
    ///         <see cref="AspectRatioEnforcer.UpdateViewport"/> writing
    ///         a fractional <c>cam.rect</c> (e.g. 0.972 of Screen.height).
    ///       * Unity's Game View composites that fractional viewport onto
    ///         the GUI canvas with sub-pixel scaling — the scaling step is
    ///         where the seam line manifests.
    ///   - Fix: round the viewport rect so the resulting <c>pixelRect</c>
    ///     has INTEGER dimensions (width, height, x, y). The letterbox /
    ///     pillarbox bars stay perfectly black either way, but the camera
    ///     output composites onto whole pixels — no sub-pixel scaling, no
    ///     seam line.
    ///
    /// Two layers of coverage, and the split matters:
    ///   * The live-component tests drive the real <c>UpdateViewport</c> at
    ///     whatever size the Game View currently is. They prove the component
    ///     is wired up, but they can only ever see ONE resolution.
    ///   * The <see cref="AspectRatioEnforcer.ComputeViewport"/> sweeps below
    ///     cover every resolution Valkur ships on plus the awkward ones. This
    ///     layer exists because the 2026-08-22 aspect-drift bug survived the
    ///     original suite precisely for lack of it — the math read
    ///     <c>Screen.*</c> directly, so 1366x768 was untestable.
    /// </summary>
    [TestFixture]
    public class AspectRatioEnforcerIntegerPixelRectTests
    {
        private GameObject _camGo;
        private Camera _cam;
        private AspectRatioEnforcer _enforcer;

        /// <summary>
        /// Window sizes the sweeps run over: the shipped presets, the common
        /// desktop modes, the historically broken ones, and degenerate edges.
        /// 1366x768 is the regression case — it produced a 1366x682 viewport
        /// (aspect 2.002933) under the pre-2026-08-22 independent rounding.
        /// </summary>
        private static readonly int[][] SweepResolutions =
        {
            new[] { 1280,  640 }, new[] { 1600,  800 }, new[] { 1920,  960 },
            new[] { 2560, 1280 }, new[] { 3200, 1600 }, new[] { 3840, 1920 },
            new[] { 1366,  768 }, new[] { 1920, 1080 }, new[] { 1600,  900 },
            new[] { 2560, 1440 }, new[] { 1280,  720 }, new[] { 1920, 1200 },
            new[] { 1440,  900 }, new[] { 1680, 1050 }, new[] { 2560, 1600 },
            new[] { 1024,  768 }, new[] { 3840, 2160 }, new[] { 1552,  773 },
            new[] { 1553,  773 }, new[] { 1551,  772 }, new[] {  801,  401 },
            new[] {    3,    1 }, new[] {    1,    1 }, new[] {    0,    0 },
        };

        private static readonly BindingFlags PrivInst =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp()
        {
            _camGo = new GameObject("TestAspectRatioCamera");
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _enforcer = _camGo.AddComponent<AspectRatioEnforcer>();

            // EditMode test runner does NOT call Awake on AddComponent — we
            // have to invoke it explicitly so AspectRatioEnforcer caches its
            // Camera reference. Without this, UpdateViewport() NREs on
            // _cam.rect = rect because _cam was never assigned.
            var awake = typeof(AspectRatioEnforcer).GetMethod("Awake", PrivInst);
            Assert.IsNotNull(awake, "AspectRatioEnforcer.Awake must exist (private instance method).");
            awake.Invoke(_enforcer, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        private static void InvokeUpdateViewport(AspectRatioEnforcer e)
        {
            var m = typeof(AspectRatioEnforcer).GetMethod("UpdateViewport", PrivInst);
            m?.Invoke(e, null);
        }

        // ────────────────────────────────────────────────────────────────────
        // Hard invariant — produced rect ALWAYS lands on integer pixel rows/cols
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void UpdateViewport_ProducesIntegerPixelRect_AtCurrentScreenSize()
        {
            // Use Screen.* as-is (whatever the editor's Game View is set to).
            // This is the most common path for the bug to fire in real play.
            InvokeUpdateViewport(_enforcer);

            var rect = _cam.rect;
            float pxW = rect.width  * Screen.width;
            float pxH = rect.height * Screen.height;
            float pxX = rect.x      * Screen.width;
            float pxY = rect.y      * Screen.height;

            Assert.That(pxW, Is.EqualTo(Mathf.Round(pxW)).Within(0.01f),
                $"rect.width * Screen.width must be integer. Got {pxW}. " +
                "Non-integer width causes Game View composite drift.");
            Assert.That(pxH, Is.EqualTo(Mathf.Round(pxH)).Within(0.01f),
                $"rect.height * Screen.height must be integer. Got {pxH}. " +
                "Non-integer height was the 2026-05-16 'horizontal seam line' bug — " +
                "Unity's Game View composites the half-pixel offset with sub-pixel " +
                "scaling, which produces a thin horizontal line across the tilemap.");
            Assert.That(pxX, Is.EqualTo(Mathf.Round(pxX)).Within(0.01f),
                $"rect.x * Screen.width must be integer. Got {pxX}.");
            Assert.That(pxY, Is.EqualTo(Mathf.Round(pxY)).Within(0.01f),
                $"rect.y * Screen.height must be integer. Got {pxY}. " +
                "Non-integer Y offset is the partner of the height bug — both " +
                "axes must be integer for the composite to be drift-free.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Hard invariant — the produced viewport is EXACTLY the target ratio
        //
        // Bug history (do not regress):
        //   - 2026-08-22: the integer-pixel fix above rounded each axis
        //     independently, so a 1366x768 window produced a 1366x682 viewport
        //     whose aspect is 2.002933, not 2. Integer pixels, wrong ratio.
        //     CameraSetup.SnapOrthoSize only guarantees whole screen pixels per
        //     art texel on the VERTICAL axis (it solves ortho from
        //     pixelHeight); the horizontal axis inherits that guarantee purely
        //     through Camera.aspect. A 0.3% aspect error therefore leaves tile
        //     quad edges landing mid-pixel across the width of the screen, and
        //     the (deliberately black) camera background shows through as
        //     VERTICAL seam lines over the tilemap.
        //   - Fix: quantise the viewport to k*p by k*q, where p:q is the target
        //     aspect reduced to an exact integer ratio. One scalar drives both
        //     axes, so the ratio is bit-exact and the pixel rect stays integer.
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void UpdateViewport_ProducesExactlyTheTargetAspect()
        {
            InvokeUpdateViewport(_enforcer);

            var rect = _cam.rect;
            float pxW = Mathf.Round(rect.width  * Screen.width);
            float pxH = Mathf.Round(rect.height * Screen.height);

            Assert.Greater(pxW, 0f, "Viewport width collapsed to zero.");
            Assert.Greater(pxH, 0f, "Viewport height collapsed to zero.");

            // Default target is 2:1 (see AspectRatioEnforcer's serialized fields).
            Assert.AreEqual(2f, pxW / pxH,
                $"Viewport is {pxW}x{pxH}, aspect {pxW / pxH:F8}. It must be EXACTLY 2:1. " +
                "Any drift breaks the horizontal half of SnapOrthoSize's whole-pixel-per-texel " +
                "guarantee and draws vertical seam lines across the tilemap.");
        }

        [Test]
        public void UpdateViewport_ViewportFitsInsideTheScreen()
        {
            InvokeUpdateViewport(_enforcer);

            var rect = _cam.rect;
            Assert.GreaterOrEqual(rect.x, 0f);
            Assert.GreaterOrEqual(rect.y, 0f);
            Assert.LessOrEqual(rect.x + rect.width,  1f + 1e-6f,
                "Viewport spills past the right edge — the quantised box must fit the window.");
            Assert.LessOrEqual(rect.y + rect.height, 1f + 1e-6f,
                "Viewport spills past the top edge — the quantised box must fit the window.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Resolution sweeps over the pure math
        //
        // This is the layer the 2026-08-22 bug slipped through. UpdateViewport
        // used to read Screen.* directly, so EditMode could only ever assert
        // against the Game View's current size and 1366x768 was unreachable.
        // ComputeViewport is now a pure function; every resolution is testable.
        // ────────────────────────────────────────────────────────────────────

        private static System.Collections.IEnumerable Resolutions()
        {
            foreach (var r in SweepResolutions)
                yield return new TestCaseData(r[0], r[1]).SetName($"{r[0]}x{r[1]}");
        }

        [TestCaseSource(nameof(Resolutions))]
        public void ComputeViewport_IsExactlyTwoToOne_AtEveryResolution(int sw, int sh)
        {
            var box = AspectRatioEnforcer.ComputeViewport(sw, sh, 2, 1);

            Assert.Greater(box.width,  0, "Viewport width collapsed.");
            Assert.Greater(box.height, 0, "Viewport height collapsed.");
            Assert.AreEqual(box.height * 2, box.width,
                $"{sw}x{sh} produced a {box.width}x{box.height} viewport — not exactly 2:1. " +
                "The pre-fix code produced 1366x682 here (aspect 2.002933), which drifts " +
                "Camera.aspect away from SnapOrthoSize's vertical guarantee and draws " +
                "vertical seam lines across the tilemap.");

            // Bit-exact float division too: this is the value Camera.aspect ends
            // up holding, and the historical drift (0.0029) was well inside any
            // epsilon someone would have reached for.
            Assert.AreEqual(2f, (float)box.width / box.height,
                $"{sw}x{sh}: Camera.aspect would be {(float)box.width / box.height:F8}.");
        }

        [TestCaseSource(nameof(Resolutions))]
        public void ComputeViewport_FitsInsideTheWindowAndIsCentred(int sw, int sh)
        {
            var box = AspectRatioEnforcer.ComputeViewport(sw, sh, 2, 1);
            int w = Mathf.Max(1, sw), h = Mathf.Max(1, sh);

            Assert.GreaterOrEqual(box.x, 0, $"{sw}x{sh}: negative x offset.");
            Assert.GreaterOrEqual(box.y, 0, $"{sw}x{sh}: negative y offset.");

            // A window smaller than one ratio unit (2x1) cannot hold an exact
            // box at all. The ratio wins there by design — see ComputeViewport
            // — so "fits" is only meaningful above that floor.
            if (w < 2 || h < 1) return;

            Assert.LessOrEqual(box.x + box.width,  w, $"{sw}x{sh}: viewport spills past the right edge.");
            Assert.LessOrEqual(box.y + box.height, h, $"{sw}x{sh}: viewport spills past the top edge.");

            // Bars split evenly; integer division may leave one extra pixel on
            // the far side, never more.
            Assert.LessOrEqual(Mathf.Abs((w - box.width  - box.x) - box.x), 1,
                $"{sw}x{sh}: horizontal bars differ by more than a pixel.");
            Assert.LessOrEqual(Mathf.Abs((h - box.height - box.y) - box.y), 1,
                $"{sw}x{sh}: vertical bars differ by more than a pixel.");
        }

        [TestCaseSource(nameof(Resolutions))]
        public void ComputeViewport_IsMaximal_NoLargerExactBoxWouldFit(int sw, int sh)
        {
            var box = AspectRatioEnforcer.ComputeViewport(sw, sh, 2, 1);
            int w = Mathf.Max(1, sw), h = Mathf.Max(1, sh);

            // The next rung up must not fit; otherwise we're wasting screen.
            int nextW = box.width + 2, nextH = box.height + 1;
            Assert.IsTrue(nextW > w || nextH > h,
                $"{sw}x{sh}: returned {box.width}x{box.height} but {nextW}x{nextH} " +
                "also fits — the viewport is smaller than it needs to be.");
        }

        [Test]
        public void ComputeViewport_ShippedPresetsUseTheWholeWindow()
        {
            // A curated preset exists precisely so there are no bars. If one of
            // them ever letterboxes, either the preset list or the target aspect
            // drifted apart from the other.
            for (int i = 1; i < DisplaySettings.Presets.Length; i++)
            {
                var p = DisplaySettings.Presets[i];
                var box = AspectRatioEnforcer.ComputeViewport(p.Width, p.Height, 2, 1);
                Assert.AreEqual(new RectInt(0, 0, p.Width, p.Height), box,
                    $"Preset {p.Label} should need no letterbox at all, got " +
                    $"{box.width}x{box.height} at ({box.x},{box.y}).");
            }
        }

        [Test]
        public void ReduceRatio_ProducesTheExactIntegerForm()
        {
            AspectRatioEnforcer.ReduceRatio(2f, 1f, out int p, out int q);
            Assert.AreEqual(2, p); Assert.AreEqual(1, q);

            AspectRatioEnforcer.ReduceRatio(16f, 9f, out p, out q);
            Assert.AreEqual(16, p); Assert.AreEqual(9, q);

            AspectRatioEnforcer.ReduceRatio(1.5f, 1f, out p, out q);
            Assert.AreEqual(3, p); Assert.AreEqual(2, q);

            // Degenerate input must not produce a zero divisor.
            AspectRatioEnforcer.ReduceRatio(0f, 0f, out p, out q);
            Assert.GreaterOrEqual(p, 1); Assert.GreaterOrEqual(q, 1);
        }

        [Test]
        public void ComputeViewport_HonoursANonTwoToOneTarget()
        {
            // The component exposes the target aspect as serialized fields, so
            // the math must stay exact for whatever a designer types in.
            AspectRatioEnforcer.ReduceRatio(16f, 9f, out int p, out int q);
            var box = AspectRatioEnforcer.ComputeViewport(1920, 1080, p, q);
            Assert.AreEqual(new RectInt(0, 0, 1920, 1080), box);
            Assert.AreEqual(box.width * 9, box.height * 16,
                "16:9 target must stay exact in integer pixels.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Source-level guard against accidentally reintroducing fractional math
        //
        // Catches the most common regression at the source level: someone
        // simplifies UpdateViewport back to "rect.height = scaleHeight"
        // (the original buggy form) without realising it produces fractional
        // pixel rects on most screen resolutions.
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void SourceCode_UpdateViewport_RoundsToIntegerPixels()
        {
            string scriptPath = System.IO.Path.Combine(
                Application.dataPath,
                "_Project", "Scripts", "Core", "AspectRatioEnforcer.cs");
            Assert.IsTrue(System.IO.File.Exists(scriptPath),
                $"Production script not found at {scriptPath}");

            string src = System.IO.File.ReadAllText(scriptPath);

            // Must round explicitly — the regression-prone form
            // "rect.height = scaleHeight" without any rounding caused the bug.
            Assert.IsTrue(src.Contains("RoundToInt") || src.Contains("Mathf.Round"),
                "AspectRatioEnforcer must use Mathf.RoundToInt (or Mathf.Round) on " +
                "the pixel-count math. Without explicit rounding the rect ends up " +
                "fractional (e.g. 0.972 → 819.5 px on a 843-px tall screen) and " +
                "the Game View composite produces the 'horizontal seam line' bug.");
        }
    }
}
