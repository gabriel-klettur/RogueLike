using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Decides which <see cref="DamageClass"/> a blow belongs to. The single owner of that
    /// question, so the resistance matrix can never be consulted with two different answers
    /// for the same swing.
    /// </summary>
    public static class DamageClassResolver
    {
        /// <summary>
        /// A magical blow is classified by its element and a physical one by the attacker's
        /// best equipped tool.
        ///
        /// <para>BEST EQUIPPED — OR CARRIED, FOR A TRUE TOOL. The equipment is typed and has ONE
        /// weapon slot, which axes, picks and swords all claim. Requiring the axe to be IN that
        /// slot would make every tree a trip to the inventory to swap a sword out and back in,
        /// which is friction with no decision behind it. So a real tool (Axe, Pick) counts from
        /// the bag as well — the woodcutter carries the axe on the belt — while a WEAPON class
        /// (Blade, Blunt) counts only when equipped: a spare sword in the bag is not a hatchet.
        /// Whichever item scores highest against THIS material wins, so carrying an axe and a
        /// pick means trees fall to the axe and rock to the pick without anyone swapping.</para>
        /// </summary>
        public static DamageClass Resolve(GameObject attacker, SpellElement? element,
            MaterialClass material, DestructionResistanceTable table, out int toolTier)
        {
            toolTier = 0;

            if (element.HasValue) return FromElement(element.Value);
            if (attacker == null) return DamageClass.None;

            var inventory = attacker.GetComponentInParent<Valkur.Gameplay.Inventory.Inventory>();
            if (inventory == null) return DamageClass.None;

            var best = DamageClass.None;
            float bestMultiplier = table != null ? table.Multiplier(material, DamageClass.None) : 0f;

            Consider(inventory.EquipmentSlots, carriedOnly: false, material, table,
                     ref best, ref bestMultiplier, ref toolTier);
            Consider(inventory.Slots, carriedOnly: true, material, table,
                     ref best, ref bestMultiplier, ref toolTier);

            return best;
        }

        private static void Consider(
            System.Collections.Generic.IReadOnlyList<Valkur.Gameplay.Inventory.InventorySlot> slots,
            bool carriedOnly, MaterialClass material, DestructionResistanceTable table,
            ref DamageClass best, ref float bestMultiplier, ref int toolTier)
        {
            if (slots == null) return;

            for (int i = 0; i < slots.Count; i++)
            {
                // InventorySlot is a struct — IsEmpty, never a null comparison.
                if (slots[i].IsEmpty) continue;

                var item = slots[i].Item;
                if (item == null || item.toolClass == DamageClass.None) continue;
                if (carriedOnly && !IsTool(item.toolClass)) continue;

                float multiplier = table != null ? table.Multiplier(material, item.toolClass) : 1f;
                if (multiplier < bestMultiplier) continue;
                if (multiplier == bestMultiplier && item.toolTier <= toolTier) continue;

                bestMultiplier = multiplier;
                best = item.toolClass;
                toolTier = item.toolTier;
            }
        }

        /// <summary>A class that is a working tool rather than a weapon, and so counts from the bag.</summary>
        public static bool IsTool(DamageClass damageClass) =>
            damageClass == DamageClass.Axe || damageClass == DamageClass.Pick;

        /// <summary>
        /// Elements map one-to-one onto the magical half of <see cref="DamageClass"/>, with
        /// one deliberate exception: a boomerang is a thrown BLADE, not a school of magic,
        /// and reads as one against wood.
        /// </summary>
        public static DamageClass FromElement(SpellElement element)
        {
            switch (element)
            {
                case SpellElement.Fire:      return DamageClass.Fire;
                case SpellElement.Ice:       return DamageClass.Ice;
                case SpellElement.Lightning: return DamageClass.Lightning;
                case SpellElement.Arcane:    return DamageClass.Arcane;
                case SpellElement.Dark:      return DamageClass.Dark;
                case SpellElement.Light:     return DamageClass.Light;
                case SpellElement.Boomerang: return DamageClass.Blade;
                default:                     return DamageClass.Arcane;
            }
        }

        /// <summary>Whether a class is swung rather than cast. Only these are tier-gated.</summary>
        public static bool IsPhysical(DamageClass damageClass)
        {
            return damageClass == DamageClass.None
                || damageClass == DamageClass.Axe
                || damageClass == DamageClass.Pick
                || damageClass == DamageClass.Blade
                || damageClass == DamageClass.Blunt;
        }
    }
}
