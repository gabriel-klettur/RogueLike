using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World;

namespace Valkur.Editor.FSM
{
    /// <summary>
    /// Generates the canonical seed JSON files consumed by the in-game FSM
    /// Editor (F12) — `StreamingAssets/FSM/{sets,assignments,animation_map}.json`.
    ///
    /// The runtime <c>FSMMonsterBrain</c> drives monster AI through hand-coded
    /// <see cref="IState"/> classes; the JSON model layered on top describes
    /// the *vocabulary* (which states an archetype may use) plus its assignment
    /// map, so the visual editor and the runtime stay synchronised.
    ///
    /// The generator is **idempotent** — re-running it never overwrites a set
    /// that lacks an <c>auto_generated</c> flag, and never overwrites an
    /// archetype assignment the user pinned manually. New states added to the
    /// codebase show up automatically in the next regen.
    ///
    /// A set that DOES carry <c>auto_generated: true</c> (every set this
    /// generator has ever written, including the shipped <c>Monster_Default</c>)
    /// is **not** replaced wholesale on a regen — only its STATE VOCABULARY is
    /// refreshed additively (<see cref="MergeStateVocabulary"/>): any state id
    /// the fresh vocabulary has that the saved set doesn't get appended, and
    /// every existing state (position, label, props) is left untouched. Every
    /// OTHER field on the set — transitions, blackboard, label, initial — is
    /// never touched by a regen, auto-flag or not. This is what F12 authoring
    /// writes to (see <c>FSMRuntimeEditor.SyncSetToRaw</c>), and a designer
    /// tuning a graph in F12 has no way to know a later regen would otherwise
    /// discard it: the shipped <c>Monster_Default</c> carries three authored
    /// transitions (t_chase_flee, t_attack_flee, t_any_alert) that drive
    /// <c>FleeState</c> and <c>AlertChaseState</c>, and a full replace used to
    /// delete all three silently on every regen.
    ///
    /// Menu: <c>Valkur &gt; FSM &gt; Generate Seed from Runtime States</c>.
    /// </summary>
    public static class FSMSeedGenerator
    {
        // ── Constants ────────────────────────────────────────────────────────────

        public const string DEFAULT_SET_ID    = "Monster_Default";
        public const string DEFAULT_SET_LABEL = "Default Hostile Monster";
        public const string INITIAL_STATE     = nameof(IdleState);
        public const string AUTO_FLAG_KEY     = "auto_generated";

        /// <summary>
        /// Canonical state vocabulary for the default hostile monster.
        /// <c>DamageState</c> is intentionally excluded — it is a transient state
        /// pushed by the engine event-queue, never a user-selectable target.
        /// Order matters: it controls the default node layout in the editor.
        /// </summary>
        public static readonly string[] DefaultStates =
        {
            nameof(IdleState),
            nameof(PatrolState),
            nameof(ChaseState),
            nameof(AlertChaseState),
            nameof(AttackState),
            nameof(FleeState),
            nameof(NPCCastState),
            nameof(UnconsciousState),
            nameof(DeathState),
        };

        /// <summary>
        /// Default sprite-atlas slot per state, matching <c>EntityAssetConfig</c>.
        /// Used to seed the per-state animation map so designers see something
        /// sensible the first time the Animations panel opens.
        /// </summary>
        public static readonly Dictionary<string, string> DefaultAnimationMap = new Dictionary<string, string>
        {
            [nameof(IdleState)]        = "idle",
            [nameof(PatrolState)]      = "walk",
            [nameof(ChaseState)]       = "chase",
            [nameof(AlertChaseState)]  = "chase",
            [nameof(AttackState)]      = "attack",
            [nameof(FleeState)]        = "walk",
            [nameof(NPCCastState)]     = "cast",
            [nameof(UnconsciousState)] = "death",
            [nameof(DeathState)]       = "death",
        };

        // ── Menu entry ───────────────────────────────────────────────────────────

        [MenuItem("Valkur/FSM/Generate Seed from Runtime States")]
        public static void RunFromMenu()
        {
            var report = Run(promptOnOverwrite: true);
            if (report == null) return; // user cancelled

            EditorUtility.DisplayDialog(
                "FSM Seed Generator",
                report.ToHumanReadable(),
                "OK");
            Debug.Log($"[FSMSeedGenerator] {report.ToHumanReadable()}");
        }

        // ── Orchestration (testable entry point) ────────────────────────────────

        /// <summary>
        /// Runs the full generation pipeline. Returns null if the user cancelled
        /// at the overwrite prompt, otherwise a <see cref="GenerationReport"/>
        /// describing what was written.
        /// </summary>
        public static GenerationReport Run(bool promptOnOverwrite)
        {
            // 1. Validate every state in the vocabulary actually exists in the
            //    runtime assembly. Refuse to write garbage.
            var missing = ValidateStatesExist(DefaultStates);
            if (missing.Count > 0)
            {
                Debug.LogError(
                    $"[FSMSeedGenerator] Refusing to write seed — these state " +
                    $"types do not implement IState: {string.Join(", ", missing)}");
                return null;
            }

            // 2. Resolve I/O paths (creates StreamingAssets/FSM/ on demand).
            string dir = Path.Combine(Application.streamingAssetsPath, "FSM");
            Directory.CreateDirectory(dir);

            string setsPath        = Path.Combine(dir, "sets.json");
            string assignmentsPath = Path.Combine(dir, "assignments.json");
            string animMapPath     = Path.Combine(dir, "animation_map.json");

            // 3. Optional overwrite confirmation (skipped in tests).
            if (promptOnOverwrite && File.Exists(setsPath))
            {
                bool ok = EditorUtility.DisplayDialog(
                    "FSM Seed Generator",
                    $"sets.json already exists at:\n{setsPath}\n\n" +
                    "Auto-generated sets will have their STATE LIST refreshed with any " +
                    "new state class — everything else on them (transitions, blackboard, " +
                    "labels, positions) is preserved untouched. Hand-created sets (without " +
                    "an 'auto_generated' flag) are preserved verbatim, as always.\n\n" +
                    "Continue?",
                    "Generate", "Cancel");
                if (!ok) return null;
            }

            // 4. Build, merge with existing, write.
            var freshSets       = BuildDefaultSetsRoot(DefaultStates, INITIAL_STATE);
            var existingSets    = ReadJsonOrEmpty(setsPath);
            var mergedSets      = MergeSetsIdempotent(existingSets, freshSets);

            var monsters        = LoadMonstersForArchetypes();
            var freshAssign     = BuildAssignmentsRoot(monsters);
            var existingAssign  = ReadJsonOrEmpty(assignmentsPath);
            var mergedAssign    = MergeAssignmentsIdempotent(existingAssign, freshAssign);

            var freshAnim       = BuildAnimationMapRoot(DefaultAnimationMap);
            var existingAnim    = ReadJsonOrEmpty(animMapPath);
            var mergedAnim      = MergeAnimationMapIdempotent(existingAnim, freshAnim);

            WriteJson(setsPath,        mergedSets);
            WriteJson(assignmentsPath, mergedAssign);
            WriteJson(animMapPath,     mergedAnim);

            AssetDatabase.Refresh();

            // Count actually-written assignments (vendors with empty fsmSet are skipped).
            int writtenAssignments = AsDict(freshAssign["by_archetype"]).Count;

            return new GenerationReport
            {
                SetsPath           = setsPath,
                AssignmentsPath    = assignmentsPath,
                AnimationMapPath   = animMapPath,
                StatesEmitted      = DefaultStates.Length,
                AssignmentsEmitted = writtenAssignments,
            };
        }

        // ── Pure builders (no I/O — fully unit-testable) ────────────────────────

        public static Dictionary<string, object> BuildDefaultSetsRoot(
            string[] stateIds, string initial)
        {
            var states = new List<object>(stateIds.Length);
            const float colW = 220f, rowH = 160f;
            for (int i = 0; i < stateIds.Length; i++)
            {
                int col = i % 3;
                int row = i / 3;
                states.Add(new Dictionary<string, object>
                {
                    ["id"]          = stateIds[i],
                    ["label"]       = HumanizeStateName(stateIds[i]),
                    ["x"]           = (double)(80f + col * colW),
                    ["y"]           = (double)(80f + row * rowH),
                    ["is_initial"]  = stateIds[i] == initial,
                    ["is_terminal"] = stateIds[i] == nameof(DeathState)
                                   || stateIds[i] == nameof(UnconsciousState),
                    ["props"]       = new Dictionary<string, object>(),
                });
            }

            var defaultSet = new Dictionary<string, object>
            {
                ["id"]              = DEFAULT_SET_ID,
                ["label"]           = DEFAULT_SET_LABEL,
                ["initial"]         = initial,
                ["states"]          = states,
                ["transitions"]     = new List<object>(),
                ["blackboard"]      = new Dictionary<string, object>(),
                [AUTO_FLAG_KEY]     = true,
            };

            return new Dictionary<string, object>
            {
                ["sets"] = new List<object> { defaultSet },
            };
        }

        public static Dictionary<string, object> BuildAssignmentsRoot(
            List<MonsterDefinition> monsters)
        {
            var byArch = new Dictionary<string, object>();
            foreach (var m in monsters)
            {
                if (m == null || string.IsNullOrEmpty(m.monsterKey)) continue;
                if (string.IsNullOrEmpty(m.fsmSet)) continue;
                byArch[m.monsterKey] = m.fsmSet;
            }

            return new Dictionary<string, object>
            {
                ["by_archetype"] = byArch,
                ["by_eid"]       = new Dictionary<string, object>(),
            };
        }

        public static Dictionary<string, object> BuildAnimationMapRoot(
            Dictionary<string, string> defaultMap)
        {
            var defaultDict = new Dictionary<string, object>();
            foreach (var kv in defaultMap)
                defaultDict[kv.Key] = kv.Value;

            return new Dictionary<string, object>
            {
                ["default"] = defaultDict,
                ["per_set"] = new Dictionary<string, object>(),
            };
        }

        // ── Idempotent merge logic ──────────────────────────────────────────────

        /// <summary>
        /// Refreshes the STATE VOCABULARY of every existing set tagged
        /// <c>auto_generated: true</c> whose id matches a freshly generated set —
        /// any state id the fresh vocabulary carries that the existing set doesn't
        /// get appended (see <see cref="MergeStateVocabulary"/>). Everything else on
        /// the existing set (transitions, blackboard, label, initial, every
        /// already-present state's position/label/props) is left completely
        /// untouched, auto-flag or not.
        ///
        /// This used to REPLACE the whole set wholesale — drop the existing dict and
        /// splice in the fresh one — which is indistinguishable from "delete every
        /// F12 edit": the shipped <c>Monster_Default</c> carries three authored
        /// transitions that drive <c>FleeState</c>/<c>AlertChaseState</c>, and a full
        /// replace silently deleted all three on every regen. The class doc's promise
        /// — "new states show up automatically in the next regen" — only needs the
        /// STATE LIST refreshed, so that is now the only thing a regen touches on an
        /// existing set.
        ///
        /// Sets without the flag (hand-created via F12's "+ New Set") are preserved
        /// verbatim, exactly as before. A freshly generated set with no existing
        /// counterpart at all (first-ever run, or a brand-new default set id) is
        /// appended as-is.
        /// </summary>
        public static Dictionary<string, object> MergeSetsIdempotent(
            Dictionary<string, object> existing,
            Dictionary<string, object> generated)
        {
            var existingList  = AsList(existing.TryGetValue("sets", out var es) ? es : null);
            var generatedList = AsList(generated.TryGetValue("sets", out var gs) ? gs : null);

            // Index generated sets by id for the vocabulary-refresh lookup below.
            var generatedById = new Dictionary<string, Dictionary<string, object>>();
            foreach (var item in generatedList)
            {
                if (item is Dictionary<string, object> dict &&
                    dict.TryGetValue("id", out var idObj))
                {
                    generatedById[idObj?.ToString() ?? ""] = dict;
                }
            }

            var merged     = new List<object>();
            var existingIds = new HashSet<string>();
            foreach (var item in existingList)
            {
                if (!(item is Dictionary<string, object> dict)) continue;
                string id   = dict.TryGetValue("id", out var idObj) ? idObj?.ToString() : null;
                bool isAuto = dict.TryGetValue(AUTO_FLAG_KEY, out var autoVal) &&
                              autoVal is bool b && b;

                if (isAuto && id != null && generatedById.TryGetValue(id, out var freshDict))
                    MergeStateVocabulary(dict, freshDict);

                if (id != null) existingIds.Add(id);
                merged.Add(dict);
            }
            // Append any freshly-generated set with no existing counterpart at all —
            // never one whose id already exists (auto or not), which the loop above
            // already refreshed in place or deliberately left alone.
            foreach (var item in generatedList)
            {
                if (!(item is Dictionary<string, object> dict)) continue;
                string id = dict.TryGetValue("id", out var idObj) ? idObj?.ToString() : null;
                if (id != null && existingIds.Contains(id)) continue;
                merged.Add(dict);
            }

            return new Dictionary<string, object> { ["sets"] = merged };
        }

        /// <summary>
        /// Appends any state in <paramref name="freshSet"/> that
        /// <paramref name="existingSet"/> doesn't already have (by id). Never
        /// mutates an already-present state's dict, never touches
        /// <c>transitions</c>/<c>blackboard</c>/<c>label</c>, and only fills
        /// <c>initial</c> in when the existing set doesn't have one at all — an
        /// authored graph's entry point is never overridden.
        /// </summary>
        private static void MergeStateVocabulary(
            Dictionary<string, object> existingSet,
            Dictionary<string, object> freshSet)
        {
            var existingStates = AsList(existingSet.TryGetValue("states", out var es) ? es : null);
            var freshStates    = AsList(freshSet.TryGetValue("states", out var fs) ? fs : null);

            var existingStateIds = new HashSet<string>();
            foreach (var s in existingStates)
                if (s is Dictionary<string, object> sd && sd.TryGetValue("id", out var sid))
                    existingStateIds.Add(sid?.ToString() ?? "");

            var mergedStates = new List<object>(existingStates);
            foreach (var s in freshStates)
            {
                if (!(s is Dictionary<string, object> sd)) continue;
                string sid = sd.TryGetValue("id", out var idObj) ? idObj?.ToString() : null;
                if (sid == null || existingStateIds.Contains(sid)) continue;
                mergedStates.Add(sd);
            }
            existingSet["states"] = mergedStates;

            if (!existingSet.TryGetValue("initial", out var initObj) ||
                string.IsNullOrEmpty(initObj as string))
            {
                if (freshSet.TryGetValue("initial", out var freshInit))
                    existingSet["initial"] = freshInit;
            }
        }

        /// <summary>
        /// Adds new entries from <paramref name="generated"/> into
        /// <paramref name="existing"/>; never overwrites an existing key
        /// (the user may have manually re-routed an archetype to a custom set).
        /// </summary>
        public static Dictionary<string, object> MergeAssignmentsIdempotent(
            Dictionary<string, object> existing,
            Dictionary<string, object> generated)
        {
            var existingArch  = AsDict(existing.TryGetValue("by_archetype", out var ea) ? ea : null);
            var generatedArch = AsDict(generated.TryGetValue("by_archetype", out var ga) ? ga : null);

            var mergedArch = new Dictionary<string, object>(existingArch);
            foreach (var kv in generatedArch)
                if (!mergedArch.ContainsKey(kv.Key)) mergedArch[kv.Key] = kv.Value;

            var existingEid = AsDict(existing.TryGetValue("by_eid", out var ee) ? ee : null);

            return new Dictionary<string, object>
            {
                ["by_archetype"] = mergedArch,
                ["by_eid"]       = existingEid,
            };
        }

        /// <summary>
        /// Like assignments: only fills in keys missing from <c>default</c>;
        /// preserves <c>per_set</c> overrides untouched.
        /// </summary>
        public static Dictionary<string, object> MergeAnimationMapIdempotent(
            Dictionary<string, object> existing,
            Dictionary<string, object> generated)
        {
            var existingDef  = AsDict(existing.TryGetValue("default", out var ed) ? ed : null);
            var generatedDef = AsDict(generated.TryGetValue("default", out var gd) ? gd : null);

            var mergedDef = new Dictionary<string, object>(existingDef);
            foreach (var kv in generatedDef)
                if (!mergedDef.ContainsKey(kv.Key)) mergedDef[kv.Key] = kv.Value;

            var existingPer = AsDict(existing.TryGetValue("per_set", out var ep) ? ep : null);

            return new Dictionary<string, object>
            {
                ["default"] = mergedDef,
                ["per_set"] = existingPer,
            };
        }

        // ── Reflection validation ───────────────────────────────────────────────

        /// <summary>
        /// Returns the subset of <paramref name="stateIds"/> that do NOT resolve
        /// to a concrete <see cref="IState"/> implementer in the runtime
        /// assembly. Empty list means everything is wired correctly.
        /// </summary>
        public static List<string> ValidateStatesExist(IEnumerable<string> stateIds)
        {
            var iStateType = typeof(IState);
            var assembly   = iStateType.Assembly;
            var available  = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && iStateType.IsAssignableFrom(t))
                .Select(t => t.Name)
                .ToHashSet();

            var missing = new List<string>();
            foreach (var id in stateIds)
                if (!available.Contains(id)) missing.Add(id);
            return missing;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts <c>"AlertChaseState"</c> → <c>"Alert Chase"</c> and
        /// <c>"NPCCastState"</c> → <c>"NPC Cast"</c>. Drops the <c>State</c>
        /// suffix, inserts a space before each interior capital that starts
        /// a new word, and treats runs of consecutive uppercase letters
        /// (acronyms like NPC, AI, FSM) as a single token.
        /// </summary>
        public static string HumanizeStateName(string stateClassName)
        {
            if (string.IsNullOrEmpty(stateClassName)) return stateClassName;
            string trimmed = stateClassName.EndsWith("State")
                ? stateClassName.Substring(0, stateClassName.Length - "State".Length)
                : stateClassName;

            var sb = new System.Text.StringBuilder(trimmed.Length + 4);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (i > 0 && char.IsUpper(c))
                {
                    char prev = trimmed[i - 1];
                    bool prevWasLower = char.IsLower(prev);
                    bool nextIsLower  = i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1]);
                    bool acronymBoundary = char.IsUpper(prev) && nextIsLower;
                    if (prevWasLower || acronymBoundary) sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static List<MonsterDefinition> LoadMonstersForArchetypes()
        {
            var result = new List<MonsterDefinition>();
            var guids  = AssetDatabase.FindAssets($"t:{nameof(MonsterDefinition)}");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                if (def != null) result.Add(def);
            }
            return result;
        }

        private static Dictionary<string, object> ReadJsonOrEmpty(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            try
            {
                var raw = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                return raw ?? new Dictionary<string, object>();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FSMSeedGenerator] Could not parse '{path}' — treating as empty: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        private static void WriteJson(string path, object obj)
        {
            File.WriteAllText(path, MiniJsonRuntime.Serialize(obj, pretty: true));
        }

        private static List<object> AsList(object o) => o as List<object> ?? new List<object>();
        private static Dictionary<string, object> AsDict(object o) =>
            o as Dictionary<string, object> ?? new Dictionary<string, object>();

        // ── Report ──────────────────────────────────────────────────────────────

        public class GenerationReport
        {
            public string SetsPath;
            public string AssignmentsPath;
            public string AnimationMapPath;
            public int    StatesEmitted;
            public int    AssignmentsEmitted;

            public string ToHumanReadable() =>
                $"FSM seed regenerated.\n\n" +
                $"  • {StatesEmitted} states in '{DEFAULT_SET_ID}'\n" +
                $"  • {AssignmentsEmitted} archetype assignments\n\n" +
                $"  → {SetsPath}\n" +
                $"  → {AssignmentsPath}\n" +
                $"  → {AnimationMapPath}\n\n" +
                $"Open the FSM Editor with F12 to inspect.";
        }
    }
}
