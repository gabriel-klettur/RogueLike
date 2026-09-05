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

        /// <summary>
        /// Repaints the controls summary from the LIVE bindings, so a key rebound in the
        /// in-game Controls editor shows here without either surface knowing about the other:
        /// they read the same asset. That is the whole point of there being one model — the
        /// panel this replaced wrote a parallel string table that gameplay never read.
        /// </summary>
        private void UpdateOptInputsPanel()
        {
            if (_optInputsPanel == null) return;

            var asset = Valkur.Core.Input.InputService.Instance?.Asset;
            for (int i = 0; i < _optInputValues.Count && i < _optInputActions.Count; i++)
            {
                var descriptor = _optInputActions[i];
                var map = asset?.FindActionMap(descriptor.Map, throwIfNotFound: false);
                var action = map?.FindAction(descriptor.Action, throwIfNotFound: false);

                string chip = action == null
                    ? "?"
                    : Valkur.Core.Input.InputBindingResolver.PrimaryLabel(action);
                _optInputValues[i].text = string.IsNullOrEmpty(chip) ? "sin asignar" : chip;
                _optInputValues[i].color = string.IsNullOrEmpty(chip) ? TextNormal : TextSelected;
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
        private static readonly Color OptSliderTrack  = new Color(0.20f, 0.22f, 0.27f, 1f);
        private static readonly Color OptSliderFill   = new Color(0.30f, 0.78f, 0.86f, 1f);
        private static readonly Color OptSliderHandle = new Color(0.78f, 0.78f, 0.78f, 1f);

        // Visible track stays slim (14 px) but the click / drag band spans
        // the full row so users don't need pixel-perfect aim. UIKit's
        // MakeSlimTrack handles the geometry: transparent raycast Image on
        // the host, slim visible track + fill as children, larger handle
        // on its own area.
        private Slider AddOptSoundSlider(Transform parent, string name, float cy, float rowH,
            Vector2 anchorX, float min, float max, float initial,
            System.Action<float> onChanged)
        {
            const float trackH = 14f;
            const float thumb  = 18f;

            var slider = UISlider.MakeSlimTrack(parent, name,
                min: min, max: max, initial: Mathf.Clamp(initial, min, max),
                onValueChanged: onChanged,
                hitHeight: rowH, trackHeight: trackH, thumbSize: thumb,
                trackColor: OptSliderTrack, fillColor: OptSliderFill, handleColor: OptSliderHandle);

            var rt = (RectTransform)slider.transform;
            rt.anchorMin = new Vector2(anchorX.x, 1f);
            rt.anchorMax = new Vector2(anchorX.y, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, cy);
            rt.sizeDelta = new Vector2(0f, rowH);
            return slider;
        }

        // Single-event PointerEnter listener used by Sound Options rows so
        // hovering any non-slider area (pill background, slider's hit band)
        // selects that row. EventTrigger.PointerEnter doesn't conflict with
        // the Slider component's IPointerDownHandler / IDragHandler.
        private void AttachOptRowHoverSelect(GameObject target, int rowIndex)
        {
            var trig  = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => { _optSoundSel = rowIndex; UpdateOptSoundsVisuals(); });
            trig.triggers.Add(entry);
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