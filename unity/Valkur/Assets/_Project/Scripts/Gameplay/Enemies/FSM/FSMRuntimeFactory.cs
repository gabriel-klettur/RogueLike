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

        /// <summary>
        /// Whether the set this monster would resolve to declares <paramref name="stateClassName"/>
        /// in its allowed-state list, and whether that could be answered at all.
        ///
        /// <para><b>A set's node list is a WHITELIST, and a refused <c>ChangeState</c> is silent
        /// from the author's side.</b> So a dial like <c>aiTuning.dodgeChance</c> can be turned up
        /// on a monster whose set has no <c>DodgeState</c> and do nothing at all, forever, with
        /// the number sitting there looking authored. This exists so the Entities editor can say
        /// that BESIDE the dial instead of leaving the author to discover it by watching.</para>
        ///
        /// <para>Resolution deliberately mirrors <see cref="TryBuildForEntity"/>'s own order —
        /// <c>by_archetype</c>, then the asset's <c>fsmSet</c> — rather than re-deriving it, so
        /// the warning cannot disagree with the machine that gets built. <paramref name="known"/>
        /// is false when nothing resolves, which is a different answer from "resolved, and it
        /// does not allow that state": a caller must not report a confident absence for a monster
        /// whose set it never found.</para>
        /// </summary>
        public static bool AllowsState(string archetypeKey, string fsmSetHint,
                                       string stateClassName, out bool known)
        {
            known = false;
            EnsureLoaded();
            if (_loadFailed || string.IsNullOrEmpty(stateClassName)) return false;

            string setId = null;
            if (!string.IsNullOrEmpty(archetypeKey))
                _archetypeToSetId.TryGetValue(archetypeKey, out setId);
            if (string.IsNullOrEmpty(setId) || !_setsById.ContainsKey(setId))
                setId = !string.IsNullOrEmpty(fsmSetHint) && _setsById.ContainsKey(fsmSetHint)
                    ? fsmSetHint
                    : null;

            if (string.IsNullOrEmpty(setId) || !_setsById.TryGetValue(setId, out var set)) return false;
            if (set == null || set.AllowedStateNames == null) return false;

            known = true;
            for (int i = 0; i < set.AllowedStateNames.Count; i++)
                if (string.Equals(set.AllowedStateNames[i], stateClassName,
                                  System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static bool TryBuildForArchetype(
            string archetypeKey, GameObject owner, out StateMachine fsm)
            => TryBuildForEntity(null, archetypeKey, owner, out fsm);

        /// <summary>
        /// True when <c>by_eid</c> names a set for this placement. Lets a caller skip
        /// rebuilding a machine that would come out identical — a placement with no
        /// override (every one shipped today) costs a dictionary probe and nothing else.
        /// </summary>
        public static bool HasPlacementOverride(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return false;
            EnsureLoaded();
            if (_loadFailed) return false;
            return _placementToSetId.ContainsKey(placementId);
        }

        /// <summary>
        /// Builds the machine for ONE placed entity, honouring a per-placement override
        /// before falling back to the archetype.
        ///
        /// <paramref name="placementId"/> is a
        /// <see cref="Valkur.Gameplay.Entities.PersistedEntityInstance.PlacementId"/> — the
        /// stable GUID an F5 placement keeps across a save/load. It is looked up in
        /// <c>assignments.json</c>'s <c>by_eid</c>, which the F12 Entities panel has always
        /// been able to author and which nothing read: every monster of an archetype got the
        /// archetype's set, so "these two guards are the same monster but that one patrols"
        /// was not expressible however carefully it was authored.
        ///
        /// An override naming a set that does not exist warns and falls through to the
        /// archetype rather than failing the spawn — a stale id in a hand-edited file must
        /// not cost the entity its brain.
        /// </summary>
        public static bool TryBuildForEntity(
            string placementId, string archetypeKey, GameObject owner, out StateMachine fsm)
            => TryBuildForEntity(placementId, archetypeKey, null, owner, out fsm);

        /// <summary>
        /// The full resolution order: <c>by_eid</c> placement override, then
        /// <c>by_archetype</c>, then <paramref name="fsmSetHint"/>, then failure (and the
        /// caller's hard-coded boot).
        ///
        /// <paramref name="fsmSetHint"/> is <c>MonsterDefinition.fsmSet</c>, passed in as a
        /// plain string so this class stays free of any Data-layer reference. That field has
        /// always been authorable and has never been read: <c>knight_red.asset</c> declares
        /// <c>fsmSet: Monster_Default</c>, which reads exactly like an assignment, while
        /// resolution went only through <c>assignments.json</c> — so knight_red took the
        /// "no entry" path and booted a bare IdleState with no transitions and no
        /// allowed-state guard. Nine of the nineteen shipped monsters were in that position.
        ///
        /// It is deliberately LAST. <c>assignments.json</c> is what F12 edits, so a designer
        /// re-pointing a monster there must win over a string typed into the asset months ago;
        /// the hint exists to rescue the monsters nobody ever assigned, not to compete.
        /// </summary>
        public static bool TryBuildForEntity(
            string placementId, string archetypeKey, string fsmSetHint,
            GameObject owner, out StateMachine fsm)
            => TryBuildForEntity(placementId, null, archetypeKey, fsmSetHint, owner, out fsm);

        /// <summary>
        /// The full order, with the encounter's own say added:
        /// <c>by_eid</c> → <paramref name="spawnSetOverride"/> → <c>by_archetype</c> →
        /// <paramref name="fsmSetHint"/> → failure.
        ///
        /// <paramref name="spawnSetOverride"/> is <see cref="SpawnBrain.FsmSet"/> — what the
        /// SPAWNER that produced this entity asked for. It sits where it does deliberately.
        /// Below <c>by_eid</c>, because that is a hand-authored statement about one specific
        /// placement and is the more specific claim. Above <c>by_archetype</c>, because
        /// otherwise a camp asking for <c>Monster_Caster</c> would be silently overruled by
        /// the archetype mapping for every monster that has one — which is every shipped
        /// monster, so the override would do nothing at all and would be the thirteenth
        /// authored-and-inert field in this subsystem.
        ///
        /// A name no set declares warns once and falls through rather than failing the spawn:
        /// a typo in a config must not cost the entity its brain. It is still LOUD, which is
        /// the half that matters — a spawner quietly ignoring its authored brain is the
        /// failure this whole parameter exists to make impossible.
        /// </summary>
        public static bool TryBuildForEntity(
            string placementId, string spawnSetOverride, string archetypeKey, string fsmSetHint,
            GameObject owner, out StateMachine fsm)
        {
            fsm = null;
            if (owner == null) return false;

            EnsureLoaded();
            if (_loadFailed) return false;

            if (!string.IsNullOrEmpty(placementId) &&
                _placementToSetId.TryGetValue(placementId, out var eidSetId))
            {
                if (_setsById.ContainsKey(eidSetId))
                {
                    if (TryBuildFromSet(eidSetId, $"Placement '{placementId}'", owner, out fsm))
                        return true;
                }
                else
                {
                    WarnOnce($"[FSMRuntimeFactory] Placement '{placementId}' is mapped by " +
                             $"by_eid to set '{eidSetId}', which does not exist in sets.json — " +
                             "using the archetype's set instead.");
                }
            }

            if (!string.IsNullOrEmpty(spawnSetOverride))
            {
                if (_setsById.ContainsKey(spawnSetOverride))
                {
                    if (TryBuildFromSet(spawnSetOverride, $"Spawner override for '{archetypeKey}'",
                                        owner, out fsm))
                        return true;
                }
                else
                {
                    WarnOnce($"[FSMRuntimeFactory] A spawner asked for FSM set " +
                             $"'{spawnSetOverride}' for '{archetypeKey}', which does not exist " +
                             "in sets.json — falling back to the archetype's set. Check the " +
                             "spelling against the set list in the FSM editor.");
                }
            }

            if (string.IsNullOrEmpty(archetypeKey))
                return TryBuildFromHint(archetypeKey, fsmSetHint, owner, out fsm);

            if (!_archetypeToSetId.TryGetValue(archetypeKey, out var setId))
            {
                // The asset's own fsmSet is the last chance before the hard-coded boot.
                if (TryBuildFromHint(archetypeKey, fsmSetHint, owner, out fsm)) return true;

                // Warn once per archetype. This used to return silently, so a monster
                // whose MonsterDefinition declares an fsmSet but which nobody re-seeded
                // into assignments.json booted on the hard-coded fallback with no
                // diagnostic at all — which is exactly what knight_red did for months.
                // The text no longer claims there is no set when fsmSet just supplied one:
                // reaching here means neither source resolved.
                WarnOnce($"[FSMRuntimeFactory] Archetype '{archetypeKey}' has no entry in " +
                         "assignments.json and its MonsterDefinition.fsmSet names no usable " +
                         "set either — falling back to the hard-coded boot. Run " +
                         "Valkur > FSM > Generate Seed from Runtime States, or assign it in F12.");
                return false;
            }

            if (!_setsById.ContainsKey(setId))
            {
                WarnOnce($"[FSMRuntimeFactory] Archetype '{archetypeKey}' is mapped to set " +
                         $"'{setId}', which does not exist in sets.json.");
                return false;
            }

            return TryBuildFromSet(setId, $"Archetype '{archetypeKey}'", owner, out fsm);
        }

        /// <summary>
        /// Builds from <c>MonsterDefinition.fsmSet</c> when it names a set that exists.
        /// A hint pointing at a set that does NOT exist warns once and returns false — that
        /// is a typo in an asset, and it is the kind of thing that otherwise shows up as
        /// "this one monster behaves oddly" months later.
        /// </summary>
        private static bool TryBuildFromHint(
            string archetypeKey, string fsmSetHint, GameObject owner, out StateMachine fsm)
        {
            fsm = null;
            if (string.IsNullOrEmpty(fsmSetHint)) return false;

            if (!_setsById.ContainsKey(fsmSetHint))
            {
                WarnOnce($"[FSMRuntimeFactory] '{archetypeKey}' declares " +
                         $"MonsterDefinition.fsmSet = '{fsmSetHint}', which does not exist in " +
                         "sets.json. Check the spelling against the set list in F12.");
                return false;
            }

            return TryBuildFromSet(fsmSetHint, $"'{archetypeKey}' (via MonsterDefinition.fsmSet)",
                                   owner, out fsm);
        }

        /// <summary>Instantiates the initial state of <paramref name="setId"/> and wires the
        /// allowed-state guard + authored transitions onto a fresh machine. The machine is
        /// returned UNENTERED — see <see cref="StateMachine.Begin"/>; the caller publishes the
        /// context first.</summary>
        private static bool TryBuildFromSet(
            string setId, string subject, GameObject owner, out StateMachine fsm)
        {
            fsm = null;
            var set = _setsById[setId];

            var initial = TryInstantiateState(set.InitialStateName);
            if (initial == null)
            {
                Debug.LogWarning(
                    $"[FSMRuntimeFactory] {subject} is mapped to set '{setId}' but its initial " +
                    $"state '{set.InitialStateName}' could not be instantiated. Falling back " +
                    "to hard-coded boot.");
                return false;
            }

            fsm = new StateMachine(owner, initial);
            if (set.AllowedStateNames != null && set.AllowedStateNames.Count > 0)
                fsm.SetAllowedStates(new HashSet<string>(set.AllowedStateNames));
            fsm.SetTransitions(set.Transitions);
            return true;
        }

        /// <summary>Forces the next call to re-parse the JSON files from disk.</summary>
        public static void InvalidateCache()
        {
            _loaded     = false;
            _loadFailed = false;
            _setsById.Clear();
            _archetypeToSetId.Clear();
            _placementToSetId.Clear();
            // Re-arm the diagnostics too: after a reload the author wants to be told
            // again whether their fix took, not silence left over from the last attempt.
            _warned.Clear();
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
        /// <summary>
        /// <c>by_eid</c> from assignments.json: one specific PLACED monster's
        /// <see cref="Valkur.Gameplay.Entities.PersistedEntityInstance.PlacementId"/> mapped
        /// to a set id, overriding whatever its archetype resolves to. Empty in the shipped
        /// data — this is the authoring path for "these two guards are the same monster, but
        /// that one patrols and this one stands watch".
        /// </summary>
        private static readonly Dictionary<string, string> _placementToSetId =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> _archetypeToSetId =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Type lookup is keyed by simple class name (e.g. "IdleState") because
        // that is what the JSON stores; the values are concrete IState types
        // discovered once via reflection.
        private static readonly Dictionary<string, Type> _typeCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        // One line per distinct problem, not one per spawn — a missing assignment would
        // otherwise print for every monster of that archetype, every wave, forever.
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string message)
        {
            if (_warned.Add(message)) Debug.LogWarning(message);
        }

        private sealed class SetSnapshot
        {
            public string Id;
            public string InitialStateName;
            public List<string> AllowedStateNames;
            public FSMTransition[] Transitions;
        }

        /// <summary>
        /// Instantiate a state by class name. Public so <see cref="StateMachine"/> can
        /// realise an authored transition's target without duplicating the reflection
        /// cache. Returns null when the name resolves to no <c>IState</c>.
        /// </summary>
        public static IState CreateState(string stateClassName) => TryInstantiateState(stateClassName);

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

                var stateClassById = ExtractStateClassMap(dict);

                _setsById[id] = new SetSnapshot
                {
                    Id                = id,
                    // The set's `initial` names a NODE; the node's `class` (falling back
                    // to its own id) names the C# type. Resolving `initial` straight to a
                    // type is what made every set authored in F12 unrunnable: CreateNewSet
                    // writes a node with id "Idle" and class "IdleState", and AddNodeAt
                    // writes id "state_1" with an empty class, so the factory reflected on
                    // "Idle" / "state_1", found nothing, and dropped the monster to the
                    // hard-coded boot with one warning.
                    InitialStateName  = ResolveClassFor(AsStr(dict, "initial"), stateClassById),
                    AllowedStateNames = new List<string>(stateClassById.Values),
                    Transitions       = ExtractTransitions(dict, id, stateClassById),
                };
            }
        }

        private static void LoadAssignmentsFromDisk()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "FSM", "assignments.json");
            if (!File.Exists(path)) return;

            var raw     = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            if (raw == null) return;

            var byArch = raw.TryGetValue("by_archetype", out var o) ? o as Dictionary<string, object> : null;
            if (byArch != null)
                foreach (var kv in byArch)
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _archetypeToSetId[kv.Key] = kv.Value.ToString();

            var byEid = raw.TryGetValue("by_eid", out var e) ? e as Dictionary<string, object> : null;
            if (byEid != null)
                foreach (var kv in byEid)
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _placementToSetId[kv.Key] = kv.Value.ToString();
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
                //
                // WarnOnce, not LogWarning: the callers retry — an authored edge whose
                // target cannot be built is re-evaluated every tick it applies, and a
                // resume into an unconstructable state repeats on every flinch — so the
                // unthrottled version printed the same line at frame rate and trained the
                // reader to scroll past the console.
                WarnOnce(
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

        /// <summary>
        /// node id → C# state class name. A node names its class in <c>class</c>; when
        /// that is empty the id doubles as the class, which is how the seeded
        /// <c>Monster_Default</c> set works (its ids ARE the type names).
        /// </summary>
        private static Dictionary<string, string> ExtractStateClassMap(Dictionary<string, object> setDict)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!setDict.TryGetValue("states", out var statesObj)) return map;
            if (!(statesObj is List<object> states)) return map;

            foreach (var s in states)
            {
                var d = s as Dictionary<string, object>;
                if (d == null) continue;

                string id = AsStr(d, "id");
                if (string.IsNullOrEmpty(id)) continue;

                string cls = AsStr(d, "class");
                map[id] = string.IsNullOrEmpty(cls) ? id : cls;
            }
            return map;
        }

        private static string ResolveClassFor(string nodeId, Dictionary<string, string> stateClassById)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;
            return stateClassById.TryGetValue(nodeId, out var cls) ? cls : nodeId;
        }

        /// <summary>
        /// Parses the authored edge list into executable transitions, sorted by descending
        /// priority. A malformed guard is reported once here rather than silently treated
        /// as "always true" — a mistyped condition that fires every frame is far worse
        /// than one that never fires, because it looks like the FSM is broken.
        /// </summary>
        private static FSMTransition[] ExtractTransitions(Dictionary<string, object> setDict,
                                                          string setId,
                                                          Dictionary<string, string> stateClassById)
        {
            if (!setDict.TryGetValue("transitions", out var raw)) return null;
            if (!(raw is List<object> list) || list.Count == 0) return null;

            var result = new List<FSMTransition>(list.Count);

            foreach (var item in list)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;

                string fromId = AsStr(d, "from");
                string toId   = AsStr(d, "to");
                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) continue;

                string from = fromId == "*" ? "*" : ResolveClassFor(fromId, stateClassById);
                string to   = ResolveClassFor(toId, stateClassById);

                if (ResolveStateType(to) == null)
                {
                    Debug.LogWarning(
                        $"[FSMRuntimeFactory] Set '{setId}': transition '{fromId}' -> '{toId}' " +
                        $"targets '{to}', which is not an IState class. Edge ignored.");
                    continue;
                }

                // `guard` is what the F12 Transition tab writes; `when` / `event` are the
                // seed generator's names for the same slot. Take whichever is present.
                string rawCondition = AsStr(d, "guard") ?? AsStr(d, "when") ?? AsStr(d, "condition");
                var condition = FSMCondition.Parse(rawCondition, out string error);
                if (error != null)
                {
                    Debug.LogWarning(
                        $"[FSMRuntimeFactory] Set '{setId}': transition '{fromId}' -> '{toId}' " +
                        $"has an unparseable guard \"{rawCondition}\" ({error}). Edge ignored.");
                    continue;
                }

                int priority = AsInt(d, "priority", 0);
                // The schema stores a frame count; the runtime needs seconds and does not
                // know the author's frame rate, so 60 is the documented reference.
                float cooldown = AsInt(d, "cooldown_frames", 0) / 60f;

                result.Add(new FSMTransition(from, to, condition, priority, cooldown, rawCondition));
            }

            if (result.Count == 0) return null;

            result.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return result.ToArray();
        }

        private static int AsInt(Dictionary<string, object> dict, string key, int fallback)
        {
            if (!dict.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is long l) return (int)l;
            if (v is int i) return i;
            if (v is double d) return (int)d;
            return int.TryParse(v.ToString(), out int parsed) ? parsed : fallback;
        }
    }
}
