using System.Collections.Generic;
using System.Reflection;
using Cinemachine;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Regression guard for the tilemap "blue seam" safety net.
    ///
    /// Bug history (do not regress):
    ///   - 2026-05-16: tilemap chunk-boundary seams (sub-pixel float drift)
    ///     exposed Camera.backgroundColor — Unity's default cyan-blue — as
    ///     a thin horizontal line between rows of tiles.
    ///   - Primary fix: ValkurAssetPostprocessor forces SpriteMeshType.FullRect
    ///     on tile sprites so adjacent tile meshes meet exactly at the cell
    ///     boundary. Guarded by TileSeamPolicyTests.
    ///   - Safety net (this file): CameraSetup forces Camera.backgroundColor
    ///     to opaque black, so any residual sub-pixel seam is invisible
    ///     rather than glowing blue.
    ///
    /// What this test guarantees:
    ///   1. After CameraSetup.Start(), Camera.main has clearFlags=SolidColor
    ///      and backgroundColor matches the configured safe colour.
    ///   2. When forceSafeBackgroundColor is disabled, Start() leaves the
    ///      camera's background untouched (regression: future scene authors
    ///      who set a custom colour explicitly mustn't be overridden).
    /// </summary>
    [TestFixture]
    public class CameraBackgroundSafetyTests
    {
        private GameObject _vcamGo;
        private GameObject _mainCamGo;
        private CameraSetup _cameraSetup;
        private Camera _mainCamera;

        private static readonly BindingFlags PrivInst =
            BindingFlags.NonPublic | BindingFlags.Instance;

        // Pre-existing MainCamera-tagged objects destroyed in SetUp so they
        // don't compete with our test camera. Restored never — they're test-
        // scene leftovers, not user content.
        private readonly List<GameObject> _purgedMainCameras = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // The Unity EditMode default scene usually ships with a "Main
            // Camera" GameObject. Camera.main returns it instead of our test
            // camera, which makes the integration assertion below mis-target.
            // Disable any pre-existing MainCamera-tagged camera so our test
            // camera is the sole resolution.
            foreach (var existing in Camera.allCameras)
            {
                if (existing == null) continue;
                if (existing.CompareTag("MainCamera"))
                {
                    existing.gameObject.SetActive(false);
                    _purgedMainCameras.Add(existing.gameObject);
                }
            }

            // Main camera (tagged) — the target of ApplySafeBackgroundColor.
            // Start with a deliberately wrong configuration to prove the
            // safety net overwrote it.
            _mainCamGo = new GameObject("TestMainCamera");
            _mainCamGo.tag = "MainCamera";
            _mainCamera = _mainCamGo.AddComponent<Camera>();
            _mainCamera.clearFlags = CameraClearFlags.Skybox;
            _mainCamera.backgroundColor = new Color(0.19f, 0.30f, 0.47f, 1f); // Unity default cyan-blue

            // Virtual camera + CameraSetup (the system under test). Lives on a
            // separate GameObject because CameraSetup requires a vcam.
            _vcamGo = new GameObject("TestCameraSetup");
            _vcamGo.AddComponent<CinemachineVirtualCamera>();
            _cameraSetup = _vcamGo.AddComponent<CameraSetup>();

            Invoke("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_vcamGo != null) Object.DestroyImmediate(_vcamGo);
            if (_mainCamGo != null) Object.DestroyImmediate(_mainCamGo);
            foreach (var go in _purgedMainCameras)
                if (go != null) go.SetActive(true);
            _purgedMainCameras.Clear();
        }

        private void Invoke(string methodName)
        {
            var m = typeof(CameraSetup).GetMethod(methodName, PrivInst);
            m?.Invoke(_cameraSetup, null);
        }

        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void ApplyHelper_ForcesSafeBackgroundColor_OnGivenCamera()
        {
            // Direct unit test on the extracted static helper. Bypasses
            // Camera.main entirely so the assertion can't be undermined by
            // EditMode tag-cache flakiness.
            CameraSetup.ApplySafeBackgroundColorTo(_mainCamera, Color.black);

            Assert.That(_mainCamera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor),
                "ApplySafeBackgroundColorTo must force clearFlags=SolidColor so the " +
                "safe background colour is what tilemap chunk seams (if any survive) " +
                "show against. Skybox/Don't Clear would defeat the safety net.");

            Assert.That(_mainCamera.backgroundColor, Is.EqualTo(Color.black),
                "ApplySafeBackgroundColorTo must overwrite the background. The " +
                "original cyan-blue is the visible 'blue seam' reported 2026-05-16.");
        }

        [Test]
        public void ApplyHelper_NoOps_WhenCameraIsNull()
        {
            Assert.DoesNotThrow(() => CameraSetup.ApplySafeBackgroundColorTo(null, Color.black),
                "ApplySafeBackgroundColorTo must tolerate a null camera (it may run " +
                "before Camera.main resolves in certain scene-setup orderings).");
        }

        [Test]
        public void Start_ForcesSafeBackgroundColor_WhenCameraMainIsTheTestCamera()
        {
            // End-to-end check: Start() routes through ResolveMainCamera which
            // falls back to walking Camera.allCameras when Camera.main misses.
            // If the test environment doesn't expose our tagged camera even
            // through allCameras, the assertion below pinpoints that — far
            // more debuggable than a silent no-op.
            var resolved = ResolveLikeProduction();
            Assert.AreSame(_mainCamera, resolved,
                "Production-path camera resolution must locate the MainCamera-tagged " +
                "camera created in SetUp. If this fails the safety net cannot run.");

            Invoke("Start");

            Assert.That(_mainCamera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor),
                "CameraSetup.Start() must force clearFlags=SolidColor on the resolved " +
                "main camera.");
            Assert.That(_mainCamera.backgroundColor, Is.EqualTo(Color.black),
                "CameraSetup.Start() must overwrite the original cyan-blue background.");
        }

        // Mirrors CameraSetup.ResolveMainCamera so the test exercises the same
        // resolution path the production code uses, without granting the test
        // visibility into a private member.
        private static Camera ResolveLikeProduction()
        {
            var cam = Camera.main;
            if (cam != null) return cam;
            foreach (var c in Camera.allCameras)
            {
                if (c == null) continue;
                if (!c.gameObject.activeInHierarchy) continue;
                if (c.CompareTag("MainCamera")) return c;
            }
            return null;
        }

        [Test]
        public void Start_LeavesBackgroundUntouched_WhenForceSafeColorDisabled()
        {
            // Toggle the serialized flag off. Tests must not assume the field
            // exists in a particular case-pattern, so use reflection.
            var field = typeof(CameraSetup).GetField("forceSafeBackgroundColor",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "forceSafeBackgroundColor field must exist on CameraSetup " +
                "for the opt-out path to function.");
            field.SetValue(_cameraSetup, false);

            var customColor = new Color(0.25f, 0.0f, 0.5f, 1f);
            _mainCamera.backgroundColor = customColor;
            _mainCamera.clearFlags = CameraClearFlags.Skybox;

            Invoke("Start");

            Assert.That(_mainCamera.backgroundColor, Is.EqualTo(customColor),
                "When forceSafeBackgroundColor is disabled, CameraSetup must NOT " +
                "rewrite a custom backgroundColor — scene authors opt out for " +
                "special cases (e.g. main-menu sky gradient).");
            Assert.That(_mainCamera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox),
                "When forceSafeBackgroundColor is disabled, clearFlags must also " +
                "be preserved.");
        }
    }
}
