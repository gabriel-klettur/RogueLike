using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — the <c>statModifiers</c> block of the Properties tab.
    ///
    /// <para>Built as its own block for exactly the reason
    /// <c>SpellsRuntimeEditor.StatusEffects</c> is: every other authored field is one value in
    /// one row and fits through the single reflection call keyed by field name, while this is
    /// a variable-length array of three-field structs and needs its own rows, keys and resize
    /// control.</para>
    ///
    /// <para>It matters that this is reachable at all, and more here than almost anywhere
    /// else. <c>SpellType.Buff</c> has no geometry, no damage and no projectile — the
    /// modifiers ARE the spell. A buff whose array cannot be authored from F4 is a cast that
    /// spends mana, plays its flourish and changes nothing, which is the authored-and-inert
    /// failure this project has recorded eleven times.</para>
    ///
    /// <para>Rows are addressed <c>statmod:&lt;index&gt;:&lt;field&gt;</c> and intercepted in
    /// <c>OnPropertyChanged</c> before the reflection lookup, which would otherwise go looking
    /// for a <c>SpellDefinition</c> field with a colon in its name.</para>
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const string STATMOD_PREFIX = "statmod:";

        /// <summary>Hard ceiling, so a typo in the count row cannot allocate a million entries.
        /// Eight is already more stats than any one buff should move at once.</summary>
        private const int STATMOD_MAX = 8;

        private void AddStatModifierRows(PropertyForm form, SpellDefinition spell)
        {
            if (!SpellFieldRelevance.Applies(spell, "statModifiers")) return;

            var list = spell.statModifiers ?? Array.Empty<StatModifier>();
            AddSectionHeader(form, "── Stat Modifiers ──");
            form.AddInt(STATMOD_PREFIX + "count", "Count", list.Length);

            string[] stats = Enum.GetNames(typeof(StatKind));
            string[] ops = Enum.GetNames(typeof(StatOp));

            for (int i = 0; i < list.Length; i++)
            {
                var entry = list[i];
                form.AddDropdown($"{STATMOD_PREFIX}{i}:stat", $"[{i}] Stat",
                    stats, Mathf.Max(0, Array.IndexOf(stats, entry.stat.ToString())));
                form.AddDropdown($"{STATMOD_PREFIX}{i}:op", "    Operation",
                    ops, Mathf.Max(0, Array.IndexOf(ops, entry.op.ToString())));
                // The label says what the number means for each op, because the same 0.25
                // is +0.25 flat and +25% additive depending on the row above it, and the two
                // are three orders of magnitude apart on a stat like MaxHp.
                form.AddFloat($"{STATMOD_PREFIX}{i}:value", "    Value (flat / fraction)", entry.value);
            }
        }

        /// <summary>
        /// True when <paramref name="key"/> belongs to this block, so the generic handler can
        /// hand it over instead of reflecting on a name that is not a field.
        /// </summary>
        private static bool IsStatModKey(string key)
            => !string.IsNullOrEmpty(key) && key.StartsWith(STATMOD_PREFIX, StringComparison.Ordinal);

        private void OnStatModValueChanged(SpellDefinition spell, string key, object val)
        {
            string rest = key.Substring(STATMOD_PREFIX.Length);
            var before = spell.statModifiers ?? Array.Empty<StatModifier>();

            if (rest == "count")
            {
                int wanted = Mathf.Clamp(ConvertInt(val, before.Length), 0, STATMOD_MAX);
                if (wanted == before.Length) return;

                var resized = new StatModifier[wanted];
                // Copy rather than reallocate blindly: shrinking then growing again must not
                // silently blank the entries that survived the round trip.
                Array.Copy(before, resized, Mathf.Min(before.Length, wanted));
                CommitStatMods(spell, before, resized, $"Stat modifier count → {wanted}", rebuild: true);
                return;
            }

            int split = rest.IndexOf(':');
            if (split <= 0) return;
            if (!int.TryParse(rest.Substring(0, split), out int index)) return;
            if (index < 0 || index >= before.Length) return;   // stale row from a previous size
            string field = rest.Substring(split + 1);

            var after = (StatModifier[])before.Clone();
            var e = after[index];

            switch (field)
            {
                case "stat":
                {
                    int pick = ConvertInt(val, (int)e.stat);
                    var names = Enum.GetNames(typeof(StatKind));
                    if (pick < 0 || pick >= names.Length) return;
                    e.stat = (StatKind)Enum.Parse(typeof(StatKind), names[pick]);
                    break;
                }
                case "op":
                {
                    int pick = ConvertInt(val, (int)e.op);
                    var names = Enum.GetNames(typeof(StatOp));
                    if (pick < 0 || pick >= names.Length) return;
                    e.op = (StatOp)Enum.Parse(typeof(StatOp), names[pick]);
                    break;
                }
                case "value": e.value = ConvertFloat(val, e.value); break;
                default: return;
            }

            if (e.Equals(after[index])) return;   // focus left an untouched row
            after[index] = e;
            CommitStatMods(spell, before, after, $"Stat modifier [{index}] {field}", rebuild: false);
        }

        /// <summary>
        /// Push one modifier edit through the shared undo stack, snapshotting the WHOLE array
        /// either side rather than the one entry that moved — for the reason the status block
        /// records: a resize changes the array's identity, so replaying the edit as an
        /// in-place mutation would undo onto an array of the wrong length.
        /// </summary>
        private void CommitStatMods(SpellDefinition spell, StatModifier[] before,
                                    StatModifier[] after, string label, bool rebuild)
        {
            var target = spell;
            _undo.Do(new UndoStack.LambdaCommand(label,
                doAction:   () => RestoreStatMods(target, after,  rebuild),
                undoAction: () => RestoreStatMods(target, before, rebuild)));
        }

        private void RestoreStatMods(SpellDefinition spell, StatModifier[] snapshot, bool rebuild)
        {
            if (spell == null) return;
            spell.statModifiers = (StatModifier[])snapshot.Clone();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(spell);
#endif
            // Rebuilt only on a resize, when the panel would otherwise show the wrong number
            // of rows. Rebuilding on a value edit would destroy the field still reporting
            // what the user typed.
            if (!rebuild) return;
            _applyingProperty = true;
            try { RefreshPropertiesForm(); }
            finally { _applyingProperty = false; }
        }
    }
}
