using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Answers "which auto-tile pack is this tile from, and which of its two terrains
    /// does it stand for" from the SPRITE the author picked in the tile picker.
    ///
    /// <para><b>Why not the category name.</b> The AUTO brush used to match
    /// <c>TilesetRuleset.FolderName</c> against the picker's selected category. That
    /// held only while a pack was exactly one folder, and it stopped being true the
    /// moment a pack cut from several sheets got one picker tab per sheet: selecting a
    /// tile from <c>grass_rock_1</c> found no ruleset named <c>grass_rock_1</c> and the
    /// brush went dead, silently, on seven tabs at once. A sprite reference cannot
    /// drift the way a folder name can — the slot that holds it IS the pack's own
    /// statement about what that tile is.</para>
    ///
    /// <para><b>Why the SLOT, not just the pack.</b> A Corner16 pack is two terrains,
    /// not one, and the whole point of the model is the boundary between them. Reading
    /// the pack alone can only ever paint its primary, so the author could never lay
    /// down the secondary and never produce a border at all. The two pure slots answer
    /// it unambiguously: <c>CornerNone</c> is the tile with no corner in the secondary,
    /// i.e. solid PRIMARY; <c>CornerFull</c> is solid SECONDARY. Every other slot is
    /// boundary art and stands for no single terrain, so picking one paints the
    /// secondary — the terrain an author is drawing INTO the field — rather than
    /// refusing the stroke over a distinction they did not intend to make.</para>
    /// </summary>
    public static class AutoBrushPackLookup
    {
        /// <summary>The corner slot whose tile is solid PRIMARY terrain (no corner is secondary).</summary>
        public const byte PurePrimaryMask = 0;

        /// <summary>The corner slot whose tile is solid SECONDARY terrain (all four corners).</summary>
        public const byte PureSecondaryMask = 15;

        /// <summary>
        /// The pack that holds <paramref name="sprite"/> and the corner mask of the slot
        /// it sits in, or <c>(null, 0)</c> when no pack claims it. Bounded by the catalog
        /// (ten rulesets x sixteen slots today) and called on a click, never per frame.
        /// </summary>
        public static (TilesetRuleset Ruleset, byte SlotMask) FindPackAndSlot(
            TerrainCatalog catalog, Sprite sprite)
            => FindPackAndSlot(catalog, sprite, null);

        /// <summary>
        /// As above, with the picker catalog so a DUPLICATE cell resolves too.
        ///
        /// <para>A sliced sheet ships every cell of the source image, and most of them repeat:
        /// each of the 64 cells of a grass_rock sheet is one of only 16 distinct tiles, and the
        /// ruleset references exactly those 16. So a lookup that matches the sprite alone
        /// answers for 16 of the 64 tiles an author can see in the picker and refuses the other
        /// 48 — measured on the shipped sheets, and it is a regression against the older
        /// category-name lookup, which accepted any tile of the pack's folder. An author who
        /// clicks a tile that LOOKS like solid rock has said "rock" whether or not that
        /// particular cell is the one the manifest happened to name.</para>
        ///
        /// <para>The manifest already answers it: <c>TileEntry.uniqueId</c> groups the cells
        /// that are pixel-identical, so the duplicate resolves through the one member of its own
        /// group that the ruleset holds. That is exact rather than a guess — the two cells are
        /// the same image — and it needs no naming convention.</para>
        /// </summary>
        public static (TilesetRuleset Ruleset, byte SlotMask) FindPackAndSlot(
            TerrainCatalog catalog, Sprite sprite, TileCatalog tiles)
        {
            if (sprite == null) return (null, 0);
            if (tiles == null) return FindBySprite(catalog, sprite);

            var index = IndexFor(catalog, tiles);
            return index.TryGetValue(sprite.name, out var hit) ? hit : (null, (byte)0);
        }

        // ── The index ───────────────────────────────────────────────────────────
        //
        // This lookup used to SCAN: the catalog's rulesets and their slots for the sprite, and
        // then, for a duplicate sheet cell, all 3,765 picker entries twice to find the sibling
        // the ruleset names. That was ~0.19 ms a call, which is nothing until you count the
        // calls. The AUTO brush asks it four times per VERTEX plus once per cell, and it runs on
        // every frame of a drag — measured on the shipped project, one click cost 13.4 ms at 1x1
        // and 113.5 ms at 8x8, i.e. 9 fps while painting, growing with the square of the brush.
        // The answer never changes while the catalogs do not, so it is computed once.
        //
        // Domain Reload is OFF, so these are reset explicitly on entering Play. Plain field
        // assignment, because DomainReloadStaticResetTests reads the hook's IL and only
        // recognises stsfld or field.Clear().
        private static TerrainCatalog _indexedCatalog;
        private static TileCatalog _indexedTiles;
        private static Dictionary<string, (TilesetRuleset Ruleset, byte SlotMask)> _index;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _index = null;
            _indexedCatalog = null;
            _indexedTiles = null;
        }

        /// <summary>Drops the index so the next lookup rebuilds it. Call after a ruleset import.</summary>
        public static void InvalidateIndex()
        {
            _index = null;
            _indexedCatalog = null;
            _indexedTiles = null;
        }

        private static Dictionary<string, (TilesetRuleset, byte)> IndexFor(
            TerrainCatalog catalog, TileCatalog tiles)
        {
            // Reference equality on both: a re-import hands back different objects, and that is
            // exactly when a cached answer would be stale.
            if (_index != null && ReferenceEquals(_indexedCatalog, catalog) && ReferenceEquals(_indexedTiles, tiles))
                return _index;

            var index = new Dictionary<string, (TilesetRuleset, byte)>();

            // 1. Every sprite a ruleset names outright.
            if (catalog != null)
            {
                var rulesets = catalog.Rulesets;
                for (int i = 0; i < rulesets.Count; i++)
                {
                    var ruleset = rulesets[i];
                    if (ruleset == null) continue;
                    AddSlots(index, ruleset, ruleset.CornerSlots.Select(c => ((byte)c.slot, c.variants)));
                    AddSlots(index, ruleset, ruleset.Slots.Select(b => ((byte)b.slot, b.variants)));
                }
            }

            // 2. The duplicate cells of a sliced sheet, through the manifest's uniqueId — the
            //    64 cells of a sheet are only 16 distinct images and the ruleset names those 16,
            //    so without this step three quarters of what an author can click is unreachable.
            if (tiles != null)
            {
                var byGroup = new Dictionary<(string, int), List<string>>();
                var entries = tiles.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.uniqueId < 0 || string.IsNullOrEmpty(e.tileName)) continue;
                    var key = (e.category, e.uniqueId);
                    if (!byGroup.TryGetValue(key, out var names)) byGroup[key] = names = new List<string>();
                    names.Add(e.tileName);
                }

                foreach (var group in byGroup.Values)
                {
                    (TilesetRuleset, byte)? known = null;
                    for (int i = 0; i < group.Count && known == null; i++)
                        if (index.TryGetValue(group[i], out var hit)) known = hit;
                    if (known == null) continue;

                    for (int i = 0; i < group.Count; i++)
                        if (!index.ContainsKey(group[i])) index[group[i]] = known.Value;
                }
            }

            _index = index;
            _indexedCatalog = catalog;
            _indexedTiles = tiles;
            return index;
        }

        private static void AddSlots(Dictionary<string, (TilesetRuleset, byte)> index,
                                     TilesetRuleset ruleset, IEnumerable<(byte Mask, Sprite[] Variants)> slots)
        {
            foreach (var (mask, variants) in slots)
            {
                if (variants == null) continue;
                for (int i = 0; i < variants.Length; i++)
                {
                    var sprite = variants[i];
                    if (sprite == null || string.IsNullOrEmpty(sprite.name)) continue;
                    // First writer wins, matching the scan's own order: rulesets in catalog
                    // order, corner slots before blob slots.
                    if (!index.ContainsKey(sprite.name)) index[sprite.name] = (ruleset, mask);
                }
            }
        }

        private static (TilesetRuleset Ruleset, byte SlotMask) FindBySprite(
            TerrainCatalog catalog, Sprite sprite)
        {
            if (catalog == null || sprite == null) return (null, 0);

            var rulesets = catalog.Rulesets;
            for (int i = 0; i < rulesets.Count; i++)
            {
                var ruleset = rulesets[i];
                if (ruleset == null) continue;

                var cornerSlots = ruleset.CornerSlots;
                for (int s = 0; s < cornerSlots.Count; s++)
                {
                    if (!HoldsSprite(cornerSlots[s].variants, sprite)) continue;
                    return (ruleset, (byte)cornerSlots[s].slot);
                }

                var slots = ruleset.Slots;
                for (int s = 0; s < slots.Count; s++)
                {
                    if (!HoldsSprite(slots[s].variants, sprite)) continue;
                    return (ruleset, (byte)slots[s].slot);
                }
            }
            return (null, 0);
        }

        /// <summary>
        /// Which of the pack's two terrains a tile in <paramref name="slotMask"/> stands
        /// for. A Blob16 pack has no secondary and always answers its primary.
        /// </summary>
        public static string TerrainForSlot(TilesetRuleset ruleset, byte slotMask)
        {
            if (ruleset == null) return null;
            if (string.IsNullOrEmpty(ruleset.TerrainSecondary)) return ruleset.TerrainPrimary;
            return slotMask == PurePrimaryMask ? ruleset.TerrainPrimary : ruleset.TerrainSecondary;
        }

        /// <summary>
        /// The pack's other terrain, for the status line that tells an author how to
        /// paint the far side of the border they are drawing. Empty for a pack with
        /// only one terrain.
        /// </summary>
        public static string OtherTerrain(TilesetRuleset ruleset, string terrain)
        {
            if (ruleset == null) return string.Empty;
            if (string.IsNullOrEmpty(ruleset.TerrainSecondary)) return string.Empty;
            return terrain == ruleset.TerrainSecondary ? ruleset.TerrainPrimary : ruleset.TerrainSecondary;
        }

        private static bool HoldsSprite(Sprite[] variants, Sprite sprite)
        {
            if (variants == null) return false;
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null) continue;
                // Name as well as reference: TerrainTileResolver rebuilds Tiles around
                // sprites across Domain Reload, and a picker entry can hold a different
                // live Sprite object for the same asset.
                if (variants[i] == sprite || variants[i].name == sprite.name) return true;
            }
            return false;
        }
    }
}
