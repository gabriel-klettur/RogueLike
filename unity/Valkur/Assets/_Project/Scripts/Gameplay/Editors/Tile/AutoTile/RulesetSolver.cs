using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Pure logic that turns a 4-bit cardinal mask into a Blob16 slot, and resolves
    /// that slot against a <see cref="TilesetRuleset"/> to produce a sprite.
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
        /// Convenience helper: combines <see cref="ComputeSlot"/> and <see cref="ResolveVariant"/>.
        /// </summary>
        public static Sprite Resolve(TilesetRuleset ruleset, byte cardinalMask, int hashSeed)
        {
            return ResolveVariant(ruleset, ComputeSlot(cardinalMask), hashSeed);
        }
    }
}
