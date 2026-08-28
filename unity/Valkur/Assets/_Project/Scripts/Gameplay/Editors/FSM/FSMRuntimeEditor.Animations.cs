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
    /// Animation Map panel — default mappings + per-set overrides.
    /// Mirrors Python <c>fsm_animation_map.json</c>.
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // "default" or a set id (e.g. "Wisp_Light")
        private string _animTarget = "default";

        private void RefreshAnimations()
        {
            var content = _uiRefs.AnimationsContent;
            if (content == null) return;
            // SafeDestroy: this refresh also runs from EditMode tests, where a raw
            // Object.Destroy is a silent no-op that logs an error instead.
            for (int i = content.childCount - 1; i >= 0; i--)
                SafeDestroy.GameObjectOf(content.GetChild(i));

            BuildAnimationsHeader(content);

            var defaults = GetAnimMapDict("default");
            var overrides = _animTarget == "default" ? null : GetAnimMapDict(_animTarget);

            // Union of keys
            var keys = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (defaults != null) foreach (var k in defaults.Keys) keys.Add(k);
            if (overrides != null) foreach (var k in overrides.Keys) keys.Add(k);
            // Also include all state classes seen in sets
            foreach (var set in _fsmSets)
                foreach (var st in set.states)
                    if (!string.IsNullOrEmpty(st.stateClass)) keys.Add(st.stateClass);

            foreach (var k in keys)
            {
                string val;
                bool inherit = false;
                if (_animTarget == "default")
                    val = defaults != null && defaults.TryGetValue(k, out var d) ? AsStr(d) : "";
                else if (overrides != null && overrides.TryGetValue(k, out var o))
                    val = AsStr(o);
                else { val = defaults != null && defaults.TryGetValue(k, out var d2) ? AsStr(d2) : ""; inherit = true; }

                BuildAnimRow(content, k, val, inherit);
            }
        }

        private void BuildAnimationsHeader(Transform parent)
        {
            var row = new GameObject("AnimHeader", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandHeight = true;
            hlg.childControlHeight = true; hlg.childControlWidth = true;

            var prev = EditorUIHelpers.MakeButton(row.transform, "<", () => CycleAnimTarget(-1), 26f, 11f);
            (prev.GetComponent<LayoutElement>() ?? prev.gameObject.AddComponent<LayoutElement>()).preferredWidth = 22f;

            var lbl = EditorUIHelpers.AddLabel(row.transform, $"target: {_animTarget}", 11f);
            (lbl.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
            lbl.alignment = TextAlignmentOptions.Center;

            var next = EditorUIHelpers.MakeButton(row.transform, ">", () => CycleAnimTarget(+1), 26f, 11f);
            (next.GetComponent<LayoutElement>() ?? next.gameObject.AddComponent<LayoutElement>()).preferredWidth = 22f;
        }

        private void CycleAnimTarget(int dir)
        {
            var list = new List<string> { "default" };
            list.AddRange(_fsmSets.Select(s => s.id));
            int idx = list.IndexOf(_animTarget);
            if (idx < 0) idx = 0;
            idx = (idx + dir + list.Count) % list.Count;
            _animTarget = list[idx];
            RefreshAnimations();
        }

        private void BuildAnimRow(Transform parent, string key, string val, bool inherit)
        {
            var row = new GameObject($"Anim_{key}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true;

            var lbl = EditorUIHelpers.AddLabel(row.transform, key, 11f);
            (lbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 110f;
            if (inherit) lbl.color = new Color(0.5f, 0.5f, 0.5f, 1f);

            string display = inherit ? $"<inherit>{(string.IsNullOrEmpty(val) ? "" : " " + val)}" : val;
            var input = EditorUIHelpers.AddInputField(row.transform, val,
                v => CommitAnim(key, v));
            (input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
            if (inherit && input.placeholder is TextMeshProUGUI ph) ph.text = display;
        }

        private Dictionary<string, object> GetAnimMapDict(string target)
        {
            if (_animationMapRoot == null) return null;
            if (target == "default")
            {
                if (!_animationMapRoot.TryGetValue("default", out var n) || !(n is Dictionary<string, object> d))
                {
                    d = new Dictionary<string, object>();
                    _animationMapRoot["default"] = d;
                }
                return d;
            }
            // "per_set" is the name FSMSeedGenerator.BuildAnimationMapRoot writes — see the
            // migration in LoadAnimationMapFromDisk for why this used to say "by_set".
            if (!_animationMapRoot.TryGetValue("per_set", out var bsObj) || !(bsObj is Dictionary<string, object> bs))
            {
                bs = new Dictionary<string, object>();
                _animationMapRoot["per_set"] = bs;
            }
            if (!bs.TryGetValue(target, out var t) || !(t is Dictionary<string, object> dt))
            {
                dt = new Dictionary<string, object>();
                bs[target] = dt;
            }
            return dt;
        }

        private void CommitAnim(string key, string val)
        {
            var dict = GetAnimMapDict(_animTarget);
            if (dict == null) return;
            val = (val ?? "").Trim();
            if (val.Length == 0) dict.Remove(key);
            else                  dict[key] = val;
            SaveAnimationMap();
            RefreshAnimations();
            SetStatus("Animations saved.");
        }
    }
}
