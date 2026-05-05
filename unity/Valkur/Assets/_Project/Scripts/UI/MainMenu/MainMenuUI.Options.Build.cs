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

        private void BuildOptionsSubmenu(Transform canvas)
        {
            _optOverlay = CreateUIObject("OptionsOverlay", canvas);
            StretchFull(_optOverlay);
            _optOverlay.AddComponent<Image>().color = OverlayColor;

            BuildOptListPanel(_optOverlay.transform);
            BuildOptSoundsPanel(_optOverlay.transform);
            BuildOptInputsPanel(_optOverlay.transform);

            _optOverlay.SetActive(false);
        }

        // ── Options list panel ───────────────────────────────────────────────

        private void BuildOptListPanel(Transform parent)
        {
            const float panelW = 380f;
            const float rowH   = 52f;
            const float titleH = 52f;
            const float padY   = 16f;
            const float barW   = 4f;
            float panelH = titleH + padY + _optMenuOptions.Length * rowH + padY;

            _optPanel = CreateUIObject("OptPanel", parent);
            var pr    = _optPanel.GetComponent<RectTransform>();
            // Anchored below the ROGUELIKE 1.0 logo (logo bottom = -260 from canvas top).
            pr.anchorMin = new Vector2(0.5f, 1f); pr.anchorMax = new Vector2(0.5f, 1f);
            pr.pivot = new Vector2(0.5f, 1f); pr.anchoredPosition = new Vector2(0f, -280f);
            pr.sizeDelta = new Vector2(panelW, panelH);
            _optPanel.AddComponent<Image>().color = PanelBg;

            AddOptPanelTitle(_optPanel.transform, "Options");

            _optMenuPills = new Image[_optMenuOptions.Length];
            _optMenuBars  = new Image[_optMenuOptions.Length];
            _optMenuTexts = new TextMeshProUGUI[_optMenuOptions.Length];

            for (int i = 0; i < _optMenuOptions.Length; i++)
            {
                float cy = -(titleH + padY + i * rowH + rowH * 0.5f);

                var pGo = CreateUIObject($"OPill_{i}", _optPanel.transform);
                var pR  = pGo.GetComponent<RectTransform>();
                pR.anchorMin = new Vector2(0f, 1f); pR.anchorMax = new Vector2(1f, 1f);
                pR.pivot = new Vector2(0.5f, 0.5f);
                pR.anchoredPosition = new Vector2(0f, cy);
                pR.sizeDelta = new Vector2(0f, rowH - 4f);
                _optMenuPills[i] = pGo.AddComponent<Image>(); _optMenuPills[i].color = Color.clear;

                var bGo = CreateUIObject($"OBar_{i}", _optPanel.transform);
                var bR  = bGo.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0f, 1f); bR.anchorMax = new Vector2(0f, 1f);
                bR.pivot = new Vector2(0f, 0.5f);
                bR.anchoredPosition = new Vector2(0f, cy);
                bR.sizeDelta = new Vector2(barW, rowH - 4f);
                _optMenuBars[i] = bGo.AddComponent<Image>(); _optMenuBars[i].color = Color.clear;

                var tGo = CreateUIObject($"OText_{i}", _optPanel.transform);
                var tR  = tGo.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(1f, 1f);
                tR.pivot = new Vector2(0f, 0.5f);
                tR.anchoredPosition = new Vector2(30f, cy);
                tR.sizeDelta = new Vector2(-30f, rowH);
                var tmp = tGo.AddComponent<TextMeshProUGUI>();
                tmp.text = _optMenuOptions[i]; tmp.fontSize = 22f;
                tmp.alignment = TextAlignmentOptions.Left; tmp.color = TextNormal;
                _optMenuTexts[i] = tmp;

                // Click/hover
                var hitGo = CreateUIObject($"OHit_{i}", _optPanel.transform);
                var hitR  = hitGo.GetComponent<RectTransform>();
                hitR.anchorMin = new Vector2(0f, 1f); hitR.anchorMax = new Vector2(1f, 1f);
                hitR.pivot = new Vector2(0.5f, 0.5f);
                hitR.anchoredPosition = new Vector2(0f, cy);
                hitR.sizeDelta = new Vector2(0f, rowH);
                var hitImg = hitGo.AddComponent<Image>(); hitImg.color = Color.clear;
                var btn = hitGo.AddComponent<Button>(); btn.targetGraphic = hitImg;
                var bc = btn.colors;
                bc.normalColor = Color.clear; bc.highlightedColor = Color.clear;
                bc.pressedColor = new Color(1f, 1f, 1f, 0.05f); bc.selectedColor = Color.clear;
                btn.colors = bc;
                int cap = i;
                btn.onClick.AddListener(() => ExecuteOptionsItem(cap));
                var trig = hitGo.AddComponent<EventTrigger>();
                var ent  = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                ent.callback.AddListener(_ => { _optMenuSel = cap; UpdateOptListVisuals(); });
                trig.triggers.Add(ent);
            }
        }

        // ── Sounds panel ─────────────────────────────────────────────────────

        private void BuildOptSoundsPanel(Transform parent)
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

            _optSoundsPanel = CreateUIObject("OptSoundsPanel", parent);
            var r = _optSoundsPanel.GetComponent<RectTransform>();
            // Anchored below the ROGUELIKE 1.0 logo (logo bottom = -260 from canvas top).
            r.anchorMin = new Vector2(0.5f, 1f); r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0f, -280f);
            r.sizeDelta = new Vector2(panelW, panelH);
            _optSoundsPanel.AddComponent<Image>().color = PanelBg;

            AddOptPanelTitle(_optSoundsPanel.transform, "Sound Options");

            _optSoundRows.Clear();
            _optSoundPills  = new Image[rowDefs.Length];
            _optSoundBars   = new Image[rowDefs.Length];
            _optSoundLabels = new TextMeshProUGUI[rowDefs.Length];

            const float btnSize = 28f;

            for (int i = 0; i < rowDefs.Length; i++)
            {
                var def = rowDefs[i];
                float cy = -58f - i * (rowH + gap) - rowH * 0.5f;

                var pillGo = CreateUIObject($"OSPill_{i}", _optSoundsPanel.transform);
                SetOptRowRect(pillGo, cy, rowH);
                _optSoundPills[i] = pillGo.AddComponent<Image>(); _optSoundPills[i].color = Color.clear;

                var barGo = CreateUIObject($"OSBar_{i}", _optSoundsPanel.transform);
                var barR  = barGo.GetComponent<RectTransform>();
                barR.anchorMin = new Vector2(0f, 1f); barR.anchorMax = new Vector2(0f, 1f);
                barR.pivot = new Vector2(0f, 0.5f);
                barR.anchoredPosition = new Vector2(0f, cy);
                barR.sizeDelta = new Vector2(4f, rowH - 4f);
                _optSoundBars[i] = barGo.AddComponent<Image>(); _optSoundBars[i].color = Color.clear;

                var lblGo = CreateUIObject($"OSLabel_{i}", _optSoundsPanel.transform);
                var lblR  = lblGo.GetComponent<RectTransform>();
                lblR.anchorMin = new Vector2(0f, 1f); lblR.anchorMax = new Vector2(0.55f, 1f);
                lblR.pivot = new Vector2(0f, 0.5f);
                lblR.anchoredPosition = new Vector2(padX + 12f, cy);
                lblR.sizeDelta = new Vector2(0f, rowH);
                var lblTMP = lblGo.AddComponent<TextMeshProUGUI>();
                lblTMP.text = def.label; lblTMP.fontSize = 18f;
                lblTMP.alignment = TextAlignmentOptions.Left; lblTMP.color = TextNormal;
                _optSoundLabels[i] = lblTMP;

                var valGo = CreateUIObject($"OSVal_{i}", _optSoundsPanel.transform);
                var valR  = valGo.GetComponent<RectTransform>();
                valR.anchorMin = new Vector2(0.58f, 1f); valR.anchorMax = new Vector2(0.72f, 1f);
                valR.pivot = new Vector2(0.5f, 0.5f);
                valR.anchoredPosition = new Vector2(0f, cy);
                valR.sizeDelta = new Vector2(0f, rowH);
                var valTMP = valGo.AddComponent<TextMeshProUGUI>();
                valTMP.fontSize = 18f; valTMP.alignment = TextAlignmentOptions.Center;
                valTMP.color = AccentGold;

                int cap = i;
                AddOptStepButton(_optSoundsPanel.transform, $"OSMin_{i}", "-",
                    new Vector2(0.75f, 0.5f), cy, btnSize, () => ChangeOptSound(cap, -1));
                AddOptStepButton(_optSoundsPanel.transform, $"OSPlus_{i}", "+",
                    new Vector2(0.88f, 0.5f), cy, btnSize, () => ChangeOptSound(cap, +1));

                var hitGo = CreateUIObject($"OSHit_{i}", _optSoundsPanel.transform);
                SetOptRowRect(hitGo, cy, rowH);
                var hitImg = hitGo.AddComponent<Image>(); hitImg.color = Color.clear;
                var hitBtn = hitGo.AddComponent<Button>(); hitBtn.targetGraphic = hitImg;
                hitBtn.onClick.AddListener(() => { _optSoundSel = cap; UpdateOptSoundsVisuals(); });
                var trig  = hitGo.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { _optSoundSel = cap; UpdateOptSoundsVisuals(); });
                trig.triggers.Add(enter);

                var sr = new SoundRow
                {
                    valueText = valTMP,
                    min = def.min, max = def.max, step = def.step,
                    get = def.get, set = def.set
                };
                _optSoundRows.Add(sr);
                RefreshOptSoundRowText(i);
            }

            AddOptHint(_optSoundsPanel.transform, "<- -> Adjust  |  Enter Save  |  Esc Back", panelH);
        }

        // ── Inputs panel ─────────────────────────────────────────────────────

    }
}