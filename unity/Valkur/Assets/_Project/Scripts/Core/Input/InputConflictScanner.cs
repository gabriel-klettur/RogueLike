using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>How badly two bindings on one control collide.</summary>
    public enum InputConflictSeverity
    {
        /// <summary>Same action map and overlapping stances: both fire, in the same frame,
        /// from one press. This is a bug in the bindings.</summary>
        SameMap = 0,

        /// <summary>Different maps. Usually deliberate and usually fine — space is Dash in
        /// Gameplay and Submit in UI, and only one of those consumers is listening at a time —
        /// but it is shown because "usually" is doing a lot of work in that sentence.</summary>
        CrossMap = 1,
    }

    /// <summary>Two bindings that name the same physical control.</summary>
    public readonly struct InputConflict
    {
        public readonly string Path;
        public readonly InputActionDescriptor A;
        public readonly InputActionDescriptor B;
        public readonly InputConflictSeverity Severity;

        /// <summary>The stances in which BOTH are live. <see cref="InputContextMask.None"/> for a
        /// cross-map conflict, where the question does not apply.</summary>
        public readonly InputContextMask Overlap;

        public InputConflict(string path, InputActionDescriptor a, InputActionDescriptor b,
                             InputConflictSeverity severity, InputContextMask overlap)
        {
            Path = path; A = a; B = b; Severity = severity; Overlap = overlap;
        }

        public string Describe()
        {
            string where = Severity == InputConflictSeverity.SameMap
                ? $"en {DescribeStances(Overlap)}"
                : $"entre {A.Map} y {B.Map}";
            return $"{InputControlPaths.LabelForPath(Path)}: {A.DisplayName} y {B.DisplayName} ({where})";
        }

        private static string DescribeStances(InputContextMask mask) => mask switch
        {
            InputContextMask.Gameplay  => "Guerra y Paz",
            InputContextMask.War   => "Guerra",
            InputContextMask.Peace => "Paz",
            _                => "ninguna postura",
        };
    }

    /// <summary>
    /// How badly two actions that answer one control in ONE CONTEXT collide.
    ///
    /// <para>Ordered so a path's verdict is the MAX over its pairs: the worst pair is what the
    /// player experiences.</para>
    /// </summary>
    public enum InputClashSeverity
    {
        /// <summary>Fewer than two live actions, or every pair is a declared coexist group.</summary>
        None = 0,

        /// <summary>
        /// One side is a UI verb. Both are enabled, and the EventSystem decides which consumes
        /// the press from what has focus — WASD is Move and Navigate at once, space is Dash
        /// and Submit, and neither has ever been a bug. Shown, not flagged.
        /// </summary>
        Arbitrated = 1,

        /// <summary>
        /// One side is a non-rebindable probe read as HELD STATE rather than as a gesture —
        /// the Ctrl and Alt modifiers, the pointer. Both really do answer, and it is usually
        /// what the author wanted, but not always: Alt is both the modifier probe and
        /// <c>ToggleOutlines</c>, so every Alt-drag flips the outlines on the way in.
        /// </summary>
        Modifier = 2,

        /// <summary>
        /// Two real gestures, both live in this context, on one control. One press fires both.
        /// This is the state the editor exists to make visible.
        /// </summary>
        Blocking = 3,
    }

    /// <summary>Everything a control's clash is, in the context being viewed.</summary>
    public readonly struct InputContextClash
    {
        public readonly string Path;
        public readonly InputClashSeverity Severity;
        public readonly IReadOnlyList<InputActionDescriptor> Live;

        public InputContextClash(string path, InputClashSeverity severity,
                                 IReadOnlyList<InputActionDescriptor> live)
        {
            Path = path; Severity = severity; Live = live;
        }

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(InputControlPaths.LabelForPath(Path)).Append(": ");
            for (int i = 0; i < Live.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(Live[i].DisplayName);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Finds every physical control that more than one action answers to.
    ///
    /// <para>WHY IT IS STANCE-AWARE AND MAP-AWARE. A naive "same path twice" scan reports the
    /// shipped asset as broken in ways it is not: WASD is Move in Gameplay and Navigate in UI,
    /// space is Dash and Submit, and neither pair is a bug because only one consumer is
    /// listening at a time. It also MISSES the thing worth finding once stances are real —
    /// two actions on one key that are live in different stances are not a conflict at all,
    /// and that is precisely the arrangement the Controls editor exists to let a player
    /// build.</para>
    ///
    /// <para>The shipped asset USED to carry four same-map collisions on the editor F-keys —
    /// F2 (Combat Ranges + Time &amp; Weather), F3 (Spawner + Lighting), F5 (Entities +
    /// QuickSave), F9 (Debug HUD + QuickLoad). Retiring the F-row took all four with it, and
    /// <c>ControlsBindingLayerTests.EditorsMap_HasNoSameMapCollisions</c> now asserts zero
    /// rather than excusing a list — a stale allowlist is worse than none, because it would go
    /// on excusing those four paths if a future binding landed on one.</para>
    ///
    /// <para>THIS IS THE ASSET AUDIT, NOT THE BOARD. It answers "is the asset sane" and is
    /// map-based, which is right for a test and wrong for a picture: two actions on one key in
    /// different contexts are not a collision, and a UI verb sharing a key with its gameplay
    /// counterpart never has been. The Controls editor paints from
    /// <see cref="ClashesInContext"/> instead.</para>
    /// </summary>
    public static class InputConflictScanner
    {
        /// <summary>Every conflict in the asset, most severe first.</summary>
        public static IReadOnlyList<InputConflict> Scan(InputActionAsset asset)
        {
            var byPath = BindingsByPath(asset);
            var conflicts = new List<InputConflict>();

            foreach (var kv in byPath)
            {
                var list = kv.Value;
                if (list.Count < 2) continue;

                for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];

                    bool sameMap = string.Equals(a.Map, b.Map, StringComparison.OrdinalIgnoreCase);
                    if (!sameMap)
                    {
                        conflicts.Add(new InputConflict(kv.Key, a, b,
                            InputConflictSeverity.CrossMap, InputContextMask.None));
                        continue;
                    }

                    var overlap = InputContextPolicy.ContextsOf(a) & InputContextPolicy.ContextsOf(b);
                    if (overlap == InputContextMask.None) continue;   // two layouts, not a collision

                    conflicts.Add(new InputConflict(kv.Key, a, b,
                        InputConflictSeverity.SameMap, overlap));
                }
            }

            conflicts.Sort((x, y) =>
            {
                int s = x.Severity.CompareTo(y.Severity);
                return s != 0 ? s : string.CompareOrdinal(x.Path, y.Path);
            });
            return conflicts;
        }

        // ── Context-aware clashes ────────────────────────────────────────────
        //
        // Scan(asset) above answers "is the ASSET sane" and is deliberately map-based: it is
        // the audit a test runs. The three methods below answer a different question — "what
        // happens when I press this key, HERE" — and they are what the drawn board paints
        // from. Keeping them separate is what stops the board reporting a design as broken:
        // two actions on one key in different contexts are not a collision, they are the whole
        // point of the context layer, and a map-based scan cannot tell those apart.

        /// <summary>
        /// Which actions are LIVE on each control in <paramref name="contextId"/> — the map the
        /// drawn board tints and labels from.
        /// </summary>
        public static Dictionary<string, List<InputActionDescriptor>> LiveByPath(
            InputActionAsset asset, string contextId)
        {
            var live = new Dictionary<string, List<InputActionDescriptor>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in BindingsByPath(asset))
            {
                List<InputActionDescriptor> kept = null;
                foreach (var d in kv.Value)
                {
                    if (!InputContextPolicy.IsLive(d, contextId)) continue;
                    (kept ??= new List<InputActionDescriptor>(2)).Add(d);
                }
                if (kept != null) live[kv.Key] = kept;
            }
            return live;
        }

        /// <summary>
        /// The verdict for one control, given everything live on it. The MAX over the pairs,
        /// because the worst pair is what the player experiences.
        /// </summary>
        public static InputClashSeverity Classify(IReadOnlyList<InputActionDescriptor> live)
        {
            if (live == null || live.Count < 2) return InputClashSeverity.None;

            var worst = InputClashSeverity.None;
            for (int i = 0; i < live.Count; i++)
            for (int j = i + 1; j < live.Count; j++)
            {
                var s = ClassifyPair(live[i], live[j]);
                if (s > worst) worst = s;
            }
            return worst;
        }

        private static InputClashSeverity ClassifyPair(InputActionDescriptor a, InputActionDescriptor b)
        {
            // Declared to fire together. Escape closing the editor AND opening the launcher is
            // the documented one-press UX, and without this it would paint red in all sixteen
            // editor tabs forever — a warning that is always on is a warning nobody reads.
            if (!string.IsNullOrEmpty(a.CoexistGroup) &&
                string.Equals(a.CoexistGroup, b.CoexistGroup, StringComparison.Ordinal))
                return InputClashSeverity.None;

            // Told apart by the modifier. Ctrl+S saves and bare S picks the select tool, and
            // they are two different presses however much they share a key — which is only
            // true because EditorInput refuses a bare-key tool while Ctrl is held AND refuses
            // a Ctrl verb while it is not. Both halves are in InputActionDescriptor.RequiresCtrl,
            // so this cannot claim a separation the readers do not make.
            if (a.RequiresCtrl != b.RequiresCtrl) return InputClashSeverity.None;

            if (string.Equals(a.Map, InputActionCatalog.MapUI, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b.Map, InputActionCatalog.MapUI, StringComparison.OrdinalIgnoreCase))
                return InputClashSeverity.Arbitrated;

            if (!a.Rebindable || !b.Rebindable) return InputClashSeverity.Modifier;

            return InputClashSeverity.Blocking;
        }

        /// <summary>Every control that clashes in this context, worst first.</summary>
        public static IReadOnlyList<InputContextClash> ClashesInContext(
            InputActionAsset asset, string contextId)
        {
            var result = new List<InputContextClash>();
            foreach (var kv in LiveByPath(asset, contextId))
            {
                var severity = Classify(kv.Value);
                if (severity == InputClashSeverity.None) continue;
                result.Add(new InputContextClash(kv.Key, severity, kv.Value));
            }
            result.Sort((x, y) =>
            {
                int s = y.Severity.CompareTo(x.Severity);
                return s != 0 ? s : string.CompareOrdinal(x.Path, y.Path);
            });
            return result;
        }

        /// <summary>
        /// Which actions answer to each control path. The map the drawn keyboard paints from,
        /// so a key showing two names and a key reported as conflicting are the same fact.
        ///
        /// <para>Composite PARTS are included and composite headers are not: WASD really is
        /// four bindings on four keys, and a board that could not show that would leave the
        /// most-used control in the game blank.</para>
        ///
        /// <para>A CHORD is keyed by its chord path (<see cref="InputChord.Compose"/>), not by
        /// its two parts. Shift+1 and bare 1 are two different presses — the resolver refuses
        /// bare 1 while Shift is held — so they must not be reported as one key answering twice,
        /// and thirty-five chords sharing a Shift must not paint Shift red.</para>
        /// </summary>
        public static Dictionary<string, List<InputActionDescriptor>> BindingsByPath(InputActionAsset asset)
        {
            var byPath = new Dictionary<string, List<InputActionDescriptor>>(StringComparer.OrdinalIgnoreCase);
            if (asset == null) return byPath;

            var slots = new List<InputChord.Slot>(128);
            foreach (var map in asset.actionMaps)
            {
                slots.Clear();
                InputChord.Slots(map.bindings, slots);
                foreach (var slot in slots)
                {
                    if (!slot.IsBound) continue;
                    var path = slot.KeyPath;

                    var descriptor = InputActionCatalog.Find(map.name, slot.Action);
                    if (descriptor == null) continue;   // reported by the catalog coverage test

                    if (!byPath.TryGetValue(path, out var list))
                        byPath[path] = list = new List<InputActionDescriptor>(2);

                    // One action can bind the same control twice (a composite that lists a key
                    // in two parts). That is not two actions on one key.
                    if (!list.Contains(descriptor)) list.Add(descriptor);
                }
            }

            return byPath;
        }
    }
}
