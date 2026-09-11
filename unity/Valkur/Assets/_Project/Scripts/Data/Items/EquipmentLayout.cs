namespace Valkur.Data
{
    /// <summary>The eight places on a character an item can be worn.</summary>
    public enum EquipmentSlotKind
    {
        Head = 0,
        Amulet = 1,
        Weapon = 2,
        Offhand = 3,
        Chest = 4,
        Ring = 5,
        Boots = 6,
        Trinket = 7,
    }

    /// <summary>
    /// The player's equipment slots, and which items each one accepts.
    ///
    /// <para><b>Why it exists.</b> The equipment used to be nine untyped cells labelled
    /// "L. Hand / Helmet / R. Hand / Arms / Chest / Gloves / Pants / Boots / Jewelry". Any item
    /// fitted any cell, so a potion sat in the helmet, and <c>EquipmentStatSource</c> summed every
    /// equippable item in all nine — nine longswords were +162 melee damage. Four of the labels
    /// (Arms, Gloves, Pants, Jewelry) named places no item in the catalogue could ever go, because
    /// the DATA speaks <see cref="EquipSlot"/> and the UI was speaking something else.</para>
    ///
    /// <para>The slots here are the data's vocabulary, collapsed where the data has aliases
    /// (<c>Head</c>/<c>Helmet</c>, <c>Body</c>/<c>Chest</c>, <c>Shield</c>/<c>Book</c>/<c>Offhand</c>,
    /// <c>Trinket</c>/<c>Accessory</c>). An index is a slot kind: index <c>i</c> is
    /// <c>(EquipmentSlotKind)i</c>, stored in that order in the save.</para>
    /// </summary>
    public static class EquipmentLayout
    {
        /// <summary>Number of equipment slots.</summary>
        public const int Count = 8;

        /// <summary>The kind of slot at <paramref name="index"/>.</summary>
        public static EquipmentSlotKind KindAt(int index) => (EquipmentSlotKind)index;

        /// <summary>The slot an item of <paramref name="slot"/> goes in, or -1 for none.</summary>
        public static int IndexFor(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Head:
                case EquipSlot.Helmet: return (int)EquipmentSlotKind.Head;
                case EquipSlot.Amulet: return (int)EquipmentSlotKind.Amulet;
                case EquipSlot.Weapon: return (int)EquipmentSlotKind.Weapon;
                case EquipSlot.Offhand:
                case EquipSlot.Shield:
                case EquipSlot.Book: return (int)EquipmentSlotKind.Offhand;
                case EquipSlot.Body:
                case EquipSlot.Chest: return (int)EquipmentSlotKind.Chest;
                case EquipSlot.Ring: return (int)EquipmentSlotKind.Ring;
                case EquipSlot.Boots: return (int)EquipmentSlotKind.Boots;
                case EquipSlot.Trinket:
                case EquipSlot.Accessory: return (int)EquipmentSlotKind.Trinket;
                default: return -1;
            }
        }

        /// <summary>The slot <paramref name="item"/> goes in, or -1 when it is not wearable.</summary>
        public static int IndexFor(ItemDefinition item) => item != null ? IndexFor(item.equipSlot) : -1;

        /// <summary>True when <paramref name="item"/> may be worn in slot <paramref name="index"/>.</summary>
        public static bool Accepts(int index, ItemDefinition item)
            => item != null && index >= 0 && index < Count && IndexFor(item) == index;

        /// <summary>Player-facing name of a slot.</summary>
        public static string DisplayName(EquipmentSlotKind kind)
        {
            switch (kind)
            {
                case EquipmentSlotKind.Head: return "Cabeza";
                case EquipmentSlotKind.Amulet: return "Amuleto";
                case EquipmentSlotKind.Weapon: return "Arma";
                case EquipmentSlotKind.Offhand: return "Mano izquierda";
                case EquipmentSlotKind.Chest: return "Torso";
                case EquipmentSlotKind.Ring: return "Anillo";
                case EquipmentSlotKind.Boots: return "Botas";
                case EquipmentSlotKind.Trinket: return "Abalorio";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// The level an item asks for, where 0 means "no requirement". The field defaults to 1 on
        /// every item while the character model starts at level 0 — reading it raw would refuse
        /// every default item to a new character.
        /// </summary>
        public static int RequiredLevel(ItemDefinition item)
            => item != null && item.levelRequirement > 1 ? item.levelRequirement : 0;
    }
}
