using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Runtime doorway on a placed building. A thin sensor that hands the player to
    /// <see cref="WorldTransitionService"/> — it owns no transition logic of its own, the
    /// same way <c>ZonePortal</c> no longer does.
    ///
    /// Lives on a CHILD of the <see cref="BuildingObject"/> so it follows the building when
    /// the F10 editor drags it.
    ///
    /// DETECTION IS A POLL, NOT A TRIGGER, and that is deliberate. Buildings carry no
    /// Rigidbody2D, so a doorway trigger would depend entirely on the PLAYER's Dynamic body
    /// to generate the contact — and a Dynamic body that has come to rest goes to sleep
    /// (Player.prefab ships Sleeping Mode = Start Awake, project Time To Sleep = 0.5 s).
    /// A SLEEPING BODY STARTS NO NEW CONTACTS, so a player who stops on the doorstep and
    /// then walks in from rest could be missed. <c>ResurrectionZone</c> already polls a
    /// building footprint for the same class of reason. The rect being polled is the exact
    /// one <see cref="BuildingDoorGeometry"/> hands the F10 overlay, so what the author
    /// draws and what the game tests are the same rectangle by construction.
    ///
    /// Entry is by walking in, not by a key press. The doorway sits inside the building's own
    /// solid footprint, so the only way to reach it is to aim at it. A key press is blocked
    /// anyway: ValkurInputActions binds Keyboard/e to BOTH the Interact action AND SpellSlash,
    /// and nothing in the game reads InputService.Gameplay.Interact. See Phase 4 of
    /// .github/BUILDING_DOORS_ROADMAP.md.
    /// </summary>
    public sealed class BuildingDoor : MonoBehaviour
    {
        /// <summary>Name given to the child GameObject, so the F10 editor can find it again.</summary>
        public const string CHILD_NAME = "BuildingDoor";

        /// <summary>
        /// World-unit slack added around the doorway rect before testing the player against it.
        /// The player's body collider is 0.5 x 0.3 while only its transform pivot is tested, so
        /// a doorway they are visibly standing in can still miss the pivot by a few centimetres.
        /// </summary>
        public const float ENTRY_PADDING_WORLD = 0.15f;

        [Header("Destination")]
        [Tooltip("Runtime-assigned by BuildingDoorFactory from overrides.door. Shown for diagnosis.")]
        [SerializeField] private string _targetOverlay = "";

        [Header("Behaviour")]
        [Tooltip("Seconds before the doorway can fire again. Guards against a failed " +
                 "transition re-firing every frame while the player stands in it.")]
        [SerializeField] private float _rearmDelay = 1f;

        private BuildingObject   _owner;
        private BuildingDoorSpec _spec;
        private float            _nextAllowedEntryTime;

        public BuildingObject   Owner         => _owner;
        public BuildingDoorSpec Spec          => _spec;
        public string           TargetOverlay => _targetOverlay;

        // ── Setup ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bind this doorway to its building and destination. Call
        /// <see cref="RefreshGeometry"/> afterwards (the factory does) once the owner's
        /// renderers exist, otherwise the doorway has no place to sit.
        /// </summary>
        public void Configure(BuildingObject owner, BuildingDoorSpec spec)
        {
            _owner = owner;
            _spec  = spec?.Clone();
            _targetOverlay = _spec != null ? _spec.target : "";
            RefreshMinimapMarker();
        }

        /// <summary>
        /// Re-place this object on the doorway's current world position. Must be called after
        /// anything that moves or resizes the building — the F10 drag paths — for the same
        /// reason <c>BuildingObject.RefreshSorting</c> must be. Returns false when the owner's
        /// bounds are not resolvable yet.
        /// </summary>
        public bool RefreshGeometry()
        {
            if (_owner == null) return false;
            if (!_owner.TryGetDoorWorldRect(out var doorWorld)) return false;

            transform.position     = new Vector3(doorWorld.center.x, doorWorld.center.y, 0f);
            transform.localRotation = Quaternion.identity;
            return true;
        }

        /// <summary>World rect this doorway occupies, for tests, for entry, and for the F10 overlay.</summary>
        public bool TryGetWorldRect(out Rect rect)
        {
            rect = default;
            if (_owner == null) return false;
            return _owner.TryGetDoorWorldRect(out rect);
        }

        /// <summary>The rect actually tested against the player: the doorway plus entry slack.</summary>
        public bool TryGetEntryRect(out Rect rect)
        {
            if (!TryGetWorldRect(out rect)) return false;
            float p = ENTRY_PADDING_WORLD;
            rect = new Rect(rect.xMin - p, rect.yMin - p, rect.width + 2f * p, rect.height + 2f * p);
            return true;
        }

        /// <summary>True when this doorway leads somewhere and is not inside its re-arm window.</summary>
        public bool IsReady => _spec != null && _spec.IsValid && Time.time >= _nextAllowedEntryTime;

        // ── Detection ───────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!IsReady) return;

            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;

            if (!TryGetEntryRect(out var entry)) return;
            if (!entry.Contains(player.position)) return;

            // Re-arm first: Enter() swaps the world, which destroys this building. Writing the
            // cooldown afterwards would be a write to an object already scheduled for death.
            _nextAllowedEntryTime = Time.time + Mathf.Max(0f, _rearmDelay);
            Enter(player.gameObject);
        }

        // ── Activation ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Perform the transition now. Public so tests and the dev console can drive the real
        /// path without staging a walk. Returns false when the doorway has no valid
        /// destination or the swap was refused.
        /// </summary>
        public bool Enter(GameObject player)
        {
            if (_spec == null || !_spec.IsValid)
            {
                Debug.LogWarning($"[BuildingDoor] {name} has no destination — nothing to enter.", this);
                return false;
            }

            // Record the way back BEFORE the swap: once the world changes, the building this
            // doorway belongs to is destroyed and can no longer be measured. The exit point
            // sits outside the doorway so a returning player does not walk straight back in.
            if (TryGetWorldRect(out var doorWorld))
            {
                WorldTransitionService.RecordReturnPoint(
                    WorldTransitionService.CurrentOverlay,
                    BuildingDoorGeometry.ResolveExitPoint(doorWorld));
            }

            bool ok = WorldTransitionService.EnterOverlay(
                _spec.target,
                _spec.SpawnPosition,
                _spec.useDefaultSpawn,
                player);

            if (!ok)
            {
                // The transition never happened, so the return point describes a trip the
                // player did not take. Leaving it armed would teleport them on the next exit.
                WorldTransitionService.ClearReturnPoint();
            }
            return ok;
        }

        // ── Presentation ────────────────────────────────────────────────────────────

        /// <summary>
        /// Put the doorway on the minimap, the way <c>ZonePortal</c> puts itself there. A door
        /// is a destination the player has to be able to find again from across the map, and
        /// it is drawn as part of the building rather than as an object of its own.
        /// </summary>
        private void RefreshMinimapMarker()
        {
            if (_spec == null || !_spec.IsValid) return;

            EntitySetup.ConfigureMinimapMarker(
                gameObject,
                color: new Color(0.95f, 0.78f, 0.35f, 1f),
                shape: EntitySetup.MinimapMarkerShape.Diamond,
                pixelSize: 4,
                pulse: false,
                pulsePeriod: 1f);
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetWorldRect(out var r)) return;
            Gizmos.color = new Color(0.9f, 0.75f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, 0f),
                                new Vector3(r.width, r.height, 0f));
        }
    }
}
