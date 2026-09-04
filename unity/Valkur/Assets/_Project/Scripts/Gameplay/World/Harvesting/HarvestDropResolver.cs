using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Turns a <see cref="HarvestDropTable"/> into world pickups. Kept apart from
    /// <see cref="BuildingDurability"/> so a felled tree, a smashed crate and a future
    /// mined rock all resolve their loot through one implementation.
    /// </summary>
    public static class HarvestDropResolver
    {
        /// <summary>
        /// How far a stack can land from the destroyed building's centre, in world units.
        /// Scattering matters: several stacks dropped on one point stack their sprites into
        /// what reads as a single pickup, and the player collects one and walks away from
        /// the rest.
        /// </summary>
        private const float SCATTER_RADIUS = 0.55f;

        /// <summary>
        /// The RNG the weighted pool draws from.
        ///
        /// <para><see cref="LootTable.Roll"/> takes a <c>System.Random</c> on purpose — it is
        /// deterministic given a state, which is what lets two machines agree on a drop. There
        /// is no shared state to seed it from here yet, so it is seeded once per session;
        /// the day a run carries a seed, this is the one line that has to read it.</para>
        ///
        /// <para>Domain Reload is OFF, so a fresh instance is assigned rather than reused —
        /// a plain <c>stsfld</c>, the only reset shape DomainReloadStaticResetTests
        /// recognises.</para>
        /// </summary>
        private static System.Random _poolRng = new System.Random();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _poolRng = new System.Random();

        /// <summary>
        /// Draw ONE item from a weighted pool and spawn it. Returns 1 when something dropped.
        ///
        /// <para>Separate from <see cref="SpawnDrops"/> because the two answer different
        /// questions: that one rolls every line independently (a felled oak always gives wood
        /// AND sometimes a sapling), this one makes the lines compete (a swing at a seam gives
        /// you one mineral and the interesting part is which). Sharing the spawn tail keeps
        /// scatter, the catalog lookup and the persistence route in one place.</para>
        /// </summary>
        public static int SpawnFromPool(LootTable pool, Vector3 origin)
        {
            if (pool == null) return 0;

            var item = pool.Roll(_poolRng);
            if (item == null) return 0;

            Vector2 offset = Random.insideUnitCircle * SCATTER_RADIUS;
            var position = new Vector3(origin.x + offset.x, origin.y + offset.y, origin.z);
            return DropSystem.SpawnDrop(item, 1, position) != null ? 1 : 0;
        }

        /// <summary>
        /// Roll every entry and spawn what came up. Returns how many stacks were spawned.
        /// </summary>
        public static int SpawnDrops(HarvestDropTable table, Vector3 origin)
        {
            if (table == null || table.entries == null || table.entries.Count == 0) return 0;

            if (!ServiceLocator.TryGet<ItemCatalog>(out var catalog) || catalog == null)
            {
                Debug.LogWarning("[HarvestDropResolver] No ItemCatalog registered; drops skipped.");
                return 0;
            }

            int spawned = 0;
            for (int i = 0; i < table.entries.Count; i++)
            {
                var entry = table.entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.itemId)) continue;
                if (Random.value > entry.chance) continue;

                var item = catalog.GetById(entry.itemId);
                if (item == null)
                {
                    Debug.LogWarning(
                        $"[HarvestDropResolver] Item id '{entry.itemId}' not in the catalog " +
                        $"(table '{table.name}').");
                    continue;
                }

                int min = Mathf.Max(1, Mathf.Min(entry.minQuantity, entry.maxQuantity));
                int max = Mathf.Max(min, entry.maxQuantity);
                int quantity = Random.Range(min, max + 1);

                Vector2 offset = Random.insideUnitCircle * SCATTER_RADIUS;
                var position = new Vector3(origin.x + offset.x, origin.y + offset.y, origin.z);

                if (DropSystem.SpawnDrop(item, quantity, position) != null) spawned++;
            }
            return spawned;
        }
    }
}
