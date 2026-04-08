using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Load game state ──────────────────────────────────────────────────
        private GameObject _mmLoadOverlay;
        private List<SaveSlotInfo> _mmLoadSaves = new List<SaveSlotInfo>();
        private int _mmLoadSel;
        private int _mmLoadScroll;
        private const int MM_LOAD_ROWS = 8;
        private Image[] _mmLoadPills;
        private Image[] _mmLoadBars;
        private TextMeshProUGUI[] _mmLoadTexts;
        private TextMeshProUGUI _mmLoadDetailText;

        // ── Build ────────────────────────────────────────────────────────────

        private void BuildLoadGameSubmenu(Transform canvas)
        {
            _mmLoadOverlay = CreateUIObject("LoadOverlay", canvas);
            StretchFull(_mmLoadOverlay);
            _mmLoadOverlay.AddComponent<Image>().color = OverlayColor;

            const float panelW = 700f;
            const float panelH = 480f;

            var panel = CreateUIObject("LoadPanel", _mmLoadOverlay.transform);
            var pr = panel.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(0.5f, 0.5f); pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f); pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            // Title
            var titleGo = CreateUIObject("LoadTitle", panel.transform);
            var tR = titleGo.GetComponent<RectTransform>();
            tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(1f, 1f);
            tR.pivot = new Vector2(0.5f, 1f);
            tR.anchoredPosition = new Vector2(0f, -12f);
            tR.sizeDelta = new Vector2(0f, 44f);
            var titleTMP = titleGo.AddComponent<TextMeshProUGUI>();
            titleTMP.text = "Cargar Juego"; titleTMP.fontSize = 28f;
            titleTMP.alignment = TextAlignmentOptions.Center;
            titleTMP.color = AccentGold; titleTMP.fontStyle = FontStyles.Bold;

            // Hint
            var hintGo = CreateUIObject("LoadHint", panel.transform);
            var hR = hintGo.GetComponent<RectTransform>();
            hR.anchorMin = new Vector2(0f, 0f); hR.anchorMax = new Vector2(1f, 0f);
            hR.pivot = new Vector2(0.5f, 0f);
            hR.anchoredPosition = new Vector2(0f, 8f);
            hR.sizeDelta = new Vector2(0f, 28f);
            var hintTMP = hintGo.AddComponent<TextMeshProUGUI>();
            hintTMP.text = "W/S Navegar  |  Enter Cargar  |  Supr Borrar  |  Esc Volver";
            hintTMP.fontSize = 14f;
            hintTMP.alignment = TextAlignmentOptions.Center;
            hintTMP.color = VersionCol;

            // Left: save list
            const float listW = 0.48f;
            var listC = CreateUIObject("MMSaveList", panel.transform);
            var lcR = listC.GetComponent<RectTransform>();
            lcR.anchorMin = new Vector2(0.02f, 0.08f); lcR.anchorMax = new Vector2(listW, 0.86f);
            lcR.pivot = new Vector2(0f, 1f); lcR.sizeDelta = Vector2.zero;
            lcR.anchoredPosition = Vector2.zero;

            _mmLoadPills = new Image[MM_LOAD_ROWS];
            _mmLoadBars  = new Image[MM_LOAD_ROWS];
            _mmLoadTexts = new TextMeshProUGUI[MM_LOAD_ROWS];

            float rowH = 36f; float gap = 4f;
            for (int i = 0; i < MM_LOAD_ROWS; i++)
            {
                float cy = -i * (rowH + gap);

                var pillGo = CreateUIObject($"MLPill_{i}", listC.transform);
                var pRt = pillGo.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0f, 1f); pRt.anchorMax = new Vector2(1f, 1f);
                pRt.pivot = new Vector2(0.5f, 1f);
                pRt.anchoredPosition = new Vector2(0f, cy);
                pRt.sizeDelta = new Vector2(0f, rowH);
                _mmLoadPills[i] = pillGo.AddComponent<Image>(); _mmLoadPills[i].color = Color.clear;

                var barGo = CreateUIObject($"MLBar_{i}", listC.transform);
                var bRt = barGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(0f, 1f);
                bRt.pivot = new Vector2(0f, 1f);
                bRt.anchoredPosition = new Vector2(0f, cy);
                bRt.sizeDelta = new Vector2(4f, rowH);
                _mmLoadBars[i] = barGo.AddComponent<Image>(); _mmLoadBars[i].color = Color.clear;

                var txtGo = CreateUIObject($"MLText_{i}", listC.transform);
                var txtR = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = new Vector2(0f, 1f); txtR.anchorMax = new Vector2(1f, 1f);
                txtR.pivot = new Vector2(0f, 1f);
                txtR.anchoredPosition = new Vector2(12f, cy);
                txtR.sizeDelta = new Vector2(-12f, rowH);
                var tmp = txtGo.AddComponent<TextMeshProUGUI>();
                tmp.text = ""; tmp.fontSize = 17f;
                tmp.alignment = TextAlignmentOptions.Left; tmp.color = TextNormal;
                tmp.enableWordWrapping = false;
                _mmLoadTexts[i] = tmp;

                // Click + hover
                var hitGo = CreateUIObject($"MLHit_{i}", listC.transform);
                var hitRt = hitGo.GetComponent<RectTransform>();
                hitRt.anchorMin = new Vector2(0f, 1f); hitRt.anchorMax = new Vector2(1f, 1f);
                hitRt.pivot = new Vector2(0.5f, 1f);
                hitRt.anchoredPosition = new Vector2(0f, cy);
                hitRt.sizeDelta = new Vector2(0f, rowH);
                var hitImg = hitGo.AddComponent<Image>(); hitImg.color = Color.clear;
                var btn = hitGo.AddComponent<Button>(); btn.targetGraphic = hitImg;
                int cap = i;
                btn.onClick.AddListener(() => { _mmLoadSel = _mmLoadScroll + cap; UpdateMMLoadVisuals(); });
                var trig = hitGo.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { _mmLoadSel = _mmLoadScroll + cap; UpdateMMLoadVisuals(); });
                trig.triggers.Add(enter);
            }

            // Right: details
            var detC = CreateUIObject("MMSaveDetails", panel.transform);
            var dcR = detC.GetComponent<RectTransform>();
            dcR.anchorMin = new Vector2(listW + 0.02f, 0.08f); dcR.anchorMax = new Vector2(0.98f, 0.86f);
            dcR.pivot = new Vector2(0f, 1f); dcR.sizeDelta = Vector2.zero;
            dcR.anchoredPosition = Vector2.zero;

            var detGo = CreateUIObject("MMDetailText", detC.transform);
            var detRt = detGo.GetComponent<RectTransform>();
            detRt.anchorMin = Vector2.zero; detRt.anchorMax = Vector2.one;
            detRt.sizeDelta = Vector2.zero; detRt.anchoredPosition = Vector2.zero;
            _mmLoadDetailText = detGo.AddComponent<TextMeshProUGUI>();
            _mmLoadDetailText.fontSize = 16f;
            _mmLoadDetailText.alignment = TextAlignmentOptions.TopLeft;
            _mmLoadDetailText.color = TextNormal;
            _mmLoadDetailText.text = "Selecciona una partida.";

            // Action buttons
            AddMMLoadButton(panel.transform, "Cargar", new Vector2(listW + 0.04f, 0f),
                new Vector2(listW + 0.22f, 0f),
                new Color(0.24f, 0.47f, 0.2f, 1f), MMLoadSelectedSave);
            AddMMLoadButton(panel.transform, "Borrar", new Vector2(listW + 0.26f, 0f),
                new Vector2(listW + 0.44f, 0f),
                new Color(0.47f, 0.2f, 0.2f, 1f), MMDeleteSelectedSave);

            _mmLoadOverlay.SetActive(false);
        }

        private void AddMMLoadButton(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color bg,
            UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject($"MMLoadBtn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorMin.x, 0f);
            rt.anchorMax = new Vector2(anchorMax.x, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 36f);
            rt.sizeDelta = new Vector2(0f, 32f);
            var img = go.AddComponent<Image>(); img.color = bg;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(action);

            var txtGo = CreateUIObject("Label", go.transform);
            var txtR = txtGo.GetComponent<RectTransform>();
            txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
            txtR.sizeDelta = Vector2.zero; txtR.anchoredPosition = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 16f;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.fontStyle = FontStyles.Bold; tmp.raycastTarget = false;
        }

        // ── Input ────────────────────────────────────────────────────────────

        private void HandleMMLoadInput()
        {
            if (_cancelAction.WasPerformedThisFrame())
            { OptionsGoBack(); return; }

            if (_mmLoadSaves.Count == 0) return;

            if (_navUpAction.WasPerformedThisFrame())
            {
                _mmLoadSel = Mathf.Max(0, _mmLoadSel - 1);
                EnsureMMLoadScroll();
                UpdateMMLoadVisuals();
            }
            else if (_navDownAction.WasPerformedThisFrame())
            {
                _mmLoadSel = Mathf.Min(_mmLoadSaves.Count - 1, _mmLoadSel + 1);
                EnsureMMLoadScroll();
                UpdateMMLoadVisuals();
            }
            else if (_confirmAction.WasPerformedThisFrame())
            {
                MMLoadSelectedSave();
            }
            else if (Keyboard.current != null && Keyboard.current.deleteKey.wasPressedThisFrame)
            {
                MMDeleteSelectedSave();
            }
        }

        private void EnsureMMLoadScroll()
        {
            if (_mmLoadSel < _mmLoadScroll) _mmLoadScroll = _mmLoadSel;
            if (_mmLoadSel >= _mmLoadScroll + MM_LOAD_ROWS)
                _mmLoadScroll = _mmLoadSel - MM_LOAD_ROWS + 1;
        }

        // ── Data ─────────────────────────────────────────────────────────────

        private void RefreshMMLoadPanel()
        {
            _mmLoadSaves = SaveFileManager.ListSaves();
            _mmLoadSel = 0;
            _mmLoadScroll = 0;
            UpdateMMLoadVisuals();
        }

        private void UpdateMMLoadVisuals()
        {
            if (_mmLoadPills == null) return;

            for (int i = 0; i < MM_LOAD_ROWS; i++)
            {
                int dataIdx = _mmLoadScroll + i;
                bool hasData = dataIdx < _mmLoadSaves.Count;
                bool selected = dataIdx == _mmLoadSel;

                _mmLoadPills[i].color = selected && hasData ? PillColor  : Color.clear;
                _mmLoadBars[i].color  = selected && hasData ? AccentGold : Color.clear;
                _mmLoadTexts[i].color = selected && hasData ? TextSelected : TextNormal;
                _mmLoadTexts[i].text  = hasData ? _mmLoadSaves[dataIdx].fileName : "";
            }

            if (_mmLoadDetailText != null)
            {
                if (_mmLoadSaves.Count == 0)
                {
                    _mmLoadDetailText.text = "No hay partidas guardadas.";
                }
                else if (_mmLoadSel >= 0 && _mmLoadSel < _mmLoadSaves.Count)
                {
                    var info = _mmLoadSaves[_mmLoadSel];
                    _mmLoadDetailText.text =
                        $"<color=#FFC800>Archivo:</color> {info.fileName}\n\n" +
                        $"<color=#FFC800>Fecha:</color> {info.timestamp}\n\n" +
                        $"<color=#FFC800>Schema:</color> {info.schemaVersion}\n\n" +
                        $"<color=#FFC800>Ruta:</color>\n<size=13>{info.path}</size>";
                }
            }
        }

        // ── Actions ──────────────────────────────────────────────────────────

        private void MMLoadSelectedSave()
        {
            if (_mmLoadSel < 0 || _mmLoadSel >= _mmLoadSaves.Count) return;
            var info = _mmLoadSaves[_mmLoadSel];
            Debug.Log($"[MainMenu] Loading save: {info.path}");
            PendingSaveLoad.Path = info.path;
            TransitionAudioToGame();
            SceneTransitionManager.LoadScene(gameplaySceneName);
        }

        private void MMDeleteSelectedSave()
        {
            if (_mmLoadSel < 0 || _mmLoadSel >= _mmLoadSaves.Count) return;
            var info = _mmLoadSaves[_mmLoadSel];
            Debug.Log($"[MainMenu] Deleting save: {info.path}");
            SaveFileManager.DeleteSave(info.path);
            RefreshMMLoadPanel();
        }
    }
}
