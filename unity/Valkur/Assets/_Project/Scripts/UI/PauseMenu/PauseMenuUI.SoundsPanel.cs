using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.UIKit;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Sounds settings panel builder ────────────────────────────────────

        private GameObject BuildSoundsPanel(Transform parent)
        {
            var gs = GameSettings.Instance;

            var rowDefs = new (string label, float min, float max, float step,
                System.Func<float> get, System.Action<float> set)[]
            {
                ("Music",                       0f,    1f,   0.02f, () => gs.musicVolume,        v => gs.musicVolume        = v),
                ("Ambient",                     0f,    1f,   0.02f, () => gs.ambientVolume,       v => gs.ambientVolume       = v),
                ("SFX",                         0f,    1f,   0.02f, () => gs.sfxVolume,            v => gs.sfxVolume            = v),
                ("Ambient: min interval (s)",   0f,   60f,   0.5f, () => gs.ambientMinInterval,  v => gs.ambientMinInterval  = v),
                ("Ambient: max interval (s)",   0f,  120f,   0.5f, () => gs.ambientMaxInterval,  v => gs.ambientMaxInterval  = v),
                ("Ducking: attenuation (dB)", -24f,    0f,   1f,   () => gs.duckingAttenuation,  v => gs.duckingAttenuation  = v),
                ("Ducking: hold (ms)",          0f, 2000f,  25f,   () => gs.duckingHoldMs,       v => gs.duckingHoldMs       = v),
                ("Ducking: release (ms)",       0f, 2000f,  25f,   () => gs.duckingReleaseMs,    v => gs.duckingReleaseMs    = v),
            };

            const float rowH   = 40f;
            const float padX   = 20f;
            const float padY   = 16f;
            const float gap    = 6f;
            const float panelW = 540f;
            float panelH = padY * 2 + rowDefs.Length * rowH + (rowDefs.Length - 1) * gap + 60f;

            var panel = CreateUIObject("SoundsPanel", parent);
            var r     = panel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            AddPanelTitle(panel.transform, "Sound Options", panelH, padX);

            _soundRows.Clear();
            _soundPills     = new Image[rowDefs.Length];
            _soundBars      = new Image[rowDefs.Length];
            _soundRowLabels = new TextMeshProUGUI[rowDefs.Length];

            for (int i = 0; i < rowDefs.Length; i++)
            {
                var def = rowDefs[i];
                float cy = -58f - i * (rowH + gap) - rowH * 0.5f;

                var pillGo = CreateUIObject($"SPill_{i}", panel.transform);
                SetRowRect(pillGo, cy, rowH, 0f);
                _soundPills[i] = pillGo.AddComponent<Image>(); _soundPills[i].color = Color.clear;

                var barGo = CreateUIObject($"SBar_{i}", panel.transform);
                var barR  = barGo.GetComponent<RectTransform>();
                barR.anchorMin = new Vector2(0f, 1f); barR.anchorMax = new Vector2(0f, 1f);
                barR.pivot = new Vector2(0f, 0.5f);
                barR.anchoredPosition = new Vector2(0f, cy);
                barR.sizeDelta = new Vector2(4f, rowH - 4f);
                _soundBars[i] = barGo.AddComponent<Image>(); _soundBars[i].color = Color.clear;

                var lblGo = CreateUIObject($"SLabel_{i}", panel.transform);
                var lblR  = lblGo.GetComponent<RectTransform>();
                lblR.anchorMin = new Vector2(0f, 1f); lblR.anchorMax = new Vector2(0.42f, 1f);
                lblR.pivot = new Vector2(0f, 0.5f);
                lblR.anchoredPosition = new Vector2(padX + 12f, cy);
                lblR.sizeDelta = new Vector2(0f, rowH);
                var lblTMP = lblGo.AddComponent<TextMeshProUGUI>();
                lblTMP.text = def.label; lblTMP.fontSize = 18f;
                lblTMP.alignment = TextAlignmentOptions.Left; lblTMP.color = TextNormal;
                _soundRowLabels[i] = lblTMP;

                var valGo = CreateUIObject($"SVal_{i}", panel.transform);
                var valR  = valGo.GetComponent<RectTransform>();
                valR.anchorMin = new Vector2(0.86f, 1f); valR.anchorMax = new Vector2(0.97f, 1f);
                valR.pivot = new Vector2(0.5f, 0.5f);
                valR.anchoredPosition = new Vector2(0f, cy);
                valR.sizeDelta = new Vector2(0f, rowH);
                var valTMP = valGo.AddComponent<TextMeshProUGUI>();
                valTMP.fontSize = 18f; valTMP.alignment = TextAlignmentOptions.Center;
                valTMP.color = AccentGold;

                int cap = i;
                var slider = AddSoundSlider(panel.transform, $"SSlider_{i}", cy,
                    new Vector2(0.44f, 0.84f), def.min, def.max, def.step, def.get(),
                    v => OnSoundSliderChanged(cap, v));

                var hitGo = CreateUIObject($"SHit_{i}", panel.transform);
                SetRowRect(hitGo, cy, rowH, 0f);
                var hitImg = hitGo.AddComponent<Image>(); hitImg.color = Color.clear;
                var trig  = hitGo.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { _soundSel = cap; UpdateSoundsPanel(); });
                trig.triggers.Add(enter);

                var sr = new SoundRow
                {
                    valueText = valTMP,
                    slider    = slider,
                    min = def.min, max = def.max, step = def.step,
                    get = def.get, set = def.set
                };
                _soundRows.Add(sr);
                RefreshSoundRowText(i);
            }

            AddHint(panel.transform, "<- -> Adjust  |  Drag handle  |  R Reset  |  Esc Back", panelH);
            return panel;
        }

        // Cyan-track / grey-handle skin shared with the main-menu Sound Options.
        private static readonly Color SoundSliderTrack  = new Color(0.20f, 0.22f, 0.27f, 1f);
        private static readonly Color SoundSliderFill   = new Color(0.30f, 0.78f, 0.86f, 1f);
        private static readonly Color SoundSliderHandle = new Color(0.78f, 0.78f, 0.78f, 1f);

        private Slider AddSoundSlider(Transform parent, string name, float cy,
            Vector2 anchorX, float min, float max, float step, float initial,
            System.Action<float> onChanged)
        {
            const float trackH = 12f;
            const float thumb  = 18f;

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
                trackColor: SoundSliderTrack, fillColor: SoundSliderFill, handleColor: SoundSliderHandle);

            // Stretch slider to fill our anchored container (no LayoutGroup here).
            var sRt = (RectTransform)slider.transform;
            sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
            sRt.offsetMin = Vector2.zero; sRt.offsetMax = Vector2.zero;
            return slider;
        }

        private void OnSoundSliderChanged(int i, float v)
        {
            if (i < 0 || i >= _soundRows.Count) return;
            var row = _soundRows[i];
            float snapped = v;
            if (row.step > 0f)
            {
                snapped = Mathf.Round((v - row.min) / row.step) * row.step + row.min;
                snapped = Mathf.Clamp(snapped, row.min, row.max);
                if (!Mathf.Approximately(snapped, v) && row.slider != null)
                    row.slider.SetValueWithoutNotify(snapped);
            }
            row.set(snapped);
            _soundSel = i;
            UpdateSoundsPanel();
            RefreshSoundRowText(i);
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            Valkur.Core.GameSettings.Instance?.Save();
        }

        // ── Sounds panel input ───────────────────────────────────────────────

        private void HandleSoundsInput()
        {
            if (_navUp != null && _navUp.WasPerformedThisFrame())
            { _soundSel = (_soundSel - 1 + _soundRows.Count) % _soundRows.Count; UpdateSoundsPanel(); }
            else if (_navDown != null && _navDown.WasPerformedThisFrame())
            { _soundSel = (_soundSel + 1) % _soundRows.Count; UpdateSoundsPanel(); }
            else if (_navLeft != null && _navLeft.WasPerformedThisFrame())
            { ChangeSound(_soundSel, -1); }
            else if (_navRight != null && _navRight.WasPerformedThisFrame())
            { ChangeSound(_soundSel, +1); }
            else if (_confirm != null && _confirm.WasPerformedThisFrame())
            { SaveAndBack(); }
            else if (_cancel != null && _cancel.WasPerformedThisFrame())
            { GoBack(); }
        }

        private void ChangeSound(int i, int dir)
        {
            if (i < 0 || i >= _soundRows.Count) return;
            var row = _soundRows[i];
            float v = Mathf.Clamp(row.get() + dir * row.step, row.min, row.max);
            row.set(v);
            if (row.slider != null) row.slider.SetValueWithoutNotify(v);
            RefreshSoundRowText(i);
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            Valkur.Core.GameSettings.Instance?.Save();
        }

        private void SaveAndBack()
        {
            Valkur.Core.GameSettings.Instance?.Save();
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            GoBack();
        }

        private void UpdateSoundsPanel()
        {
            if (_soundPills == null || _soundBars == null) return;
            for (int i = 0; i < _soundPills.Length; i++)
            {
                bool s = i == _soundSel;
                if (i < _soundPills.Length) _soundPills[i].color         = s ? PillColor    : Color.clear;
                if (i < _soundBars.Length)  _soundBars[i].color          = s ? AccentGold   : Color.clear;
                if (_soundRowLabels != null && i < _soundRowLabels.Length)
                    _soundRowLabels[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void RefreshSoundRowText(int i)
        {
            if (i < 0 || i >= _soundRows.Count) return;
            var row = _soundRows[i];
            float v = row.get();
            row.valueText.text = row.max <= 1f
                ? Mathf.RoundToInt(v * 100f).ToString()
                : v.ToString("F1");
        }
    }
}
