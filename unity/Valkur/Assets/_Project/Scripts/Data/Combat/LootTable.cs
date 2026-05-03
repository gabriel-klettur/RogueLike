using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Weighted drop table. One asset per drop pool (e.g. <c>WolfLoot</c>,
    /// <c>BossDungeonLoot</c>); referenced by spawners / monster definitions
    /// to choose what falls when the source dies.
    ///
    /// Each entry is an <see cref="ItemDefinition"/> + a per-mille weight.
    /// When weights are zero, the table falls back to <see cref="RarityPalette.DefaultDropWeight"/>
    /// using the item's rarity, so a designer can drop in a list of items
    /// and immediately get genre-typical odds without authoring weights by
    /// hand.
    ///
    /// Selection is fully deterministic given a (System.Random) — pass the
    /// caller's RNG so the same encounter always produces the same drop.
    /// Pure data + pure function: no Unity API surface, EditMode-testable.
    /// </summary>
    [CreateAssetMenu(fileName = "NewLootTable", menuName = "Valkur/Data/Loot Table")]
    public sealed class LootTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public ItemDefinition item;
            [Tooltip("Per-mille weight. 0 = derive from item.rarity via " +
                     "RarityPalette.DefaultDropWeight (Common 600, Uncommon 250, " +
                     "Rare 100, Epic 40, Legendary 10).")]
            [Min(0)] public int weight;
        }

        [Header("Drop pool")]
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [Tooltip("Probability per-mille that this drop fires at all. 1000 = always rolls. " +
                 "200 = 20% chance the source drops anything. Stack with monster-level " +
                 "drop modifiers in the caller.")]
        [Range(0, 1000)] [SerializeField] private int dropChancePerMille = 1000;

        public IReadOnlyList<Entry> Entries => entries;
        public int DropChancePerMille => dropChancePerMille;

        /// <summary>
        /// Pick a single item from the table using <paramref name="rng"/>.
        /// Returns <c>null</c> when:
        ///   - the table is empty,
        ///   - the dropChance roll fails,
        ///   - all weights resolve to 0 (mis-authored table).
        ///
        /// The probability comparison is pure-integer (per-mille) so two
        /// machines with the same RNG state pick the same item — Phase-4
        /// networking parity even for loot.
        /// </summary>
        public ItemDefinition Roll(System.Random rng)
        {
            if (rng == null) return null;
            if (entries == null || entries.Length == 0) return null;

            // Outer drop-chance gate.
            if (dropChancePerMille < 1000 && rng.Next(1000) >= dropChancePerMille)
                return null;

            int total = ComputeTotalWeight();
            if (total <= 0) return null;

            int roll = rng.Next(total);
            int cursor = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                int w = ResolveWeight(entries[i]);
                if (w <= 0) continue;
                cursor += w;
                if (roll < cursor) return entries[i].item;
            }
            // Fall-through guard for FP rounding (shouldn't hit with int math).
            return entries[entries.Length - 1].item;
        }

        public int ComputeTotalWeight()
        {
            if (entries == null) return 0;
            int total = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                int w = ResolveWeight(entries[i]);
                if (w > 0) total += w;
            }
            return total;
        }

        private static int ResolveWeight(Entry e)
        {
            if (e == null || e.item == null) return 0;
            if (e.weight > 0) return e.weight;
            return RarityPalette.DefaultDropWeight(e.item.rarity);
        }

#if UNITY_EDITOR
        public void EditorSetEntries(Entry[] newEntries)
        {
            entries = newEntries ?? Array.Empty<Entry>();
            UnityEditor.EditorUtility.SetDirty(this);
        }
        public void EditorSetDropChance(int perMille)
        {
            dropChancePerMille = Mathf.Clamp(perMille, 0, 1000);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
