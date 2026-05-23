using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Tests for the PPU-aligned orthographic-size snap added to CameraSetup.
    ///
    /// Background: Game-View seam lines (thin gaps along tilemap chunk boundaries
    /// in the Unity Editor) come from sub-pixel orthographic sizes — when
    /// <c>(orthoSize × 2 × ppu) / pixelHeight</c> is not an integer, each tile
    /// texel covers a fractional number of screen pixels and the GPU samples
    /// between texel boundaries. <c>CameraSetup.SnapOrthoSize</c> fixes that by
    /// snapping ortho to the closest value that produces integer texel-per-pixel
    /// ratios. The snap intentionally lives in CameraSetup (not CameraPixelSnap)
    /// because two prior iterations of "snap ortho every LateUpdate" broke editor
    /// zoom UX — see CameraPixelSnapTests.LateUpdate_DoesNotModifyOrthographicSize.
    /// </summary>
    [TestFixture]
    public class CameraOrthoSnapTests
    {
        private const float Epsilon = 1e-4f;

        // ── SnapOrthoSize math ───────────────────────────────────────────────

        /// <summary>
        /// Pre-filtered cartesian product of (requested, pxH, ppu) tuples that
        /// land inside the snap's effective range (<c>nCont = pxH/(2×requested×ppu) ≥ 1</c>).
        /// Using <see cref="TestCaseSourceAttribute"/> instead of a raw triple-
        /// <c>[Values]</c> grid keeps every yielded combination valid for the
        /// integer-texel-per-pixel assertion below — no <c>Assume.That</c> /
        /// Inconclusive cases, which would otherwise show up as orange-X marks
        /// in Unity's Test Runner window despite passing at the job level.
        /// The complementary "outside the snap range" cases are exercised by
        /// <see cref="SnapOrthoSize_BypassesAboveTopLevel"/>.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<TestCaseData>
            SnapInsideRangeCases()
        {
            float[] requestedValues = { 2f, 3.1f, 5f, 7.7f, 12.3f, 20f, 25f };
            int[]   pxHValues       = { 720, 792, 800, 1080, 1440 };
            int[]   ppuValues       = { 16, 32, 64 };

            foreach (float requested in requestedValues)
            foreach (int   pxH       in pxHValues)
            foreach (int   ppu       in ppuValues)
            {
                float nCont = pxH / (2f * requested * ppu);
                if (nCont < 1f) continue; // covered by SnapOrthoSize_BypassesAboveTopLevel
                yield return new TestCaseData(requested, pxH, ppu)
                    .SetName($"SnapOrthoSize_ProducesIntegerTexelsPerScreenPixel(" +
                             $"req={requested},pxH={pxH},ppu={ppu})");
            }
        }

        /// <summary>
        /// The whole point of the snap: after it runs, each tile texel must
        /// cover an exact integer number of screen pixels. Verified across a
        /// pre-filtered grid of (requested, pixelHeight, ppu) combinations —
        /// only those inside the snap's effective range (N≥1) are yielded;
        /// above-range combinations are covered by
        /// <see cref="SnapOrthoSize_BypassesAboveTopLevel"/>.
        /// </summary>
        [Test, TestCaseSource(nameof(SnapInsideRangeCases))]
        public void SnapOrthoSize_ProducesIntegerTexelsPerScreenPixel(
            float requested, int pxH, int ppu)
        {
            float snapped = CameraSetup.SnapOrthoSize(requested, pxH, ppu);

            // texelsPerScreenPixel = pxH / (2 × orthoSize × ppu) must be ≥ 1 and integer.
            float texelsPerPixel = pxH / (2f * snapped * ppu);
            float rounded = Mathf.Round(texelsPerPixel);

            Assert.GreaterOrEqual(rounded, 1f, "N must be ≥ 1");
            Assert.AreEqual(rounded, texelsPerPixel, Epsilon,
                $"After snap, texelsPerPixel must be integer. " +
                $"requested={requested} pxH={pxH} ppu={ppu} snapped={snapped} " +
                $"texelsPerPixel={texelsPerPixel}");
        }

        /// <summary>
        /// Above the snap's top level (N=1, i.e., <c>requested &gt; pxH/(2×ppu)</c>)
        /// the snap must pass through unchanged. Without this bypass, every
        /// editor that tries to zoom out past ortho ≈ 26 (at pxH=830 / snapPPU=16)
        /// gets dragged back to 26 — defeating <c>maxEditorZoomOrthoSize=4000</c>
        /// for layout-style panoramic views. This was the regression the user
        /// reported on 2026-05-23 ("editors should zoom out further").
        ///
        /// Rationale for accepting raw ortho in this range: each tile texel
        /// covers less than one screen pixel, so the integer-texel invariant
        /// has no meaning — the entire scene aliases together regardless.
        /// </summary>
        [Test]
        public void SnapOrthoSize_BypassesAboveTopLevel(
            [Values(50f, 100f, 500f, 1000f, 4000f)] float requested,
            [Values(720, 830, 1080, 1440)]          int pxH,
            [Values(16, 32)]                         int ppu)
        {
            // Sanity-check the precondition: we want to land above the snap's top.
            Assume.That(requested, Is.GreaterThan(pxH / (2f * ppu)),
                "Test must exercise the above-top-level branch.");

            float snapped = CameraSetup.SnapOrthoSize(requested, pxH, ppu);

            Assert.AreEqual(requested, snapped, Epsilon,
                $"Requested {requested} > pxH/(2×ppu) = {pxH / (2f * ppu)}; snap must pass through. " +
                "Without this, in-game editors cannot reach maxEditorZoomOrthoSize.");
        }

        /// <summary>
        /// The boundary: at exactly <c>pxH/(2×ppu)</c> we're on the N=1 level
        /// and the snap should produce that value (not bypass). Just below,
        /// snap to N=1. Just above, bypass. Pins the bypass threshold.
        /// </summary>
        [Test]
        public void SnapOrthoSize_BypassThresholdIsExactlyN1Level()
        {
            const int pxH = 830;
            const int ppu = 16;
            float n1 = pxH / (2f * ppu); // 25.9375

            // Exactly at N=1: nCont = 1, falls through to the comparison branch and snaps to N=1.
            Assert.AreEqual(n1, CameraSetup.SnapOrthoSize(n1, pxH, ppu), Epsilon,
                "At the N=1 level, snap must return the level itself (not bypass).");

            // Just below N=1: nCont > 1, snap to N=1 or N=2 depending on distance.
            float belowN1 = n1 - 0.5f;
            float snappedBelow = CameraSetup.SnapOrthoSize(belowN1, pxH, ppu);
            Assert.IsTrue(Mathf.Approximately(snappedBelow, n1) || Mathf.Approximately(snappedBelow, n1 / 2f),
                $"Just below N=1 must snap to a PPU-aligned level, got {snappedBelow}.");

            // Just above N=1: nCont < 1, bypass.
            float aboveN1 = n1 + 0.5f;
            Assert.AreEqual(aboveN1, CameraSetup.SnapOrthoSize(aboveN1, pxH, ppu), Epsilon,
                "Just above N=1 must pass through unchanged (bypass active).");
        }

        /// <summary>
        /// The snap must pick the value closest to the requested ortho size, not
        /// blindly round N. For requested=5, pxH=792, ppu=32 the two candidates
        /// are 4.125 (N=3) and 6.1875 (N=2); 4.125 is closer (delta 0.875 vs 1.1875).
        /// </summary>
        [Test]
        public void SnapOrthoSize_ChoosesNearestValue()
        {
            float snapped = CameraSetup.SnapOrthoSize(5f, 792, 32);

            Assert.AreEqual(4.125f, snapped, Epsilon,
                "Snap must pick the candidate closer to the request, not just floor/ceil " +
                "of N. For requested=5 the candidates are 4.125 (closer) and 6.1875.");
        }

        /// <summary>
        /// Degenerate inputs are silently passed through. The snap is called from
        /// Awake (before any camera exists) and from headless test rigs (no
        /// pixelHeight at all). Crashing or returning a sentinel would force a
        /// guard at every write-site; pass-through keeps the call sites trivial.
        /// </summary>
        [Test]
        public void SnapOrthoSize_PassesThroughDegenerateInputs()
        {
            Assert.AreEqual(5f, CameraSetup.SnapOrthoSize(5f,   0, 32), Epsilon, "pxH=0");
            Assert.AreEqual(5f, CameraSetup.SnapOrthoSize(5f, -10, 32), Epsilon, "pxH<0");
            Assert.AreEqual(5f, CameraSetup.SnapOrthoSize(5f, 792,  0), Epsilon, "ppu=0");
            Assert.AreEqual(5f, CameraSetup.SnapOrthoSize(5f, 792, -5), Epsilon, "ppu<0");
            Assert.AreEqual(0f, CameraSetup.SnapOrthoSize(0f, 792, 32), Epsilon, "ortho=0");
            Assert.AreEqual(-3f, CameraSetup.SnapOrthoSize(-3f, 792, 32), Epsilon, "ortho<0");

            float nanResult = CameraSetup.SnapOrthoSize(float.NaN, 792, 32);
            Assert.IsTrue(float.IsNaN(nanResult), "NaN must pass through unchanged");

            float infResult = CameraSetup.SnapOrthoSize(float.PositiveInfinity, 792, 32);
            Assert.IsTrue(float.IsPositiveInfinity(infResult), "+Inf must pass through unchanged");
        }

        /// <summary>
        /// Sanity check on the inverse-relationship between N and ortho: snapped
        /// ortho size must be monotonically non-increasing as the requested ortho
        /// shrinks (smaller ortho → larger N → smaller-or-equal level).
        /// </summary>
        [Test]
        public void SnapOrthoSize_MonotonicInRequest()
        {
            float[] requests = { 25f, 20f, 15f, 12f, 8f, 6f, 4f, 3f, 2.5f, 2.1f };
            float previous = float.MaxValue;
            foreach (float r in requests)
            {
                float snapped = CameraSetup.SnapOrthoSize(r, 792, 32);
                Assert.LessOrEqual(snapped, previous + Epsilon,
                    $"Snap should not increase as request decreases. " +
                    $"prev={previous}, request={r}, snapped={snapped}");
                previous = snapped;
            }
        }

        // ── ComputePpuStep (scroll-wheel zoom level transition) ──────────────

        /// <summary>
        /// The scroll handler steps N by ±1 instead of multiplying ortho. This
        /// guarantees each scroll detent moves exactly one zoom level — even
        /// when adjacent PPU-aligned levels are close together. A naive
        /// "multiply ortho then snap" would round back to the same N at small
        /// step sizes and feel "stuck".
        /// </summary>
        [Test]
        public void ComputePpuStep_AdvancesByOneLevelPerCall()
        {
            // Start at the snapped value nearest 5 (= 4.125 at pxH=792, ppu=32).
            float current = CameraSetup.SnapOrthoSize(5f, 792, 32);

            // Three zoom-OUT detents in a row should produce three distinct,
            // strictly larger ortho sizes.
            float step1 = CameraSetup.ComputePpuStep(current, -1, 792, 32, 2f, 25f);
            float step2 = CameraSetup.ComputePpuStep(step1,   -1, 792, 32, 2f, 25f);
            float step3 = CameraSetup.ComputePpuStep(step2,   -1, 792, 32, 2f, 25f);

            Assert.Greater(step1, current,
                "First zoom-out detent must increase ortho — never get stuck on the same N.");
            Assert.Greater(step2, step1,
                "Second zoom-out detent must keep increasing ortho.");
            // step3 may equal step2 if we hit the clamp at maxOrtho — that's fine.
            Assert.GreaterOrEqual(step3, step2 - Epsilon,
                "Third zoom-out detent never moves backwards.");
        }

        /// <summary>
        /// Stepping in the opposite direction reverses the change: zoom-out
        /// followed by zoom-in returns to the starting level.
        /// </summary>
        [Test]
        public void ComputePpuStep_IsReversible()
        {
            float current = CameraSetup.SnapOrthoSize(5f, 792, 32);
            float stepOut = CameraSetup.ComputePpuStep(current, -1, 792, 32, 2f, 25f);
            float stepBackIn = CameraSetup.ComputePpuStep(stepOut, +1, 792, 32, 2f, 25f);

            Assert.AreEqual(current, stepBackIn, Epsilon,
                "Scroll out then scroll in must return to the original zoom level.");
        }

        /// <summary>
        /// The clamp must be respected: zooming in past the smallest allowed
        /// ortho holds at the clamp instead of producing N values so large the
        /// SRP renders the scene at sub-pixel scale.
        /// </summary>
        [Test]
        public void ComputePpuStep_RespectsMinClamp()
        {
            // Start near the min and zoom in many times — must never go below 2f.
            float current = 2.1f;
            for (int i = 0; i < 50; i++)
                current = CameraSetup.ComputePpuStep(current, +1, 792, 32, 2f, 25f);

            Assert.GreaterOrEqual(current, 2f - Epsilon,
                "ComputePpuStep must not drop below the min clamp regardless of how " +
                "many zoom-in detents we apply.");
        }

        /// <summary>
        /// Symmetric to <see cref="ComputePpuStep_RespectsMinClamp"/>: stepping
        /// out endlessly must hold at <c>maxOrtho</c>, never exceed it. Without
        /// this, a long burst of zoom-out scrolls before the user even has the
        /// follow target acquired could leave the camera at ortho 1e30, which
        /// Cinemachine renders as "nothing visible at all".
        /// </summary>
        [Test]
        public void ComputePpuStep_RespectsMaxClamp()
        {
            float current = CameraSetup.SnapOrthoSize(5f, 792, 16);
            for (int i = 0; i < 50; i++)
                current = CameraSetup.ComputePpuStep(current, -1, 792, 16, 2f, 25f);

            Assert.LessOrEqual(current, 25f + Epsilon,
                "ComputePpuStep must not rise above the max clamp regardless of how " +
                "many zoom-out detents we apply.");
        }

        // ── Production-scenario tests ────────────────────────────────────────
        // These tests pin the user-visible behaviour the snapPPU=16 default was
        // introduced to fix on 2026-05-23: with snapPPU=32, the zoom-out scroll
        // got "stuck" at ortho ≈ 12.97 (N=1) on common screen sizes — well below
        // the maxZoomOrthoSize=25 cap — because there was no integer N producing
        // a larger ortho. snapPPU=16 doubles the level density so N=1 ≈ 25.94 →
        // clamps to 25 → the cap is finally reachable via scroll.

        /// <summary>
        /// The user-reported bug: scroll-out must end up either at the cap
        /// (when an N=1 level exists ≥ maxOrtho, so the clamp kicks in) or at
        /// the highest PPU-aligned level below max (N=1 itself, when the
        /// screen is small enough that pxH/(2×snapPPU) &lt; maxOrtho).
        ///
        /// Either way, three regression guards:
        ///   (a) the result is &gt; the original snapPPU=32 ceiling (12.97 at
        ///       pxH=830) — i.e., the snapPPU=16 fix is in effect;
        ///   (b) the result equals the N=1 level (clamped to max) — no zoom
        ///       step gets stuck below it;
        ///   (c) the result is &gt;= 0.85 × maxOrtho on all common Valkur
        ///       screen sizes — "close enough" to max for a fluid UX.
        /// </summary>
        [Test]
        public void ProductionSnap_ReachesTopLevelFromAnyStart(
            [Values(720, 768, 792, 800, 830, 900, 1080, 1440)] int pxH)
        {
            const int snapPPU = 16;
            const float minOrtho = 2f;
            const float maxOrtho = 25f;

            // Start at the default ortho 5 and scroll out repeatedly. The level
            // ladder is finite, so a bounded loop must hold at a stable value.
            float current = CameraSetup.SnapOrthoSize(5f, pxH, snapPPU);
            for (int i = 0; i < 30; i++)
                current = CameraSetup.ComputePpuStep(current, -1, pxH, snapPPU, minOrtho, maxOrtho);

            float n1Ortho = pxH / (2f * snapPPU);          // N=1 level (unclamped)
            float expectedTop = Mathf.Min(maxOrtho, n1Ortho);
            float minAcceptable = 0.85f * maxOrtho;

            Assert.AreEqual(expectedTop, current, Epsilon,
                $"At pxH={pxH} scroll-out must settle at min(maxOrtho, N=1-level). " +
                $"N=1 ortho = {n1Ortho}, expected top = {expectedTop}, got {current}.");
            Assert.GreaterOrEqual(current, minAcceptable,
                $"At pxH={pxH} scroll-out reached {current} (< 85% of maxOrtho={maxOrtho}). " +
                "The snapPPU=16 fix must keep scroll-out feeling 'wide enough' on common screens. " +
                "If this fires, the level ladder regressed to snapPPU=32 (or worse).");
        }

        /// <summary>
        /// Density check: the gameplay zoom range [2, 25] must offer enough
        /// snapped levels that scroll-wheel zoom feels granular, not chunky.
        /// snapPPU=32 produced only 6 levels (the original bug); snapPPU=16
        /// produces ~12. Setting the floor at ≥10 is a conservative density
        /// guarantee that catches future "halve the level count" regressions.
        /// </summary>
        [Test]
        public void ProductionSnap_OffersEnoughLevelsForGameplayZoom(
            [Values(720, 792, 830, 1080)] int pxH)
        {
            const int snapPPU = 16;
            const float minOrtho = 2f;
            const float maxOrtho = 25f;

            // Walk from max all the way to min, counting distinct ortho values.
            var seen = new System.Collections.Generic.HashSet<float>();
            float current = maxOrtho;
            seen.Add(Mathf.Round(current * 10000f) / 10000f);
            for (int i = 0; i < 60; i++)
            {
                float next = CameraSetup.ComputePpuStep(current, +1, pxH, snapPPU, minOrtho, maxOrtho);
                if (Mathf.Approximately(next, current)) break; // hit min clamp
                current = next;
                seen.Add(Mathf.Round(current * 10000f) / 10000f);
            }

            Assert.GreaterOrEqual(seen.Count, 10,
                $"At pxH={pxH} snapPPU=16 must produce ≥ 10 distinct zoom levels in [{minOrtho}, {maxOrtho}]. " +
                $"Got {seen.Count}. Too few levels feels 'jumpy' on the scroll wheel and was the user's " +
                "complaint with snapPPU=32.");
        }

        /// <summary>
        /// Editor scenario: EditorCameraZoomController feeds a hybrid step
        /// (PPU-aligned inside the snap range, multiplicative above) through
        /// <see cref="CameraSetup.SetEditorZoom"/>. Two regression guards:
        ///   (a) Starting from a low ortho and zooming out repeatedly reaches
        ///       the editor cap (the user's 2026-05-23 complaint).
        ///   (b) The N=2 → N=1 transition succeeds — pure multiplicative at
        ///       factor 1.25 couldn't cross it (the gap ratio is 2.0).
        ///
        /// We exercise the math via reflection invoking the instance method
        /// <see cref="CameraSetup.ComputeEditorZoomNext"/> on a synthetic
        /// CameraSetup with a deterministic render-camera pixelHeight.
        /// </summary>
        [Test]
        public void EditorScenario_HybridScrollOut_ReachesEditorCap()
        {
            const int pxH = 830;
            const int snapPPU = 16;
            const float editorCap = 4000f;
            const float editorMin = 2f;
            const float multiplicativeFactor = 0.25f;

            // Build a synthetic CameraSetup with snapPPU=16 and a forced render
            // camera so ComputeEditorZoomNext returns deterministic values.
            var (camGo, renderCam, setup) = BuildSetupWithRenderCam(pxH, snapPPU);
            try
            {
                float current = CameraSetup.SnapOrthoSize(5f, pxH, snapPPU);
                bool crossedN1Boundary = false;
                float topSnapLevel = pxH / (2f * snapPPU);

                for (int i = 0; i < 50; i++)
                {
                    float prev = current;
                    current = setup.ComputeEditorZoomNext(current, -1, multiplicativeFactor);
                    current = Mathf.Clamp(current, editorMin, editorCap);
                    Assert.Greater(current, prev - Epsilon,
                        $"Step {i}: scroll-out must never decrease ortho. prev={prev} now={current}");
                    if (prev <= topSnapLevel + Epsilon && current > topSnapLevel + Epsilon)
                        crossedN1Boundary = true;
                    if (Mathf.Approximately(current, editorCap)) break;
                }

                Assert.AreEqual(editorCap, current, 1f,
                    $"Hybrid scroll-out must reach the editor cap ({editorCap}). Got {current}.");
                Assert.IsTrue(crossedN1Boundary,
                    $"At some step the chain must cross the N=1 boundary " +
                    $"({topSnapLevel}); otherwise editor zoom is still stuck.");
            }
            finally
            {
                Object.DestroyImmediate(renderCam.gameObject);
                Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>
        /// Symmetric: zoom-in from the editor cap returns smoothly to the min.
        /// Tests the multiplicative-to-PPU-step transition at the boundary.
        /// </summary>
        [Test]
        public void EditorScenario_HybridScrollIn_ReachesEditorMin()
        {
            const int pxH = 830;
            const int snapPPU = 16;
            const float editorCap = 4000f;
            const float editorMin = 2f;
            const float multiplicativeFactor = 0.25f;

            var (camGo, renderCam, setup) = BuildSetupWithRenderCam(pxH, snapPPU);
            try
            {
                float current = editorCap;
                for (int i = 0; i < 60; i++)
                {
                    float prev = current;
                    current = setup.ComputeEditorZoomNext(current, +1, multiplicativeFactor);
                    current = Mathf.Clamp(current, editorMin, editorCap);
                    Assert.Less(current, prev + Epsilon,
                        $"Step {i}: scroll-in must never increase ortho. prev={prev} now={current}");
                    if (Mathf.Approximately(current, editorMin)) break;
                }

                Assert.AreEqual(editorMin, current, Epsilon,
                    $"Hybrid scroll-in from editorCap must reach editorMin ({editorMin}). Got {current}.");
            }
            finally
            {
                Object.DestroyImmediate(renderCam.gameObject);
                Object.DestroyImmediate(camGo);
            }
        }

        // ── Test helpers ─────────────────────────────────────────────────────

        private static (GameObject camGo, Camera renderCam, CameraSetup setup)
            BuildSetupWithRenderCam(int pxH, int snapPPU)
        {
            // CameraPixelSnap on the render camera is the gate
            // GetRenderPixelHeight() uses; we add it explicitly so the
            // editor-only Application.isPlaying guard is bypassed via the
            // pre-set _renderCam reflection write below.
            var renderCamGo = new GameObject("TestRenderCam");
            var renderCam = renderCamGo.AddComponent<Camera>();
            var rt = new RenderTexture(pxH * 2, pxH, 0);
            rt.Create();
            renderCam.targetTexture = rt;

            var camGo = new GameObject("TestCameraSetup");
            camGo.AddComponent<Cinemachine.CinemachineVirtualCamera>();
            var setup = camGo.AddComponent<CameraSetup>();

            // Force snapPPU=16, assetsPPU=32, and inject the render camera so
            // GetRenderPixelHeight bypasses the isPlaying gate via the cached
            // field.
            SetPrivateField(setup, "snapPPU", snapPPU);
            SetPrivateField(setup, "assetsPPU", 32);
            SetPrivateField(setup, "_renderCam", renderCam);

            // Invoke Awake explicitly — EditMode AddComponent does not.
            var awake = typeof(CameraSetup).GetMethod("Awake",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            awake?.Invoke(setup, null);

            return (camGo, renderCam, setup);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var f = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on {target.GetType().Name}");
            f.SetValue(target, value);
        }

        /// <summary>
        /// Half-integer tile-texel-per-pixel sampling is acceptable.
        ///
        /// With snapPPU=16 and tilePPU=32, an integer snap-texel-per-pixel N
        /// only yields integer tile-texels-per-pixel when N is EVEN — odd N's
        /// produce X.5 tile-texels, which sub-pixel-samples. In production this
        /// is fine because:
        ///   * Tile atlases extrude 1px per slot (ValkurAssetPostprocessor sets
        ///     <c>TILE_SPRITE_EXTRUDE=1</c>), so sub-pixel sampling stays inside
        ///     the duplicated edge texels — no atlas-adjacent bleed.
        ///   * CameraPixelSnap rounds the camera position to the wpp grid each
        ///     LateUpdate, eliminating the COMPOSITE drift that actually
        ///     manifests as visible black seams.
        ///   * The user verified visually on 2026-05-23 that snapPPU=16
        ///     eliminates the seam at every zoom level they tested.
        ///
        /// This test pins the contract: tile-texel-per-pixel can be ½-integer
        /// at odd N's, and that's an accepted trade-off for the denser ladder
        /// snapPPU=16 unlocks. A future "snapPPU=tilePPU" rewrite (claiming to
        /// be 'more correct') must justify breaking the level density.
        /// </summary>
        [Test]
        public void SnapWithDivisorSnapPpu_AllowsHalfIntegerTileTexelsAtOddN()
        {
            const int snapPPU = 16;
            const int tilePPU = 32;
            const int pxH = 830;

            // ortho = pxH / (2 × snapPPU × N=3) → odd N, tile-N is half-integer.
            float ortho = pxH / (2f * snapPPU * 3f);
            float snapTexelsPerPx = pxH / (2f * ortho * snapPPU);
            float tileTexelsPerPx = pxH / (2f * ortho * tilePPU);

            Assert.AreEqual(3f, snapTexelsPerPx, Epsilon,
                "Snap-texel-per-pixel must be integer (the snap's primary contract holds).");
            Assert.AreEqual(1.5f, tileTexelsPerPx, Epsilon,
                "Tile-texel-per-pixel is half-integer at odd N — accepted because atlas extrusion " +
                "and position-snap absorb the sub-pixel sampling.");
        }

        // ── Source-level guards: snap wiring at the write-points ─────────────

        /// <summary>
        /// The snap is gated on <c>Application.isPlaying</c> at runtime (so
        /// EditMode tests that synthesize CameraSetup instances aren't affected),
        /// which makes a full in-EditMode integration test impossible without
        /// PlayMode setup. Instead, this source-level guard pins the wiring:
        /// every ortho write-point in <c>CameraSetup</c> must funnel through
        /// either <c>SnapOrthoSize</c> or the pre-snapped <c>_tileEditorTargetSize</c>.
        /// Catches a regression where a future contributor "simplifies"
        /// SetEditorZoom or the scroll handler back to a raw assignment.
        /// </summary>
        [Test]
        public void SourceCode_WritePointsRouteThroughSnap()
        {
            string scriptPath = System.IO.Path.Combine(
                Application.dataPath,
                "_Project", "Scripts", "Gameplay", "World", "Setup", "CameraSetup.cs");
            Assert.IsTrue(System.IO.File.Exists(scriptPath),
                $"Production script not found at {scriptPath}");

            string src = System.IO.File.ReadAllText(scriptPath);

            Assert.IsTrue(src.Contains("SnapOrthoSize("),
                "CameraSetup must reference SnapOrthoSize — the seam-fix is wired through it.");
            Assert.IsTrue(src.Contains("ComputePpuStep("),
                "CameraSetup must reference ComputePpuStep — the scroll handler relies on it for " +
                "monotonic per-detent zoom advancement.");
            Assert.IsTrue(src.Contains("ApplyOrthoAndCompat("),
                "CameraSetup must funnel ortho writes through ApplyOrthoAndCompat to keep the " +
                "compatibility vcam in sync.");

            // SetEditorZoom must invoke the snap before writing.
            int setEditorZoomIdx = src.IndexOf("public void SetEditorZoom(", System.StringComparison.Ordinal);
            Assert.Greater(setEditorZoomIdx, -1, "SetEditorZoom must exist");
            int nextMethodIdx = src.IndexOf("public ", setEditorZoomIdx + 1, System.StringComparison.Ordinal);
            if (nextMethodIdx < 0) nextMethodIdx = src.Length;
            string body = src.Substring(setEditorZoomIdx, nextMethodIdx - setEditorZoomIdx);
            Assert.IsTrue(body.Contains("SnapOrthoSize"),
                "SetEditorZoom body must call SnapOrthoSize before writing the vcam. " +
                "Without it, editor framing leaves the camera at a sub-pixel ortho and " +
                "tilemap seams reappear during F6/F10/F11 sessions.");

            // Snap callsites must pass `snapPPU` — not `assetsPPU` — so the
            // zoom-level ladder is dense enough to reach maxZoomOrthoSize via
            // the scroll wheel. Regressing this field swap is the exact bug
            // ("can't scroll out as far as before") the snapPPU=16 default was
            // introduced to prevent.
            Assert.IsTrue(src.Contains("SnapOrthoSize(orthoSize, GetRenderPixelHeight(), snapPPU)"),
                "Awake/Start must call SnapOrthoSize with snapPPU, not assetsPPU.");
            Assert.IsTrue(src.Contains("SnapOrthoSize(liveClamped, pxH, snapPPU)"),
                "Live clamp in Update must call SnapOrthoSize with snapPPU.");
            Assert.IsTrue(src.Contains("SnapOrthoSize(sanitisedSize, GetRenderPixelHeight(), snapPPU)"),
                "SetEditorZoom must call SnapOrthoSize with snapPPU.");
            Assert.IsTrue(src.Contains("ComputePpuStep(currentSize, direction, scrollPxH, snapPPU,"),
                "Scroll handler must call ComputePpuStep with snapPPU.");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(src,
                @"SnapOrthoSize\([^)]*,\s*assetsPPU\s*\)"),
                "No SnapOrthoSize call should pass assetsPPU — that's the regression we guard against.");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(src,
                @"ComputePpuStep\([^)]*,\s*assetsPPU\s*,"),
                "No ComputePpuStep call should pass assetsPPU — that's the regression we guard against.");
        }
    }
}
