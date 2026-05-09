using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

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

            // Size the trigger to cover the room's world bounds so the player
            // walking anywhere inside fires OnTriggerEnter2D.
            if (room != null)
            {
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
        // Door lock/unlock — Phase 5 will replace these stubs with calls
        // into the Door MonoBehaviours instantiated for connected doorways.
        // Kept here so subscribers (RoomEnemyTracker) have a stable target.
        // ─────────────────────────────────────────────────────────────────

        private readonly List<MonoBehaviour> _doors = new List<MonoBehaviour>();

        public void LockDoors() { /* Phase 5 */ }
        public void UnlockDoors() { /* Phase 5 */ }
    }
}
