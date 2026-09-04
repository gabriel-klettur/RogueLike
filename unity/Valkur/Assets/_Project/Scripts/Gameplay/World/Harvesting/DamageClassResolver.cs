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
        /// <para>BEST EQUIPPED, not "the weapon in hand", because the game has no weapon
        /// slot: <c>Inventory.EquipmentSlots</c> is a flat 3x3 grid the player drags items
        /// into, and the armed loadout is art rather than data. Picking whichever equipped
        /// item scores highest against THIS material is both the honest reading of that
        /// model and the one a player expects — carrying an axe and a pick means trees fall
        /// to the axe and rock to the pick without anyone swapping anything. When a real
        /// weapon slot exists this narrows to it and nothing else here changes.</para>
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

            var slots = inventory.EquipmentSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                // InventorySlot is a struct — IsEmpty, never a null comparison.
                if (slots[i].IsEmpty) continue;

                var item = slots[i].Item;
                if (item == null || item.toolClass == DamageClass.None) continue;

                float multiplier = table != null ? table.Multiplier(material, item.toolClass) : 1f;
                if (multiplier <= bestMultiplier) continue;

                bestMultiplier = multiplier;
                best = item.toolClass;
                toolTier = item.toolTier;
            }

            return best;
        }

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
