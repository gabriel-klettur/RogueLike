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
    /// <para>The shipped asset does have genuine same-map collisions on the editor F-keys —
    /// F2 (Combat Ranges + Time &amp; Weather), F3 (Spawner + Lighting), F5 (Entities +
    /// QuickSave), F9 (Debug HUD + QuickLoad) — and three of those four have survived because
    /// one half is reached with a modifier that lives in C# rather than in the binding. They
    /// are reported rather than fixed here: which one should move is a design decision.</para>
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

        /// <summary>
        /// Which actions answer to each control path. The map the drawn keyboard paints from,
        /// so a key showing two names and a key reported as conflicting are the same fact.
        ///
        /// <para>Composite PARTS are included and composite headers are not: WASD really is
        /// four bindings on four keys, and a board that could not show that would leave the
        /// most-used control in the game blank.</para>
        /// </summary>
        public static Dictionary<string, List<InputActionDescriptor>> BindingsByPath(InputActionAsset asset)
        {
            var byPath = new Dictionary<string, List<InputActionDescriptor>>(StringComparer.OrdinalIgnoreCase);
            if (asset == null) return byPath;

            foreach (var map in asset.actionMaps)
            {
                var bindings = map.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b.isComposite) continue;
                    var path = b.effectivePath;
                    if (string.IsNullOrEmpty(path)) continue;

                    var descriptor = InputActionCatalog.Find(map.name, b.action);
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
