using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.UIKit;

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
            if (row.slider != null) row.slider.SetValueWithoutNotify(v);
            RefreshOptSoundRowText(i);
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            GameSettings.Instance?.Save();
        }

        // Routed through OnOptSoundSliderChanged so drag-from-handle and
        // arrow-key nudge share the exact same persistence pipeline. The
        // step snap keeps fractional drags landing on tunable boundaries
        // (e.g. ducking-hold lands on whole 25 ms increments).
        private void OnOptSoundSliderChanged(int i, float v)
        {
            if (i < 0 || i >= _optSoundRows.Count) return;
            var row = _optSoundRows[i];
            float snapped = v;
            if (row.step > 0f)
            {
                snapped = Mathf.Round((v - row.min) / row.step) * row.step + row.min;
                snapped = Mathf.Clamp(snapped, row.min, row.max);
                if (!Mathf.Approximately(snapped, v) && row.slider != null)
                    row.slider.SetValueWithoutNotify(snapped);
            }
            row.set(snapped);
            _optSoundSel = i;
            UpdateOptSoundsVisuals();
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

        // Cyan-track / grey-handle slider skin for Sound Options rows.
        // Reuses Valkur.UIKit.UISlider so the drag math, focus + raycast
        // wiring stay consistent with the rest of the UI kit; the kit's
        // gold defaults are overridden via the optional colour params.
        private static readonly Color OptSliderTrack  = new Color(0.20f, 0.22f, 0.27f, 1f);
        private static readonly Color OptSliderFill   = new Color(0.30f, 0.78f, 0.86f, 1f);
        private static readonly Color OptSliderHandle = new Color(0.78f, 0.78f, 0.78f, 1f);

        private Slider AddOptSoundSlider(Transform parent, string name, float cy,
            Vector2 anchorX, float min, float max, float step, float initial,
            System.Action<float> onChanged)
        {
            const float trackH  = 12f;
            const float thumb   = 18f;

            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorX.x, 1f);
            rt.anchorMax = new Vector2(anchorX.y, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, cy);
            rt.sizeDelta = new Vector2(0f, trackH);

            var slider = UISlider.Make(go.transform,
                min: min, max: max, initial: Mathf.Clamp(initial, min, max),
                onValueChanged: onChanged, height: trackH, thumbSize: thumb,
                trackColor: OptSliderTrack, fillColor: OptSliderFill, handleColor: OptSliderHandle);

            // UISlider.Make creates its child rect with a LayoutElement, but here
            // it lives outside a layout group; stretch the slider to fill the
            // anchored container so its hit area matches the visible track.
            var sRt = (RectTransform)slider.transform;
            sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
            sRt.offsetMin = Vector2.zero; sRt.offsetMax = Vector2.zero;
            return slider;
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