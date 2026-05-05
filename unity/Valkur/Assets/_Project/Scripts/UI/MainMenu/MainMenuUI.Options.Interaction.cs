using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {

        private void UpdateOptListVisuals()
        {
            if (_optMenuPills == null) return;
            for (int i = 0; i < _optMenuPills.Length; i++)
            {
                bool s = i == _optMenuSel;
                _optMenuPills[i].color = s ? PillColor  : Color.clear;
                _optMenuBars[i].color  = s ? AccentGold : Color.clear;
                _optMenuTexts[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void UpdateOptSoundsVisuals()
        {
            if (_optSoundPills == null) return;
            for (int i = 0; i < _optSoundPills.Length; i++)
            {
                bool s = i == _optSoundSel;
                _optSoundPills[i].color = s ? PillColor  : Color.clear;
                _optSoundBars[i].color  = s ? AccentGold : Color.clear;
                if (_optSoundLabels != null && i < _optSoundLabels.Length)
                    _optSoundLabels[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void UpdateOptInputsPanel()
        {
            if (_optTabLabels == null || _optInputsPanel == null) return;
            for (int i = 0; i < _optTabLabels.Length; i++)
            {
                if (_optTabLabels[i] != null)
                    _optTabLabels[i].color = i == _optInputsTabSel ? TextSelected : TextNormal;
                var container = _optInputsPanel.transform.Find($"OTabContent_{i}");
                if (container != null) container.gameObject.SetActive(i == _optInputsTabSel);
            }

            // When switching to the Editors tab, refresh editor sub-tab visuals.
            if (_optInputsTabSel == 3)
            {
                var editorsContainer = _optInputsPanel.transform.Find("OTabContent_3");
                if (editorsContainer != null)
                    RefreshOptEditorSubTabVisuals(editorsContainer, "OESubContent");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Sound helpers
        // ════════════════════════════════════════════════════════════════════

        private void ChangeOptSound(int i, int dir)
        {
            if (i < 0 || i >= _optSoundRows.Count) return;
            var row = _optSoundRows[i];
            float v = Mathf.Clamp(row.get() + dir * row.step, row.min, row.max);
            row.set(v);
            RefreshOptSoundRowText(i);
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            GameSettings.Instance?.Save();
        }

        private void RefreshOptSoundRowText(int i)
        {
            if (i < 0 || i >= _optSoundRows.Count) return;
            var row = _optSoundRows[i];
            float v = row.get();
            row.valueText.text = row.max <= 1f
                ? Mathf.RoundToInt(v * 100f).ToString()
                : v.ToString("F1");
        }

        // ════════════════════════════════════════════════════════════════════
        // UI builder helpers (prefixed to avoid conflicts with UIBuilder.cs)
        // ════════════════════════════════════════════════════════════════════

        private void AddOptPanelTitle(Transform parent, string text)
        {
            var go = CreateUIObject("OptTitle", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -12f);
            rt.sizeDelta = new Vector2(0f, 44f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 28f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = AccentGold; tmp.fontStyle = FontStyles.Bold;
        }

        private void AddOptHint(Transform parent, string text, float panelH)
        {
            var go = CreateUIObject("OptHint", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 8f);
            rt.sizeDelta = new Vector2(0f, 28f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 14f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = VersionCol;
        }

        private void AddOptStepButton(Transform parent, string name, string label,
            Vector2 anchor, float cy, float size, UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchor.x, 1f); rt.anchorMax = new Vector2(anchor.x, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, cy);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.28f, 1f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(action);
            // Text as child
            var txtGo = CreateUIObject("Label", go.transform);
            var txtR  = txtGo.GetComponent<RectTransform>();
            txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
            txtR.sizeDelta = Vector2.zero; txtR.anchoredPosition = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 20f;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = AccentGold;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
        }

        private void AddOptTableCell(Transform parent, string text, TextAlignmentOptions align,
            float anchorX, float anchorW)
        {
            var go = CreateUIObject(text.Replace(" ", "_"), parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorX, 0f);
            rt.anchorMax = new Vector2(anchorX + anchorW, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 15f;
            tmp.alignment = align; tmp.color = TextNormal;
            tmp.enableWordWrapping = false;
        }

        private static void SetOptRowRect(GameObject go, float cy, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, cy);
            rt.sizeDelta = new Vector2(0f, h);
        }
    }
}