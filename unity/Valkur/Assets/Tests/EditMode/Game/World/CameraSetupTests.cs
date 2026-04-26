using System.Reflection;
using Cinemachine;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Regression tests for CameraSetup follow-target acquisition.
    ///
    /// Key regression prevented:
    ///   - CameraSetup.Start() reads EntityRegistry.Player ONCE. Because
    ///     GameplaySceneSetup spawns the player from a long coroutine, the
    ///     registry is usually still empty when CameraSetup.Start() runs.
    ///     Without lazy re-acquisition in Update(), the vcam.Follow stays null,
    ///     the camera never moves to the player's spawn position (~75,75), and
    ///     the player + tilemap render off-screen.
    ///
    /// The lazy-acquire path lives in CameraSetup.Update(): when _vcam.Follow
    /// is null AND the camera is not detached for editor mode, it must pick up
    /// EntityRegistry.Player as soon as the player exists.
    /// </summary>
    [TestFixture]
    public class CameraSetupTests
    {
        private GameObject _camGo;
        private GameObject _playerGo;
        private CameraSetup _cameraSetup;
        private CinemachineVirtualCamera _vcam;

        [SetUp]
        public void SetUp()
        {
            // Reset registry to a clean state
            EntityRegistry.Clear();

            // Cinemachine is a RequireComponent of CameraSetup. AddComponent does
            // not auto-add required dependencies in EditMode tests, so add it first.
            _camGo = new GameObject("TestCameraSetup");
            _vcam = _camGo.AddComponent<CinemachineVirtualCamera>();
            _cameraSetup = _camGo.AddComponent<CameraSetup>();

            // Trigger Awake() to wire _vcam internally
            InvokePrivate("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_camGo != null)    Object.DestroyImmediate(_camGo);
            EntityRegistry.Clear();
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private void InvokePrivate(string methodName)
        {
            var m = typeof(CameraSetup).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(_cameraSetup, null);
        }

        private GameObject MakeFakePlayer(Vector3 pos)
        {
            var go = new GameObject("FakePlayer");
            go.transform.position = pos;
            EntityRegistry.RegisterPlayer(go);
            return go;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// CRITICAL: When Start() runs before the player exists (the normal case
        /// because GameplaySceneSetup spawns from a coroutine), Update() MUST
        /// pick up the player as soon as it appears in the registry.
        /// </summary>
        [Test]
        public void Update_AcquiresFollowTarget_WhenPlayerAppearsAfterStart()
        {
            // Arrange — Start() runs while EntityRegistry is empty
            Assert.IsNull(EntityRegistry.Player, "Precondition: registry is empty");
            InvokePrivate("Start");
            Assert.IsNull(_vcam.Follow,
                "Precondition: vcam.Follow is null because the player did not exist yet");

            // Act — player spawns later (simulates GameplaySceneSetup completing)
            _playerGo = MakeFakePlayer(new Vector3(75f, 75f, 0f));
            InvokePrivate("Update");

            // Assert
            Assert.IsNotNull(_vcam.Follow,
                "vcam.Follow must be assigned via lazy re-acquisition once the " +
                "player exists. Without this fix the player renders off-screen " +
                "because the camera stays at the origin.");
            Assert.AreSame(_playerGo.transform, _vcam.Follow,
                "vcam.Follow must point at the registered player's transform");
        }

        [Test]
        public void Update_AcquiresFollowTarget_OnTheVeryFirstFrameThePlayerExists()
        {
            // Player appears between Start() and the first Update — common when
            // GameplaySceneSetup spawns at the end of its coroutine.
            InvokePrivate("Start");
            _playerGo = MakeFakePlayer(Vector3.zero);

            InvokePrivate("Update");

            Assert.AreSame(_playerGo.transform, _vcam.Follow,
                "Lazy acquisition must succeed on the first Update where the " +
                "player is present in the registry");
        }

        [Test]
        public void Update_DoesNotOverwriteExistingFollowTarget()
        {
            // If Start() managed to grab the player (lucky timing), subsequent
            // Update() calls must NOT clobber the assignment with another lookup.
            _playerGo = MakeFakePlayer(Vector3.zero);
            InvokePrivate("Start");
            var initialFollow = _vcam.Follow;
            Assert.AreSame(_playerGo.transform, initialFollow,
                "Precondition: Start picked up the player");

            // Simulate another GO claiming to be the player
            var imposter = new GameObject("ImposterPlayer");
            EntityRegistry.RegisterPlayer(imposter);
            try
            {
                InvokePrivate("Update");
                Assert.AreSame(initialFollow, _vcam.Follow,
                    "Update must NOT reassign vcam.Follow when it is already set");
            }
            finally
            {
                Object.DestroyImmediate(imposter);
            }
        }

        [Test]
        public void Update_DoesNothing_WhenPlayerStillMissing()
        {
            // No player ever spawns. Update must not throw and must leave Follow null.
            InvokePrivate("Start");
            Assert.DoesNotThrow(() => InvokePrivate("Update"),
                "Update must not throw when EntityRegistry.Player is null");
            Assert.IsNull(_vcam.Follow,
                "Follow must remain null until a player is registered");
        }

        [Test]
        public void Update_DoesNotReacquire_WhileCameraIsDetached()
        {
            // Runtime editors call DetachFollow() to free-pan the camera.
            // Lazy re-acquisition must respect that detached state.
            _playerGo = MakeFakePlayer(Vector3.zero);
            InvokePrivate("Start");
            Assert.IsNotNull(_vcam.Follow, "Precondition: follow assigned");

            _cameraSetup.DetachFollow();
            Assert.IsNull(_vcam.Follow, "Precondition: detach cleared Follow");

            InvokePrivate("Update");
            Assert.IsNull(_vcam.Follow,
                "Update must NOT re-acquire the player while the camera is detached " +
                "(otherwise free-pan in runtime editors would snap back to the player)");
        }
    }
}
