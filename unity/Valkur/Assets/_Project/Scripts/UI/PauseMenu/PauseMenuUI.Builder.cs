using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Canvas & list-panel builder ──────────────────────────────────────

        partial void BuildCanvas()
        {
            var cGo = new GameObject("PauseCanvas", typeof(RectTransform));
            cGo.transform.SetParent(transform, false);
            _canvas = cGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = cGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            cGo.AddComponent<GraphicRaycaster>();

            _overlayRoot = CreateUIObject("OverlayRoot", cGo.transform);
            StretchFull(_overlayRoot);
            _overlayRoot.AddComponent<Image>().color = OverlayBg;

            // Options, Sounds, Inputs panels
            _optionsPanel = BuildListPanel(_overlayRoot.transform, "Opciones",
                _optOptions, out _optPills, out _optBars, out _optTexts);
            _soundsPanel = BuildSoundsPanel(_overlayRoot.transform);
            _inputsPanel = BuildInputsPanel(_overlayRoot.transform);
            _loadGamePanel = BuildLoadGamePanel(_overlayRoot.transform);

            // Pause panel: shell only – rows are rebuilt dynamically via RebuildPausePanelRows
            _pausePanel = CreateUIObject("PausadoPanel", _overlayRoot.transform);
            var pr = _pausePanel.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(0.5f, 0.5f); pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f); pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(380f, 52f);
            _pausePanel.AddComponent<Image>().color = PanelBg;
            AddPanelTitle(_pausePanel.transform, "Pausado", 52f, 0f);
        }

        partial void RebuildPausePanelRows()
        {
            if (_pausePanel == null || _pauseOptions == null) return;

            const float rowH   = 52f;
            const float titleH = 52f;
            const float padY   = 16f;
            const float barW   = 4f;
            const float panelW = 380f;

            // Remove old rows
            foreach (Transform child in _pausePanel.transform)
            {
                var n = child.name;
                if (n.StartsWith("Pill_") || n.StartsWith("Bar_") || n.StartsWith("Text_"))
                    Destroy(child.gameObject);
            }

            float panelH = titleH + padY + _pauseOptions.Length * rowH + padY;
            _pausePanel.GetComponent<RectTransform>().sizeDelta = new Vector2(panelW, panelH);

            _pausePills = new Image[_pauseOptions.Length];
            _pauseBars  = new Image[_pauseOptions.Length];
            _pauseTexts = new TextMeshProUGUI[_pauseOptions.Length];

            for (int i = 0; i < _pauseOptions.Length; i++)
            {
                float cy = -(titleH + padY + i * rowH + rowH * 0.5f);

                var pGo = CreateUIObject($"Pill_{i}", _pausePanel.transform);
                var pR  = pGo.GetComponent<RectTransform>();
                pR.anchorMin = new Vector2(0f, 1f); pR.anchorMax = new Vector2(1f, 1f);
                pR.pivot = new Vector2(0.5f, 0.5f);
                pR.anchoredPosition = new Vector2(0f, cy);
                pR.sizeDelta = new Vector2(0f, rowH - 4f);
                _pausePills[i] = pGo.AddComponent<Image>(); _pausePills[i].color = Color.clear;

                var bGo = CreateUIObject($"Bar_{i}", _pausePanel.transform);
                var bR  = bGo.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0f, 1f); bR.anchorMax = new Vector2(0f, 1f);
                bR.pivot = new Vector2(0f, 0.5f);
                bR.anchoredPosition = new Vector2(0f, cy);
                bR.sizeDelta = new Vector2(barW, rowH - 4f);
                _pauseBars[i] = bGo.AddComponent<Image>(); _pauseBars[i].color = Color.clear;

                var tGo = CreateUIObject($"Text_{i}", _pausePanel.transform);
                var tR  = tGo.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(1f, 1f);
                tR.pivot = new Vector2(0f, 0.5f);
                tR.anchoredPosition = new Vector2(30f, cy);
                tR.sizeDelta = new Vector2(-30f, rowH);
                var tmp = tGo.AddComponent<TextMeshProUGUI>();
                tmp.text = _pauseOptions[i]; tmp.fontSize = 22f;
                tmp.alignment = TextAlignmentOptions.Left; tmp.color = TextNormal;
                _pauseTexts[i] = tmp;
            }
        }

        // ── Static list panel ────────────────────────────────────────────────

        private GameObject BuildListPanel(Transform parent, string title, string[] options,
            out Image[] pills, out Image[] bars, out TextMeshProUGUI[] texts)
        {
            const float panelW = 380f;
            const float rowH   = 52f;
            const float titleH = 52f;
            const float padY   = 16f;
            const float barW   = 4f;
            float panelH = titleH + padY + options.Length * rowH + padY;

            var panel = CreateUIObject(title + "Panel", parent);
            var pr    = panel.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(0.5f, 0.5f); pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f); pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            AddPanelTitle(panel.transform, title, panelH, 0f);

            pills = new Image[options.Length];
            bars  = new Image[options.Length];
            texts = new TextMeshProUGUI[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                float cy = -(titleH + padY + i * rowH + rowH * 0.5f);

                var pGo = CreateUIObject($"Pill_{i}", panel.transform);
                var pR  = pGo.GetComponent<RectTransform>();
                pR.anchorMin = new Vector2(0f, 1f); pR.anchorMax = new Vector2(1f, 1f);
                pR.pivot = new Vector2(0.5f, 0.5f);
                pR.anchoredPosition = new Vector2(0f, cy);
                pR.sizeDelta = new Vector2(0f, rowH - 4f);
                pills[i] = pGo.AddComponent<Image>(); pills[i].color = Color.clear;

                var bGo = CreateUIObject($"Bar_{i}", panel.transform);
                var bR  = bGo.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0f, 1f); bR.anchorMax = new Vector2(0f, 1f);
                bR.pivot = new Vector2(0f, 0.5f);
                bR.anchoredPosition = new Vector2(0f, cy);
                bR.sizeDelta = new Vector2(barW, rowH - 4f);
                bars[i] = bGo.AddComponent<Image>(); bars[i].color = Color.clear;

                var tGo = CreateUIObject($"Text_{i}", panel.transform);
                var tR  = tGo.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(1f, 1f);
                tR.pivot = new Vector2(0f, 0.5f);
                tR.anchoredPosition = new Vector2(30f, cy);
                tR.sizeDelta = new Vector2(-30f, rowH);
                var tmp = tGo.AddComponent<TextMeshProUGUI>();
                tmp.text = options[i]; tmp.fontSize = 22f;
                tmp.alignment = TextAlignmentOptions.Left; tmp.color = TextNormal;
                texts[i] = tmp;
            }

            return panel;
        }

        // ── Shared UI helpers ────────────────────────────────────────────────

        private void AddPanelTitle(Transform parent, string text, float panelH, float padX)
        {
            var go = CreateUIObject("PanelTitle", parent);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -12f);
            r.sizeDelta = new Vector2(0f, 44f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 28f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = AccentGold; tmp.fontStyle = FontStyles.Bold;
        }

        private void AddHint(Transform parent, string text, float panelH)
        {
            var go = CreateUIObject("Hint", parent);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(0f, 8f);
            r.sizeDelta = new Vector2(0f, 28f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 14f;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = VersionCol;
        }

        private void AddStepButton(Transform parent, string name, string label,
            Vector2 anchor, float cy, float size, UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject(name, parent);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchor.x, 1f); r.anchorMax = new Vector2(anchor.x, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(0f, cy);
            r.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.28f, 1f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(action);
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

        private void AddTableCell(Transform parent, string text, TextAlignmentOptions align,
            float anchorX, float anchorW)
        {
            var go = CreateUIObject(text.Replace(" ", "_"), parent);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchorX, 0f);
            r.anchorMax = new Vector2(anchorX + anchorW, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = Vector2.zero;
            r.anchoredPosition = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 15f;
            tmp.alignment = align; tmp.color = TextNormal;
            tmp.enableWordWrapping = false;
        }

        private void SetRowRect(GameObject go, float cy, float h, float extraW)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(0f, cy);
            r.sizeDelta = new Vector2(extraW, h);
        }

        private static void StretchFull(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.sizeDelta = Vector2.zero; r.anchoredPosition = Vector2.zero;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
