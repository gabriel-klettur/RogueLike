using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;
using Valkur.Gameplay.World.Dungeon.Udemy.Doors;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Runtime
{
    /// <summary>
    /// Lightweight runtime companion attached to every instantiated room
    /// GameObject. Holds the trigger that fires <see cref="GameEvents.OnRoomChanged"/>
    /// when the player walks in, and exposes <see cref="LockDoors"/> /
    /// <see cref="UnlockDoors"/> stubs that Phase 5 will wire up to
    /// <c>Door</c> components.
    ///
    /// Tilemap stamping + A* penalty computation lives in
    /// <see cref="RoomTilemapStamper"/>; that's run by the dungeon strategy
    /// before this component goes live so all of the room's tiles are
    /// already in the global tilemaps by the time the trigger fires.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public class InstantiatedRoom : MonoBehaviour
    {
        [Tooltip("Player layer index — fired RoomChanged when a collider on this layer enters the trigger.")]
        [SerializeField] private int playerLayer = 8;

        // Filled by Initialise. Public so the strategy/spawner can mutate state.
        [HideInInspector] public Room Room;

        private BoxCollider2D _trigger;
        private bool _initialised;

        private void Awake()
        {
            _trigger = GetComponent<BoxCollider2D>();
            _trigger.isTrigger = true;
        }

        /// <summary>
        /// Bind this MonoBehaviour to the in-flight builder room. Must be
        /// called by the strategy after stamping; otherwise the trigger fires
        /// will be silently ignored.
        /// </summary>
        public void Initialise(Room room, int? playerLayerOverride = null)
        {
            Room = room;
            if (playerLayerOverride.HasValue) playerLayer = playerLayerOverride.Value;
            if (Room != null) Room.instantiatedRoom = this;

            // Awake may not have run yet (AddComponent + Instantiate ordering),
            // so resolve the trigger lazily here too.
            if (_trigger == null) _trigger = GetComponent<BoxCollider2D>();

            // Size the trigger to cover the room's world bounds so the player
            // walking anywhere inside fires OnTriggerEnter2D.
            if (room != null && _trigger != null)
            {
                _trigger.isTrigger = true;
                int width = room.upperBounds.x - room.lowerBounds.x + 1;
                int height = room.upperBounds.y - room.lowerBounds.y + 1;
                _trigger.size = new Vector2(width, height);
                _trigger.offset = new Vector2(
                    (width / 2f) + room.lowerBounds.x - transform.position.x,
                    (height / 2f) + room.lowerBounds.y - transform.position.y);
            }

            _initialised = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_initialised || Room == null) return;
            if (other == null || other.gameObject.layer != playerLayer) return;

            Room.isPreviouslyVisited = true;

            var snapshot = Room.ToSnapshot();
            GameEvents.FireRoomChanged(
                snapshot.Id,
                snapshot.Bounds,
                snapshot.EntrancePosition,
                snapshot.IsClearedOfEnemies);
        }

        // ─────────────────────────────────────────────────────────────────
        // Door wiring — populated by the strategy after instantiating door
        // prefabs at each connected doorway. Boss room doors start locked.
        // ─────────────────────────────────────────────────────────────────

        private readonly List<Door> _doors = new List<Door>();

        public IReadOnlyList<Door> Doors => _doors;

        /// <summary>Called by the strategy once per door instance attached to this room.</summary>
        public void RegisterDoor(Door door)
        {
            if (door != null) _doors.Add(door);
        }

        /// <summary>
        /// Instantiate <see cref="Doorway.doorPrefab"/> at every CONNECTED
        /// doorway of this room and register the resulting Door MonoBehaviour
        /// for the lock / unlock flow. Mirrors Udemy's
        /// <c>InstantiatedRoom.AddDoorsToRooms</c>: corridors don't get
        /// doors (they're the seam, not the gate), and doorways with no
        /// authored prefab are silently skipped.
        ///
        /// World position of each spawned door follows Udemy's offsets
        /// (1 tile out from the doorway anchor, with a sub-tile shift on
        /// E/W to centre vertically against a 2-tile-tall doorframe). All
        /// positions are computed in tile units; we keep PPU = 1 so a tile
        /// equals one world unit.
        /// </summary>
        public void AddDoorsToRooms()
        {
            if (Room == null || Room.doorWayList == null) return;

            // Corridors don't get doors. Mirrors Udemy's AddDoorsToRooms guard.
            if (Room.roomNodeType != null
                && (Room.roomNodeType.IsCorridor
                    || Room.roomNodeType.IsCorridorNS
                    || Room.roomNodeType.IsCorridorEW))
                return;

            var roomLowerWorld = (Vector3)(Vector2)Room.lowerBounds;
            var templateLower = (Vector3)(Vector2)Room.templateLowerBounds;

            for (int i = 0; i < Room.doorWayList.Count; i++)
            {
                var doorway = Room.doorWayList[i];
                if (doorway == null || !doorway.isConnected || doorway.doorPrefab == null)
                    continue;

                // Translate doorway template-local position into world space
                // (Room was placed at lowerBounds; templateLowerBounds is the
                // template-local anchor of that lower-left corner).
                Vector3 doorwayWorld = (Vector3)(Vector2)doorway.position
                    + roomLowerWorld - templateLower;

                Vector3 offset;
                switch (doorway.orientation)
                {
                    case Orientation.North: offset = new Vector3(0.5f, 1f, 0); break;
                    case Orientation.South: offset = new Vector3(0.5f, 0f, 0); break;
                    case Orientation.East:  offset = new Vector3(1f, 1.25f, 0); break;
                    case Orientation.West:  offset = new Vector3(0f, 1.25f, 0); break;
                    default: offset = Vector3.zero; break;
                }

                var doorGo = Object.Instantiate(doorway.doorPrefab,
                    doorwayWorld + offset, Quaternion.identity, transform);
                var doorComponent = doorGo.GetComponent<Door>();
                if (doorComponent != null)
                {
                    if (Room.roomNodeType != null && Room.roomNodeType.IsBossRoom)
                    {
                        doorComponent.isBossRoomDoor = true;
                        doorComponent.LockDoor();
                    }
                    _doors.Add(doorComponent);
                }
            }
        }

        public void LockDoors()
        {
            for (int i = 0; i < _doors.Count; i++)
                if (_doors[i] != null) _doors[i].LockDoor();
        }

        public void UnlockDoors()
        {
            for (int i = 0; i < _doors.Count; i++)
                if (_doors[i] != null) _doors[i].UnlockDoor();
        }
    }
}
