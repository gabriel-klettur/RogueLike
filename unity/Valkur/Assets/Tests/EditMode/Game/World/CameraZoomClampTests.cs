using System.Reflection;
using Cinemachine;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Regression tests for CameraSetup zoom clamping.
    ///
    /// Background: an earlier "stress-test" refactor removed every clamp from
    /// the multiplicative zoom model. A small burst of scroll-out detents could
    /// inflate ortho size to 50+ in seconds, rendering the player / NPCs /
    /// buildings as sub-pixel placeholders — players reported "everything
    /// disappeared". The fix re-introduces clamps:
    ///
    ///   * Update() (gameplay scroll wheel) → [minZoomOrthoSize, maxZoomOrthoSize]
    ///   * SetTileEditorZoom()              → [minZoomOrthoSize, maxEditorZoomOrthoSize]
    ///
    /// These tests pin the contract so the clamp can never be silently removed
    /// again. Inspector defaults: minZoom=2, maxZoom=25, maxEditorZoom=4000.
    /// The editor cap is intentionally extreme — designers want effectively-
    /// unbounded zoom-out for layout work; the cap only exists to reject
    /// ortho ∞ / NaN drift that would crash the SRP. The tests below read
    /// the actual serialized field, so they are resilient to that value.
    /// </summary>
    [TestFixture]
    public class CameraZoomClampTests
    {
        private GameObject _camGo;
        private CameraSetup _cameraSetup;
        private CinemachineVirtualCamera _vcam;

        [SetUp]
        public void SetUp()
        {
            _camGo = new GameObject("TestCameraZoom");
            _vcam = _camGo.AddComponent<CinemachineVirtualCamera>();
            _cameraSetup = _camGo.AddComponent<CameraSetup>();
            InvokePrivate("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private void InvokePrivate(string methodName)
        {
            var m = typeof(CameraSetup).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(_cameraSetup, null);
        }

        private float GetSerializedFloat(string fieldName)
        {
            var f = typeof(CameraSetup).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found");
            return (float)f.GetValue(_cameraSetup);
        }

        private void SetSerializedFloat(string fieldName, float value)
        {
            var f = typeof(CameraSetup).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found");
            f.SetValue(_cameraSetup, value);
        }

        // ── SetTileEditorZoom contract ────────────────────────────────────────

        [Test]
        public void SetTileEditorZoom_ClampsAboveEditorMax()
        {
            float editorMax = GetSerializedFloat("maxEditorZoomOrthoSize");

            _cameraSetup.SetTileEditorZoom(editorMax + 100f);

            Assert.AreEqual(editorMax, _vcam.m_Lens.OrthographicSize, 1e-4f,
                "SetTileEditorZoom must clamp inputs above maxEditorZoomOrthoSize. " +
                "Without this, an editor with a buggy zoom request can strand the " +
                "camera at ortho 1e30 and make everything render as a single pixel.");
        }

        [Test]
        public void SetTileEditorZoom_ClampsBelowMin()
        {
            float min = GetSerializedFloat("minZoomOrthoSize");

            _cameraSetup.SetTileEditorZoom(0.001f);

            Assert.AreEqual(min, _vcam.m_Lens.OrthographicSize, 1e-4f,
                "SetTileEditorZoom must clamp inputs below minZoomOrthoSize.");
        }

        [Test]
        public void SetTileEditorZoom_RejectsNaN()
        {
            float min = GetSerializedFloat("minZoomOrthoSize");

            _cameraSetup.SetTileEditorZoom(float.NaN);

            Assert.AreEqual(min, _vcam.m_Lens.OrthographicSize, 1e-4f,
                "NaN is a malformed lens value (Cinemachine stops rendering). " +
                "It must collapse to the minimum, never propagate.");
        }

        [Test]
        public void SetTileEditorZoom_RejectsPositiveInfinity()
        {
            float min = GetSerializedFloat("minZoomOrthoSize");

            _cameraSetup.SetTileEditorZoom(float.PositiveInfinity);

            Assert.AreEqual(min, _vcam.m_Lens.OrthographicSize, 1e-4f,
                "+Inf is a malformed lens value; must collapse to the minimum.");
        }

        [Test]
        public void SetTileEditorZoom_RejectsZeroAndNegative()
        {
            float min = GetSerializedFloat("minZoomOrthoSize");

            _cameraSetup.SetTileEditorZoom(0f);
            Assert.AreEqual(min, _vcam.m_Lens.OrthographicSize, 1e-4f,
                "Zero is rejected (Cinemachine renders nothing at ortho=0).");

            _cameraSetup.SetTileEditorZoom(-5f);
            Assert.AreEqual(min, _vcam.m_Lens.OrthographicSize, 1e-4f,
                "Negative ortho is rejected.");
        }

        [Test]
        public void SetTileEditorZoom_ForwardsValuesInsideEditorRange()
        {
            float min = GetSerializedFloat("minZoomOrthoSize");
            float editorMax = GetSerializedFloat("maxEditorZoomOrthoSize");
            float midpoint = (min + editorMax) * 0.5f;

            _cameraSetup.SetTileEditorZoom(midpoint);

            Assert.AreEqual(midpoint, _vcam.m_Lens.OrthographicSize, 1e-4f,
                "Values inside [min, editorMax] must be forwarded unchanged.");
        }

        // ── Update() continuous-clamp contract ────────────────────────────────
        //
        // The PRIMARY user-visible regression: hot-reloading scripts during
        // Play does NOT call Awake/Start again, so a lens left at ortho 50+
        // from the previous build stays at 50 — entities render as sub-pixel
        // dots and look like they "disappeared". The continuous clamp in
        // Update() folds the lens back into [min, max] every frame regardless
        // of input, so a stale value self-heals on the next Update.

        [Test]
        public void Update_ContinuousClamp_FoldsStaleHugeOrthoBackIntoGameplayMax()
        {
            float gameplayMax = GetSerializedFloat("maxZoomOrthoSize");

            // Simulate what hot-reload (or a corrupt scene) leaves behind: an
            // ortho size 4x above the gameplay cap. Without the continuous
            // clamp, the next Update runs the early-return scroll handler
            // (no scroll input → no change) and the camera stays huge.
            _vcam.m_Lens.OrthographicSize = gameplayMax * 4f;

            InvokePrivate("Update");

            Assert.AreEqual(gameplayMax, _vcam.m_Lens.OrthographicSize, 1e-3f,
                "Update() must clamp a stale out-of-bounds ortho size DOWN to " +
                "maxZoomOrthoSize on the very next frame. Without this, the " +
                "user reported 'I can't see the player or buildings' because " +
                "the lens stayed at 50+ after a hot-reload during Play.");
        }

        [Test]
        public void Update_ContinuousClamp_FoldsStaleTinyOrthoUpToMin()
        {
            float gameplayMin = GetSerializedFloat("minZoomOrthoSize");

            // Symmetric case: a corrupt save file or a bad editor request can
            // leave the lens at a sub-1 ortho size — sprites alias and the SRP
            // gives up. The clamp must lift it back to the configured min.
            _vcam.m_Lens.OrthographicSize = 0.1f;

            InvokePrivate("Update");

            Assert.AreEqual(gameplayMin, _vcam.m_Lens.OrthographicSize, 1e-3f,
                "Update() must clamp a stale sub-min ortho size UP to " +
                "minZoomOrthoSize on the very next frame.");
        }

        [Test]
        public void Update_ContinuousClamp_LeavesInRangeOrthoUntouched()
        {
            // The clamp is one-directional: it must not jiggle a lens that's
            // already inside [min, max]. Any drift would compound across
            // frames and produce a slow zoom-creep visible to the player.
            float min = GetSerializedFloat("minZoomOrthoSize");
            float max = GetSerializedFloat("maxZoomOrthoSize");
            float midpoint = (min + max) * 0.5f;
            _vcam.m_Lens.OrthographicSize = midpoint;

            InvokePrivate("Update");
            float afterOnce = _vcam.m_Lens.OrthographicSize;
            InvokePrivate("Update");
            float afterTwice = _vcam.m_Lens.OrthographicSize;

            Assert.AreEqual(midpoint, afterOnce, 1e-4f,
                "First Update must NOT alter an in-range ortho.");
            Assert.AreEqual(midpoint, afterTwice, 1e-4f,
                "Repeated Updates must be idempotent on an in-range ortho.");
        }

        [Test]
        public void Update_ContinuousClamp_AppliesOnFirstFrameAfterHotReloadSimulation()
        {
            // Simulates the exact bug timeline:
            //   1. Play running with ortho=8 (sane).
            //   2. User scrolls out to ortho=50 (now out-of-range under the
            //      new clamp, but tolerated because the multiplicative scroll
            //      wasn't clamped previously).
            //   3. Script hot-reload — Awake/Start NOT re-invoked, lens stays
            //      at 50.
            //   4. The first Update after hot-reload MUST detect the
            //      out-of-bounds value and clamp it. We're testing #4.
            float max = GetSerializedFloat("maxZoomOrthoSize");
            _vcam.m_Lens.OrthographicSize = 50f;
            Assert.Greater(50f, max,
                "Test setup precondition: 50 must exceed maxZoomOrthoSize. " +
                "If your inspector default is >50, raise the test value.");

            InvokePrivate("Update");

            Assert.LessOrEqual(_vcam.m_Lens.OrthographicSize, max + 1e-3f,
                "First post-hot-reload Update must rescue the camera from a " +
                "stale ortho 50 (the exact value reported by the user) by " +
                "clamping back to maxZoomOrthoSize.");
        }

        // ── Defaults sanity ───────────────────────────────────────────────────

        [Test]
        public void Defaults_AreInsidePlayableRange()
        {
            // Pin the inspector defaults: anybody bumping these to zero / huge
            // numbers in the prefab YAML re-introduces the regression.
            float min       = GetSerializedFloat("minZoomOrthoSize");
            float max       = GetSerializedFloat("maxZoomOrthoSize");
            float editorMax = GetSerializedFloat("maxEditorZoomOrthoSize");

            Assert.Greater(min, 0f, "minZoomOrthoSize must be strictly positive");
            Assert.Greater(max, min, "maxZoomOrthoSize must be greater than minZoomOrthoSize");
            Assert.GreaterOrEqual(editorMax, max,
                "Editor zoom-out cap must be >= gameplay cap (editors need wider framing)");
            Assert.Less(max, 100f,
                "maxZoomOrthoSize over 100 makes a 1×1-unit player < 8 pixels on " +
                "a 1080p screen — anything that high looks like 'sprites disappeared'");
        }
    }
}
