using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — the <c>statusApplications</c> block of the Properties tab.
    ///
    /// <para>Every other authored field is one value in one row, which is why they all fit
    /// through one reflection call keyed by field name. This one is a variable-length array
    /// of four-field structs, so it needs its own rows, its own keys and its own resize
    /// control — and putting that in the main Properties file would have doubled it.</para>
    ///
    /// <para>Rows are addressed <c>status:&lt;index&gt;:&lt;field&gt;</c> and intercepted in
    /// <c>OnPropertyChanged</c> before the reflection lookup, which would otherwise go
    /// looking for a <c>SpellDefinition</c> field with a colon in its name.</para>
    ///
    /// <para>It matters that this is reachable at all: <c>StatusApplicationFactory.ApplyAll</c>
    /// runs on every spell that lands a hit, and a fresh array entry defaults to
    /// <c>chance = 0</c> — "never". Authored anywhere but here it is invisible, and a spell
    /// that was supposed to burn simply does not.</para>
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const string STATUS_PREFIX = "status:";

        /// <summary>Hard ceiling on the array, so a typo in the count row cannot allocate a million entries.</summary>
        private const int STATUS_MAX = 8;

        private void AddStatusApplicationRows(PropertyForm form, SpellDefinition spell)
        {
            if (!SpellFieldRelevance.Applies(spell, "statusApplications")) return;

            var list = spell.statusApplications ?? Array.Empty<StatusApplication>();
            AddSectionHeader(form, "── Status Effects ──");
            form.AddInt(STATUS_PREFIX + "count", "Count", list.Length);

            string[] kinds = Enum.GetNames(typeof(StatusEffectKind));
            for (int i = 0; i < list.Length; i++)
            {
                var entry = list[i];
                form.AddDropdown($"{STATUS_PREFIX}{i}:type", $"[{i}] Type",
                    kinds, Mathf.Max(0, Array.IndexOf(kinds, entry.type.ToString())));
                form.AddFloat($"{STATUS_PREFIX}{i}:duration",  "    Duration (s)", entry.duration);
                form.AddFloat($"{STATUS_PREFIX}{i}:magnitude", "    Magnitude",    entry.magnitude);
                form.AddFloat($"{STATUS_PREFIX}{i}:chance",    "    Chance 0-1",   entry.chance);
            }
        }

        /// <summary>
        /// True when <paramref name="key"/> belongs to this block, so the generic handler
        /// can hand it over instead of reflecting on a name that is not a field.
        /// </summary>
        private static bool IsStatusKey(string key)
            => !string.IsNullOrEmpty(key) && key.StartsWith(STATUS_PREFIX, StringComparison.Ordinal);

        private void OnStatusValueChanged(SpellDefinition spell, string key, object val)
        {
            string rest = key.Substring(STATUS_PREFIX.Length);
            var before = spell.statusApplications ?? Array.Empty<StatusApplication>();

            if (rest == "count")
            {
                int wanted = Mathf.Clamp(ConvertInt(val, before.Length), 0, STATUS_MAX);
                if (wanted == before.Length) return;

                var resized = new StatusApplication[wanted];
                // Copy rather than reallocate blindly: shrinking then growing again must not
                // silently blank the entries that survived the round trip.
                Array.Copy(before, resized, Mathf.Min(before.Length, wanted));
                CommitStatus(spell, before, resized, $"Status count → {wanted}", rebuild: true);
                return;
            }

            int split = rest.IndexOf(':');
            if (split <= 0) return;
            if (!int.TryParse(rest.Substring(0, split), out int index)) return;
            if (index < 0 || index >= before.Length) return;   // stale row from a previous size
            string field = rest.Substring(split + 1);

            var after = (StatusApplication[])before.Clone();
            var e = after[index];

            switch (field)
            {
                case "type":
                    int pick = ConvertInt(val, (int)e.type);
                    var names = Enum.GetNames(typeof(StatusEffectKind));
                    if (pick < 0 || pick >= names.Length) return;
                    e.type = (StatusEffectKind)Enum.Parse(typeof(StatusEffectKind), names[pick]);
                    break;
                case "duration":  e.duration  = ConvertFloat(val, e.duration);  break;
                case "magnitude": e.magnitude = ConvertFloat(val, e.magnitude); break;
                // Clamped because the field carries [Range(0,1)] and the factory rolls
                // against it directly — a 5 typed here would read as "always" forever.
                case "chance":    e.chance    = Mathf.Clamp01(ConvertFloat(val, e.chance)); break;
                default: return;
            }

            if (e.Equals(after[index])) return;   // focus left an untouched row
            after[index] = e;
            CommitStatus(spell, before, after, $"Status [{index}] {field}", rebuild: field == "chance");
        }

        /// <summary>
        /// Push one status edit through the shared undo stack.
        ///
        /// <para>The whole array is snapshotted either side rather than the one entry that
        /// moved, for the reason the gather bag is: a resize changes the array's identity,
        /// so replaying it as an in-place mutation would undo onto an array of the wrong
        /// length. Arrays here are at most <see cref="STATUS_MAX"/> four-field structs.</para>
        /// </summary>
        private void CommitStatus(SpellDefinition spell, StatusApplication[] before,
                                  StatusApplication[] after, string label, bool rebuild)
        {
            var target = spell;
            _undo.Do(new UndoStack.LambdaCommand(label,
                doAction:   () => RestoreStatus(target, after,  rebuild),
                undoAction: () => RestoreStatus(target, before, rebuild)));
        }

        private void RestoreStatus(SpellDefinition spell, StatusApplication[] snapshot, bool rebuild)
        {
            if (spell == null) return;
            spell.statusApplications = (StatusApplication[])snapshot.Clone();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(spell);
#endif
            // Rebuilt only when the panel would otherwise lie: a resize changes how many rows
            // there are, and `chance` is clamped on the way in, so the field can be holding a
            // number that is not what was stored. Every other edit already shows what the user
            // typed, and rebuilding there would destroy the row still reporting it.
            if (!rebuild) return;
            _applyingProperty = true;
            try { RefreshPropertiesForm(); }
            finally { _applyingProperty = false; }
        }

        private static int ConvertInt(object val, int fallback)
        {
            if (val is int i) return i;
            if (val is float f) return Mathf.RoundToInt(f);
            if (val is string s && int.TryParse(s, out var p)) return p;
            return fallback;
        }

        private static float ConvertFloat(object val, float fallback)
        {
            if (val is float f) return f;
            if (val is int i) return i;
            if (val is double d) return (float)d;
            if (val is string s && float.TryParse(s, out var p)) return p;
            return fallback;
        }
    }
}
