using System.Reflection;
using Cinemachine;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Invariants for CameraSetup's seam-snap configuration fields:
    ///   * Production defaults (snapPPU=16, assetsPPU=32) must stay consistent
    ///     with the math the snap relies on (snapPPU divides assetsPPU, so an
    ///     integer snap-texel-per-pixel also yields integer tile-texel-per-pixel).
    ///   * OnValidate must auto-correct invalid inspector inputs that would
    ///     silently re-introduce the chunk-boundary seam (snapPPU=0, negative,
    ///     or not a divisor of assetsPPU).
    /// Separate from <c>CameraOrthoSnapTests</c> because these tests need a
    /// live <c>CameraSetup</c> instance with private fields manipulated via
    /// reflection — the math file stays pure-static and reflection-free.
    /// </summary>
    [TestFixture]
    public class CameraSnapDefaultsTests
    {
        private const BindingFlags PrivInst = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _camGo;
        private CameraSetup _cameraSetup;

        [SetUp]
        public void SetUp()
        {
            _camGo = new GameObject("TestCameraSetupDefaults");
            _camGo.AddComponent<CinemachineVirtualCamera>();
            _cameraSetup = _camGo.AddComponent<CameraSetup>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        // ── Reflection helpers ───────────────────────────────────────────────

        private int GetInt(string fieldName)
        {
            var f = typeof(CameraSetup).GetField(fieldName, PrivInst);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on CameraSetup");
            return (int)f.GetValue(_cameraSetup);
        }

        private void SetInt(string fieldName, int value)
        {
            var f = typeof(CameraSetup).GetField(fieldName, PrivInst);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on CameraSetup");
            f.SetValue(_cameraSetup, value);
        }

        private void InvokeOnValidate()
        {
            var m = typeof(CameraSetup).GetMethod("OnValidate", PrivInst);
            // OnValidate is gated behind UNITY_EDITOR; in Editor tests it MUST exist.
            Assert.IsNotNull(m, "CameraSetup.OnValidate (UNITY_EDITOR) must be defined to enforce " +
                                "the snapPPU divisor invariant.");
            m.Invoke(_cameraSetup, null);
        }

        // ── Production defaults — pinned invariants ──────────────────────────

        /// <summary>
        /// Pins the chosen production ratio. snapPPU=16 with assetsPPU=32 means
        /// "snap to half-tile-texel alignment" — dense enough that the gameplay
        /// scroll range [2, 25] is reachable, while still seam-free.
        /// </summary>
        [Test]
        public void ProductionDefaults_AreSnapPpu16_AssetsPpu32()
        {
            Assert.AreEqual(32, GetInt("assetsPPU"),
                "assetsPPU default must remain 32 — matches the tile PPU used by Valkur " +
                "and the PixelPerfectCamera reference.");
            Assert.AreEqual(16, GetInt("snapPPU"),
                "snapPPU default must remain 16. Lower densifies the level ladder (good for UX " +
                "scroll feel) and higher leaves the user stuck mid-range (the 2026-05-23 bug). " +
                "16 = assetsPPU/2 keeps the seam-fix invariant intact while doubling the level count.");
        }

        /// <summary>
        /// The seam-fix invariant: snapPPU must divide assetsPPU. When it does,
        /// integer snap-texel-per-screen-pixel cascades into integer tile-texel-
        /// per-screen-pixel automatically. The cascade math is checked in
        /// CameraOrthoSnapTests.SnapWithDivisorSnapPpu_AlsoFixesTilePpuSeam;
        /// this test pins the precondition at the inspector-field level.
        /// </summary>
        [Test]
        public void ProductionDefaults_SnapPpuDividesAssetsPpu()
        {
            int snap = GetInt("snapPPU");
            int assets = GetInt("assetsPPU");

            Assert.Greater(snap, 0, "snapPPU must be positive");
            Assert.Greater(assets, 0, "assetsPPU must be positive");
            Assert.AreEqual(0, assets % snap,
                $"snapPPU ({snap}) must divide assetsPPU ({assets}) exactly. " +
                "Without this, integer snap-texels-per-pixel doesn't imply integer " +
                "tile-texels-per-pixel and the chunk-boundary seam reappears.");
        }

        // ── OnValidate auto-correction ───────────────────────────────────────

        /// <summary>
        /// snapPPU=0 entered in the inspector would crash the snap math
        /// (division by 2 × 0 = 0). OnValidate must clamp it to a safe value.
        /// </summary>
        [Test]
        public void OnValidate_ClampsZeroSnapPpu()
        {
            SetInt("snapPPU", 0);
            InvokeOnValidate();

            Assert.Greater(GetInt("snapPPU"), 0,
                "OnValidate must clamp snapPPU=0 to a positive value. " +
                "Leaving zero crashes the snap math.");
        }

        /// <summary>
        /// Negative snapPPU has no physical meaning. OnValidate clamps it.
        /// </summary>
        [Test]
        public void OnValidate_ClampsNegativeSnapPpu()
        {
            SetInt("snapPPU", -5);
            InvokeOnValidate();

            Assert.Greater(GetInt("snapPPU"), 0,
                "OnValidate must clamp negative snapPPU to a positive value.");
        }

        /// <summary>
        /// snapPPU=24 is a positive value that does NOT divide assetsPPU=32
        /// (32 % 24 = 8). It would silently re-introduce the seam. OnValidate
        /// must reject and snap up to assetsPPU as the safe fallback.
        /// </summary>
        [Test]
        public void OnValidate_ClampsNonDivisorSnapPpu()
        {
            SetInt("assetsPPU", 32);
            SetInt("snapPPU", 24); // 32 % 24 = 8 (not a divisor)
            InvokeOnValidate();

            int corrected = GetInt("snapPPU");
            Assert.AreEqual(0, 32 % corrected,
                $"OnValidate must correct snapPPU={corrected} to a divisor of assetsPPU=32 " +
                "to preserve the seam-fix invariant.");
        }

        /// <summary>
        /// Conversely, a snapPPU that already divides assetsPPU is valid and
        /// must be left alone (no silent re-mapping that would lose the user's
        /// chosen density). Tested at several divisors so a buggy "always
        /// reset to assetsPPU" implementation is caught.
        /// </summary>
        [Test]
        public void OnValidate_LeavesValidDivisorAlone(
            [Values(1, 2, 4, 8, 16, 32)] int validSnapPpu)
        {
            SetInt("assetsPPU", 32);
            SetInt("snapPPU", validSnapPpu);
            InvokeOnValidate();

            Assert.AreEqual(validSnapPpu, GetInt("snapPPU"),
                $"OnValidate must leave snapPPU={validSnapPpu} unchanged when it already " +
                "divides assetsPPU=32. Re-mapping would silently override the user's " +
                "inspector-chosen density.");
        }

        /// <summary>
        /// Edge case: snapPPU equal to assetsPPU is valid (32 % 32 = 0) and
        /// represents "snap at tile-texel granularity" — the original, coarser
        /// behaviour before the 2026-05-23 fix. The test pins that this is
        /// still a legal configuration (someone may want it on a tiny screen
        /// where 32 levels fit, or to disable the densification entirely).
        /// </summary>
        [Test]
        public void OnValidate_AcceptsSnapPpuEqualToAssetsPpu()
        {
            SetInt("assetsPPU", 32);
            SetInt("snapPPU", 32);
            InvokeOnValidate();

            Assert.AreEqual(32, GetInt("snapPPU"),
                "snapPPU == assetsPPU is a legal (coarser) configuration; OnValidate must accept it.");
        }
    }
}
