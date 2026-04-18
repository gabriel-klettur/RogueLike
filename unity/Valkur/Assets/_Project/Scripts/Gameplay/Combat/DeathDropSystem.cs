using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Drops loot from an entity's inventory when it dies.
    /// Maps to Python's DeathDropSystem.
    /// Subscribes to GameEvents.OnEntityDied.
    /// </summary>
    public class DeathDropSystem : MonoBehaviour
    {
        [SerializeField, Tooltip("Max spiral search radius in tiles for free drop positions.")]
#pragma warning disable CS0414 // reserved for upcoming spiral search; surfaced in inspector
        private int maxSearchRadius = 12;
#pragma warning restore CS0414

        [SerializeField, Tooltip("TTL in seconds for ground drops before they despawn. 0 = never.")]
        private float dropDespawnTime = 120f;

        [SerializeField, Tooltip("Random offset range for drop scatter.")]
        private float scatterRadius = 1.5f;

        private void OnEnable()
        {
            GameEvents.OnEntityDied += HandleEntityDied;
        }

        private void OnDisable()
        {
            GameEvents.OnEntityDied -= HandleEntityDied;
        }

        private void HandleEntityDied(GameObject victim, GameObject killer)
        {
            if (victim == null) return;
            if (victim.CompareTag("Player")) return; // Players don't drop loot

            var inventory = victim.GetComponent<Inventory.Inventory>();
            if (inventory == null || inventory.UsedSlots == 0) return;

            Vector3 deathPos = victim.transform.position;

            // Drop all inventory items
            for (int i = 0; i < inventory.Slots.Count; i++)
            {
                var slot = inventory.Slots[i];
                if (slot.IsEmpty) continue;

                Vector2 offset = Random.insideUnitCircle * scatterRadius;
                Vector3 dropPos = deathPos + new Vector3(offset.x, offset.y, 0f);

                var pickup = DropSystem.SpawnDrop(slot.Item, slot.Quantity, dropPos);
                if (pickup != null && dropDespawnTime > 0f)
                {
                    var despawn = pickup.gameObject.AddComponent<TimedDespawn>();
                    despawn.TTL = dropDespawnTime;
                }
            }

            // Also spawn XP orb
            int xpValue = EstimateXpValue(victim);
            if (xpValue > 0)
            {
                SpawnXpOrb(deathPos, xpValue);
            }

            Debug.Log($"[DeathDropSystem] Dropped loot from {victim.name} at {deathPos}");
        }

        private void SpawnXpOrb(Vector3 position, int xpAmount)
        {
            Vector2 offset = Random.insideUnitCircle * 0.5f;
            Vector3 orbPos = position + new Vector3(offset.x, offset.y, 0f);

            var go = new GameObject("XP_Orb");
            go.layer = LayerMask.NameToLayer("Pickup") != -1
                ? LayerMask.NameToLayer("Pickup") : 0;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sprite = XpOrb.GetOrbSprite();

            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.3f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var orb = go.AddComponent<XpOrb>();
            orb.Initialize(xpAmount, orbPos);

            var despawn = go.AddComponent<TimedDespawn>();
            despawn.TTL = 60f;
        }

        /// <summary>
        /// Estimate XP based on monster stats (HP * power factor). 
        /// Python uses item-based XP orbs; Unity uses stat-based estimate.
        /// </summary>
        private int EstimateXpValue(GameObject entity)
        {
            var brain = entity.GetComponent<FSM.FSMMonsterBrain>();
            if (brain != null && brain.Definition != null)
            {
                var stats = brain.Definition.stats;
                return Mathf.Max(1, stats.hp / 5 + stats.power);
            }
            var health = entity.GetComponent<Health>();
            if (health != null) return Mathf.Max(1, health.MaxHp / 5);
            return 5;
        }
    }
}
