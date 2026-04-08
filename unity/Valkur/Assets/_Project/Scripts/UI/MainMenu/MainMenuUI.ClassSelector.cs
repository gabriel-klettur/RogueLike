using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Save;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        private void BuildClassSelectorPanel(Transform canvasTransform)
        {
            _classSelectionPanel = CreateUIObject("ClassSelectionOverlay", canvasTransform);
            var overlayRect = _classSelectionPanel.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;
            _classSelectionPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var panel     = CreateUIObject("ClassSelectionPanel", _classSelectionPanel.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot     = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1120f, 560f);
            panel.AddComponent<Image>().color = new Color(0.11f, 0.11f, 0.16f, 0.97f);

            var titleGo = CreateUIObject("Title", panel.transform);
            var titleR  = titleGo.GetComponent<RectTransform>();
            titleR.anchorMin        = new Vector2(0.5f, 1f);
            titleR.anchorMax        = new Vector2(0.5f, 1f);
            titleR.pivot            = new Vector2(0.5f, 1f);
            titleR.anchoredPosition = new Vector2(0f, -20f);
            titleR.sizeDelta        = new Vector2(900f, 48f);
            var titleTMP = titleGo.AddComponent<TextMeshProUGUI>();
            titleTMP.text      = "Selecciona Personaje";
            titleTMP.fontSize  = 40f;
            titleTMP.alignment = TextAlignmentOptions.Center;
            titleTMP.color     = AccentGold;
            titleTMP.fontStyle = FontStyles.Bold;

            var rowGo   = CreateUIObject("CardsRow", panel.transform);
            var rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.anchorMin        = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax        = new Vector2(0.5f, 0.5f);
            rowRect.pivot            = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0f, 30f);
            rowRect.sizeDelta        = new Vector2(1040f, 320f);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing              = 12f;
            rowLayout.childControlWidth    = true;
            rowLayout.childControlHeight   = true;
            rowLayout.childForceExpandWidth  = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childAlignment       = TextAnchor.MiddleCenter;

            _classButtons.Clear();
            _classMarkerTexts.Clear();
            _classKeys.Clear();

            var presets = PlayerClassCatalog.AllPresets;
            for (int i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var key    = preset.PlayerKey;

                var cardGo = CreateUIObject($"Class_{key}", rowGo.transform);
                var layoutEl = cardGo.AddComponent<LayoutElement>();
                layoutEl.preferredWidth  = 196f;
                layoutEl.preferredHeight = 300f;

                var cardImg = cardGo.AddComponent<Image>();
                cardImg.color = new Color(0.18f, 0.18f, 0.24f, 0.9f);

                var cardBtn = cardGo.AddComponent<Button>();
                cardBtn.targetGraphic = cardImg;
                int captured = i;
                cardBtn.onClick.AddListener(() => OnClassCardClicked(captured));

                var cardTrigger = cardGo.AddComponent<EventTrigger>();
                var cardHover   = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                cardHover.callback.AddListener(_ => SetSelectedClassIndex(captured));
                cardTrigger.triggers.Add(cardHover);

                var nameGo = CreateUIObject("Name", cardGo.transform);
                var nameR  = nameGo.GetComponent<RectTransform>();
                nameR.anchorMin        = new Vector2(0f, 1f);
                nameR.anchorMax        = new Vector2(1f, 1f);
                nameR.pivot            = new Vector2(0.5f, 1f);
                nameR.anchoredPosition = new Vector2(0f, -12f);
                nameR.sizeDelta        = new Vector2(-12f, 40f);
                var nameTMP = nameGo.AddComponent<TextMeshProUGUI>();
                nameTMP.text      = preset.DisplayName;
                nameTMP.fontSize  = 24f;
                nameTMP.alignment = TextAlignmentOptions.Center;
                nameTMP.color     = TextNormal;
                nameTMP.fontStyle = FontStyles.Bold;

                var markerGo = CreateUIObject("Marker", cardGo.transform);
                var markerR  = markerGo.GetComponent<RectTransform>();
                markerR.anchorMin        = new Vector2(0.5f, 0.5f);
                markerR.anchorMax        = new Vector2(0.5f, 0.5f);
                markerR.pivot            = new Vector2(0.5f, 0.5f);
                markerR.anchoredPosition = new Vector2(0f, 20f);
                markerR.sizeDelta        = new Vector2(90f, 90f);
                var markerTMP = markerGo.AddComponent<TextMeshProUGUI>();
                markerTMP.text      = string.Empty;
                markerTMP.fontSize  = 68f;
                markerTMP.alignment = TextAlignmentOptions.Center;
                markerTMP.color     = AccentGold;
                markerTMP.fontStyle = FontStyles.Bold;

                var statsGo = CreateUIObject("Stats", cardGo.transform);
                var statsR  = statsGo.GetComponent<RectTransform>();
                statsR.anchorMin        = new Vector2(0f, 0f);
                statsR.anchorMax        = new Vector2(1f, 0f);
                statsR.pivot            = new Vector2(0.5f, 0f);
                statsR.anchoredPosition = new Vector2(0f, 10f);
                statsR.sizeDelta        = new Vector2(-18f, 130f);
                var statsTMP = statsGo.AddComponent<TextMeshProUGUI>();
                statsTMP.text      = $"HP {preset.MaxStrength}\nMP {preset.MaxIntelligence}\nSPD {preset.BasicSpeed:0.#}\nATK {preset.BasicAttack}";
                statsTMP.fontSize  = 19f;
                statsTMP.alignment = TextAlignmentOptions.TopLeft;
                statsTMP.color     = new Color(0.84f, 0.84f, 0.9f, 1f);

                _classButtons.Add(cardBtn);
                _classMarkerTexts.Add(markerTMP);
                _classKeys.Add(key);
            }

            var actGo   = CreateUIObject("Actions", panel.transform);
            var actRect = actGo.GetComponent<RectTransform>();
            actRect.anchorMin        = new Vector2(0.5f, 0f);
            actRect.anchorMax        = new Vector2(0.5f, 0f);
            actRect.pivot            = new Vector2(0.5f, 0f);
            actRect.anchoredPosition = new Vector2(0f, 20f);
            actRect.sizeDelta        = new Vector2(520f, 56f);
            var actLayout = actGo.AddComponent<HorizontalLayoutGroup>();
            actLayout.spacing              = 16f;
            actLayout.childControlWidth    = true;
            actLayout.childControlHeight   = true;
            actLayout.childForceExpandWidth  = true;
            actLayout.childForceExpandHeight = true;

            AddActionButton(actGo.transform, "Confirmar", new Color(0.24f, 0.47f, 0.2f, 1f), ApplySelectedClassAndStartGame);
            AddActionButton(actGo.transform, "Cancelar",  new Color(0.34f, 0.2f,  0.2f, 1f), CloseClassSelector);

            var hintGo = CreateUIObject("SelectorHint", panel.transform);
            var hintR  = hintGo.GetComponent<RectTransform>();
            hintR.anchorMin        = new Vector2(0.5f, 0f);
            hintR.anchorMax        = new Vector2(0.5f, 0f);
            hintR.pivot            = new Vector2(0.5f, 0f);
            hintR.anchoredPosition = new Vector2(0f, 84f);
            hintR.sizeDelta        = new Vector2(900f, 28f);
            var hintTMP = hintGo.AddComponent<TextMeshProUGUI>();
            hintTMP.text      = "Click selecciona y empieza.  A/D o <- -> para elegir,  Enter para confirmar";
            hintTMP.fontSize  = 18f;
            hintTMP.alignment = TextAlignmentOptions.Center;
            hintTMP.color     = VersionCol;

            _classSelectionPanel.SetActive(false);
            _selectedClassIndex = FindSelectedClassIndex();
            UpdateClassSelectionUI();
        }

        private void AddActionButton(Transform parent, string label, Color bgColor, UnityEngine.Events.UnityAction action)
        {
            var go  = CreateUIObject(label, parent);
            go.AddComponent<LayoutElement>().preferredHeight = 56f;
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(action);

            var textGo = CreateUIObject("Text", go.transform);
            var textR  = textGo.GetComponent<RectTransform>();
            textR.anchorMin = Vector2.zero;
            textR.anchorMax = Vector2.one;
            textR.sizeDelta = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = 24f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = Color.white;
            tmp.fontStyle = FontStyles.Bold;
        }
    }
}
