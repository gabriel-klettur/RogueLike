using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One line of a <see cref="HarvestDropTable"/>: an item, how many, and how often.
    /// </summary>
    [Serializable]
    public class HarvestDropEntry
    {
        [Tooltip("ItemCatalog id, e.g. 'wood'. An id the catalog does not hold is skipped " +
                 "with one warning, not silently.")]
        public string itemId = "";

        [Tooltip("Smallest stack this entry can produce, inclusive.")]
        public int minQuantity = 1;

        [Tooltip("Largest stack this entry can produce, inclusive.")]
        public int maxQuantity = 1;

        [Tooltip("Probability this entry produces anything at all. 1 = always.")]
        [Range(0f, 1f)] public float chance = 1f;
    }

    /// <summary>
    /// What a destroyed building leaves behind.
    ///
    /// <para>Every entry is rolled INDEPENDENTLY rather than one entry being picked out of
    /// a weighted pool. Harvesting is not a loot box: felling an oak should always give
    /// wood AND sometimes give a sapling, and a weighted pick can only express "one or the
    /// other". Monster loot uses the pooled shape for the opposite reason — a kill drops a
    /// weapon or a potion, not both.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "HDT_NewTable", menuName = "Valkur/World/Harvest Drop Table")]
    public class HarvestDropTable : ScriptableObject
    {
        [Tooltip("Rolled independently, top to bottom. An empty list drops nothing, which " +
                 "is a legitimate profile for scenery that is merely destructible.")]
        public List<HarvestDropEntry> entries = new List<HarvestDropEntry>();
    }
}
