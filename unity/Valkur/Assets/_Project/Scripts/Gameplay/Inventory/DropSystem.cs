using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Utility for spawning item drops in the world.
    /// Maps to Python's InventoryDropSystem + ItemDropManager.
    /// 
    /// Creates WorldPickup GameObjects at specified positions.
    /// </summary>
    public static class DropSystem
    {
        /// <summary>
        /// Spawn a world pickup at the given position.
        /// </summary>
        public static WorldPickup SpawnDrop(ItemDefinition item, int quantity, Vector3 position)
        {
            if (item == null || quantity <= 0) return null;

            var go = new GameObject($"Drop_{item.itemId}");
            int pickupLayer = LayerMask.NameToLayer("Pickup");
            go.layer = pickupLayer != -1 ? pickupLayer : 0;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = item.icon ?? item.iconSmall;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 1f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var pickup = go.AddComponent<WorldPickup>();
            pickup.Initialize(item, quantity, position);

            Debug.Log($"[DropSystem] Spawned {quantity}x {item.displayName} at {position}");
            return pickup;
        }

        /// <summary>
        /// Drop an item from a specific inventory at the owner's position with a random offset.
        /// </summary>
        public static WorldPickup DropFromInventory(Inventory inventory, ItemDefinition item, int quantity, Vector3 ownerPosition)
        {
            if (inventory == null || item == null || quantity <= 0) return null;

            int removed = inventory.RemoveItem(item, quantity);
            if (removed <= 0) return null;

            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(1f, 2f);
            Vector3 dropPos = ownerPosition + new Vector3(offset.x, offset.y, 0f);

            return SpawnDrop(item, removed, dropPos);
        }
    }
}
