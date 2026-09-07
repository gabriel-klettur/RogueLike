using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Restricts a pack's slot variants to the sprites of ONE picker category — the sheet the
    /// author actually clicked.
    ///
    /// <para><b>Why a pack is not a sheet.</b> A Corner16 pack may be cut from several sheets of
    /// the same terrain pair, and they are merged into one ruleset on purpose: a terrain NAME
    /// resolves to exactly one ruleset, so four packs all claiming 'grass' would leave three
    /// permanently unreachable. The cost of that merge lands at PAINT time. <c>grass_rock</c>
    /// carries seven sheets, so each of its sixteen slots holds eight or nine variants and
    /// <see cref="RulesetSolver"/> picks one per cell by hash — measured on a single stroke over
    /// a 12x8 field, SEVEN visually different arts scattered cell by cell, with a grass tile
    /// from one sheet sitting against a grass tile from another. The pack was internally
    /// consistent and the result disagreed only with the screen.</para>
    ///
    /// <para>The author already answered the question by picking a tile. Every cell of that
    /// stroke resolves from that tile's own category, so a sheet stays a coherent look and the
    /// four grass_rock variants become four choices instead of noise. Variety WITHIN a sheet is
    /// untouched: a category that really ships two tiles for one slot still alternates by cell
    /// hash.</para>
    ///
    /// <para><b>Only a COMPLETE sheet may drive a stroke.</b> A category that covers some of
    /// the pack's sixteen slots and not the rest is worse than no filter at all: the covered
    /// cells come from it and every other cell falls back to the whole pack, so one stroke
    /// mixes the chosen art with all the others — measured on <c>tileset6</c>, which
    /// contributes ONE tile to grass_rock, eight different sheets in a single stroke. A
    /// partial sheet is a leftover, not a look. Picking one substitutes the pack's first
    /// complete sheet and the status line names it, because silently painting a scatter is
    /// the failure this whole filter exists to remove.</para>
        ///
    /// <para>Within a complete sheet, a slot it does not cover cannot arise; the fallback to
    /// the pack's full list stays as the guard for a pack whose data changes underneath —
    /// a hole in the middle of a stroke reads as a broken editor.</para>
    /// </summary>
    public sealed class AutoTileSheetFilter
    {
        private readonly HashSet<string> _allowed;

        /// <summary>The picker category this filter came from, for the status line.</summary>
        public string Category { get; }

        private AutoTileSheetFilter(string category, HashSet<string> allowed)
        {
            Category = category;
            _allowed = allowed;
        }

        /// <summary>
        /// The filter for the category that holds <paramref name="selected"/>, or null when the
        /// catalog is missing or nothing claims that sprite. Null means "no restriction", which
        /// is the behaviour every caller had before this existed.
        /// </summary>
        public static AutoTileSheetFilter ForSprite(TileCatalog catalog, Sprite selected)
            => ForSprite(catalog, selected, null);

        /// <summary>
        /// As above, but when <paramref name="pack"/> is supplied the chosen sheet must cover
        /// every one of its slots. A partial sheet is replaced by the pack's first complete one.
        /// </summary>
        public static AutoTileSheetFilter ForSprite(TileCatalog catalog, Sprite selected, TilesetRuleset pack)
        {
            if (catalog == null || selected == null) return null;

            string category = CategoryOf(catalog, selected.name);
            if (string.IsNullOrEmpty(category)) return null;

            var filter = Build(catalog, category);
            if (pack == null || filter == null || filter.Covers(pack)) return filter;

            foreach (string sibling in CompleteSheetsOf(catalog, pack))
            {
                var complete = Build(catalog, sibling);
                if (complete != null) return complete;
            }
            return filter;
        }

        /// <summary>Every category of <paramref name="pack"/> that covers all of its slots,
        /// in a stable order so the substitution is the same on every stroke.</summary>
        public static List<string> CompleteSheetsOf(TileCatalog catalog, TilesetRuleset pack)
        {
            var result = new List<string>();
            if (catalog == null || pack == null) return result;

            var slotsByCategory = new Dictionary<string, HashSet<byte>>();
            var all = new HashSet<byte>();
            foreach (var slot in pack.CornerSlots)
            {
                all.Add((byte)slot.slot);
                if (slot.variants == null) continue;
                foreach (var sprite in slot.variants)
                {
                    if (sprite == null) continue;
                    string category = CategoryOf(catalog, sprite.name);
                    if (string.IsNullOrEmpty(category)) continue;
                    if (!slotsByCategory.TryGetValue(category, out var covered))
                        slotsByCategory[category] = covered = new HashSet<byte>();
                    covered.Add((byte)slot.slot);
                }
            }

            foreach (var kv in slotsByCategory)
                if (kv.Value.Count == all.Count) result.Add(kv.Key);
            result.Sort(System.StringComparer.Ordinal);
            return result;
        }

        /// <summary>True when this sheet has a tile for every slot of <paramref name="pack"/>.</summary>
        public bool Covers(TilesetRuleset pack)
        {
            if (pack == null) return true;
            foreach (var slot in pack.CornerSlots)
                if (CountAllowed(slot.variants) == 0) return false;
            return true;
        }

        private static string CategoryOf(TileCatalog catalog, string spriteName)
        {
            var entries = catalog.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].tileName == spriteName) return entries[i].category;
            return null;
        }

        private static AutoTileSheetFilter Build(TileCatalog catalog, string category)
        {
            var allowed = new HashSet<string>();
            var entries = catalog.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].category == category && !string.IsNullOrEmpty(entries[i].tileName))
                    allowed.Add(entries[i].tileName);
            return allowed.Count == 0 ? null : new AutoTileSheetFilter(category, allowed);
        }

        /// <summary>Test seam: a filter over an explicit set of sprite names.</summary>
        public static AutoTileSheetFilter ForNames(string category, IEnumerable<string> names)
        {
            if (names == null) return null;
            var allowed = new HashSet<string>(names);
            return allowed.Count == 0 ? null : new AutoTileSheetFilter(category, allowed);
        }

        public bool Allows(Sprite sprite) => sprite != null && _allowed.Contains(sprite.name);

        /// <summary>
        /// How many of <paramref name="variants"/> this filter admits. Zero means the caller
        /// must fall back to the whole list.
        /// </summary>
        public int CountAllowed(Sprite[] variants)
        {
            if (variants == null) return 0;
            int n = 0;
            for (int i = 0; i < variants.Length; i++)
                if (Allows(variants[i])) n++;
            return n;
        }

        /// <summary>
        /// The <paramref name="ordinal"/>-th admitted variant, counting from zero. Walks the
        /// array rather than building a filtered copy: this runs once per cell of every stroke,
        /// and a per-cell allocation there is what a brush drag multiplies by a few hundred.
        /// </summary>
        public Sprite NthAllowed(Sprite[] variants, int ordinal)
        {
            if (variants == null) return null;
            int n = 0;
            for (int i = 0; i < variants.Length; i++)
            {
                if (!Allows(variants[i])) continue;
                if (n == ordinal) return variants[i];
                n++;
            }
            return null;
        }
    }
}
