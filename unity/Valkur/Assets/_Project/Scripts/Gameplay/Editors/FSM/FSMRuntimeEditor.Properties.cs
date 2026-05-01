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

            // Clear previous dynamic rows
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var c = content.GetChild(i);
                if (c.name.StartsWith(PROPS_ROW_TAG)) Destroy(c.gameObject);
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
                case PropsTab.Conditions:  BuildKeyValueTab(content, "guard");      break;
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
            BuildPropRow(content, "label", t.label ?? "",
                v => { t.label = v; if (t.raw != null) t.raw["label"] = v; PersistSets(); RefreshGraph(); });
            BuildPropRow(content, "priority", t.priority.ToString(),
                v => { if (int.TryParse(v, out var n)) { t.priority = n; if (t.raw != null) t.raw["priority"] = (long)n; PersistSets(); } });
            BuildPropRow(content, "cooldown_frames", t.cooldownFrames.ToString(),
                v => { if (int.TryParse(v, out var n)) { t.cooldownFrames = n; if (t.raw != null) t.raw["cooldown_frames"] = (long)n; PersistSets(); } });
            BuildPropRow(content, "condition", t.condition ?? "",
                v => { t.condition = v; if (t.raw != null) t.raw["guard"] = v; PersistSets(); });
        }

        // ── Generic key→value tab for actions/guard/blackboard ────────────────

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

        // ── Row primitives ────────────────────────────────────────────────────

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
