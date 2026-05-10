using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World; // MiniJsonRuntime

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Bridges the FSM Editor's JSON model (<c>StreamingAssets/FSM/sets.json</c>
    /// + <c>assignments.json</c>) to the runtime <see cref="StateMachine"/>.
    ///
    /// Each monster archetype has an entry in <c>assignments.json</c> pointing at
    /// a set in <c>sets.json</c>; the set declares which states the archetype
    /// may use and which one is initial. Hand-coded <see cref="IState"/> classes
    /// still own per-state behaviour (Enter/Execute/Exit) — the JSON only
    /// supplies the *vocabulary* and the *guard* (allowed states for
    /// <see cref="StateMachine.SetAllowedStates"/>).
    ///
    /// Failure modes (any of these → <see cref="TryBuildForArchetype"/> returns
    /// false so the caller can fall back to a hard-coded boot sequence):
    ///   • JSON files missing / unreadable / malformed
    ///   • Archetype not present in <c>by_archetype</c>
    ///   • Set ID points at a non-existent set
    ///   • Initial state class cannot be resolved via reflection
    ///   • Initial state has no public parameterless constructor
    ///
    /// Cache: parsed JSON + resolved Type lookups are cached at first use.
    /// Call <see cref="InvalidateCache"/> after the editor saves new data
    /// (or after the seed generator runs) so the next monster spawn picks up
    /// the changes without an Editor restart.
    /// </summary>
    public static class FSMRuntimeFactory
    {
        // ── Public API ───────────────────────────────────────────────────────────

        public static bool HasSetForArchetype(string archetypeKey)
        {
            EnsureLoaded();
            if (_loadFailed) return false;
            return !string.IsNullOrEmpty(archetypeKey) &&
                   _archetypeToSetId.ContainsKey(archetypeKey);
        }

        public static bool TryBuildForArchetype(
            string archetypeKey, GameObject owner, out StateMachine fsm)
        {
            fsm = null;
            if (owner == null || string.IsNullOrEmpty(archetypeKey)) return false;

            EnsureLoaded();
            if (_loadFailed) return false;

            if (!_archetypeToSetId.TryGetValue(archetypeKey, out var setId)) return false;
            if (!_setsById.TryGetValue(setId, out var set)) return false;

            var initial = TryInstantiateState(set.InitialStateName);
            if (initial == null)
            {
                Debug.LogWarning(
                    $"[FSMRuntimeFactory] Archetype '{archetypeKey}' is mapped to set " +
                    $"'{setId}' but its initial state '{set.InitialStateName}' could not " +
                    $"be instantiated. Falling back to hard-coded boot.");
                return false;
            }

            fsm = new StateMachine(owner, initial);
            if (set.AllowedStateNames != null && set.AllowedStateNames.Count > 0)
                fsm.SetAllowedStates(new HashSet<string>(set.AllowedStateNames));
            return true;
        }

        /// <summary>Forces the next call to re-parse the JSON files from disk.</summary>
        public static void InvalidateCache()
        {
            _loaded     = false;
            _loadFailed = false;
            _setsById.Clear();
            _archetypeToSetId.Clear();
        }

        /// <summary>
        /// Indicates whether the JSON model is loaded and ready. Useful for
        /// integration tests that need to differentiate "no data on disk"
        /// (legitimate) from "data on disk but parse failed" (regression).
        /// </summary>
        public static bool IsLoaded => _loaded && !_loadFailed;

        // ── Domain reload reset (Domain Reload OFF) ─────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            InvalidateCache();
            _typeCache.Clear();
        }

        // ── State ────────────────────────────────────────────────────────────────

        private static bool _loaded;
        private static bool _loadFailed;

        private static readonly Dictionary<string, SetSnapshot> _setsById =
            new Dictionary<string, SetSnapshot>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _archetypeToSetId =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Type lookup is keyed by simple class name (e.g. "IdleState") because
        // that is what the JSON stores; the values are concrete IState types
        // discovered once via reflection.
        private static readonly Dictionary<string, Type> _typeCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        private sealed class SetSnapshot
        {
            public string Id;
            public string InitialStateName;
            public List<string> AllowedStateNames;
        }

        // ── Loading ──────────────────────────────────────────────────────────────

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true; // even on failure — guarantees we don't retry every spawn

            try
            {
                LoadSetsFromDisk();
                LoadAssignmentsFromDisk();
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                Debug.LogWarning(
                    $"[FSMRuntimeFactory] FSM JSON load failed — runtime will fall " +
                    $"back to hard-coded boot for every monster: {ex.Message}");
            }
        }

        private static void LoadSetsFromDisk()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "FSM", "sets.json");
            if (!File.Exists(path)) return;

            var raw  = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            var sets = raw != null && raw.TryGetValue("sets", out var o) ? o as List<object> : null;
            if (sets == null) return;

            foreach (var item in sets)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;

                string id = AsStr(dict, "id");
                if (string.IsNullOrEmpty(id)) continue;

                _setsById[id] = new SetSnapshot
                {
                    Id                = id,
                    InitialStateName  = AsStr(dict, "initial"),
                    AllowedStateNames = ExtractStateNames(dict),
                };
            }
        }

        private static void LoadAssignmentsFromDisk()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "FSM", "assignments.json");
            if (!File.Exists(path)) return;

            var raw     = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            var byArch  = raw != null && raw.TryGetValue("by_archetype", out var o) ? o as Dictionary<string, object> : null;
            if (byArch == null) return;

            foreach (var kv in byArch)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                    _archetypeToSetId[kv.Key] = kv.Value.ToString();
        }

        // ── Reflection: state-class lookup ──────────────────────────────────────

        private static IState TryInstantiateState(string stateClassName)
        {
            if (string.IsNullOrEmpty(stateClassName)) return null;

            var t = ResolveStateType(stateClassName);
            if (t == null) return null;

            try
            {
                return Activator.CreateInstance(t) as IState;
            }
            catch (Exception ex)
            {
                // Most likely cause: the state has no public parameterless
                // constructor (DamageState is an example — but DamageState is
                // intentionally excluded from any user-selectable set).
                Debug.LogWarning(
                    $"[FSMRuntimeFactory] State '{stateClassName}' could not be " +
                    $"instantiated: {ex.Message}");
                return null;
            }
        }

        private static Type ResolveStateType(string simpleName)
        {
            if (_typeCache.TryGetValue(simpleName, out var cached)) return cached;

            var iStateAsm = typeof(IState).Assembly;
            foreach (var t in iStateAsm.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IState).IsAssignableFrom(t)) continue;
                if (t.Name != simpleName) continue;

                _typeCache[simpleName] = t;
                return t;
            }
            _typeCache[simpleName] = null;
            return null;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static string AsStr(Dictionary<string, object> dict, string key) =>
            dict.TryGetValue(key, out var v) && v != null ? v.ToString() : null;

        private static List<string> ExtractStateNames(Dictionary<string, object> setDict)
        {
            var result = new List<string>();
            if (!setDict.TryGetValue("states", out var statesObj)) return result;
            if (!(statesObj is List<object> states)) return result;

            foreach (var s in states)
            {
                var d = s as Dictionary<string, object>;
                if (d == null) continue;
                string id = AsStr(d, "id");
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }
    }
}
