using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Handles manual item pickup from the world via click/interact input.
    /// Maps to Python's InventoryPickupSystem (proximity check + input trigger).
    /// 
    /// Attach to the Player. Scans for nearby WorldPickup objects on interact.
    /// </summary>
    public class PickupSystem : MonoBehaviour
    {
        [SerializeField] private float pickupRange = 2f;
        [SerializeField] private LayerMask pickupLayers = ~0;

        private Inventory _inventory;

        private void Awake()
        {
            _inventory = GetComponent<Inventory>();

            // Was a SECOND Interact binding on `e`, built here while the asset already had
            // Gameplay/Interact on the same key — two actions firing together, and only one
            // of them visible to the Controls editor or the conflict scanner.
        }

        private void Update()
        {
            if (!InputBindingResolver.WasPerformedThisFrame(
                    InputService.Instance?.Gameplay?.Interact)) return;

            TryPickupNearest();
        }

        /// <summary>
        /// Find the nearest WorldPickup within range and pick it up.
        /// </summary>
        public bool TryPickupNearest()
        {
            if (_inventory == null) return false;

            var hits = Physics2D.OverlapCircleAll(transform.position, pickupRange, pickupLayers);
            WorldPickup nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var pickup = hit.GetComponent<WorldPickup>();
                if (pickup == null) continue;

                float dist = Vector2.Distance(transform.position, pickup.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = pickup;
                }
            }

            if (nearest == null) return false;

            return nearest.TryPickup(gameObject);
        }

        // No OnDisable / OnDestroy teardown: the interact action belongs to the canonical
        // asset, and disposing it would take it from PlayerInteractionController and every
        // other consumer for the rest of the session.

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, pickupRange);
        }
#endif
    }
}
