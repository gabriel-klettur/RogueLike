using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Turns what the player is WEARING into the <see cref="StatLayer.Equipment"/> layer.
    ///
    /// This is the line that makes 180 authored items matter. <c>ItemDefinition.damage</c>,
    /// <c>attackSpeed</c>, <c>critChance</c> and <c>critMultiplier</c> had been authored on
    /// 14 shipped items and read by NOTHING in combat — the only consumers in the project
    /// were the F7 editor's table and a resolver that reads equipment to decide how well
    /// you chop down a tree. Equipping the best sword in the catalogue left the player's
    /// swing at the class's base 1 or 2 damage.
    ///
    /// It rebuilds the whole layer on every inventory change rather than tracking equips
    /// and unequips, for the reason the whole store is built that way: a rebuild makes
    /// "equipped", "unequipped", "swapped" and "loaded a save" one code path, and none of
    /// them can leave a stale bonus behind.
    /// </summary>
    [RequireComponent(typeof(PlayerStats))]
    public sealed class EquipmentStatSource : MonoBehaviour
    {
        private PlayerStats _stats;
        private Inventory.Inventory _inventory;

        private readonly List<StatModifier> _scratch = new List<StatModifier>(24);
        private readonly ItemDefinition[] _equipped =
            new ItemDefinition[Inventory.EquipmentView.SLOT_COUNT];

        private void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _inventory = GetComponent<Inventory.Inventory>();
        }

        private void OnEnable()
        {
            if (_inventory == null) _inventory = GetComponent<Inventory.Inventory>();
            if (_inventory != null) _inventory.OnInventoryChanged += Rebuild;
            Rebuild();
        }

        private void OnDisable()
        {
            if (_inventory != null) _inventory.OnInventoryChanged -= Rebuild;
        }

        /// <summary>Recollects every equipped item's contribution and replaces the layer.</summary>
        public void Rebuild()
        {
            if (_stats == null) _stats = GetComponent<PlayerStats>();
            if (_stats == null) return;

            _scratch.Clear();

            if (_inventory != null)
            {
                Inventory.EquipmentView.Resolve(_inventory, _equipped);
                foreach (var item in _equipped)
                    AppendItem(item, _scratch);
            }

            _stats.SetLayer(StatLayer.Equipment, _scratch);
        }

        /// <summary>
        /// Every modifier one item contributes. Public and static so an item tooltip can
        /// show exactly what equipping it would do, using the same code that will actually
        /// do it — a tooltip computed separately is a second source of truth that goes
        /// stale the first time this mapping is retuned.
        /// </summary>
        public static void AppendItem(ItemDefinition item, List<StatModifier> into)
        {
            if (item == null || into == null) return;
            if (item.equipSlot == EquipSlot.None) return;

            if (item.damage != 0)
                into.Add(StatModifier.Flat(StatKind.MeleeDamage, item.damage));

            // attackSpeed is a RATE multiplier in the shipped data (authored 0.8 to 1.5,
            // with 1 as normal), while the stat store speaks in cooldown SECONDS. The
            // conversion is the reciprocal, not the negation: 1.5 attacks per second is a
            // cooldown of 1/1.5, i.e. -33 %, and writing -0.5 there would have made a fast
            // weapon halve the interval instead of shortening it by a third.
            if (item.attackSpeed > 0f && !Mathf.Approximately(item.attackSpeed, 1f))
                into.Add(StatModifier.Percent(StatKind.MeleeCooldown, (1f / item.attackSpeed) - 1f));

            if (item.critChance != 0f)
                into.Add(StatModifier.Flat(StatKind.CritChance, item.critChance));

            // critMultiplier rests at 1 in the schema, so the BONUS is what it exceeds 1 by.
            // Adding the raw field would give every weapon in the catalogue a free +150 %
            // crit damage just for carrying its own default.
            if (item.critMultiplier > 1f)
                into.Add(StatModifier.Flat(StatKind.CritMultiplier, item.critMultiplier - 1f));

            // ItemDefinition.range is deliberately NOT mapped. Its shipped values are 1, 2,
            // 5, 6 and 8 against a melee reach authored between 0.6 and 3.0 world units, so
            // it is plainly in some other unit — almost certainly the Python tile or pixel
            // scale this project has already caught leaking five separate times (wallWidth,
            // the totem radius, the vortex radius, coneLength, arcane_flame's radius). A
            // guessed conversion would be the sixth. Reach is authored deliberately through
            // statModifiers until someone establishes what the field meant.

            if (item.statModifiers != null)
                into.AddRange(item.statModifiers);
        }
    }
}
