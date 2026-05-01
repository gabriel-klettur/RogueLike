using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Editors;
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
    /// Lookup order for the JSON folder:
    ///   1. <c>Application.streamingAssetsPath/FSM/</c>
    ///   2. <c>{project root}/python/data/fsm/</c> (development convenience)
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const bool AUTO_INCLUDE_DAMAGE = true;

        // ── Path resolution ──────────────────────────────────────────────────────

        private static string FsmDataDir
        {
            get
            {
                // Production: StreamingAssets/FSM
                string sa = Path.Combine(Application.streamingAssetsPath, "FSM");
                if (Directory.Exists(sa)) return sa;

                // Dev: walk up to the repo root (contains both 'unity' and 'python')
                try
                {
                    var dir = new DirectoryInfo(Application.dataPath);
                    while (dir != null)
                    {
                        var py = Path.Combine(dir.FullName, "python", "data", "fsm");
                        if (Directory.Exists(py)) return py;
                        dir = dir.Parent;
                    }
                }
                catch { /* fall through */ }

                // Fallback: ensure StreamingAssets/FSM exists for first-time saves.
                Directory.CreateDirectory(sa);
                return sa;
            }
        }

        private static string SetsPath          => Path.Combine(FsmDataDir, "sets.json");
        private static string AssignmentsPath   => Path.Combine(FsmDataDir, "assignments.json");
        private static string AnimationMapPath  => Path.Combine(FsmDataDir, "animation_map.json");
        private static string LayoutsPath       => Path.Combine(FsmDataDir, "layouts.json");
        private static string FsmIdsPath        => Path.Combine(FsmDataDir, "fsm_ids.json");

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

        private static Dictionary<string, object> ReadJsonObject(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            try
            {
                var raw = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                return raw ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FSMEditor] Failed to read '{path}': {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        private static void WriteJson(string path, object obj)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, MiniJsonRuntime.Serialize(obj, pretty: true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FSMEditor] Failed to write '{path}': {ex.Message}");
            }
        }

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
                    stateIds.Add(AsStr(st["id"]));
                    if (!st.ContainsKey("props")) st["props"] = new Dictionary<string, object>();
                    if (!st.ContainsKey("label") || string.IsNullOrEmpty(AsStr(st["label"])))
                        st["label"] = AsStr(st["id"]);
                    if (!st.ContainsKey("class")) st["class"] = "";
                }

                // AUTO_INCLUDE_DAMAGE
                if (AUTO_INCLUDE_DAMAGE && !stateIds.Contains("Damage"))
                {
                    var damage = new Dictionary<string, object>
                    {
                        { "id", "Damage" }, { "label", "Damage" },
                        { "class", "DamageState" }, { "props", new Dictionary<string, object>() },
                        { "special", "damage" }, { "external_entry", true }, { "terminal", false },
                    };
                    AsList(set["states"]).Add(damage);
                    stateIds.Add("Damage");
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
            _setsRoot = ReadJsonObject(SetsPath);
            NormalizeSets(_setsRoot);
            BuildTypedSetsFromRaw();
        }

        /// <summary>Normalize → write sets.json → export fsm_ids.json.</summary>
        public void SaveSets()
        {
            if (_setsRoot == null) _setsRoot = new Dictionary<string, object>();
            NormalizeSets(_setsRoot);
            WriteJson(SetsPath, _setsRoot);
            ExportFsmIds();
            if (_statusTmp != null) _statusTmp.text = $"Saved {SetsPath}";
        }

        public void LoadAssignmentsFromDisk()
        {
            _assignmentsRoot = ReadJsonObject(AssignmentsPath);
            if (!_assignmentsRoot.ContainsKey("by_archetype"))
                _assignmentsRoot["by_archetype"] = new Dictionary<string, object>();
            if (!_assignmentsRoot.ContainsKey("by_eid"))
                _assignmentsRoot["by_eid"] = new Dictionary<string, object>();
        }

        public void SaveAssignments() => WriteJson(AssignmentsPath, _assignmentsRoot ?? new Dictionary<string, object>());

        public void LoadAnimationMapFromDisk()
        {
            _animationMapRoot = ReadJsonObject(AnimationMapPath);
            if (!_animationMapRoot.ContainsKey("default"))
                _animationMapRoot["default"] = new Dictionary<string, object>();
            if (!_animationMapRoot.ContainsKey("overrides"))
                _animationMapRoot["overrides"] = new Dictionary<string, object>();
        }

        public void SaveAnimationMap() => WriteJson(AnimationMapPath, _animationMapRoot ?? new Dictionary<string, object>());

        public void LoadLayoutsFromDisk()
        {
            _layoutsRoot = ReadJsonObject(LayoutsPath);
            if (!_layoutsRoot.ContainsKey("by_set"))
                _layoutsRoot["by_set"] = new Dictionary<string, object>();
            ApplyLayoutToSelectedSet();
        }

        public void SaveLayouts() => WriteJson(LayoutsPath, _layoutsRoot ?? new Dictionary<string, object>());

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
