using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — Gather tab. One row per <c>CastFlourishProfile</c> knob, each with a
    /// checkbox that pins it to this spell.
    ///
    /// <para>PINNED PER KNOB, not wholesale. An unpinned knob keeps coming from the spell's
    /// family — Hurl for a projectile, Edge for a slash — so retuning a family still reaches
    /// every spell that has not overruled that particular value. A single master switch would
    /// freeze a spell against its family in all thirty knobs the moment a designer wanted to
    /// change one, which is how the eight shipped charges would have stopped tracking Ki.</para>
    ///
    /// <para>EVERY row states the value the spell actually USES, pinned or not, plus a tag
    /// naming where it came from. That is not decoration: ticking a box seeds the pin from the
    /// family, so the number is deliberately unchanged and only the tag can say the spell has
    /// stopped tracking. Showing the value in one state and not the other made the checkbox
    /// look inert — the panel showed less after a click than before it.</para>
    ///
    /// <para>Rows are addressed by the profile's own field names through two prefixes, and
    /// deliberately NOT by <c>SpellDefinition</c> fields: this form edits a different object
    /// from the Properties tab, so it carries its own <c>ValueChanged</c> rather than
    /// widening that one's reflection lookup to mean two things.</para>
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        /// <summary>Key prefix for a row's pin checkbox.</summary>
        private const string GATHER_PIN_PREFIX = "pin:";

        /// <summary>Key prefix for a pinned row's value input.</summary>
        private const string GATHER_VALUE_PREFIX = "val:";

        /// <summary>
        /// Key prefix for a SECTION's draw switch. A third prefix rather than a smarter parse:
        /// the three controls answer three different questions and must never be confused for
        /// one another at the point they are routed.
        /// </summary>
        private const string GATHER_SECTION_PREFIX = "sec:";

        private bool _gatherFormSubscribed;
        private bool _gatherButtonsBuilt;
        private bool _applyingGather;

        // ── Build ─────────────────────────────────────────────────────────────────

        /// <summary>Set when the selection moved while the Gather tab was hidden.</summary>
        private bool _gatherFormDirty = true;

        internal const string PROPS_TAB_GATHER = "gather";

        private string ActivePropsTab
            => _uiRefs.PropsTabStrip != null ? _uiRefs.PropsTabStrip.ActiveKey : null;

        /// <summary>
        /// Build whichever Properties tab is on screen if the selection moved while it was
        /// hidden. Wired to the tab strip, so revealing Gather is the moment it catches up.
        /// </summary>
        private void OnPropsTabChanged(int _, string key)
        {
            if (key == PROPS_TAB_GATHER && _gatherFormDirty) RefreshGatherForm();
        }

        private void RefreshGatherForm()
        {
            var form = _uiRefs.PropsGatherForm;
            if (form == null) return;
            _gatherFormDirty = false;

            if (!_gatherFormSubscribed)
            {
                form.ValueChanged += OnGatherValueChanged;
                _gatherFormSubscribed = true;
            }

            BuildGatherButtonsOnce();
            form.Clear();

            SpellDefinition spell = SelectedSpell();
            if (spell == null)
            {
                if (_uiRefs.GatherFamilyTmp != null)
                    _uiRefs.GatherFamilyTmp.text = "(no spell selected)";
                return;
            }

            // Two profiles, and both are needed: the family alone is what an unpinned row
            // reports, the resolved one is what the spell will actually play.
            CastFlourishProfile family = CastFlourishProfile.BuildFamily(spell);
            CastFlourishProfile resolved = CastFlourishProfile.Build(spell);
            var overrides = EnsureGatherOverride(spell);

            if (_uiRefs.GatherFamilyTmp != null)
            {
                int pinned = overrides.Count;
                _uiRefs.GatherFamilyTmp.text =
                    $"Family <b>{family.FamilyName}</b> — {pinned} of {CountKnobs()} knobs pinned.\n" +
                    "Every row shows the value this spell USES. <b>(family)</b> follows " +
                    $"{family.FamilyName}; <b>[pinned]</b> is owned by this spell and editable below it.";
            }

            // Driven by the piece table rather than by the knob list, so a section's header,
            // its switch and its rows are one decision instead of three that can disagree.
            foreach (var piece in CastFlourishPieces.All)
            {
                bool on = CastFlourishPieces.IsOn(resolved, piece);
                bool gatePinned = !piece.IsLocked && overrides.Has(piece.GateKnob);

                AddSectionHeader(form, "── " + piece.Section.ToUpperInvariant() +
                                       (piece.IsLocked ? " (locked)" : "") + " ──");

                if (!piece.IsLocked)
                    form.AddBool(GATHER_SECTION_PREFIX + piece.Section,
                                 SectionRowLabel(piece, family, on, gatePinned), on);

                // A piece switched off by its FAMILY hides its unpinned rows — exactly the old
                // funnel behaviour, generalised. One switched off by a PIN keeps every row:
                // that state is the direct result of a click, and nothing may vanish under the
                // cursor that produced it. Pinned rows always render, or an override could be
                // stranded with no row to release it from.
                bool offByFamily = !on && !gatePinned;

                if (!on && piece.DependsOn == null && offByFamily && !AnyKnobPinned(overrides, piece))
                    continue;   // nothing to show: the family draws none and the spell owns none

                WarnIfSectionDependencyBroken(form, piece, resolved);

                for (int i = 0; i < piece.Knobs.Length; i++)
                {
                    var knob = CastGatherOverrides.Knob(piece.Knobs[i]);
                    if (knob == null) continue;

                    bool pinnedKnob = overrides.Has(knob.Name);
                    if (!pinnedKnob && offByFamily) continue;

                    // A knob whose type the form cannot edit must not offer a checkbox either,
                    // or ticking it pins a value with no input underneath.
                    if (!CanEditKnob(knob.FieldType))
                    {
                        if (pinnedKnob)
                            Debug.LogWarning($"[SpellsEditor] Gather knob '{knob.Name}' is pinned but " +
                                             $"its type ({knob.FieldType.Name}) has no editor row.");
                        continue;
                    }

                    form.AddBool(GATHER_PIN_PREFIX + knob.Name,
                                 PinRowLabel(knob, resolved, pinnedKnob), pinnedKnob);
                    if (pinnedKnob) AddGatherValueRow(form, knob, resolved);
                }
            }
        }

        // ── Section switches ──────────────────────────────────────────────────────

        /// <summary>
        /// The section switch's label, naming which of FOUR states the piece is in.
        ///
        /// <para>The state matters more than the boolean, because two of them look identical
        /// from the checkbox alone. OFF-by-family is "this gesture never draws one" — every
        /// non-vortex funnel, and Edge's sigil. OFF-by-pin is "this spell turned it off", which
        /// is what <c>fireball</c> ships. Releasing the first seeds a whole piece; releasing the
        /// second is one entry removed. A designer has to be able to tell them apart before
        /// clicking.</para>
        /// </summary>
        internal static string SectionRowLabel(CastFlourishPiece piece, CastFlourishProfile family,
                                               bool on, bool gatePinned)
        {
            string state = on
                ? (gatePinned ? "ON (pinned)" : "ON (family)")
                : (gatePinned ? "OFF (pinned)" : $"OFF ({family.FamilyName} draws none)");
            return $"DRAW {piece.Section}  -  {state}";
        }

        /// <summary>Whether the spell owns any knob under this piece, gate included.</summary>
        private static bool AnyKnobPinned(CastGatherOverride overrides, CastFlourishPiece piece)
        {
            for (int i = 0; i < piece.Knobs.Length; i++)
                if (overrides.Has(piece.Knobs[i])) return true;
            return false;
        }

        /// <summary>
        /// One line when a piece silently needs a sibling that is switched off.
        ///
        /// <para>Read off the RESOLVED profile, so it states a fact rather than enforcing a
        /// rule: the mote spiral really does read the funnel's radii and spin without ever
        /// consulting <c>FunnelBands</c>, so a SpiralFunnel approach with no funnel drawn
        /// gathers debris around a cone nobody can see.</para>
        /// </summary>
        private void WarnIfSectionDependencyBroken(PropertyForm form, CastFlourishPiece piece,
                                                   CastFlourishProfile resolved)
        {
            if (piece.DependsOn != "Funnel") return;
            if (resolved.Approach != MoteApproach.SpiralFunnel) return;
            if (CastFlourishPieces.IsOn(resolved, CastFlourishPieces.Funnel)) return;

            AddSectionHeader(form, "!  Approach is SpiralFunnel but the Funnel section is off.");
        }

        /// <summary>
        /// Turn a whole piece on or off. Distinct from a knob pin: this decides whether the
        /// piece is DRAWN, and it does so by writing the one gate knob the table names.
        ///
        /// <para>Turning ON has two branches and the difference is not cosmetic. Off-by-pin
        /// releases the gate and the piece goes back to whatever the family draws — one entry
        /// removed. Off-by-family cannot do that: Edge ships the sigil's radius, spin and alpha
        /// all at zero beside <c>Sigil = None</c>, so releasing the gate alone would build two
        /// rings at radius 0.05 and alpha 0 — a switch that reads ON and draws nothing, which is
        /// the exact class of bug this whole pass exists to remove. There it seeds every knob
        /// the piece owns from a donor family, and says so in the undo label.</para>
        /// </summary>
        private void ToggleSection(SpellDefinition spell, string sectionName, bool on)
        {
            CastFlourishPiece piece = default;
            bool found = false;
            foreach (var candidate in CastFlourishPieces.All)
                if (candidate.Section == sectionName) { piece = candidate; found = true; break; }
            if (!found || piece.IsLocked) return;

            var overrides = EnsureGatherOverride(spell);
            var resolved = CastFlourishProfile.Build(spell);
            if (on == CastFlourishPieces.IsOn(resolved, piece)) return;

            var before = SnapshotGather(overrides);
            string label;

            if (!on)
            {
                CastFlourishPieces.WriteOff(overrides, piece);
                label = "Turn off " + piece.Section;
            }
            else if (overrides.Has(piece.GateKnob)
                     && CastFlourishPieces.IsOn(CastFlourishProfile.BuildFamily(spell), piece))
            {
                // The family already draws it — releasing the pin is the whole job.
                overrides.Clear(piece.GateKnob);
                label = "Turn on " + piece.Section;
            }
            else
            {
                var donor = piece.Donor(spell);
                CastFlourishPieces.SeedFrom(overrides, piece, donor);
                label = $"Turn on {piece.Section} ({piece.Knobs.Length} knobs pinned from {donor.FamilyName})";
                Toast(label);
            }

            CommitGather(spell, before, SnapshotGather(overrides), label);
        }

        /// <summary>
        /// The checkbox row's label: the knob's name and <b>the value the spell will actually
        /// use</b>, always, in both states.
        ///
        /// <para>It reads from the RESOLVED profile rather than the family one, which is the
        /// whole point — for an unpinned knob the two are equal by definition, and for a
        /// pinned one the family value is precisely what the spell is NOT using. The first
        /// version showed the family's number when unpinned and dropped the value entirely
        /// when pinned, so ticking a box made the panel show strictly less than before and
        /// there was no way to see what a pin had actually done.</para>
        ///
        /// <para>The suffix names the source, because the same number can mean two different
        /// things: pinning seeds from the family, so a freshly pinned knob reads identically
        /// to an unpinned one and only the tag says the spell has stopped tracking.</para>
        ///
        /// <para>Internal + static so tests can pin the wording without standing up a canvas.</para>
        /// </summary>
        internal static string PinRowLabel(FieldInfo knob, CastFlourishProfile resolved, bool pinned)
            => $"{Prettify(knob.Name)}  ·  {Describe(knob, resolved)}  {(pinned ? "[pinned]" : "(family)")}";

        /// <summary>
        /// The editable row under a pinned checkbox. Named after its knob rather than the bare
        /// word "value": the row sits among twenty-nine others and a column of identical
        /// "value" labels says nothing about which one it belongs to.
        /// </summary>
        /// <para>ASCII only. The shipped TMP atlas carries Latin-1 but not the arrows block, so
        /// a U+21B3 here rendered as a missing-glyph box in front of every editable row.</para>
        internal static string ValueRowLabel(FieldInfo knob) => "     - " + Prettify(knob.Name);

        private void AddGatherValueRow(PropertyForm form, FieldInfo knob, CastFlourishProfile resolved)
        {
            string key = GATHER_VALUE_PREFIX + knob.Name;
            string label = ValueRowLabel(knob);
            object current = knob.GetValue(resolved);

            if (knob.FieldType == typeof(float))
                form.AddFloat(key, label, (float)current);
            else if (knob.FieldType == typeof(int))
                form.AddInt(key, label, (int)current);
            else if (knob.FieldType == typeof(bool))
                form.AddBool(key, label, (bool)current);
            else if (knob.FieldType.IsEnum)
                form.AddDropdown(key, label,
                    Enum.GetNames(knob.FieldType),
                    Mathf.Max(0, Array.IndexOf(Enum.GetNames(knob.FieldType), current.ToString())));
        }

        private void BuildGatherButtonsOnce()
        {
            if (_gatherButtonsBuilt || _uiRefs.PropsGatherRoot == null) return;

            EditorUIHelpers.MakeButton(_uiRefs.PropsGatherRoot, "Reset to family",
                ResetGatherToFamily, 24f, 11f);
            EditorUIHelpers.MakeButton(_uiRefs.PropsGatherRoot, "Recast",
                RecastPreview, 24f, 11f);
            _gatherButtonsBuilt = true;
        }

        // ── Mutation ──────────────────────────────────────────────────────────────

        private void OnGatherValueChanged(string key, object val)
        {
            if (_applyingGather || string.IsNullOrEmpty(key)) return;

            var spell = SelectedSpell();
            if (spell == null) { Toast("No spell selected."); return; }

            // Checked first: a section key is not a knob name and would otherwise be looked up
            // as one.
            if (key.StartsWith(GATHER_SECTION_PREFIX, StringComparison.Ordinal))
            {
                ToggleSection(spell, key.Substring(GATHER_SECTION_PREFIX.Length), val is bool sOn && sOn);
                return;
            }
            if (key.StartsWith(GATHER_PIN_PREFIX, StringComparison.Ordinal))
            {
                TogglePin(spell, key.Substring(GATHER_PIN_PREFIX.Length), val is bool b && b);
                return;
            }
            if (key.StartsWith(GATHER_VALUE_PREFIX, StringComparison.Ordinal))
                EditPinnedKnob(spell, key.Substring(GATHER_VALUE_PREFIX.Length), val);
        }

        /// <summary>
        /// Pin or release one knob. Pinning SEEDS the entry from the family's current value,
        /// so the tick alone is never a visual change — the spell keeps looking exactly as it
        /// did and only stops tracking. Releasing drops the entry outright; presence in the
        /// bag is the switch, so there is no stale value left behind to come back later.
        /// </summary>
        private void TogglePin(SpellDefinition spell, string knobName, bool pin)
        {
            var knob = CastGatherOverrides.Knob(knobName);
            if (knob == null) return;

            var overrides = EnsureGatherOverride(spell);
            if (pin == overrides.Has(knobName)) return;

            var before = SnapshotGather(overrides);

            if (pin)
            {
                object seed = knob.GetValue(CastFlourishProfile.BuildFamily(spell));
                if (knob.FieldType.IsEnum) overrides.SetText(knobName, seed.ToString());
                else                       overrides.SetNumber(knobName, ToNumber(seed));
            }
            else overrides.Clear(knobName);

            CommitGather(spell, before, SnapshotGather(overrides),
                (pin ? "Pin " : "Release ") + Prettify(knobName));
        }

        private void EditPinnedKnob(SpellDefinition spell, string knobName, object val)
        {
            var knob = CastGatherOverrides.Knob(knobName);
            if (knob == null) return;

            var overrides = EnsureGatherOverride(spell);
            // Editing a row that is not pinned would write a value nothing reads: the row only
            // exists while the pin is on, so this is a stale event, not a state to support.
            if (!overrides.Has(knobName)) return;

            // TMP fires onEndEdit when focus LEAVES a field, changed or not, so without this
            // clicking a row and clicking away pushes an undo step that does nothing. The
            // Properties tab guards the same way before it writes.
            object currentValue = knob.GetValue(CastFlourishProfile.Build(spell));

            var before = SnapshotGather(overrides);

            if (knob.FieldType.IsEnum)
            {
                string name = ResolveEnumName(knob.FieldType, val);
                if (name == null || name == currentValue.ToString()) return;
                overrides.SetText(knobName, name);
            }
            else
            {
                float number;
                try { number = ToNumber(ConvertValue(val, knob.FieldType == typeof(bool)
                                                          ? typeof(bool) : knob.FieldType)); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpellsEditor] Gather '{knobName}': {ex.Message}");
                    return;
                }
                if (Mathf.Approximately(number, ToNumber(currentValue))) return;
                overrides.SetNumber(knobName, number);
            }

            CommitGather(spell, before, SnapshotGather(overrides), "Edit " + Prettify(knobName));
        }

        private void ResetGatherToFamily()
        {
            var spell = SelectedSpell();
            if (spell == null) { Toast("No spell selected."); return; }

            var overrides = EnsureGatherOverride(spell);
            if (overrides.Count == 0) { Toast("Already pure family — nothing pinned."); return; }

            var before = SnapshotGather(overrides);
            int n = overrides.Count;
            overrides.ClearAll();
            CommitGather(spell, before, SnapshotGather(overrides), "Reset gather to family");
            Toast($"Released {n} knob(s) — '{spell.spellKey}' follows its family again.");
        }

        /// <summary>
        /// Fire the preview from the top so an edit is visible now rather than whenever the
        /// loop next comes round. The flourish is roughly half a second inside a cycle of at
        /// least a second and a bit, so without this the change lands two beats after the edit
        /// and reads as the panel having ignored it.
        /// </summary>
        private void RecastPreview()
        {
            if (SelectedSpell() == null) { Toast("No spell selected."); return; }
            if (_previewService == null) { Toast("Preview is not open."); return; }
            _previewService.Restart();
        }

        // ── Undo + persistence ────────────────────────────────────────────────────

        /// <summary>
        /// Push one gather edit onto the shared undo stack and write the change through.
        ///
        /// <para>The whole bag is snapshotted either side rather than the single entry that
        /// moved, because a pin both ADDS an entry and seeds it: replaying that as two steps
        /// would let an undo land between them, on a spell pinned to a value nobody chose.
        /// The bag is a handful of small entries, so copying it is cheaper than the bookkeeping
        /// that avoiding the copy would need.</para>
        /// </summary>
        private void CommitGather(SpellDefinition spell, CastGatherOverride before,
                                  CastGatherOverride after, string label)
        {
            var target = spell;
            _undo.Do(new UndoStack.LambdaCommand(label,
                doAction:   () => RestoreGather(target, after),
                undoAction: () => RestoreGather(target, before)));
        }

        private void RestoreGather(SpellDefinition spell, CastGatherOverride snapshot)
        {
            if (spell == null) return;
            spell.gatherOverride = SnapshotGather(snapshot);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(spell);
#endif
            _applyingGather = true;
            try { RefreshGatherForm(); }
            finally { _applyingGather = false; }

            // A pinned knob changes how the cast LOOKS, so show it immediately — the whole
            // point of the tab is the loop between moving a number and seeing it.
            if (_previewService != null) _previewService.Restart();
        }

        /// <summary>Deep copy. The bag is mutable and the undo stack holds it across edits.</summary>
        private static CastGatherOverride SnapshotGather(CastGatherOverride source)
        {
            var copy = new CastGatherOverride();
            if (source?.fields == null) return copy;

            for (int i = 0; i < source.fields.Count; i++)
            {
                var f = source.fields[i];
                if (f == null) continue;
                copy.fields.Add(new CastGatherOverride.Field
                {
                    name = f.name, number = f.number, text = f.text,
                });
            }
            return copy;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private SpellDefinition SelectedSpell()
        {
            if (_catalog == null || string.IsNullOrEmpty(_selectedKey)) return null;
            return _catalog.TryGet(_selectedKey, out var s) ? s : null;
        }

        /// <summary>
        /// Never null: a spell asset serialized before the field existed deserializes it as
        /// null, and every caller here would otherwise have to answer for that.
        /// </summary>
        private static CastGatherOverride EnsureGatherOverride(SpellDefinition spell)
            => spell.gatherOverride ?? (spell.gatherOverride = new CastGatherOverride());

        private static int CountKnobs()
        {
            int n = 0;
            foreach (var _ in CastGatherOverrides.AuthorableKnobs()) n++;
            return n;
        }

        private static float ToNumber(object value)
        {
            if (value is bool b) return b ? 1f : 0f;
            if (value is int i) return i;
            return Convert.ToSingle(value);
        }

        /// <summary>
        /// A dropdown reports its selected INDEX, a test or a script may hand over the member
        /// name. Both resolve to a name here, because the bag stores names — an index would
        /// silently re-point at a different member the day the enum is reordered.
        /// </summary>
        private static string ResolveEnumName(Type enumType, object val)
        {
            string[] names = Enum.GetNames(enumType);
            if (val is int index)
                return index >= 0 && index < names.Length ? names[index] : null;

            string text = val as string;
            if (string.IsNullOrEmpty(text)) return null;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase)) return names[i];
            return null;
        }

        /// <summary>
        /// Whether <see cref="AddGatherValueRow"/> can build an input for this knob's type.
        /// One predicate rather than an inline check so the form and the test that pins the
        /// coverage cannot disagree about what "editable" means.
        /// </summary>
        internal static bool CanEditKnob(Type type)
            => type == typeof(float) || type == typeof(int)
            || type == typeof(bool)  || type.IsEnum;

        /// <summary>"MoteSpeedMin" → "Mote Speed Min". Internal so the label tests can
        /// assert against the same transform the rows use rather than a copy of it.</summary>
        internal static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new System.Text.StringBuilder(name.Length + 6);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// One knob's value as the row prints it. Takes whichever profile the caller means —
        /// pass the RESOLVED one to state what the spell uses, the family one to state what it
        /// would fall back to.
        /// </summary>
        internal static string Describe(FieldInfo knob, CastFlourishProfile profile)
        {
            object value = knob.GetValue(profile);
            if (value is float f) return f.ToString("0.###");
            return value != null ? value.ToString() : "—";
        }
    }
}
