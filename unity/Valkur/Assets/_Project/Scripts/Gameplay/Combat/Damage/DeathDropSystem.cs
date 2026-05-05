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

            Vector3 deathPos = victim.transform.position;

            // Inventory drops are optional — most NPCs (including barbols) have no
            // inventory at all, but they should still grant XP. Treat the two
            // sources independently so a missing / empty inventory never
            // silences the XP orb spawn.
            int itemsDropped = TryDropInventory(victim, deathPos);

            int xpValue = EstimateXpValue(victim);
            bool xpSpawned = false;
            if (xpValue > 0)
            {
                SpawnXpOrb(deathPos, xpValue);
                xpSpawned = true;
            }

            if (itemsDropped > 0 || xpSpawned)
                Debug.Log($"[DeathDropSystem] {victim.name} died: dropped {itemsDropped} item(s)" +
                          $"{(xpSpawned ? $" and {xpValue} XP orb" : "")} at {deathPos}");
        }

        /// <summary>
        /// Spawns one ground pickup per non-empty inventory slot. Returns the
        /// number of items dropped — zero when the entity has no
        /// <see cref="Inventory.Inventory"/> component or every slot is empty.
        /// Pure helper so the caller's XP-orb path can ignore inventory state.
        /// </summary>
        private int TryDropInventory(GameObject victim, Vector3 deathPos)
        {
            var inventory = victim.GetComponent<Inventory.Inventory>();
            if (inventory == null || inventory.UsedSlots == 0) return 0;

            int dropped = 0;
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
                dropped++;
            }
            return dropped;
        }

        private void SpawnXpOrb(Vector3 position, int xpAmount)
        {
            Vector2 offset = Random.insideUnitCircle * 0.5f;
            Vector3 orbPos = position + new Vector3(offset.x, offset.y, 0f);

            var go = new GameObject("XP_Orb");
            go.transform.position = orbPos;
            go.layer = LayerMask.NameToLayer("Pickup") != -1
                ? LayerMask.NameToLayer("Pickup") : 0;

            // Canonical visual: blue gradient sprite + sparkle particles + scale pulse.
            // Kept in XpOrb.BuildVisuals so future spawn paths share the look.
            XpOrb.BuildVisuals(go);

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
        /// XP value granted by killing <paramref name="entity"/>. Reads the
        /// explicit <see cref="MonsterDefinition.xpReward"/> when set,
        /// otherwise falls back to the legacy heuristic.
        /// </summary>
        private int EstimateXpValue(GameObject entity)
        {
            var brain = entity.GetComponent<FSM.FSMMonsterBrain>();
            var def   = brain != null ? brain.Definition : null;
            int maxHpFallback = 0;
            if (def == null)
            {
                var health = entity.GetComponent<Health>();
                if (health != null) maxHpFallback = health.MaxHp;
            }
            return ComputeXpReward(def, maxHpFallback);
        }

        /// <summary>
        /// Pure computation seam — exposed for tests and for any other system
        /// that wants to know the canonical XP reward of a monster definition
        /// without spawning the entity. Order of precedence:
        ///  1. Explicit <c>def.xpReward</c> when &gt; 0 (designer override).
        ///  2. Legacy heuristic <c>hp/5 + power</c> when a definition exists.
        ///  3. <c>maxHpFallback/5</c> when only a Health component is known.
        ///  4. Constant default of 5 (last resort, mirrors prior behaviour).
        /// </summary>
        public static int ComputeXpReward(Valkur.Data.MonsterDefinition def, int maxHpFallback)
        {
            if (def != null && def.xpReward > 0) return def.xpReward;
            if (def != null) return Mathf.Max(1, def.stats.hp / 5 + def.stats.power);
            if (maxHpFallback > 0) return Mathf.Max(1, maxHpFallback / 5);
            return 5;
        }
    }
}
