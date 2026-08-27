using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Editable property rows for the right-hand Properties panel
    /// (State / Transition / Actions / Conditions / Blackboard tabs).
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // Marker on dynamically-built rows so we can clear them between refreshes
        // without nuking the legacy PropsText sibling.
        private const string PROPS_ROW_TAG = "__FSMPropsRow";

        private void BuildPropertiesRows()
        {
            // Locate the scroll-view content (parent of PropsText).
            if (_propsTmp == null) return;
            var content = _propsTmp.transform.parent;
            if (content == null) return;

            // Ensure VerticalLayoutGroup on content (PropsText was anchored, that's fine
            // because we now hide it). Add layout if absent so rows stack.
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg == null)
            {
                vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 3f;
                vlg.padding = new RectOffset(4, 4, 4, 4);
                vlg.childForceExpandHeight = false;
                vlg.childForceExpandWidth  = true;
                vlg.childControlHeight     = true;
                vlg.childControlWidth      = true;
            }
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            // Clear previous dynamic rows. SafeDestroy: this refresh also runs from EditMode
            // tests, where a raw Object.Destroy is a silent no-op that logs an error instead.
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var c = content.GetChild(i);
                if (c.name.StartsWith(PROPS_ROW_TAG)) SafeDestroy.GameObjectOf(c);
            }
        }

        private void RebuildPropertiesContent()
        {
            BuildPropertiesRows();
            if (_propsTmp == null) return;
            var content = _propsTmp.transform.parent;
            if (content == null) return;

            // Default → fall back to text mode
            switch (_propsTab)
            {
                case PropsTab.State:       BuildStateTab(content);       break;
                case PropsTab.Transition:  BuildTransitionTab(content);  break;
                case PropsTab.Actions:     BuildKeyValueTab(content, "actions");    break;
                case PropsTab.Conditions:  BuildConditionsTab(content);             break;
                case PropsTab.Blackboard:  BuildKeyValueTab(content, "blackboard"); break;
            }
        }

        // ── State tab ─────────────────────────────────────────────────────────

        private void BuildStateTab(Transform content)
        {
            if (_selectedState == null)
            {
                _propsTmp.gameObject.SetActive(true);
                _propsTmp.text = "Click a state node to view properties.";
                return;
            }
            _propsTmp.gameObject.SetActive(false);
            var s = _selectedState;

            if (IsSeedGeneratedSet(_selectedSet))
            {
                BuildPropRow(content, "[seed]",
                    "state list auto-refreshes on regen — transitions/blackboard/labels are preserved", null);
            }

            BuildPropRow(content, "id", s.id, null);                                // RO
            BuildPropRow(content, "label", s.label, v => { s.label = v; if (s.raw != null) s.raw["label"] = v; PersistSets(); RefreshGraph(); });
            BuildPropRow(content, "class", s.stateClass, v => { s.stateClass = v; if (s.raw != null) s.raw["class"] = v; PersistSets(); RefreshGraph(); });
            BuildPropRow(content, "x", s.x.ToString("0"), v => { if (float.TryParse(v, out var f)) { s.x = f; if (s.raw != null) s.raw["x"] = (long)Mathf.RoundToInt(f); PersistSets(); RefreshGraph(); } });
            BuildPropRow(content, "y", s.y.ToString("0"), v => { if (float.TryParse(v, out var f)) { s.y = f; if (s.raw != null) s.raw["y"] = (long)Mathf.RoundToInt(f); PersistSets(); RefreshGraph(); } });
            BuildPropRow(content, "w", s.w.ToString("0"), v => { if (float.TryParse(v, out var f)) { s.w = f; if (s.raw != null) s.raw["w"] = (long)Mathf.RoundToInt(f); PersistSets(); RefreshGraph(); } });
            BuildPropRow(content, "h", s.h.ToString("0"), v => { if (float.TryParse(v, out var f)) { s.h = f; if (s.raw != null) s.raw["h"] = (long)Mathf.RoundToInt(f); PersistSets(); RefreshGraph(); } });
            BuildBoolRow(content, "terminal", s.isTerminal, val => { s.isTerminal = val; if (s.raw != null) s.raw["terminal"] = val; PersistSets(); RefreshGraph(); });

            // props.* (raw["props"] dict)
            if (s.raw != null && s.raw.TryGetValue("props", out var pObj) && pObj is Dictionary<string, object> props)
            {
                BuildSubHeader(content, "props");
                BuildInertBanner(content, "props",
                    "props round-trip with the state, but no runtime code executes them yet " +
                    "— a state's actual behavior is its hand-written IState class.");
                foreach (var k in props.Keys.OrderBy(x => x).ToList())
                {
                    var key = k;
                    BuildPropRow(content, key, AsStr(props[key]), v =>
                    {
                        if (string.IsNullOrEmpty(v)) props.Remove(key);
                        else                         props[key] = v;
                        PersistSets();
                    });
                }
            }
        }

        // ── Transition tab ────────────────────────────────────────────────────

        private void BuildTransitionTab(Transform content)
        {
            if (_selectedTransition == null)
            {
                _propsTmp.gameObject.SetActive(true);
                _propsTmp.text = "Click a transition label to view properties.";
                return;
            }
            _propsTmp.gameObject.SetActive(false);
            var t = _selectedTransition;

            BuildPropRow(content, "id", t.id, null);
            BuildPropRow(content, "from", t.from, null);
            BuildPropRow(content, "to",   t.to,   null);
            BuildPropRow(content, "when",  t.whenEvent ?? "",
                v => { t.whenEvent = v; if (t.raw != null) { t.raw["when"] = v; t.raw["event"] = v; } PersistSets(); RefreshGraph(); });
            BuildInertBanner(content, "when",
                "'when' is inert: this editor always writes a 'guard' key, and the runtime " +
                "takes the first non-null of guard/when/condition — so guard always wins. " +
                "Author the guard in 'condition' below.");
            BuildPropRow(content, "label", t.label ?? "",
                v => { t.label = v; if (t.raw != null) t.raw["label"] = v; PersistSets(); RefreshGraph(); });
            BuildPropRow(content, "priority", t.priority.ToString(),
                v => { if (int.TryParse(v, out var n)) { t.priority = n; if (t.raw != null) t.raw["priority"] = (long)n; PersistSets(); } });
            // The hint is rewritten in place on commit rather than rebuilding the tab:
            // RefreshProperties from inside the input field's own onEndEdit would destroy
            // the field mid-callback. The closure sees the assignment below.
            TextMeshProUGUI cooldownHint = null;
            BuildPropRow(content, "cooldown (frames)", t.cooldownFrames.ToString(),
                v =>
                {
                    if (!int.TryParse(v, out var n)) return;
                    t.cooldownFrames = n;
                    if (t.raw != null) t.raw["cooldown_frames"] = (long)n;
                    PersistSets();
                    if (cooldownHint != null) cooldownHint.text = ComposeCooldownHint(n);
                });
            cooldownHint = BuildHintRow(content, "cooldown", ComposeCooldownHint(t.cooldownFrames));
            BuildPropRow(content, "condition", t.condition ?? "",
                v => { t.condition = v; if (t.raw != null) t.raw["guard"] = v; PersistSets(); ReportGuardDiagnostics(t, v); });
        }

        /// <summary>
        /// The guard, with the grammar the runtime actually evaluates spelled out.
        ///
        /// This tab used to call <c>BuildKeyValueTab(content, "guard")</c>, and that
        /// helper replaces any non-dictionary value it finds with an empty dictionary —
        /// so simply LOOKING at this tab destroyed the condition the Transition tab had
        /// written. It is now the same single editable field, plus the reference a
        /// designer needs to type something the runtime will accept.
        /// </summary>
        private void BuildConditionsTab(Transform content)
        {
            if (_selectedTransition == null)
            {
                _propsTmp.gameObject.SetActive(true);
                _propsTmp.text = "Select a transition to edit its condition.";
                return;
            }
            _propsTmp.gameObject.SetActive(false);
            var t = _selectedTransition;

            BuildSubHeader(content, $"{t.from} → {t.to}");
            BuildPropRow(content, "condition", t.condition ?? "",
                v => { t.condition = v; if (t.raw != null) t.raw["guard"] = v; PersistSets(); RefreshGraph(); ReportGuardDiagnostics(t, v); });

            BuildSubHeader(content, "grammar");
            BuildPropRow(content, "form", "<signal> <op> <value>, joined by &&", null);
            BuildPropRow(content, "operators", "<   <=   >   >=   ==   !=", null);

            BuildSubHeader(content, "signals");
            BuildPropRow(content, "hp_pct", "0..1 of max HP", null);
            BuildPropRow(content, "distance_to_player", "world units", null);
            BuildPropRow(content, "state_time", "seconds in the current state", null);
            BuildPropRow(content, "is_stunned", "1 while stunned, else 0", null);
            BuildPropRow(content, "has_target", "1 when a live, non-spirit player exists", null);
            BuildPropRow(content, "time_since_hit", "seconds since last damage taken", null);
            BuildPropRow(content, "distance_from_home", "world units from the spawn anchor", null);
            BuildPropRow(content, "<any context key>", "aggro_range, melee_range, speed, …", null);

            BuildSubHeader(content, "examples");
            BuildPropRow(content, "flee when hurt", "hp_pct < 0.3", null);
            BuildPropRow(content, "give up chasing", "distance_to_player > aggro_range && state_time > 3", null);
            BuildPropRow(content, "retaliate at range", "time_since_hit < 0.5 && distance_to_player > aggro_range", null);
            BuildPropRow(content, "leash home", "distance_from_home > 30", null);
            BuildPropRow(content, "always", "(leave empty)", null);
        }

        // ── Generic key→value tab for actions/blackboard ───────────────────────

        private void BuildKeyValueTab(Transform content, string rawKey)
        {
            // Source: selected transition (preferred) or selected state.
            Dictionary<string, object> rawHost = null;
            if (_selectedTransition != null)        rawHost = _selectedTransition.raw;
            else if (_selectedState != null)        rawHost = _selectedState.raw;
            if (rawHost == null)
            {
                _propsTmp.gameObject.SetActive(true);
                _propsTmp.text = "Select a state or transition.";
                return;
            }
            _propsTmp.gameObject.SetActive(false);

            // Verified by grep: outside Scripts/Gameplay/Editors + Scripts/Editor no code
            // reads the 'actions' or 'blackboard' keys — FSMRuntimeFactory consumes only
            // from/to/guard/priority/cooldown_frames off a transition record.
            BuildInertBanner(content, rawKey,
                $"'{rawKey}' saves into sets.json and round-trips, but no runtime code " +
                "executes it yet — authoring here changes nothing in game.");

            if (!rawHost.TryGetValue(rawKey, out var dObj) || !(dObj is Dictionary<string, object> dict))
            {
                if (dObj is List<object> list)
                {
                    BuildSubHeader(content, $"{rawKey} (list, {list.Count} items)");
                    BuildPropRow(content, $"<list>", string.Join(", ", list.Select(o => AsStr(o))), null);
                    return;
                }
                dict = new Dictionary<string, object>();
                rawHost[rawKey] = dict;
            }
            BuildSubHeader(content, rawKey);
            foreach (var k in dict.Keys.OrderBy(x => x).ToList())
            {
                var key = k;
                BuildPropRow(content, key, AsStr(dict[key]),
                    v => { if (string.IsNullOrEmpty(v)) dict.Remove(key); else dict[key] = v; PersistSets(); });
            }
            // Add row
            var addBtn = EditorUIHelpers.MakeButton(content, $"+ Add {rawKey} entry", () =>
            {
                UIModal.Form(_canvas.transform, $"Add {rawKey} entry",
                    new[] { UIModal.FormField.Text("key", ""), UIModal.FormField.Text("value", "") },
                    res =>
                    {
                        var k2 = res.GetString("key").Trim();
                        var v2 = res.GetString("value").Trim();
                        if (k2.Length == 0) return;
                        dict[k2] = v2;
                        PersistSets();
                        RefreshProperties();
                    });
            }, 26f, 11f);
            addBtn.gameObject.name = PROPS_ROW_TAG + "_add";
            addBtn.GetComponent<Image>().color = new Color(0.20f, 0.35f, 0.20f, 0.95f);
        }

        // ── Author-time guard diagnostics ─────────────────────────────────────

        /// <summary>
        /// The built-in guard signals <c>FSMCondition.ResolveTerm</c> measures from the live
        /// entity, taken from the consts on <see cref="Valkur.Gameplay.FSM.FSMCondition"/>
        /// so this list cannot drift from the runtime through a rename. Any other left-hand
        /// term falls through to <c>GetContextFloat(term, 0f)</c> — which is exactly how a
        /// misspelled signal ('hp_pctt') becomes a guard that is permanently 0.
        /// </summary>
        [SelfHealingStatic("Immutable lookup table built once from FSMCondition consts. " +
            "Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly string[] KNOWN_GUARD_SIGNALS =
        {
            Valkur.Gameplay.FSM.FSMCondition.HpPct,
            Valkur.Gameplay.FSM.FSMCondition.DistanceToPlayer,
            Valkur.Gameplay.FSM.FSMCondition.StateTime,
            Valkur.Gameplay.FSM.FSMCondition.IsStunned,
            Valkur.Gameplay.FSM.FSMCondition.HasTarget,
            Valkur.Gameplay.FSM.FSMCondition.TimeSinceHit,
            Valkur.Gameplay.FSM.FSMCondition.DistanceFromHome,
        };

        /// <summary>
        /// Author-time advice for the condition field, shown in the status line on every
        /// commit. Advice only — the save is never rejected. Prevents two silent failures:
        /// a malformed guard makes <c>FSMRuntimeFactory</c> drop the WHOLE edge at load, so
        /// the graph shows an arrow that does not exist at runtime; and a misspelled signal
        /// parses fine but resolves through <c>GetContextFloat(term, 0f)</c>, so
        /// 'hp_pctt &lt; 0.25' is permanently true and 'hp_pctt &gt; 0.25' can never fire.
        /// </summary>
        private void ReportGuardDiagnostics(FSMTransitionData t, string text)
        {
            if (_statusTmp == null || t == null) return;
            string edge = $"'{t.from}' → '{t.to}'";

            var parsed = Valkur.Gameplay.FSM.FSMCondition.Parse(text, out string error);
            if (error != null)
            {
                _statusTmp.text = $"Condition saved, but {edge} will NOT exist at runtime — " +
                                   $"FSMRuntimeFactory drops the whole edge on a parse error: {error}";
                return;
            }
            if (parsed == null)
            {
                _statusTmp.text = $"Condition cleared — {edge} is UNCONDITIONAL and fires on " +
                                   "its first eligible frame.";
                return;
            }

            var unknown = CollectUnknownGuardLeftTerms(text);
            if (unknown.Count > 0)
            {
                _statusTmp.text = $"Condition on {edge} saved, but '{string.Join("', '", unknown)}' " +
                                   "is not a built-in signal — it will read as the context key " +
                                   $"'{unknown[0]}' and evaluate as 0 unless something publishes it. " +
                                   $"Built-ins: {string.Join(", ", KNOWN_GUARD_SIGNALS)}.";
                return;
            }
            _statusTmp.text = $"Condition on {edge} parses; every signal is recognised.";
        }

        /// <summary>
        /// The left-hand term of every clause in <paramref name="text"/> that is neither a
        /// numeric literal, nor true/false, nor one of <see cref="KNOWN_GUARD_SIGNALS"/>.
        /// <c>FSMCondition</c> validates clause SHAPE only and keeps its parsed clauses
        /// private, so the misspelled-signal check re-reads the text itself: split on
        /// '&amp;&amp;', cut each clause at its first operator character. Called only after
        /// Parse succeeded, so every clause is known to carry an operator; a clause without
        /// one is skipped rather than guessed at — shape errors are Parse's job.
        /// </summary>
        private List<string> CollectUnknownGuardLeftTerms(string text)
        {
            var unknown = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return unknown;

            foreach (var clauseRaw in text.Split(new[] { "&&" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var clause = clauseRaw.Trim();
                int opIdx = clause.IndexOfAny(new[] { '<', '>', '=', '!' });
                if (opIdx <= 0) continue;
                string left = clause.Substring(0, opIdx).Trim();
                if (left.Length == 0) continue;
                if (float.TryParse(left, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out _)) continue;
                if (left == "true" || left == "false") continue;
                if (KNOWN_GUARD_SIGNALS.Contains(left)) continue;
                if (!unknown.Contains(left)) unknown.Add(left);
            }
            return unknown;
        }

        /// <summary>
        /// The cooldown row's secondary line: what the number actually means at runtime.
        /// Prevents two misreadings the raw 'cooldown_frames' label invited:
        /// <c>FSMRuntimeFactory</c> divides by a hardcoded 60 at load, so the value is
        /// SECONDS at the documented reference rate, not frames of whatever this machine
        /// renders; and <c>StateMachine</c> tests <c>AppliesTo</c> before the cooldown, so
        /// the clock advances only on ticks spent in the edge's from-state — 180 is not
        /// "3 s from now", it is 3 s of accumulated time IN that state.
        /// </summary>
        private string ComposeCooldownHint(int frames)
        {
            if (frames <= 0) return "0 = no cooldown — the edge may re-fire immediately";
            return $"{frames} = {frames / 60f:0.0##} s, counted only while in the from-state";
        }

        // ── Row primitives ────────────────────────────────────────────────────

        /// <summary>
        /// Amber banner marking data this panel saves but NO runtime code reads — the same
        /// hue as <see cref="BuildEntitiesByEidWarning"/> (<see cref="ENT_GAP_COLOR"/>), so
        /// "this will be silently ignored" stays one colour across the whole editor rather
        /// than a designer learning a second warning language. TMP only, deliberately no
        /// Image: an Image and a TMP on the same GameObject throw. Named under
        /// <see cref="PROPS_ROW_TAG"/> so the between-refresh clear removes it.
        /// </summary>
        private TextMeshProUGUI BuildInertBanner(Transform parent, string name, string text)
        {
            var go = new GameObject(PROPS_ROW_TAG + "_inert_" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 42f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 10f;
            tmp.color = ENT_GAP_COLOR;
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return tmp;
        }

        /// <summary>
        /// Grey secondary line under an editable row — the read-only value colour, because
        /// it explains rather than warns. The TMP is returned so a commit handler can
        /// rewrite it in place: rebuilding the whole tab from inside an input field's own
        /// onEndEdit would destroy the field mid-callback.
        /// </summary>
        private TextMeshProUGUI BuildHintRow(Transform parent, string name, string text)
        {
            var go = new GameObject(PROPS_ROW_TAG + "_hint_" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 16f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 10f;
            tmp.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return tmp;
        }

        private void BuildPropRow(Transform parent, string label, string val, System.Action<string> onCommit)
        {
            var row = new GameObject(PROPS_ROW_TAG + "_" + label, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandHeight = true;
            hlg.childControlHeight = true; hlg.childControlWidth = true;

            var lbl = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            (lbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 90f;

            if (onCommit == null)
            {
                var ro = EditorUIHelpers.AddLabel(row.transform, val ?? "", 11f);
                (ro.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
                ro.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            }
            else
            {
                var input = EditorUIHelpers.AddInputField(row.transform, val ?? "", onCommit);
                (input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>())
                    .flexibleWidth = 1f;
            }
        }

        private void BuildBoolRow(Transform parent, string label, bool val, System.Action<bool> onChange)
        {
            var row = new GameObject(PROPS_ROW_TAG + "_" + label, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandHeight = true;
            hlg.childControlHeight = true; hlg.childControlWidth = true;

            var lbl = EditorUIHelpers.AddLabel(row.transform, label, 11f);
            (lbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 90f;

            var btn = EditorUIHelpers.MakeButton(row.transform, val ? "true" : "false",
                () => onChange?.Invoke(!val), 24f, 11f);
            (btn.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
            btn.GetComponent<Image>().color = val
                ? new Color(0.20f, 0.45f, 0.20f, 0.95f)
                : new Color(0.40f, 0.20f, 0.20f, 0.95f);
        }

        private void BuildSubHeader(Transform parent, string text)
        {
            var go = new GameObject(PROPS_ROW_TAG + "_h_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 11f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = EditorUIHelpers.ACCENT;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }
    }
}
