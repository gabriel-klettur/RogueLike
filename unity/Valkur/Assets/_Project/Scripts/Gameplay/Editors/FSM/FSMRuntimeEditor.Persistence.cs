using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.VFX;   // AtomicJsonFile
using Valkur.Gameplay.World; // MiniJsonRuntime

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Persistence façade for the FSM Runtime Editor — mirrors Python
    /// <c>roguelike_editors/fsm/services/fsm_persistence/*</c>:
    ///   • load / save sets.json, assignments.json, animation_map.json, layouts.json
    ///   • normalize (auto-fill ids, props, AUTO_INCLUDE_DAMAGE, transition cross-fill)
    ///   • ID generator (parity with <c>fsm_id.new_id</c>)
    ///   • single-call <see cref="SaveSets"/> = normalize + write + reload notification.
    ///
    /// JSON folder: <c>Application.streamingAssetsPath/FSM/</c> (created on first save).
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const bool AUTO_INCLUDE_DAMAGE = true;

        /// <summary>
        /// Mirrors <c>Valkur.Editor.FSM.FSMSeedGenerator.AUTO_FLAG_KEY</c> — duplicated
        /// rather than referenced because <c>Valkur.Gameplay</c> cannot depend on
        /// <c>Valkur.Editor</c> (see the assembly table in CLAUDE.md). A set carrying this
        /// flag has its STATE VOCABULARY refreshed by "Valkur &gt; FSM &gt; Generate Seed
        /// from Runtime States" whenever a new state class ships — everything else on it
        /// (transitions, blackboard, per-state props/label/position) is the designer's and
        /// is never touched by that regen (<c>FSMSeedGenerator.MergeStateVocabulary</c>).
        /// Surfaced in the UI: <c>FSMRuntimeEditor.Sets.BuildSetRow</c> (list badge),
        /// <c>FSMRuntimeEditor.SelectSet</c> (status line) and
        /// <c>FSMRuntimeEditor.Properties.BuildStateTab</c> (info row).
        /// </summary>
        private const string AUTO_GENERATED_FLAG_KEY = "auto_generated";

        // ── Test seam ─────────────────────────────────────────────────────────────
        //
        // FSM StreamingAssets writes have no repository abstraction to inject (unlike
        // Particles' IParticleInstanceStore — FileParticleInstanceStore /
        // InMemoryParticleInstanceStore). A test that exercises the real mutation path
        // (PersistSets / SyncSetToRaw / BuildTypedSetsFromRaw) needs some way to redirect
        // off the real StreamingAssets/FSM/ without ever touching the shipped
        // Monster_Default set. Set from a test's [SetUp], cleared in [TearDown];
        // production code never assigns it. Static because Domain Reload is OFF — reset
        // so a fixture that threw before its TearDown can't leave a later test silently
        // writing into a stale temp folder for the rest of the session.

        internal static string TestDataDirOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTestDataDirOverride() => TestDataDirOverride = null;

        // ── Path resolution ──────────────────────────────────────────────────────

        private static string FsmDataDir
        {
            get
            {
                string sa = TestDataDirOverride ?? Path.Combine(Application.streamingAssetsPath, "FSM");
                if (!Directory.Exists(sa)) Directory.CreateDirectory(sa);
                return sa;
            }
        }

        private static string SetsPath          => Path.Combine(FsmDataDir, "sets.json");
        private static string AssignmentsPath   => Path.Combine(FsmDataDir, "assignments.json");
        private static string AnimationMapPath  => Path.Combine(FsmDataDir, "animation_map.json");
        private static string LayoutsPath       => Path.Combine(FsmDataDir, "layouts.json");
        private static string FsmIdsPath        => Path.Combine(FsmDataDir, "fsm_ids.json");

        // ── Anti-wipe guard ──────────────────────────────────────────────────────
        //
        // ReadJsonObject used to swallow a parse failure and hand the caller an empty
        // dict indistinguishable from "file legitimately doesn't exist yet". NormalizeSets
        // then built `sets: []` out of it, and the very next click (every mutation calls
        // PersistSets immediately) wrote that emptiness over whatever was on disk.
        // Buildings/Particles/Spawners/Lights all refuse a write after a failed load for
        // the same reason — these four flags are that guard for FSM's four files. Only a
        // genuine parse failure (file exists, content is not valid JSON / not the expected
        // shape) sets one; a missing file is the legitimate first-run state and must not
        // block saving.

        private bool _setsLoadFailed;
        private bool _assignmentsLoadFailed;
        private bool _animMapLoadFailed;
        private bool _layoutsLoadFailed;

        // ── ID generation ────────────────────────────────────────────────────────

        /// <summary>Mirrors Python <c>fsm_id.new_id(prefix, existing)</c>.</summary>
        public static string NewId(string prefix, IEnumerable<string> existing)
        {
            var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.Ordinal);
            int i = 1;
            while (set.Contains($"{prefix}_{i}")) i++;
            return $"{prefix}_{i}";
        }

        public static string NewTrId(IEnumerable<string> existing)
        {
            var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.Ordinal);
            for (int tries = 0; tries < 16; tries++)
            {
                var id = "tr_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (!set.Contains(id)) return id;
            }
            return "tr_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static string NewNodeId(IEnumerable<string> existing)
        {
            var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.Ordinal);
            for (int tries = 0; tries < 16; tries++)
            {
                var id = "node_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (!set.Contains(id)) return id;
            }
            return "node_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        // ── Generic JSON helpers ─────────────────────────────────────────────────

        /// <param name="parseFailed">
        /// True only when the file EXISTS but could not be read as a JSON object —
        /// never true just because the file is absent. Callers wire this into the
        /// per-file anti-wipe flag above.
        /// </param>
        private static Dictionary<string, object> ReadJsonObject(string path, out bool parseFailed)
        {
            parseFailed = false;
            if (!File.Exists(path)) return new Dictionary<string, object>();
            try
            {
                var raw = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                if (raw == null)
                {
                    parseFailed = true;
                    Debug.LogError($"[FSMEditor] '{path}' did not deserialize to a JSON object — " +
                                    "refusing to treat it as empty.");
                    return new Dictionary<string, object>();
                }
                return raw;
            }
            catch (Exception ex)
            {
                parseFailed = true;
                Debug.LogError($"[FSMEditor] Failed to read '{path}': {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Atomic write (temp file + rename via <see cref="AtomicJsonFile"/>) so a crash
        /// or process kill mid-write can never leave sets.json/assignments.json/etc. half
        /// written — the same mechanism <c>FileParticleInstanceStore</c> uses for
        /// <c>particles_instances.json</c>.
        /// </summary>
        private static void WriteJson(string path, object obj)
        {
            try
            {
                AtomicJsonFile.Write(path, MiniJsonRuntime.Serialize(obj, pretty: true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FSMEditor] Failed to write '{path}': {ex.Message}");
            }
        }

        /// <summary>True when <paramref name="set"/> carries <see cref="AUTO_GENERATED_FLAG_KEY"/>
        /// — its state vocabulary (not its transitions/blackboard/labels) refreshes on
        /// the next "Generate Seed from Runtime States".</summary>
        private static bool IsSeedGeneratedSet(FSMSetData set) =>
            set?.raw != null &&
            set.raw.TryGetValue(AUTO_GENERATED_FLAG_KEY, out var flag) &&
            flag is bool b && b;

        private static List<object> AsList(object o) => o as List<object> ?? new List<object>();
        private static Dictionary<string, object> AsDict(object o)
            => o as Dictionary<string, object> ?? new Dictionary<string, object>();
        private static string AsStr(object o, string fallback = "")
            => o == null ? fallback : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
        private static int AsInt(object o, int fallback = 0)
        {
            if (o is long l) return (int)l;
            if (o is int i) return i;
            if (o is double d) return (int)d;
            if (o is float f) return (int)f;
            if (o is string s && int.TryParse(s, out var n)) return n;
            return fallback;
        }
        private static bool AsBool(object o, bool fallback = false)
        {
            if (o is bool b) return b;
            if (o is string s)
            {
                s = s.Trim().ToLowerInvariant();
                return s == "1" || s == "true" || s == "yes" || s == "y" || s == "on";
            }
            return fallback;
        }
        private static float AsFloat(object o, float fallback = 0f)
        {
            if (o is float f) return f;
            if (o is double d) return (float)d;
            if (o is long l) return l;
            if (o is int i) return i;
            if (o is string s && float.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return n;
            return fallback;
        }

        // ── Normalization ────────────────────────────────────────────────────────

        /// <summary>
        /// Mirrors Python <c>fsm_persistence/normalize._ensure_ids_and_defaults()</c>.
        /// Forces version, ids, props={}, AUTO_INCLUDE_DAMAGE, transition cross-fill.
        /// Also migrates two writer/reader key mismatches so a saved file only ever
        /// carries the name the rest of the editor (and the runtime factory) actually
        /// reads — see the inline comments at each migration.
        /// </summary>
        public static void NormalizeSets(Dictionary<string, object> root)
        {
            if (root == null) return;
            if (!root.ContainsKey("version") || AsInt(root["version"]) < 1) root["version"] = 1L;
            if (!root.ContainsKey("sets") || !(root["sets"] is List<object>))
                root["sets"] = new List<object>();

            foreach (var raw in AsList(root["sets"]))
            {
                if (!(raw is Dictionary<string, object> set)) continue;

                if (!set.ContainsKey("id") || string.IsNullOrEmpty(AsStr(set["id"])))
                    set["id"] = "Set_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (!set.ContainsKey("label") || string.IsNullOrEmpty(AsStr(set["label"])))
                    set["label"] = AsStr(set["id"]);

                if (!set.ContainsKey("states") || !(set["states"] is List<object>))
                    set["states"] = new List<object>();
                if (!set.ContainsKey("transitions") || !(set["transitions"] is List<object>))
                    set["transitions"] = new List<object>();

                // Normalize states
                var stateIds = new HashSet<string>();
                foreach (var sraw in AsList(set["states"]))
                {
                    if (!(sraw is Dictionary<string, object> st)) continue;
                    if (!st.ContainsKey("id") || string.IsNullOrEmpty(AsStr(st["id"])))
                        st["id"] = NewNodeId(stateIds);

                    // Legacy sentinel for the synthetic Damage vocabulary entry (see
                    // AUTO_INCLUDE_DAMAGE below). Every other node's id equals its class
                    // name unless overridden — "Damage" (not "DamageState") was the one
                    // exception, predating that convention. Renaming here keeps the
                    // Contains("DamageState") check below single-name and stops a stale
                    // "Damage" copy from ever coexisting with a fresh "DamageState" one.
                    if (AsStr(st["id"]) == "Damage") st["id"] = "DamageState";

                    stateIds.Add(AsStr(st["id"]));
                    if (!st.ContainsKey("props")) st["props"] = new Dictionary<string, object>();
                    if (!st.ContainsKey("label") || string.IsNullOrEmpty(AsStr(st["label"])))
                        st["label"] = AsStr(st["id"]);
                    if (!st.ContainsKey("class")) st["class"] = "";

                    // Writer/reader mismatch: FSMSeedGenerator.BuildDefaultSetsRoot emits
                    // "is_terminal"; the editor has only ever read/written "terminal"
                    // (BuildTypedSetsFromRaw / SyncSetToRaw). Left alone, the two states the
                    // seed marks terminal (Death/Unconscious) rendered as ordinary nodes, and
                    // after one save each state carried BOTH keys — one of them stale. Fold
                    // the legacy value in (only when "terminal" isn't already authored, so a
                    // hand-edit via the Properties panel always wins) and drop the old key.
                    if (st.ContainsKey("is_terminal"))
                    {
                        if (!st.ContainsKey("terminal"))
                            st["terminal"] = AsBool(st["is_terminal"]);
                        st.Remove("is_terminal");
                    }

                    // "is_initial" is a second source of truth for what the set-level
                    // "initial" field already names, and nothing keeps it in sync with the
                    // Mark-Initial tool (MarkInitial only ever updates set["initial"]) — so a
                    // stale "is_initial: true" on a state that is no longer initial could
                    // linger forever. Strip it; nothing reads it.
                    st.Remove("is_initial");
                }

                // AUTO_INCLUDE_DAMAGE — every set needs a vocabulary entry for DamageState so
                // a designer inspecting the graph can see it exists, even though the runtime
                // enters it directly via StateMachine.HandleHitEvent regardless of whether a
                // node is present (StateMachine.ChangeState's isSpecial escape hatch bypasses
                // the allowed-state guard for it unconditionally either way).
                if (AUTO_INCLUDE_DAMAGE && !stateIds.Contains("DamageState"))
                {
                    var damage = new Dictionary<string, object>
                    {
                        { "id", "DamageState" }, { "label", "Damage" }, { "class", "DamageState" },
                        { "props", new Dictionary<string, object>() },
                        { "special", "damage" }, { "external_entry", true }, { "terminal", false },
                    };
                    AsList(set["states"]).Add(damage);
                    stateIds.Add("DamageState");
                }

                // initial: must reference a state id
                if (!set.ContainsKey("initial") || string.IsNullOrEmpty(AsStr(set["initial"])))
                {
                    set["initial"] = stateIds.FirstOrDefault() ?? "";
                }

                // Normalize transitions
                var trIds = new HashSet<string>();
                foreach (var traw in AsList(set["transitions"]))
                {
                    if (!(traw is Dictionary<string, object> tr)) continue;
                    if (!tr.ContainsKey("id") || string.IsNullOrEmpty(AsStr(tr["id"])))
                        tr["id"] = NewTrId(trIds);
                    trIds.Add(AsStr(tr["id"]));
                    tr["from"] = AsStr(tr.ContainsKey("from") ? tr["from"] : "");
                    tr["to"]   = AsStr(tr.ContainsKey("to")   ? tr["to"]   : "");
                    // event ↔ when cross-fill
                    string when  = tr.ContainsKey("when")  ? AsStr(tr["when"])  : "";
                    string evnt  = tr.ContainsKey("event") ? AsStr(tr["event"]) : "";
                    if (string.IsNullOrEmpty(when) && !string.IsNullOrEmpty(evnt)) when = evnt;
                    if (string.IsNullOrEmpty(evnt) && !string.IsNullOrEmpty(when)) evnt = when;
                    tr["when"]  = when;
                    tr["event"] = evnt;
                    if (!tr.ContainsKey("priority"))        tr["priority"]        = 0L;
                    else                                     tr["priority"]        = (long)AsInt(tr["priority"]);
                    if (!tr.ContainsKey("cooldown_frames")) tr["cooldown_frames"] = 0L;
                    else                                     tr["cooldown_frames"] = (long)AsInt(tr["cooldown_frames"]);
                    if (!tr.ContainsKey("actions") || !(tr["actions"] is List<object>))
                        tr["actions"] = new List<object>();
                    if (AsStr(tr["from"]) == "*") tr["global"] = true;
                }
            }
        }

        // ── Public Save / Load API ───────────────────────────────────────────────

        /// <summary>Load + normalize sets.json into the editor's typed model.</summary>
        public void LoadSetsFromDisk()
        {
            _setsRoot = ReadJsonObject(SetsPath, out _setsLoadFailed);
            NormalizeSets(_setsRoot);
            BuildTypedSetsFromRaw();
        }

        /// <summary>Normalize → write sets.json → export fsm_ids.json → invalidate the
        /// runtime FSM cache so a live monster's next spawn (or `reconfig`) picks up the
        /// edit without a console command.</summary>
        public void SaveSets()
        {
            if (_setsLoadFailed)
            {
                Debug.LogError($"[FSMEditor] Refusing to save — '{SetsPath}' failed to parse " +
                                "on load. Fix or delete the file on disk and reopen F12 before saving again.");
                SetStatus("SAVE BLOCKED — sets.json failed to parse. See console.");
                return;
            }
            if (_setsRoot == null) _setsRoot = new Dictionary<string, object>();
            NormalizeSets(_setsRoot);
            WriteJson(SetsPath, _setsRoot);
            ExportFsmIds();
            // Every mutation funnels through PersistSets → SaveSets, so this is the single
            // choke point that turns the iteration loop into "edit → fight" instead of
            // "edit → remember to type reloadfsm → fight". InvalidateCache's only production
            // caller used to be that console command.
            FSMRuntimeFactory.InvalidateCache();
            SetStatus($"Saved {SetsPath}");
        }

        public void LoadAssignmentsFromDisk()
        {
            _assignmentsRoot = ReadJsonObject(AssignmentsPath, out _assignmentsLoadFailed);
            if (!_assignmentsRoot.ContainsKey("by_archetype"))
                _assignmentsRoot["by_archetype"] = new Dictionary<string, object>();
            if (!_assignmentsRoot.ContainsKey("by_eid"))
                _assignmentsRoot["by_eid"] = new Dictionary<string, object>();
        }

        public void SaveAssignments()
        {
            if (_assignmentsLoadFailed)
            {
                Debug.LogError($"[FSMEditor] Refusing to save — '{AssignmentsPath}' failed to " +
                                "parse on load. Fix or delete the file on disk and reopen F12 before saving again.");
                SetStatus("SAVE BLOCKED — assignments.json failed to parse. See console.");
                return;
            }
            WriteJson(AssignmentsPath, _assignmentsRoot ?? new Dictionary<string, object>());
            // assignments.json is the other file FSMRuntimeFactory reads (by_archetype) —
            // an archetype re-pointed at a different set needs the same cache invalidation
            // sets.json gets, or the next spawn still resolves the old set.
            FSMRuntimeFactory.InvalidateCache();
        }

        public void LoadAnimationMapFromDisk()
        {
            _animationMapRoot = ReadJsonObject(AnimationMapPath, out _animMapLoadFailed);
            if (!_animationMapRoot.ContainsKey("default"))
                _animationMapRoot["default"] = new Dictionary<string, object>();

            // Three names have existed for "per-set animation overrides": the seed
            // generator writes "per_set" (FSMSeedGenerator.BuildAnimationMapRoot), and the
            // Animations panel — the only real reader/writer — now agrees
            // (GetAnimMapDict). This method used to inject a third name, "overrides", that
            // nothing else in the project ever read. Fold whichever legacy key carries data
            // into "per_set" once, then drop both legacy names so a save carries exactly one.
            if (!_animationMapRoot.ContainsKey("per_set"))
            {
                var migrated = AsDict(_animationMapRoot.ContainsKey("overrides") ? _animationMapRoot["overrides"] : null);
                if (migrated.Count == 0)
                    migrated = AsDict(_animationMapRoot.ContainsKey("by_set") ? _animationMapRoot["by_set"] : null);
                _animationMapRoot["per_set"] = migrated;
            }
            _animationMapRoot.Remove("overrides");
            _animationMapRoot.Remove("by_set");
        }

        public void SaveAnimationMap()
        {
            if (_animMapLoadFailed)
            {
                Debug.LogError($"[FSMEditor] Refusing to save — '{AnimationMapPath}' failed to " +
                                "parse on load. Fix or delete the file on disk and reopen F12 before saving again.");
                SetStatus("SAVE BLOCKED — animation_map.json failed to parse. See console.");
                return;
            }
            WriteJson(AnimationMapPath, _animationMapRoot ?? new Dictionary<string, object>());
        }

        public void LoadLayoutsFromDisk()
        {
            _layoutsRoot = ReadJsonObject(LayoutsPath, out _layoutsLoadFailed);
            if (!_layoutsRoot.ContainsKey("by_set"))
                _layoutsRoot["by_set"] = new Dictionary<string, object>();
            ApplyLayoutToSelectedSet();
        }

        public void SaveLayouts()
        {
            if (_layoutsLoadFailed)
            {
                Debug.LogError($"[FSMEditor] Refusing to save — '{LayoutsPath}' failed to " +
                                "parse on load. Fix or delete the file on disk and reopen F12 before saving again.");
                SetStatus("SAVE BLOCKED — layouts.json failed to parse. See console.");
                return;
            }
            WriteJson(LayoutsPath, _layoutsRoot ?? new Dictionary<string, object>());
        }

        // ── Layout per-set helpers ──────────────────────────────────────────────

        private void PersistLayoutForSelectedSet()
        {
            if (_selectedSet == null || _layoutsRoot == null) return;
            var bySet = AsDict(_layoutsRoot["by_set"]);
            var entry = new Dictionary<string, object>();
            var nodes = new Dictionary<string, object>();
            foreach (var st in _selectedSet.states)
            {
                nodes[st.id] = new Dictionary<string, object>
                {
                    { "x", (long)Mathf.RoundToInt(st.x) },
                    { "y", (long)Mathf.RoundToInt(st.y) },
                };
            }
            entry["nodes"] = nodes;
            entry["viewport"] = new Dictionary<string, object>
            {
                { "zoom", (double)_zoom },
                { "pan_x", (double)_pan.x },
                { "pan_y", (double)_pan.y },
            };
            bySet[_selectedSet.id] = entry;
            _layoutsRoot["by_set"] = bySet;
            SaveLayouts();
        }

        private void ApplyLayoutToSelectedSet()
        {
            if (_selectedSet == null || _layoutsRoot == null) return;
            var bySet = AsDict(_layoutsRoot["by_set"]);
            if (!bySet.ContainsKey(_selectedSet.id)) return;
            var entry = AsDict(bySet[_selectedSet.id]);
            var nodes = AsDict(entry.ContainsKey("nodes") ? entry["nodes"] : null);
            foreach (var st in _selectedSet.states)
            {
                if (!nodes.ContainsKey(st.id)) continue;
                var n = AsDict(nodes[st.id]);
                st.x = AsFloat(n.ContainsKey("x") ? n["x"] : st.x, st.x);
                st.y = AsFloat(n.ContainsKey("y") ? n["y"] : st.y, st.y);
                if (st.raw != null)
                {
                    st.raw["x"] = (long)Mathf.RoundToInt(st.x);
                    st.raw["y"] = (long)Mathf.RoundToInt(st.y);
                }
            }
            var vp = AsDict(entry.ContainsKey("viewport") ? entry["viewport"] : null);
            if (vp.ContainsKey("zoom"))  _zoom  = AsFloat(vp["zoom"],  _zoom);
            if (vp.ContainsKey("pan_x")) _pan.x = AsFloat(vp["pan_x"], _pan.x);
            if (vp.ContainsKey("pan_y")) _pan.y = AsFloat(vp["pan_y"], _pan.y);
        }

        // ── Build typed view from raw _setsRoot ──────────────────────────────────

        private void BuildTypedSetsFromRaw()
        {
            _fsmSets.Clear();
            if (_setsRoot == null) return;
            // Auto-layout: simple grid for any state missing x/y.
            int autoCol = 0, autoRow = 0;
            foreach (var raw in AsList(_setsRoot["sets"]))
            {
                if (!(raw is Dictionary<string, object> setDict)) continue;
                var set = new FSMSetData
                {
                    raw     = setDict,
                    id      = AsStr(setDict["id"]),
                    label   = setDict.ContainsKey("label") ? AsStr(setDict["label"]) : null,
                    initial = setDict.ContainsKey("initial") ? AsStr(setDict["initial"]) : null,
                };
                int idx = 0;
                int cols = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(AsList(setDict["states"]).Count)));
                foreach (var sraw in AsList(setDict["states"]))
                {
                    if (!(sraw is Dictionary<string, object> stDict)) continue;
                    float defX = (idx % cols) * 160f + 40f;
                    float defY = (idx / cols) *  90f + 40f;
                    idx++;
                    var node = new FSMStateNode
                    {
                        raw        = stDict,
                        id         = AsStr(stDict["id"]),
                        label      = stDict.ContainsKey("label") ? AsStr(stDict["label"]) : AsStr(stDict["id"]),
                        stateClass = stDict.ContainsKey("class") ? AsStr(stDict["class"]) : "",
                        isInitial  = AsStr(set.initial) == AsStr(stDict["id"]),
                        isTerminal = stDict.ContainsKey("terminal") && AsBool(stDict["terminal"]),
                        x = AsFloat(stDict.ContainsKey("x") ? stDict["x"] : (object)defX, defX),
                        y = AsFloat(stDict.ContainsKey("y") ? stDict["y"] : (object)defY, defY),
                        w = AsFloat(stDict.ContainsKey("w") ? stDict["w"] : (object)120f, 120f),
                        h = AsFloat(stDict.ContainsKey("h") ? stDict["h"] : (object)60f, 60f),
                    };
                    set.states.Add(node);
                }
                foreach (var traw in AsList(setDict["transitions"]))
                {
                    if (!(traw is Dictionary<string, object> trDict)) continue;
                    var tr = new FSMTransitionData
                    {
                        raw    = trDict,
                        id     = AsStr(trDict.ContainsKey("id") ? trDict["id"] : ""),
                        from   = AsStr(trDict.ContainsKey("from") ? trDict["from"] : ""),
                        to     = AsStr(trDict.ContainsKey("to") ? trDict["to"] : ""),
                        whenEvent = AsStr(trDict.ContainsKey("when") ? trDict["when"]
                                         : trDict.ContainsKey("event") ? trDict["event"] : ""),
                        // The guard was write-only: the Transition tab pushed it into
                        // raw["guard"] but nothing ever read it back, so reopening the
                        // editor showed an empty field over a condition that was on disk
                        // — and pressing Enter on that empty field overwrote the real one.
                        condition = AsStr(trDict.ContainsKey("guard") ? trDict["guard"]
                                         : trDict.ContainsKey("condition") ? trDict["condition"] : ""),
                        priority       = AsInt(trDict.ContainsKey("priority") ? trDict["priority"] : 0),
                        cooldownFrames = AsInt(trDict.ContainsKey("cooldown_frames") ? trDict["cooldown_frames"] : 0),
                    };
                    tr.label = tr.whenEvent;
                    set.transitions.Add(tr);
                }
                _fsmSets.Add(set);
                autoCol++; autoRow++;
            }
        }

        // ── Sync typed → raw before saving ───────────────────────────────────────

        private void SyncSetToRaw(FSMSetData set)
        {
            if (set == null || set.raw == null) return;
            set.raw["id"] = set.id;
            if (set.label != null) set.raw["label"] = set.label;
            if (set.initial != null) set.raw["initial"] = set.initial;

            // Rebuild states list preserving raw dicts.
            var rawStates = new List<object>();
            foreach (var st in set.states)
            {
                if (st.raw == null) st.raw = new Dictionary<string, object>();
                st.raw["id"]       = st.id;
                st.raw["label"]    = st.label ?? st.id;
                st.raw["class"]    = st.stateClass ?? "";
                st.raw["terminal"] = st.isTerminal;
                st.raw["x"] = (long)Mathf.RoundToInt(st.x);
                st.raw["y"] = (long)Mathf.RoundToInt(st.y);
                if (st.w > 0) st.raw["w"] = (long)Mathf.RoundToInt(st.w);
                if (st.h > 0) st.raw["h"] = (long)Mathf.RoundToInt(st.h);
                if (!st.raw.ContainsKey("props")) st.raw["props"] = new Dictionary<string, object>();
                rawStates.Add(st.raw);
            }
            set.raw["states"] = rawStates;

            var rawTrans = new List<object>();
            foreach (var tr in set.transitions)
            {
                if (tr.raw == null) tr.raw = new Dictionary<string, object>();
                tr.raw["id"]    = tr.id;
                tr.raw["from"]  = tr.from ?? "";
                tr.raw["to"]    = tr.to   ?? "";
                tr.raw["when"]  = tr.whenEvent ?? "";
                tr.raw["event"] = tr.whenEvent ?? "";
                // Written from the typed model, not only from the commit handler, so the
                // guard survives every save path — including saves triggered by editing a
                // different field on the same transition.
                tr.raw["guard"] = tr.condition ?? "";
                tr.raw["priority"]        = (long)tr.priority;
                tr.raw["cooldown_frames"] = (long)tr.cooldownFrames;
                if (!tr.raw.ContainsKey("actions")) tr.raw["actions"] = new List<object>();
                rawTrans.Add(tr.raw);
            }
            set.raw["transitions"] = rawTrans;
        }

        /// <summary>Sync all sets, normalize, and write.</summary>
        private void PersistSets()
        {
            if (_setsRoot == null) _setsRoot = new Dictionary<string, object> { { "version", 1L }, { "sets", new List<object>() } };
            // Make sure raw["sets"] reflects current order (clones may have appended).
            var rawSets = new List<object>();
            foreach (var s in _fsmSets)
            {
                SyncSetToRaw(s);
                if (s.raw != null) rawSets.Add(s.raw);
            }
            _setsRoot["sets"] = rawSets;
            SaveSets();
        }

        // ── fsm_ids.json export ──────────────────────────────────────────────────

        private void ExportFsmIds()
        {
            var setIds = new List<object>();
            var statesBySet = new Dictionary<string, object>();
            var transBySet  = new Dictionary<string, object>();
            foreach (var s in _fsmSets)
            {
                setIds.Add(s.id);
                statesBySet[s.id] = s.states.Select(st => (object)st.id).ToList();
                transBySet[s.id]  = s.transitions.Select(t => (object)t.id).ToList();
            }
            var doc = new Dictionary<string, object>
            {
                { "SET_IDS", setIds },
                { "STATES_BY_SET", statesBySet },
                { "TRANSITIONS_BY_SET", transBySet },
            };
            WriteJson(FsmIdsPath, doc);
        }

        // ── Existing-id helpers ──────────────────────────────────────────────────

        private HashSet<string> CollectAllStateIds(FSMSetData set)
        {
            var s = new HashSet<string>(StringComparer.Ordinal);
            if (set != null) foreach (var st in set.states) if (!string.IsNullOrEmpty(st.id)) s.Add(st.id);
            return s;
        }

        private HashSet<string> CollectAllTransitionIds(FSMSetData set)
        {
            var s = new HashSet<string>(StringComparer.Ordinal);
            if (set != null) foreach (var t in set.transitions) if (!string.IsNullOrEmpty(t.id)) s.Add(t.id);
            return s;
        }

        private HashSet<string> CollectAllSetIds()
        {
            var s = new HashSet<string>(StringComparer.Ordinal);
            foreach (var x in _fsmSets) if (!string.IsNullOrEmpty(x.id)) s.Add(x.id);
            return s;
        }
    }
}
