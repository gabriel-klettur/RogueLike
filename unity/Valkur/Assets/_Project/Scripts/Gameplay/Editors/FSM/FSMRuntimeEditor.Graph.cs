using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void RefreshGraph()
        {
            // Clear old nodes/edges
            foreach (var kv in _nodeRects)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _nodeRects.Clear();
            foreach (var e in _edgeObjects)
                if (e != null) Destroy(e);
            _edgeObjects.Clear();

            if (_selectedSet == null)
            {
                _graphInfoTmp.gameObject.SetActive(true);
                return;
            }
            _graphInfoTmp.gameObject.SetActive(false);

            // Draw nodes
            foreach (var state in _selectedSet.states)
            {
                var node = CreateNodeVisual(state);
                _nodeRects[state.id] = node;
            }

            // Draw edges
            foreach (var trans in _selectedSet.transitions)
            {
                CreateEdgeVisual(trans);
            }

            ApplyZoomPan();
        }

        private RectTransform CreateNodeVisual(FSMStateNode state)
        {
            float w = state.w > 0 ? state.w : 100f;
            float h = state.h > 0 ? state.h : 50f;

            var go = new GameObject($"Node_{state.id}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_graphContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); // top-left anchor
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(state.x, -state.y);

            var img = go.GetComponent<Image>();
            bool isSelected = _selectedState != null && _selectedState.id == state.id;
            bool isInitial = state.isInitial || (_selectedSet != null && _selectedSet.initial == state.id);
            bool isTerminal = state.isTerminal;

            if (isSelected)
                img.color = EditorUIHelpers.BTN_ACTIVE;
            else if (isInitial)
                img.color = new Color(0.2f, 0.5f, 0.2f, 0.9f);
            else if (isTerminal)
                img.color = new Color(0.55f, 0.15f, 0.15f, 0.9f);
            else
                img.color = new Color(0.15f, 0.18f, 0.22f, 0.9f);

            // Label
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4, 2); lrt.offsetMax = new Vector2(-4, -2);
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = $"<b>{state.label ?? state.id}</b>\n<size=9>{state.stateClass}</size>";
            tmp.fontSize = 11f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;

            // Click handler — dispatched via current GraphTool
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => OnNodeClicked(state));

            return rt;
        }

        private void CreateEdgeVisual(FSMTransitionData trans)
        {
            if (!_nodeRects.TryGetValue(trans.from, out var fromRect)) return;
            if (!_nodeRects.TryGetValue(trans.to, out var toRect)) return;

            // Simple line between node centres using a stretched image
            var lineGo = new GameObject($"Edge_{trans.id}", typeof(RectTransform), typeof(Image));
            lineGo.transform.SetParent(_graphContent, false);
            lineGo.transform.SetAsFirstSibling(); // Behind nodes

            var fromCenter = fromRect.anchoredPosition + fromRect.sizeDelta * new Vector2(0.5f, -0.5f);
            var toCenter = toRect.anchoredPosition + toRect.sizeDelta * new Vector2(0.5f, -0.5f);

            var diff = toCenter - fromCenter;
            float dist = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            var rt = lineGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = fromCenter;
            rt.sizeDelta = new Vector2(dist, 2f);
            rt.localRotation = Quaternion.Euler(0, 0, angle);

            bool isSelected = _selectedTransition != null && _selectedTransition.id == trans.id;
            lineGo.GetComponent<Image>().color = isSelected
                ? EditorUIHelpers.ACCENT
                : new Color(0.5f, 0.5f, 0.5f, 0.7f);

            // Edge label
            var labelGo = new GameObject("EdgeLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(_graphContent, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            var midpoint = (fromCenter + toCenter) * 0.5f + new Vector2(0, 10f);
            lrt.anchoredPosition = midpoint;
            lrt.sizeDelta = new Vector2(100, 18);
            var lbl = labelGo.GetComponent<TextMeshProUGUI>();
            lbl.text = trans.label ?? trans.whenEvent ?? "";
            lbl.fontSize = 9f;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.color = new Color(0.7f, 0.7f, 0.5f, 0.9f);
            lbl.enableWordWrapping = false;
            lbl.overflowMode = TextOverflowModes.Truncate;

            // Click handler on edge label — dispatched via current GraphTool
            var edgeBtnGo = labelGo;
            var edgeBtn = edgeBtnGo.AddComponent<Button>();
            edgeBtn.onClick.AddListener(() => OnEdgeClicked(trans));

            _edgeObjects.Add(lineGo);
            _edgeObjects.Add(labelGo);
        }

    }
}