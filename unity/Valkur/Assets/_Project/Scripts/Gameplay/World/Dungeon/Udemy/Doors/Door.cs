using UnityEngine;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Doors
{
    /// <summary>
    /// Animated room door. State machine ported from Udemy's <c>Door.cs</c>:
    /// open / locked / closed, with auto-reopen-on-unlock when the door was
    /// previously seen open.
    ///
    /// Differences from Udemy:
    /// - Filters incoming triggers by Unity <b>physics layer</b> (Player(8) +
    ///   Projectile(10) by default) instead of GameObject tags. Matches
    ///   Valkur's input pipeline conventions.
    /// - Sound effect playback is delegated to a project-level
    ///   <see cref="ISoundEffectChannel"/> hook (null = silent), so the Door
    ///   doesn't pull in the full Valkur AudioManager surface in this phase.
    ///
    /// Required setup on the prefab:
    /// - This MonoBehaviour itself owns the <c>BoxCollider2D doorTrigger</c>
    ///   (auto-detected, set to isTrigger).
    /// - A <c>BoxCollider2D doorCollider</c> on a child GameObject, assigned
    ///   via <see cref="doorCollider"/>. This is the SOLID collider toggled
    ///   on when the door is locked.
    /// - An <c>Animator</c> with a bool parameter named "open".
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class Door : MonoBehaviour
    {
        [Tooltip("BoxCollider2D on a child GameObject — toggled solid when the door is locked.")]
        [SerializeField] private BoxCollider2D doorCollider;

        [Tooltip("Layer index used to detect the player (Valkur Player layer = 8).")]
        [SerializeField] private int playerLayer = 8;

        [Tooltip("Layer index used to detect player projectiles (Valkur Projectile layer = 10).")]
        [SerializeField] private int projectileLayer = 10;

        [Tooltip("True when this door belongs to a boss room. Boss doors start locked.")]
        public bool isBossRoomDoor;

        private Animator _animator;
        private BoxCollider2D _trigger;
        private bool _isOpen;
        private bool _previouslyOpened;

        // Lazy-init properties so EditMode tests (no Awake) and prefab edge
        // cases see a valid component reference even if Awake never fired.
        private Animator Animator => _animator != null
            ? _animator
            : (_animator = GetComponent<Animator>());

        private BoxCollider2D Trigger => _trigger != null
            ? _trigger
            : (_trigger = GetComponent<BoxCollider2D>());

        // Optional pluggable SFX hook — null = silent. Phase 6/7 may install
        // a real implementation backed by Valkur.AudioManager.
        public ISoundEffectChannel SfxChannel { get; set; }

        public bool IsOpen => _isOpen;
        public bool IsLocked => doorCollider != null && doorCollider.enabled;
        public bool PreviouslyOpened => _previouslyOpened;

        /// <summary>
        /// Bind the solid collider after the prefab has been instantiated.
        /// Useful for code-driven prefab assembly (Phase 8 sample door prefab)
        /// or EditMode tests where the inspector wiring isn't available.
        /// Resets the door to its default closed-but-unlocked state.
        /// </summary>
        public void BindDoorCollider(BoxCollider2D collider)
        {
            doorCollider = collider;
            if (doorCollider != null) doorCollider.enabled = false;
            _isOpen = false;
            _previouslyOpened = false;
            var trigger = Trigger;
            if (trigger != null)
            {
                trigger.isTrigger = true;
                trigger.enabled = true;
            }
        }

        private void Awake()
        {
            // Cache + initial state. Lazy-init covers the case where Awake never
            // fires (EditMode tests); we keep Awake to set isTrigger and the
            // closed-but-unlocked default before the first frame in PlayMode.
            var trigger = Trigger;
            if (trigger != null) trigger.isTrigger = true;
            _ = Animator; // warm cache
            if (doorCollider != null) doorCollider.enabled = false;
        }

        private void OnEnable()
        {
            // Animator state resets when the room GameObject is disabled
            // (player moved far away). Restore the open/closed visual state.
            var anim = Animator;
            if (anim != null) anim.SetBool(DoorAnimatorParameters.Open, _isOpen);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null) return;
            int layer = other.gameObject.layer;
            if (layer == playerLayer || layer == projectileLayer)
                OpenDoor();
        }

        public void OpenDoor()
        {
            if (_isOpen) return;

            _isOpen = true;
            _previouslyOpened = true;

            if (doorCollider != null) doorCollider.enabled = false;
            var trigger = Trigger;
            if (trigger != null) trigger.enabled = false;
            var anim = Animator;
            if (anim != null) anim.SetBool(DoorAnimatorParameters.Open, true);

            SfxChannel?.PlayDoorOpenClose();
        }

        public void LockDoor()
        {
            _isOpen = false;
            if (doorCollider != null) doorCollider.enabled = true;
            var trigger = Trigger;
            if (trigger != null) trigger.enabled = false;
            var anim = Animator;
            if (anim != null) anim.SetBool(DoorAnimatorParameters.Open, false);
        }

        public void UnlockDoor()
        {
            if (doorCollider != null) doorCollider.enabled = false;
            var trigger = Trigger;
            if (trigger != null) trigger.enabled = true;

            if (_previouslyOpened)
            {
                _isOpen = false; // reset so OpenDoor's guard runs the open flow
                OpenDoor();
            }
        }
    }

    /// <summary>
    /// Pluggable sfx channel for door open/close. Null implementation = silent.
    /// </summary>
    public interface ISoundEffectChannel
    {
        void PlayDoorOpenClose();
    }
}
