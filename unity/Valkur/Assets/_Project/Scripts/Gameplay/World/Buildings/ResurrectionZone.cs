using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Component bolted onto the resurrection altar building. Polls
    /// <see cref="EntityRegistry.Player"/> every <see cref="checkInterval"/>;
    /// if the player is in spirit form and standing inside the building's
    /// footprint, calls <see cref="DeathSequenceController.Revive"/>.
    ///
    /// We poll instead of using OnTriggerEnter2D because (a) we want to
    /// resurrect even if the player was already inside the footprint at
    /// the moment of death (edge case if you die at the altar); (b) the
    /// spirit's collider may exclude many layers — a trigger isn't
    /// guaranteed to fire reliably across that switch.
    ///
    /// Bound to the right building automatically by
    /// <see cref="ResurrectionZoneAutoBinder"/>.
    /// </summary>
    [RequireComponent(typeof(BuildingObject))]
    public class ResurrectionZone : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds between footprint checks. Cheap, can be tight.")]
        private float checkInterval = 0.1f;

        [SerializeField, Tooltip("Optional debug log when the spirit enters / leaves the altar.")]
        private bool debugLogs;

        private BuildingObject _building;
        private float _timer;
        private bool _spiritInside;

        private void Awake()
        {
            _building = GetComponent<BuildingObject>();
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < checkInterval) return;
            _timer = 0f;

            var player = EntityRegistry.Player;
            if (player == null) { _spiritInside = false; return; }

            var spirit = player.GetComponent<PlayerSpiritState>();
            if (spirit == null || !spirit.IsSpirit) { _spiritInside = false; return; }

            if (_building == null || !_building.TryGetWorldRect(out var rect))
            {
                _spiritInside = false;
                return;
            }

            Vector3 pos = player.transform.position;
            bool inside = rect.Contains(new Vector2(pos.x, pos.y));

            if (inside && !_spiritInside)
            {
                _spiritInside = true;
                if (debugLogs) Debug.Log($"[ResurrectionZone] Spirit entered altar building #{_building.InstanceId}.");

                var controller = ServiceLocator.Get<DeathSequenceController>();
                if (controller != null)
                {
                    controller.Revive();
                }
                else
                {
                    Debug.LogWarning("[ResurrectionZone] DeathSequenceController not registered in ServiceLocator.");
                }
            }
            else if (!inside && _spiritInside)
            {
                _spiritInside = false;
            }
        }
    }
}
