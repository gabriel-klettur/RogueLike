using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The way out of a swapped-in overlay. Dropped by
    /// <see cref="WorldTransitionService.EnterOverlay"/> on the tile the player arrives on,
    /// and destroyed when they leave through it.
    ///
    /// Placing it on the ARRIVAL TILE is what removes the authoring burden entirely. An
    /// interior is a hand-drawn overlay of tile names — there are no components in the file
    /// and no editor that could put one there — so any design that asks an author to also
    /// place an exit produces an interior someone is trapped inside the first time they
    /// forget. Arriving standing on your own exit is the natural reading of a doorway: you
    /// come in through the door, and the door is behind you.
    ///
    /// ARMING is therefore the whole mechanism. The player starts INSIDE the exit, so it
    /// stays inert until they have stepped off it once. Detection is a poll for the same
    /// reason <see cref="BuildingDoor"/> polls: buildings and interiors carry no
    /// Rigidbody2D, a Dynamic player body that comes to rest goes to sleep, and a sleeping
    /// body starts no new contacts — a player who walks in, stops, and steps back would
    /// never generate the trigger enter.
    /// </summary>
    public sealed class InteriorExit : MonoBehaviour
    {
        /// <summary>Half-extent of the square the player must be inside to leave, in world units.</summary>
        public const float EXIT_HALF_EXTENT_WORLD = 0.6f;

        /// <summary>
        /// How far the player must get from the exit before it arms. Larger than the exit
        /// itself so a player shuffling on the spot cannot arm and re-enter in the same
        /// motion, which would read as the interior spitting them straight back out.
        /// </summary>
        public const float ARMING_DISTANCE_WORLD = 1.4f;

        private bool _armed;
        private bool _used;

        /// <summary>True once the player has stepped far enough away for the exit to work.</summary>
        public bool IsArmed => _armed;

        /// <summary>Rect the player has to be inside to leave.</summary>
        public Rect ExitRect
        {
            get
            {
                Vector3 c = transform.position;
                float e = EXIT_HALF_EXTENT_WORLD;
                return new Rect(c.x - e, c.y - e, e * 2f, e * 2f);
            }
        }

        private void Start() => ConfigureMinimapMarker();

        private void Update()
        {
            if (_used) return;

            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;

            Vector2 playerPos = player.position;

            if (!_armed)
            {
                if (Vector2.Distance(playerPos, transform.position) >= ARMING_DISTANCE_WORLD)
                    _armed = true;
                return;
            }

            if (!ExitRect.Contains(playerPos)) return;

            _used = true;
            Leave(player.gameObject);
        }

        /// <summary>
        /// Leave now, regardless of arming. Public so tests and the dev console can drive the
        /// real path without staging a walk. Returns false when there was nothing to return
        /// to or the trip back was refused — in which case the exit re-arms itself rather
        /// than staying spent, because a spent exit in a sealed interior is a soft-lock.
        /// </summary>
        public bool Leave(GameObject player)
        {
            bool ok = WorldTransitionService.ReturnToCaller(player);
            if (!ok)
            {
                _used = false;
                Debug.LogWarning("[InteriorExit] The trip back was refused; the exit stays usable.", this);
            }
            return ok;
        }

        /// <summary>
        /// Show the way out on the minimap. An interior is a room the player has never seen
        /// before and the exit is a single tile in it; leaving that unmarked is how a player
        /// ends up walking the walls looking for a door that is behind them.
        /// </summary>
        private void ConfigureMinimapMarker()
        {
            EntitySetup.ConfigureMinimapMarker(
                gameObject,
                color: new Color(0.45f, 0.85f, 1f, 1f),
                shape: EntitySetup.MinimapMarkerShape.Diamond,
                pixelSize: 5,
                pulse: false,
                pulsePeriod: 1f);
        }

        private void OnDrawGizmos()
        {
            var r = ExitRect;
            Gizmos.color = _armed ? new Color(0.4f, 0.9f, 1f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, 0f), new Vector3(r.width, r.height, 0f));
        }
    }
}
