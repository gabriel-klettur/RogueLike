using UnityEngine;
using UnityEngine.InputSystem;

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

        private InputAction _interactAction;
        private Inventory _inventory;

        private void Awake()
        {
            _inventory = GetComponent<Inventory>();

            _interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _interactAction.Enable();
        }

        private void Update()
        {
            if (_interactAction == null) return;
            if (!_interactAction.WasPerformedThisFrame()) return;

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

        private void OnDisable()
        {
            _interactAction?.Disable();
        }

        private void OnDestroy()
        {
            _interactAction?.Dispose();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, pickupRange);
        }
#endif
    }
}
