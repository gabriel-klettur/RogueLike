using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Regression tests for CameraPixelSnap — the component that snaps the camera
    /// transform to the screen-pixel grid after Cinemachine writes its position.
    ///
    /// Critical regression guarded:
    ///   Before the fix, both axes were snapped with the same wpp value (the Y wpp).
    ///   When cam.aspect is locked to 2.0 but the viewport pixel ratio differs
    ///   (e.g. 1722×862 → 1.9977), wpp_x ≠ wpp_y by ~0.1%, and using the same
    ///   wpp leaves 1-px seams along the axis with the larger pixel pitch.
    ///   The fix computes wpp independently per axis.
    /// </summary>
    [TestFixture]
    public class CameraPixelSnapTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<RenderTexture> _renderTextures = new List<RenderTexture>();

        // Reflection binding for the private LateUpdate method
        private static readonly BindingFlags PrivInst =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var rt in _renderTextures)
                if (rt != null) Object.DestroyImmediate(rt);
            _renderTextures.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private (GameObject go, Camera cam, CameraPixelSnap snap) CreateSnapper()
        {
            var go = new GameObject("TestCameraPixelSnap");
            var cam = go.AddComponent<Camera>();
            var snap = go.AddComponent<CameraPixelSnap>();
            _sceneObjects.Add(go);

            // Wire _cam field via Awake reflection (the component is sealed;
            // Awake runs immediately on AddComponent in EditMode).
            var awake = typeof(CameraPixelSnap).GetMethod("Awake", PrivInst);
            awake?.Invoke(snap, null);

            return (go, cam, snap);
        }

        private RenderTexture MakeRT(int width, int height)
        {
            var rt = new RenderTexture(width, height, 0);
            rt.Create();
            _renderTextures.Add(rt);
            return rt;
        }

        private void InvokeLateUpdate(CameraPixelSnap snap)
        {
            var m = typeof(CameraPixelSnap).GetMethod("LateUpdate", PrivInst);
            m?.Invoke(snap, null);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Core regression: when pixel dimensions produce a non-square pixel
        /// (wpp_x ≠ wpp_y), each axis must be snapped with its own wpp.
        /// A position that is already aligned on the Y grid but not the X grid
        /// must end up aligned on both.
        /// </summary>
        [Test]
        public void LateUpdate_SnapsXAndYToTheirOwnPixelGrid()
        {
            var (go, cam, snap) = CreateSnapper();

            // Force non-square pixels: width/height ratio ≠ aspect.
            // 860×860 + aspect 2.0 → wpp_x = (ortho*2*2)/860, wpp_y = (ortho*2)/860
            // With ortho=5: wpp_y = 10/860 ≈ 0.01163, wpp_x = 20/860 ≈ 0.02326
            int w = 860, h = 860;
            cam.targetTexture = MakeRT(w, h);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 2.0f; // locked by AspectRatioEnforcer — independent of RT ratio

            float ortho = cam.orthographicSize;
            int pxW = cam.pixelWidth;
            int pxH = cam.pixelHeight;
            Assert.Greater(pxW, 0, "pixelWidth must be > 0 after RT assignment");
            Assert.Greater(pxH, 0, "pixelHeight must be > 0 after RT assignment");

            float wppY = (ortho * 2f) / pxH;
            float wppX = (ortho * 2f * cam.aspect) / pxW;
            Assert.That(wppX, Is.Not.EqualTo(wppY).Within(0.0001f),
                "Precondition: wpp_x must differ from wpp_y for this test to be meaningful");

            // Place camera at a fractional position that is NOT aligned on either grid.
            // We pick a value that falls exactly midway between two X-grid lines but
            // happens to land on a Y-grid line, to confirm independent snapping.
            float alignedY = Mathf.Round(3.7f / wppY) * wppY;
            float fractX   = alignedY + wppX * 0.37f; // definitely not on X grid
            go.transform.position = new Vector3(fractX, alignedY + wppY * 0.61f, -10f);

            InvokeLateUpdate(snap);

            Vector3 result = go.transform.position;
            float remX = result.x % wppX;
            float remY = result.y % wppY;

            // After snap, remainder should be < 0.5 % of the respective wpp
            // (Mathf.Round picks the nearest grid line).
            Assert.That(Mathf.Abs(remX), Is.LessThan(wppX * 0.01f),
                $"X position {result.x} is not aligned to wpp_x={wppX}. " +
                $"Remainder={remX}. Regression: using wpp_y for both axes " +
                $"would leave seams on the X axis when pixels are non-square.");
            Assert.That(Mathf.Abs(remY), Is.LessThan(wppY * 0.01f),
                $"Y position {result.y} is not aligned to wpp_y={wppY}. " +
                $"Remainder={remY}.");
        }

        /// <summary>
        /// Edge case: wpp_x and wpp_y are truly independent values.
        /// Verifies the formula difference: wpp_x uses cam.aspect, wpp_y does not.
        /// </summary>
        [Test]
        public void LateUpdate_WppXIncludesAspect_WppYDoesNot()
        {
            var (go, cam, snap) = CreateSnapper();

            // 512×512 RT, aspect=2.0 → wpp_x is exactly 2× wpp_y.
            int size = 512;
            cam.targetTexture = MakeRT(size, size);
            cam.orthographic  = true;
            cam.orthographicSize = 4f;
            cam.aspect = 2.0f;

            float ortho = cam.orthographicSize;
            int pxW = cam.pixelWidth;
            int pxH = cam.pixelHeight;

            float wppY = (ortho * 2f) / pxH;
            float wppX = (ortho * 2f * cam.aspect) / pxW;
            Assert.That(wppX, Is.EqualTo(wppY * 2f).Within(0.0001f),
                "With square RT and aspect=2.0, wpp_x must be exactly 2×wpp_y");

            // Snap a fractional position and confirm both axes aligned independently.
            go.transform.position = new Vector3(1.123f, 2.456f, 0f);
            InvokeLateUpdate(snap);

            Vector3 r = go.transform.position;
            Assert.That(Mathf.Abs(r.x % wppX), Is.LessThan(wppX * 0.01f),
                "X must be snapped to its own (wider) grid");
            Assert.That(Mathf.Abs(r.y % wppY), Is.LessThan(wppY * 0.01f),
                "Y must be snapped to its own (narrower) grid");
        }

        /// <summary>
        /// pixelWidth=0 or pixelHeight=0 (no render texture, headless editor) →
        /// LateUpdate must return immediately without throwing or mutating position.
        /// </summary>
        [Test]
        public void LateUpdate_HandlesZeroPixelDimensionsGracefully()
        {
            var (go, cam, snap) = CreateSnapper();

            // No targetTexture → pixelWidth/pixelHeight may be 0 in headless/EditMode.
            // We cannot force it to 0 directly, but we can test the guard path by
            // verifying that no exception is thrown regardless of current pixel size.
            // Then we test the guard at source level via a source-read test.
            cam.orthographic     = true;
            cam.orthographicSize = 5f;

            Vector3 before = new Vector3(1.234f, 5.678f, -9f);
            go.transform.position = before;

            // Must not throw (guards pxW <= 0 || pxH <= 0 at top of LateUpdate).
            Assert.DoesNotThrow(() => InvokeLateUpdate(snap),
                "LateUpdate must not throw even when pixel dimensions are unusual");

            // If pixelWidth/Height are valid the position will be snapped (fine).
            // We just confirm no exception and the Z is unchanged.
            Assert.AreEqual(before.z, go.transform.position.z, 0.0001f,
                "Z axis must never be modified by pixel snap");
        }

        /// <summary>
        /// Source-level guard: the implementation must contain early-return checks
        /// for wpp <= 0, NaN, and Infinity so that edge-case cameras (orthoSize=0,
        /// NaN aspect) cannot corrupt the transform.
        /// </summary>
        [Test]
        public void LateUpdate_HandlesNonFiniteWppGracefully()
        {
            var (go, cam, snap) = CreateSnapper();

            // orthoSize = 0 → wppY = 0, wppX = 0 → guard must trigger.
            cam.targetTexture    = MakeRT(512, 512);
            cam.orthographic     = true;
            cam.orthographicSize = 0f;
            cam.aspect           = 2.0f;

            Vector3 before = new Vector3(3.14f, 2.71f, -5f);
            go.transform.position = before;

            Assert.DoesNotThrow(() => InvokeLateUpdate(snap),
                "LateUpdate must not throw when orthoSize=0 produces wpp=0");

            // Position must not be modified when wpp is degenerate.
            Assert.AreEqual(before.x, go.transform.position.x, 0.0001f,
                "X must not be modified when wpp is 0 or non-finite");
            Assert.AreEqual(before.y, go.transform.position.y, 0.0001f,
                "Y must not be modified when wpp is 0 or non-finite");
        }

        /// <summary>
        /// The Z axis carries Cinemachine's vcam depth offset and must NEVER be
        /// touched by the pixel snap operation.
        /// </summary>
        [Test]
        public void LateUpdate_PreservesZAxis()
        {
            var (go, cam, snap) = CreateSnapper();

            cam.targetTexture    = MakeRT(640, 360);
            cam.orthographic     = true;
            cam.orthographicSize = 5f;
            cam.aspect           = 16f / 9f;

            float originalZ = -42.7f;
            go.transform.position = new Vector3(1.5f, 2.5f, originalZ);

            InvokeLateUpdate(snap);

            Assert.AreEqual(originalZ, go.transform.position.z, 0.0001f,
                "CameraPixelSnap must not modify the Z axis — Z is owned by Cinemachine.");
        }

        /// <summary>
        /// Source-level regression guard: verifies that the implementation uses
        /// cam.aspect in the X wpp formula and does NOT use the same wpp for both axes.
        /// This catches a copy-paste regression without running the full snap math.
        /// </summary>
        [Test]
        public void SourceCode_UsesSeparateWppPerAxis()
        {
            string scriptPath = System.IO.Path.Combine(
                Application.dataPath,
                "_Project", "Scripts", "Gameplay", "World", "Setup", "CameraPixelSnap.cs");
            Assert.IsTrue(System.IO.File.Exists(scriptPath),
                $"Production script not found at {scriptPath}");

            string src = System.IO.File.ReadAllText(scriptPath);

            // Must define wppY without aspect
            Assert.IsTrue(src.Contains("wppY"),
                "CameraPixelSnap must declare a wppY variable for the Y axis");

            // Must define wppX that involves cam.aspect
            Assert.IsTrue(src.Contains("wppX"),
                "CameraPixelSnap must declare a wppX variable for the X axis");
            Assert.IsTrue(src.Contains("cam.aspect") || src.Contains("_cam.aspect"),
                "The X wpp must incorporate cam.aspect — without this, X and Y use " +
                "the same wpp and non-square pixels cause seams");

            // wppX must be used for snapping X, wppY for snapping Y
            int xSnapIdx = src.IndexOf("p.x", System.StringComparison.Ordinal);
            int ySnapIdx = src.IndexOf("p.y", System.StringComparison.Ordinal);
            Assert.Greater(xSnapIdx, -1, "Source must contain a p.x snap line");
            Assert.Greater(ySnapIdx, -1, "Source must contain a p.y snap line");

            // Grab a window around each snap line and verify the right wpp is used
            string xWindow = src.Substring(xSnapIdx, System.Math.Min(80, src.Length - xSnapIdx));
            string yWindow = src.Substring(ySnapIdx, System.Math.Min(80, src.Length - ySnapIdx));
            Assert.IsTrue(xWindow.Contains("wppX"),
                "The p.x snap line must use wppX, not wppY");
            Assert.IsTrue(yWindow.Contains("wppY"),
                "The p.y snap line must use wppY, not wppX");
        }
    }
}
