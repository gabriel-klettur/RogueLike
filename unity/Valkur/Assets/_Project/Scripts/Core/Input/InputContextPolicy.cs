using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core.Input
{
    /// <summary>Why an assignment was refused. The editor prints this, so it is the whole
    /// explanation the author ever gets.</summary>
    public enum InputAssignmentVerdict
    {
        Allowed = 0,
        /// <summary>The action can put damage on something, and Peace is a safe posture.</summary>
        RefusedDamageInPeace,
        /// <summary>The action's path is structural — a modifier probe, the pointer, the UI
        /// confirm — and moving it breaks a mechanism rather than expressing a taste.</summary>
        RefusedNotRebindable,
        /// <summary>No descriptor: the action is not in <see cref="InputActionCatalog"/>.</summary>
        RefusedUnknownAction,
        /// <summary>
        /// The action's mask is a mechanism rather than a preference — see
        /// <see cref="InputActionDescriptor.ContextLocked"/>. Walking, aiming, the dash, the
        /// stance toggle, the General Editor key and every UI verb: switching any of them off
        /// is a soft lock, and three of the six would make themselves unrecoverable.
        /// </summary>
        RefusedContextLocked,
    }

    /// <summary>
    /// The whitelist that makes Peace a SAFE POSTURE rather than a convention, plus the live
    /// per-action CONTEXT mask the player edits in the Controls editor.
    ///
    /// <para>A context is a play posture (War / Peace) or one runtime editor. An open editor
    /// wins over the posture unconditionally — see <see cref="InputContexts.Current"/> — which
    /// is why an editor is a context in its own right and not a third stance: the postures do
    /// not apply inside one at all.
    ///
    /// <para>THE ONE RULE THAT IS NOT CONFIGURABLE. An action whose
    /// <see cref="InputActionDescriptor.ReachesDamage"/> is true can never be given a Peace
    /// binding — not by the editor, not by a saved profile, not by a future caller. Everything
    /// else about a stance is a preference. The reason is recorded on
    /// <see cref="Valkur.Core.PlayerStance"/> and has not changed: nothing in the damage path
    /// reads a faction, <c>EntitySetup</c> gives every NPC a <c>Health</c>, and left click both
    /// locks a target and casts — so clicking a vendor to trade with her threw a fireball at
    /// her and she could be killed by it. A guarantee the player can configure their way out
    /// of is not a guarantee, which is why this is enforced at ASSIGNMENT time and asserted
    /// again at READ time.</para>
    ///
    /// <para>WHY THE MASK IS AN OVERRIDE TABLE AND NOT A FIELD ON THE DESCRIPTOR. The
    /// descriptors are immutable and shared; the player's choice is session state that has to
    /// be resettable and persistable. The table is normally EMPTY — nobody has changed
    /// anything — and <see cref="ContextsOf"/> short-circuits on that, so the per-frame read
    /// costs a <c>Count</c> check in the case that is virtually always true. Same shape as
    /// <c>AlliedUnit.Live</c> returning a shared empty array.</para>
    /// </summary>
    public static class InputContextPolicy
    {
        // Dictionary rather than an array cache on purpose: CLAUDE.md records that a
        // `static readonly` ARRAY cannot be reset in a form DomainReloadStaticResetTests
        // recognises (Array.Clear passes the field as an argument), while `field.Clear()` is
        // accepted. This is that shape.
        private static readonly Dictionary<string, InputContextMask> _overrides =
            new Dictionary<string, InputContextMask>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised whenever any action's mask changes. The Controls editor is the only
        /// subscriber — the comment used to name the stance HUD as well, which reads
        /// <see cref="PlayerStance.OnChanged"/> and has never touched this. Nothing in the
        /// gameplay loop listens: the masks are read where they are used.</summary>
        public static event Action OnChanged;

        /// <summary>True when the player has moved at least one action off its shipped mask.</summary>
        public static bool HasOverrides => _overrides.Count > 0;

        // ── Reading ──────────────────────────────────────────────────────────

        /// <summary>The live context mask for an action, override first, shipped default behind it.</summary>
        public static InputContextMask ContextsOf(InputActionDescriptor descriptor)
        {
            if (descriptor == null) return InputContextMask.None;
            if (_overrides.Count == 0) return descriptor.DefaultContexts;
            return _overrides.TryGetValue(descriptor.Id, out var m) ? m : descriptor.DefaultContexts;
        }

        public static InputContextMask ContextsOf(string actionId) =>
            ContextsOf(InputActionCatalog.Find(actionId));

        /// <summary>
        /// Is this action live right now? The read-time half of the guarantee: a damage action
        /// is refused in Peace here as well as at assignment, so a profile written by an older
        /// build — or by hand — cannot re-open the hole.
        /// </summary>
        /// <summary>
        /// Is this action live in the given context? The read-time half of the guarantee: a
        /// damage action is refused in Peace here as well as at assignment, so a profile
        /// written by an older build — or by hand — cannot re-open the hole.
        /// </summary>
        public static bool IsLive(InputActionDescriptor descriptor, string contextId)
        {
            if (descriptor == null) return false;

            var bit = InputContexts.MaskOf(contextId);
            if (bit == InputContextMask.None) return false;
            if ((ContextsOf(descriptor) & bit) == 0) return false;

            if (bit == InputContextMask.Peace && descriptor.ReachesDamage) return false;

            // An editor's OWN tool is live in that editor and nowhere else. A shared verb
            // names no owner and is live in all of them — that is the entire difference
            // between "every editor zooms the same way" and "this is the Tile brush".
            if (bit == InputContextMask.Editors && !string.IsNullOrEmpty(descriptor.OwnerEditor))
                return string.Equals(descriptor.OwnerEditor, InputContexts.EditorNameOf(contextId),
                                     StringComparison.OrdinalIgnoreCase);

            return true;
        }

        public static bool IsLive(InputActionDescriptor descriptor, Stance stance) =>
            IsLive(descriptor, stance == Stance.Peace ? InputContexts.Peace : InputContexts.War);

        /// <summary>Is this action live RIGHT NOW — the open editor if there is one, the
        /// player's posture otherwise.</summary>
        public static bool IsLive(InputActionDescriptor descriptor) =>
            IsLive(descriptor, InputContexts.Current);

        public static bool IsLive(string actionId) =>
            IsLive(InputActionCatalog.Find(actionId), InputContexts.Current);

        public static InputContextMask ToMask(Stance stance) =>
            stance == Stance.Peace ? InputContextMask.Peace : InputContextMask.War;

        // ── Writing ──────────────────────────────────────────────────────────

        /// <summary>
        /// Would this mask be accepted for this action? Pure — the Controls editor calls it to
        /// grey a control out BEFORE the author clicks, so a refusal is never a surprise.
        /// </summary>
        public static InputAssignmentVerdict Evaluate(InputActionDescriptor descriptor, InputContextMask mask)
        {
            if (descriptor == null) return InputAssignmentVerdict.RefusedUnknownAction;

            // A locked action accepts exactly its shipped mask and nothing else — including
            // None. Tested BEFORE the damage rule so a locked damage action reports the reason
            // that actually applies to it.
            if (descriptor.ContextLocked && mask != descriptor.DefaultContexts)
                return InputAssignmentVerdict.RefusedContextLocked;

            if ((mask & InputContextMask.Peace) != 0 && descriptor.ReachesDamage)
                return InputAssignmentVerdict.RefusedDamageInPeace;

            // None is ALLOWED for anything not locked, and that is what makes "silence one
            // spell without leaving War" possible. It used to be refused outright, for a real
            // reason — an action live nowhere is a control the player cannot find and cannot
            // switch back on. What removes that reason is the LIST: the Controls editor now
            // shows every action that BELONGS to the context being viewed (see
            // <see cref="BelongsTo"/>) and marks the silenced ones, so switching one back on
            // is a click on the row it was always on. The locked set above is what remains
            // genuinely unrecoverable, and it is refused by name rather than by a blanket rule.
            return InputAssignmentVerdict.Allowed;
        }

        /// <summary>
        /// Is this action part of the vocabulary of <paramref name="contextId"/> — whether or
        /// not it is live there right now?
        ///
        /// <para>The distinction is what lets the Controls editor list a SILENCED action
        /// instead of hiding it, which is the whole reason an empty mask is allowed at all.
        /// It reads the SHIPPED mask, never the live one: a player who silences a spell has
        /// not moved it out of the War vocabulary, they have switched it off inside it.</para>
        ///
        /// <para>The one thing that really is absent rather than silenced is a damage action
        /// in Peace. That is deliberate and predates this method: a greyed row is a control
        /// the player keeps trying, and Peace's promise is not "combat is off", it is
        /// "combat is not here".</para>
        /// </summary>
        public static bool BelongsTo(InputActionDescriptor descriptor, string contextId)
        {
            if (descriptor == null) return false;

            var bit = InputContexts.MaskOf(contextId);
            if (bit == InputContextMask.None) return false;

            if (bit == InputContextMask.Peace && descriptor.ReachesDamage) return false;

            if (bit == InputContextMask.Editors)
            {
                if ((descriptor.DefaultContexts & InputContextMask.Editors) == 0) return false;
                if (string.IsNullOrEmpty(descriptor.OwnerEditor)) return true;
                return string.Equals(descriptor.OwnerEditor, InputContexts.EditorNameOf(contextId),
                                     StringComparison.OrdinalIgnoreCase);
            }

            // Both play postures share one vocabulary: a spell belongs to the War tab and to
            // the Peace tab's "not permitted here" answer, and it is the ReachesDamage check
            // above that separates the two.
            return (descriptor.DefaultContexts & InputContextMask.Gameplay) != 0;
        }

        /// <summary>Would this action accept a rebind at all?</summary>
        public static InputAssignmentVerdict EvaluateRebind(InputActionDescriptor descriptor)
        {
            if (descriptor == null) return InputAssignmentVerdict.RefusedUnknownAction;
            if (!descriptor.Rebindable) return InputAssignmentVerdict.RefusedNotRebindable;
            return InputAssignmentVerdict.Allowed;
        }

        /// <summary>
        /// Applies a mask. Returns the verdict; on anything but
        /// <see cref="InputAssignmentVerdict.Allowed"/> nothing is written.
        /// </summary>
        public static InputAssignmentVerdict SetContexts(InputActionDescriptor descriptor, InputContextMask mask)
        {
            var verdict = Evaluate(descriptor, mask);
            if (verdict != InputAssignmentVerdict.Allowed) return verdict;

            // Writing the shipped default is a REMOVAL, not an entry. Keeping the table empty
            // whenever nothing has really been changed is what makes the per-frame fast path
            // in ContextsOf worth having, and it also means a saved profile records only what
            // the player actually decided.
            if (mask == descriptor.DefaultContexts) _overrides.Remove(descriptor.Id);
            else _overrides[descriptor.Id] = mask;

            OnChanged?.Invoke();
            return InputAssignmentVerdict.Allowed;
        }

        public static InputAssignmentVerdict SetContexts(string actionId, InputContextMask mask) =>
            SetContexts(InputActionCatalog.Find(actionId), mask);

        /// <summary>Drops every override back to the shipped masks.</summary>
        public static void ResetToDefaults()
        {
            if (_overrides.Count == 0) return;
            _overrides.Clear();
            OnChanged?.Invoke();
        }

        /// <summary>The overrides, for the persistence layer. Never the backing table — a
        /// registry that hands out its own storage is the <c>AlliedUnit.Live</c> defect.</summary>
        public static IReadOnlyList<KeyValuePair<string, InputContextMask>> SnapshotOverrides()
        {
            if (_overrides.Count == 0)
                return Array.Empty<KeyValuePair<string, InputContextMask>>();
            var list = new List<KeyValuePair<string, InputContextMask>>(_overrides.Count);
            foreach (var kv in _overrides) list.Add(kv);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        /// <summary>
        /// Replaces every override in one pass, raising <see cref="OnChanged"/> once. Each
        /// entry still goes through <see cref="Evaluate"/>, so a profile that asks for a
        /// damage action in Peace loses that entry and keeps the rest rather than being
        /// rejected whole — a file the player has been editing for a year should not stop
        /// loading because one line became illegal.
        /// </summary>
        public static void LoadOverrides(IEnumerable<KeyValuePair<string, InputContextMask>> entries)
        {
            _overrides.Clear();
            if (entries != null)
            {
                foreach (var kv in entries)
                {
                    var d = InputActionCatalog.Find(kv.Key);
                    if (d == null)
                    {
                        Debug.LogWarning($"[InputContextPolicy] Unknown action '{kv.Key}' in the " +
                                         "saved stance profile — dropped.");
                        continue;
                    }
                    if (Evaluate(d, kv.Value) != InputAssignmentVerdict.Allowed)
                    {
                        Debug.LogWarning($"[InputContextPolicy] Refused stance mask {kv.Value} for " +
                                         $"'{d.Id}' from the saved profile — it reaches the damage path.");
                        continue;
                    }
                    if (kv.Value == d.DefaultContexts) continue;
                    _overrides[d.Id] = kv.Value;
                }
            }
            OnChanged?.Invoke();
        }

        // ── Diagnostics ──────────────────────────────────────────────────────

        public static string Explain(InputAssignmentVerdict verdict) => verdict switch
        {
            InputAssignmentVerdict.Allowed              => "OK",
            InputAssignmentVerdict.RefusedDamageInPeace => "En Paz no se puede asignar nada que haga dano.",
            InputAssignmentVerdict.RefusedNotRebindable => "Esta accion es estructural y no se puede reasignar.",
            InputAssignmentVerdict.RefusedUnknownAction => "Accion desconocida.",
            InputAssignmentVerdict.RefusedContextLocked  => "Esta accion vive siempre donde vive: quitarla dejaria al jugador sin forma de recuperarla.",
            _                                            => "Rechazado.",
        };

        /// <summary>
        /// Domain Reload is OFF, so both the table and the subscriber list survive into the
        /// next Play session — the second carrying delegates that point at destroyed editor
        /// panels. Clearing the event matters as much as clearing the table.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _overrides.Clear();
            OnChanged = null;
        }

        /// <summary>Test hook — same reason <see cref="PlayerStance.ResetForTests"/> exists:
        /// an event cannot be cleared from outside the class that declares it.</summary>
        public static void ResetForTests()
        {
            _overrides.Clear();
            OnChanged = null;
        }
    }
}
