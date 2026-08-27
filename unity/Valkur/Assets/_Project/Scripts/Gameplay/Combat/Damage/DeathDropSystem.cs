using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Drops loot from an entity's inventory, plus (for hostile FSM monsters) a roll of its
    /// <see cref="MonsterDefinition.lootTable"/>, when it dies.
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

        /// <summary>
        /// Faction that marks an NPC as a hostile whose <see cref="MonsterDefinition.lootTable"/>
        /// may roll. Mirrors <see cref="NPCRespawnSystem.HostileFaction"/> — same convention
        /// (empty faction defaults to hostile, comparison is case-insensitive), kept as an
        /// independent constant so the two systems stay decoupled.
        /// </summary>
        private const string HostileFaction = "EVIL";

        /// <summary>
        /// RNG consumed by <see cref="LootTable.Roll"/>. The project has no run-wide seed
        /// reachable from this MonoBehaviour — <c>DungeonGenerator</c> / <c>BiomeContext</c> /
        /// the Buildings Fill tool each seed their OWN local generator for world-gen
        /// reproducibility, but nothing establishes a persisted "combat RNG" a scene-wide
        /// system like this one could pull from. So this is a fresh, unseeded
        /// <see cref="System.Random"/> created once per <see cref="DeathDropSystem"/> instance
        /// and reused across every kill it handles for the life of the scene: loot varies
        /// kill to kill within a session but a run is NOT reproducible from a fixed seed today.
        /// If a project-wide run seed is added later (LootTable's own doc-comment already
        /// promises "Phase-4 networking parity" for a supplied RNG), reseed this field from it
        /// instead of replacing the field.
        /// </summary>
        private readonly System.Random _lootRng = new System.Random();

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
            // inventory at all, but they should still grant XP. Treat the four
            // sources (inventory, loot table, boss loot table, XP) independently so a
            // missing / empty inventory or absent loot table never silences the others.
            int itemsDropped = TryDropInventory(victim, deathPos);
            int lootDropped = TryDropLootTable(victim, deathPos);
            int bossLootDropped = TryDropBossLootTable(victim, deathPos);
            int totalLootDropped = lootDropped + bossLootDropped;

            int xpValue = EstimateXpValue(victim);
            bool xpSpawned = false;
            if (xpValue > 0)
            {
                SpawnXpOrb(deathPos, xpValue);
                xpSpawned = true;
            }

            if (itemsDropped > 0 || totalLootDropped > 0 || xpSpawned)
                Debug.Log($"[DeathDropSystem] {victim.name} died: dropped {itemsDropped} item(s)" +
                          $"{(totalLootDropped > 0 ? $" + {totalLootDropped} loot-table drop(s)" : "")}" +
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

        /// <summary>
        /// Rolls the victim's <see cref="MonsterDefinition.lootTable"/> (when present) and
        /// spawns the result as a ground pickup, using the same shell / TTL path as an
        /// inventory drop. Returns 1 when an item dropped, 0 otherwise — never throws.
        ///
        /// Gated to hostile monsters (<c>stats.faction == "EVIL"</c>, or unset — mirrors
        /// <see cref="NPCRespawnSystem"/>'s hostile-defaults convention) so a NEUTRAL vendor
        /// authored with a lootTable for some other feature never drops it through ordinary
        /// combat death.
        /// </summary>
        private int TryDropLootTable(GameObject victim, Vector3 deathPos)
        {
            var brain = victim.GetComponent<FSM.FSMMonsterBrain>();
            var def = brain != null ? brain.Definition : null;
            if (def == null || def.lootTable == null) return 0;
            if (!IsHostileFaction(def.stats.faction)) return 0;

            return RollAndSpawnLoot(def.lootTable, deathPos);
        }

        /// <summary>
        /// Rolls <see cref="Valkur.Data.BossDefinition.bossLoot"/> (when the victim carries a
        /// <see cref="BossConfigurator"/> whose definition names one) and spawns the result the
        /// same way <see cref="TryDropLootTable"/> does. Stacks with the monster's own
        /// <c>lootTable</c> roll above — see <c>bossLoot</c>'s own doc comment: "bosses can drop
        /// guaranteed items via this table AND the regular monster drop pool simultaneously."
        /// Gated to the same hostile-faction check as <see cref="TryDropLootTable"/> so a boss
        /// authored on a neutral/faction-less NPC for some other reason still can't bypass the
        /// "vendors never drop loot" rule through this second path.
        /// </summary>
        private int TryDropBossLootTable(GameObject victim, Vector3 deathPos)
        {
            // Explicit null checks rather than `?.` — the null-conditional operator on a
            // UnityEngine.Object bypasses its overloaded `==`, which is a real footgun for a
            // "fake null" destroyed object. Neither BossConfigurator nor BossDefinition is ever
            // in that state here (GetComponent already returns a genuine null when absent), but
            // the explicit form costs nothing and matches the rest of the file's style.
            var configurator = victim.GetComponent<BossConfigurator>();
            LootTable table = null;
            if (configurator != null && configurator.Definition != null)
                table = configurator.Definition.bossLoot;
            if (table == null) return 0;

            var brain = victim.GetComponent<FSM.FSMMonsterBrain>();
            var def = brain != null ? brain.Definition : null;
            if (!IsHostileFaction(def != null ? def.stats.faction : null)) return 0;

            return RollAndSpawnLoot(table, deathPos);
        }

        /// <summary>
        /// Mirrors <see cref="NPCRespawnSystem.HostileFaction"/>'s convention: empty faction
        /// defaults to hostile, comparison is case-insensitive.
        /// </summary>
        private static bool IsHostileFaction(string faction)
        {
            if (string.IsNullOrEmpty(faction)) faction = HostileFaction;
            return string.Equals(faction, HostileFaction, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Rolls one item from <paramref name="table"/> and spawns it as a ground pickup.</summary>
        private int RollAndSpawnLoot(LootTable table, Vector3 deathPos)
        {
            var item = table.Roll(_lootRng);
            if (item == null) return 0;

            Vector2 offset = Random.insideUnitCircle * scatterRadius;
            Vector3 dropPos = deathPos + new Vector3(offset.x, offset.y, 0f);

            var pickup = DropSystem.SpawnDrop(item, 1, dropPos);
            if (pickup == null) return 0;

            if (dropDespawnTime > 0f)
            {
                var despawn = pickup.gameObject.AddComponent<TimedDespawn>();
                despawn.TTL = dropDespawnTime;
            }
            return 1;
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
            // Scaled, not authored: a levelled copy of a monster is a tougher kill, so the
            // heuristic that derives XP from how much HP you had to chew through has to see
            // the same pool the player actually fought. Level <= 1 reads identically.
            if (def != null)
            {
                var scaled = def.GetScaledStats();
                return Mathf.Max(1, scaled.hp / 5 + scaled.power);
            }
            if (maxHpFallback > 0) return Mathf.Max(1, maxHpFallback / 5);
            return 5;
        }
    }
}
