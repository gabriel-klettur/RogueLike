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

        private void BuildMenuPanel(Transform canvas)
        {
            const float rowH   = 42f;
            const float padX   = 28f;
            const float padY   = 20f;
            const float gap    = 8f;
            const float panelW = 300f;

            int   count  = _menuOptions.Length;
            float panelH = padY * 2 + count * rowH + (count - 1) * gap;

            var panelGo   = CreateUIObject("MenuPanel", canvas);
            _menuPanelGo  = panelGo;
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRect.pivot            = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, -150f);
            panelRect.sizeDelta        = new Vector2(panelW, panelH);
            panelGo.AddComponent<Image>().color = PanelBg;

            _pillImages = new Image[count];
            _accentBars = new Image[count];
            _menuTexts  = new TextMeshProUGUI[count];

            for (int i = 0; i < count; i++)
            {
                float rowCY = -padY - i * (rowH + gap) - rowH * 0.5f;

                // Translucent gold pill
                var pill  = CreateUIObject($"Pill_{i}", panelGo.transform);
                var pillR = pill.GetComponent<RectTransform>();
                pillR.anchorMin        = new Vector2(0f, 1f);
                pillR.anchorMax        = new Vector2(1f, 1f);
                pillR.pivot            = new Vector2(0.5f, 0.5f);
                pillR.anchoredPosition = new Vector2(0f, rowCY);
                pillR.sizeDelta        = new Vector2(0f, rowH);
                _pillImages[i] = pill.AddComponent<Image>();
                _pillImages[i].color = Color.clear;

                // 4 px gold left bar
                var bar  = CreateUIObject($"Bar_{i}", panelGo.transform);
                var barR = bar.GetComponent<RectTransform>();
                barR.anchorMin        = new Vector2(0f, 1f);
                barR.anchorMax        = new Vector2(0f, 1f);
                barR.pivot            = new Vector2(0f, 0.5f);
                barR.anchoredPosition = new Vector2(0f, rowCY);
                barR.sizeDelta        = new Vector2(4f, rowH - 4f);
                _accentBars[i] = bar.AddComponent<Image>();
                _accentBars[i].color = Color.clear;

                // Label
                var label  = CreateUIObject($"Label_{i}", panelGo.transform);
                var labelR = label.GetComponent<RectTransform>();
                labelR.anchorMin        = new Vector2(0f, 1f);
                labelR.anchorMax        = new Vector2(1f, 1f);
                labelR.pivot            = new Vector2(0f, 0.5f);
                labelR.anchoredPosition = new Vector2(padX + 12f, rowCY);
                labelR.sizeDelta        = new Vector2(-(padX + 12f), rowH);
                var tmp = label.AddComponent<TextMeshProUGUI>();
                tmp.text      = _menuOptions[i];
                tmp.fontSize  = 22f;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.color     = TextNormal;
                _menuTexts[i] = tmp;

                // Invisible clickable row
                var row  = CreateUIObject($"Row_{i}", panelGo.transform);
                var rowR = row.GetComponent<RectTransform>();
                rowR.anchorMin        = new Vector2(0f, 1f);
                rowR.anchorMax        = new Vector2(1f, 1f);
                rowR.pivot            = new Vector2(0.5f, 0.5f);
                rowR.anchoredPosition = new Vector2(0f, rowCY);
                rowR.sizeDelta        = new Vector2(0f, rowH);
                var hitImg = row.AddComponent<Image>();
                hitImg.color = Color.clear;
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = hitImg;
                var bc = btn.colors;
                bc.normalColor      = Color.clear;
                bc.highlightedColor = Color.clear;
                bc.pressedColor     = new Color(1f, 1f, 1f, 0.05f);
                bc.selectedColor    = Color.clear;
                btn.colors = bc;

                int cap = i;
                btn.onClick.AddListener(() => ExecuteOption(cap));

                var trig       = row.AddComponent<EventTrigger>();
                var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enterEntry.callback.AddListener(_ => { _selectedIndex = cap; UpdateSelection(); });
                trig.triggers.Add(enterEntry);
            }
        }

        private void BuildFooter(Transform canvas)
        {
            var verGo = CreateUIObject("Version", canvas);
            var verR  = verGo.GetComponent<RectTransform>();
            verR.anchorMin        = new Vector2(1f, 0f);
            verR.anchorMax        = new Vector2(1f, 0f);
            verR.pivot            = new Vector2(1f, 0f);
            verR.anchoredPosition = new Vector2(-15f, 10f);
            verR.sizeDelta        = new Vector2(400f, 30f);
            var verTMP = verGo.AddComponent<TextMeshProUGUI>();
            verTMP.text      = $"v{Application.version} | Unity {Application.unityVersion}";
            verTMP.fontSize  = 14f;
            verTMP.alignment = TextAlignmentOptions.Right;
            verTMP.color     = VersionCol;

            var hintGo = CreateUIObject("ControlsHint", canvas);
            var hintR  = hintGo.GetComponent<RectTransform>();
            hintR.anchorMin        = new Vector2(0f, 0f);
            hintR.anchorMax        = new Vector2(0f, 0f);
            hintR.pivot            = new Vector2(0f, 0f);
            hintR.anchoredPosition = new Vector2(15f, 10f);
            hintR.sizeDelta        = new Vector2(500f, 30f);
            var hintTMP = hintGo.AddComponent<TextMeshProUGUI>();
            hintTMP.text      = "Mouse o W/S Navegar  |  Click o Enter Seleccionar";
            hintTMP.fontSize  = 14f;
            hintTMP.alignment = TextAlignmentOptions.Left;
            hintTMP.color     = VersionCol;
        }

        private IEnumerator RunCarousel()
        {
            if (BgPaths.Length < 2) yield break;
            while (true)
            {
                yield return new WaitForSeconds(CAROUSEL_INTERVAL);

                int nextBg   = (_bgIndex + 1) % BgPaths.Length;
                int nextSlot = 1 - _carouselSlot;

                var tex = Resources.Load<Texture2D>(BgPaths[nextBg]);
                if (tex == null) { _bgIndex = nextBg; continue; }

                _bgImages[nextSlot].sprite = MakeSprite(tex);
                _bgImages[nextSlot].color  = Color.clear;
                var fitter = _bgImages[nextSlot].GetComponent<AspectRatioFitter>();
                if (fitter != null)
                    fitter.aspectRatio = (float)tex.width / tex.height;

                float elapsed = 0f;
                while (elapsed < CAROUSEL_CROSSFADE)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / CAROUSEL_CROSSFADE);
                    _bgImages[nextSlot].color     = new Color(1f, 1f, 1f, t);
                    _bgImages[_carouselSlot].color = new Color(1f, 1f, 1f, 1f - t);
                    yield return null;
                }

                _bgImages[nextSlot].color     = Color.white;
                _bgImages[_carouselSlot].color = Color.clear;
                _carouselSlot = nextSlot;
                _bgIndex      = nextBg;
            }
        }

        private IEnumerator DeferredInit()
        {
            yield return null;
            UpdateSelection();
        }

        // ── UI helper methods shared across partial files ─────────────────

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFull(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.sizeDelta = Vector2.zero;
            r.anchoredPosition = Vector2.zero;
        }

        private static Sprite MakeSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}