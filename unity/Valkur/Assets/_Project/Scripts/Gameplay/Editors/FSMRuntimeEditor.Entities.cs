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
    /// Entities panel — by_archetype + by_eid editing.
    /// Mirrors Python <c>fsm_assignments.json</c> structure.
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private string _entitiesCategory = "by_archetype"; // or "by_eid"

        private void RefreshEntities()
        {
            var content = _uiRefs.EntitiesContent;
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            // Header: prev/next category arrows + label
            BuildEntitiesHeader(content);

            // Get current category dict from raw assignments
            var dict = GetAssignmentCategoryDict();
            if (dict == null)
            {
                EditorUIHelpers.AddLabel(content, "(no assignments loaded)", 11f);
                return;
            }

            // Sorted entries
            var keys = dict.Keys.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var k in keys) BuildEntityRow(content, k, AsStr(dict[k]));

            // Add row
            BuildEntityAddRow(content);
        }

        private void BuildEntitiesHeader(Transform parent)
        {
            var row = new GameObject("EntHeader", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandHeight = true;
            hlg.childControlHeight = true; hlg.childControlWidth = true;

            var prev = EditorUIHelpers.MakeButton(row.transform, "<", () => { _entitiesCategory = "by_archetype"; RefreshEntities(); }, 26f, 11f);
            var prevLE = prev.GetComponent<LayoutElement>() ?? prev.gameObject.AddComponent<LayoutElement>();
            prevLE.preferredWidth = 22f; prevLE.flexibleWidth = 0f;

            var lbl = EditorUIHelpers.AddLabel(row.transform, _entitiesCategory, 11f);
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.flexibleWidth = 1f;
            lbl.alignment = TextAlignmentOptions.Center;

            var next = EditorUIHelpers.MakeButton(row.transform, ">", () => { _entitiesCategory = "by_eid"; RefreshEntities(); }, 26f, 11f);
            var nextLE = next.GetComponent<LayoutElement>() ?? next.gameObject.AddComponent<LayoutElement>();
            nextLE.preferredWidth = 22f; nextLE.flexibleWidth = 0f;
        }

        private void BuildEntityRow(Transform parent, string key, string val)
        {
            var row = new GameObject($"Ent_{key}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true;

            var keyLbl = EditorUIHelpers.AddLabel(row.transform, key, 11f);
            (keyLbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 100f;

            var input = EditorUIHelpers.AddInputField(row.transform, val, v => CommitAssignment(key, v));
            var inLE = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
            inLE.flexibleWidth = 1f;

            var del = EditorUIHelpers.MakeButton(row.transform, "X", () => { CommitAssignment(key, ""); }, 26f, 11f);
            var dLE = del.GetComponent<LayoutElement>() ?? del.gameObject.AddComponent<LayoutElement>();
            dLE.preferredWidth = 22f; dLE.flexibleWidth = 0f;
            del.GetComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f, 0.95f);
        }

        private void BuildEntityAddRow(Transform parent)
        {
            var add = EditorUIHelpers.MakeButton(parent, $"+ Add to {_entitiesCategory}", () =>
            {
                UIModal.Form(_canvas.transform, $"Add {_entitiesCategory}",
                    new[]
                    {
                        UIModal.FormField.Text("key", ""),
                        UIModal.FormField.Text("fsm_set_id", ""),
                    },
                    result =>
                    {
                        var k = (result.GetString("key") ?? "").Trim();
                        var v = (result.GetString("fsm_set_id") ?? "").Trim();
                        if (k.Length == 0 || v.Length == 0) return;
                        CommitAssignment(k, v);
                    });
            }, 26f, 11f);
            add.GetComponent<Image>().color = new Color(0.20f, 0.35f, 0.20f, 0.95f);
        }

        // ── Data plumbing ─────────────────────────────────────────────────────

        private Dictionary<string, object> GetAssignmentCategoryDict()
        {
            if (_assignmentsRoot == null) return null;
            if (!_assignmentsRoot.TryGetValue(_entitiesCategory, out var node) ||
                !(node is Dictionary<string, object> d))
            {
                d = new Dictionary<string, object>();
                _assignmentsRoot[_entitiesCategory] = d;
            }
            return d;
        }

        private void CommitAssignment(string key, string value)
        {
            var d = GetAssignmentCategoryDict();
            if (d == null) return;
            value = (value ?? "").Trim();
            if (value.Length == 0) d.Remove(key);
            else                    d[key] = value;
            SaveAssignments();
            RefreshEntities();
            if (_statusTmp != null) _statusTmp.text = $"Assignments saved.";
        }
    }
}
