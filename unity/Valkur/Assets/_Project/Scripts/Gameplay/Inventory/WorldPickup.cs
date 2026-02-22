using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Rendering;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// World-space item pickup entity. Represents a dropped or spawned item on the ground.
    /// Maps to Python's PhysicalItemComponent + CollectibleComponent + MapLoadDropsSystem.
    /// 
    /// When the player enters the pickup radius, the item is added to their inventory.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class WorldPickup : MonoBehaviour
    {
        [Header("Item")]
        [SerializeField] private ItemDefinition itemDefinition;
        [SerializeField] private int quantity = 1;

        [Header("Pickup")]
        [SerializeField] private float pickupRadius = 1f;
        [SerializeField] private bool autoPickup;
        [SerializeField] private float autoPickupDelay = 0.5f;

        [Header("Visual")]
        [SerializeField] private float bobAmplitude = 0.05f;
        [SerializeField] private float bobFrequency = 2f;

        private float _spawnTime;
        private float _baseY;
        private CircleCollider2D _collider;
        private bool _pickedUp;

        public ItemDefinition Item => itemDefinition;
        public int Quantity => quantity;

        public void Initialize(ItemDefinition item, int qty, Vector3 position)
        {
            itemDefinition = item;
            quantity = qty;
            transform.position = position;
            _baseY = position.y;
            _spawnTime = Time.time;

            // Set sprite
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && item != null)
            {
                sr.sprite = item.icon ?? item.iconSmall;
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = SortingConfig.Z_LOW_OBJECT + SortingConfig.YToSortingOrder(position.y);
            }

            // Set collider as trigger
            _collider = GetComponent<CircleCollider2D>();
            _collider.isTrigger = true;
            _collider.radius = pickupRadius;

            // Add Y-sort
            var ySort = GetComponent<YSortEntity>();
            if (ySort == null)
                ySort = gameObject.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_LOW_OBJECT;

            gameObject.name = item != null ? $"Pickup_{item.itemId}" : "Pickup_unknown";
        }

        private void Update()
        {
            if (_pickedUp) return;

            // Bob animation
            float bob = Mathf.Sin((Time.time - _spawnTime) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            var pos = transform.position;
            pos.y = _baseY + bob;
            transform.position = pos;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_pickedUp) return;
            if (!autoPickup) return;
            if (Time.time - _spawnTime < autoPickupDelay) return;

            TryPickup(other.gameObject);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_pickedUp) return;
            if (!autoPickup) return;
            if (Time.time - _spawnTime < autoPickupDelay) return;

            TryPickup(other.gameObject);
        }

        /// <summary>
        /// Attempt to pick up this item into the given entity's inventory.
        /// Returns true if successful.
        /// </summary>
        public bool TryPickup(GameObject collector)
        {
            if (_pickedUp || itemDefinition == null) return false;

            if (!collector.CompareTag("Player")) return false;

            var inventory = collector.GetComponent<Inventory>();
            if (inventory == null) return false;

            int overflow = inventory.AddItem(itemDefinition, quantity);
            if (overflow >= quantity) return false;

            int picked = quantity - overflow;
            quantity = overflow;

            Debug.Log($"[WorldPickup] {collector.name} picked up {picked}x {itemDefinition.displayName}");

            if (quantity <= 0)
            {
                _pickedUp = true;
                Destroy(gameObject);
            }

            return true;
        }
    }
}
