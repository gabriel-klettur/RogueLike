using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Dungeon.Udemy.Doors;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Doors
{
    /// <summary>
    /// State-machine tests for <see cref="Door"/>. Covers open/lock/unlock
    /// transitions and the auto-reopen-on-unlock behavior. The Animator
    /// itself isn't asserted (no controller asset in EditMode); we rely on
    /// the public <see cref="Door.IsOpen"/> / <see cref="Door.IsLocked"/>
    /// flags as the testable state.
    /// </summary>
    public class DoorTests
    {
        private GameObject _doorGo;
        private GameObject _colliderChild;
        private Door _door;
        private BoxCollider2D _trigger;
        private BoxCollider2D _solidCollider;

        [SetUp]
        public void SetUp()
        {
            _doorGo = new GameObject("Door");
            _trigger = _doorGo.AddComponent<BoxCollider2D>();
            _doorGo.AddComponent<Animator>();
            _door = _doorGo.AddComponent<Door>();

            _colliderChild = new GameObject("DoorCollider");
            _colliderChild.transform.SetParent(_doorGo.transform);
            _solidCollider = _colliderChild.AddComponent<BoxCollider2D>();

            // BindDoorCollider re-establishes the closed-but-unlocked baseline,
            // sidestepping Awake having seen a null doorCollider when the
            // component was first added.
            _door.BindDoorCollider(_solidCollider);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_doorGo);
        }

        [Test]
        public void InitialState_IsClosedAndUnlockedAndTriggerEnabled()
        {
            Assert.IsFalse(_door.IsOpen);
            Assert.IsFalse(_door.IsLocked);
            Assert.IsFalse(_door.PreviouslyOpened);
        }

        [Test]
        public void OpenDoor_FlipsIsOpenAndDisablesBothColliders()
        {
            // Pre-conditions established by SetUp + BindDoorCollider:
            Assert.IsFalse(_door.IsOpen, "pre: IsOpen should be false");
            Assert.IsFalse(_solidCollider.enabled, "pre: solidCollider should be disabled");
            Assert.IsTrue(_trigger.enabled, "pre: trigger should be enabled");

            _door.OpenDoor();

            Assert.IsTrue(_door.IsOpen, "post: IsOpen should be true");
            Assert.IsTrue(_door.PreviouslyOpened, "post: PreviouslyOpened should be true");
            Assert.IsFalse(_solidCollider.enabled, "post: solidCollider should be disabled");
            Assert.IsFalse(_trigger.enabled, "post: trigger should be disabled");
        }

        [Test]
        public void OpenDoor_IdempotentWhenAlreadyOpen()
        {
            _door.OpenDoor();
            // Re-open should be a no-op (no SFX double-fire, no state churn).
            int sfxCalls = 0;
            _door.SfxChannel = new SfxCounter(() => sfxCalls++);
            _door.OpenDoor();
            Assert.AreEqual(0, sfxCalls);
        }

        [Test]
        public void LockDoor_EnablesSolidColliderAndDisablesTrigger()
        {
            _door.LockDoor();

            Assert.IsFalse(_door.IsOpen);
            Assert.IsTrue(_door.IsLocked);
            Assert.IsTrue(_solidCollider.enabled);
            Assert.IsFalse(_trigger.enabled);
        }

        [Test]
        public void UnlockDoor_AfterLock_LeavesDoorPassableAndTriggerOn()
        {
            _door.LockDoor();
            _door.UnlockDoor();

            Assert.IsFalse(_door.IsLocked);
            Assert.IsTrue(_trigger.enabled);
        }

        [Test]
        public void UnlockDoor_WhenPreviouslyOpened_ReopensAutomatically()
        {
            _door.OpenDoor();
            _door.LockDoor();
            Assert.IsFalse(_door.IsOpen); // locked = closed visually

            _door.UnlockDoor();
            Assert.IsTrue(_door.IsOpen); // reopened
        }

        [Test]
        public void SfxChannel_IsCalledExactlyOncePerOpenTransition()
        {
            int sfxCalls = 0;
            _door.SfxChannel = new SfxCounter(() => sfxCalls++);

            _door.OpenDoor();
            Assert.AreEqual(1, sfxCalls);

            _door.OpenDoor();
            Assert.AreEqual(1, sfxCalls); // idempotent

            _door.LockDoor();
            _door.UnlockDoor();
            Assert.AreEqual(2, sfxCalls); // reopen fires SFX
        }

        private sealed class SfxCounter : ISoundEffectChannel
        {
            private readonly System.Action _onPlay;
            public SfxCounter(System.Action onPlay) { _onPlay = onPlay; }
            public void PlayDoorOpenClose() => _onPlay?.Invoke();
        }
    }
}
