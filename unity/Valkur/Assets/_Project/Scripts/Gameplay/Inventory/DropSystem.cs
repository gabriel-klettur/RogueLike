using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.WorldDrops;

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
        /// Spawn a world pickup at the given position. When an
        /// <see cref="ItemDropService"/> is registered the drop is recorded in
        /// the active run save (Source = <see cref="ItemDropSource.Loot"/>) so
        /// it survives a save / load cycle. Without a service it falls back to
        /// a fully-ephemeral pickup that lives only for the current scene —
        /// keeping the legacy behaviour for unit tests / sandbox scenes.
        ///
        /// For F7 authoring drops call <c>ItemDropService.SpawnPersistent</c>
        /// directly so the source flag is correct.
        /// </summary>
        public static WorldPickup SpawnDrop(ItemDefinition item, int quantity, Vector3 position)
        {
            if (item == null || quantity <= 0) return null;

            // Persistence path: route through the service so loot drops survive
            // a save / load cycle. The service uses BuildPickupShell internally.
            if (ServiceLocator.TryGet<ItemDropService>(out var service))
            {
                float ttl = item.despawnTime;
                var inst = service.SpawnGameplay(item, quantity, position, ttl,
                    zoneId: "", source: ItemDropSource.Loot);
                var live = inst != null ? service.GetLivePickup(inst.dropId) : null;
                if (live != null)
                {
                    Debug.Log($"[DropSystem] Spawned {quantity}x {item.displayName} at {position} (run-persistent).");
                    return live;
                }
            }

            // Ephemeral fallback (no service registered).
            var pickup = BuildPickupShell(item, position);
            if (pickup == null) return null;

            pickup.Initialize(item, quantity, position);
            Debug.Log($"[DropSystem] Spawned {quantity}x {item.displayName} at {position} (ephemeral).");
            return pickup;
        }

        /// <summary>
        /// Build the GameObject + components for a pickup but leave
        /// <c>WorldPickup.Initialize*</c> to the caller. Used by both the legacy
        /// ephemeral <see cref="SpawnDrop"/> path and the persistent
        /// <c>ItemDropService</c> rehydration path so they share one place to
        /// configure layer / collider / rigidbody.
        /// </summary>
        public static WorldPickup BuildPickupShell(ItemDefinition item, Vector3 position)
        {
            if (item == null) return null;

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

            return go.AddComponent<WorldPickup>();
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
