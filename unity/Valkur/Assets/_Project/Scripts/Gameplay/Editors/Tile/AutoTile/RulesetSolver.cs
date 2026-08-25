using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Pure logic that turns a 4-bit mask into a slot (<see cref="Blob16Slot"/> for
    /// the cardinal model, <see cref="Corner16Slot"/> for the corner model), and
    /// resolves that slot against a <see cref="TilesetRuleset"/> to produce a sprite.
    /// Which mask a caller should compute for a given ruleset is decided by
    /// <see cref="TilesetRuleset.Model"/> — see <see cref="TerrainTileResolver.ResolveVariantForCell"/>
    /// for the dispatch.
    ///
    /// Stateless — safe under domain reload OFF.
    /// </summary>
    public static class RulesetSolver
    {
        /// <summary>
        /// Maps a 4-bit cardinal mask (low nibble) to its <see cref="Blob16Slot"/>.
        /// Upper nibble is ignored so callers can pass an 8-bit mask without
        /// pre-masking — useful when Blob47 ships and reuses the low 4 bits.
        /// </summary>
        public static Blob16Slot ComputeSlot(byte cardinalMask)
        {
            return (Blob16Slot)(cardinalMask & 0x0F);
        }

        /// <summary>
        /// Picks one variant for the given slot. Returns null if the ruleset is null,
        /// the slot is unassigned, or every variant in the slot is null.
        /// When a slot has multiple variants, the choice is deterministic in
        /// <paramref name="hashSeed"/> so the same cell always renders the same variant.
        /// </summary>
        public static Sprite ResolveVariant(TilesetRuleset ruleset, Blob16Slot slot, int hashSeed)
        {
            if (ruleset == null) return null;
            var variants = ruleset.GetVariants(slot);
            if (variants == null || variants.Length == 0) return null;

            if (variants.Length == 1) return variants[0];

            int idx = ((hashSeed % variants.Length) + variants.Length) % variants.Length;
            return variants[idx];
        }

        /// <summary>
        /// Convenience helper: combines <see cref="ComputeSlot"/> and <see cref="ResolveVariant(TilesetRuleset, Blob16Slot, int)"/>.
        /// </summary>
        public static Sprite Resolve(TilesetRuleset ruleset, byte cardinalMask, int hashSeed)
        {
            return ResolveVariant(ruleset, ComputeSlot(cardinalMask), hashSeed);
        }

        /// <summary>
        /// Maps a 4-bit corner mask (see <see cref="BitmaskCalculator.CornerMask"/>) to
        /// its <see cref="Corner16Slot"/>. Upper nibble is ignored, mirroring
        /// <see cref="ComputeSlot"/>'s forward-compat behavior.
        /// </summary>
        public static Corner16Slot ComputeCornerSlot(byte cornerMask)
        {
            return (Corner16Slot)(cornerMask & 0x0F);
        }

        /// <summary>
        /// Picks one variant for the given corner slot. Same null-ruleset /
        /// unassigned-slot / deterministic-seed rules as
        /// <see cref="ResolveVariant(TilesetRuleset, Blob16Slot, int)"/>.
        /// </summary>
        public static Sprite ResolveVariant(TilesetRuleset ruleset, Corner16Slot slot, int hashSeed)
        {
            if (ruleset == null) return null;
            var variants = ruleset.GetVariants(slot);
            if (variants == null || variants.Length == 0) return null;

            if (variants.Length == 1) return variants[0];

            int idx = ((hashSeed % variants.Length) + variants.Length) % variants.Length;
            return variants[idx];
        }

        /// <summary>
        /// Convenience helper: combines <see cref="ComputeCornerSlot"/> and
        /// <see cref="ResolveVariant(TilesetRuleset, Corner16Slot, int)"/>.
        /// </summary>
        public static Sprite ResolveCorner(TilesetRuleset ruleset, byte cornerMask, int hashSeed)
        {
            return ResolveVariant(ruleset, ComputeCornerSlot(cornerMask), hashSeed);
        }
    }
}
