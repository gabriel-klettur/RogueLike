using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Load Game panel builder ──────────────────────────────────────────

        private GameObject BuildLoadGamePanel(Transform parent)
        {
            const float panelW = 700f;
            const float panelH = 480f;

            var panel = CreateUIObject("LoadGamePanel", parent);
            var r = panel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            AddPanelTitle(panel.transform, "Load Game", panelH, 20f);

            // Left column: save list (scrollable)
            const float listW = 0.48f;
            var listContainer = CreateUIObject("SaveList", panel.transform);
            var lcR = listContainer.GetComponent<RectTransform>();
            lcR.anchorMin = new Vector2(0.02f, 0.08f); lcR.anchorMax = new Vector2(listW, 0.86f);
            lcR.pivot = new Vector2(0f, 1f); lcR.sizeDelta = Vector2.zero;
            lcR.anchoredPosition = Vector2.zero;

            // Pre-allocate row GameObjects (will be populated in RefreshLoadGamePanel)
            _loadPills = new Image[LOAD_VISIBLE_ROWS];
            _loadBars  = new Image[LOAD_VISIBLE_ROWS];
            _loadTexts = new TextMeshProUGUI[LOAD_VISIBLE_ROWS];

            float rowH = 36f;
            float gap  = 4f;
            for (int i = 0; i < LOAD_VISIBLE_ROWS; i++)
            {
                float cy = -i * (rowH + gap);

                var pillGo = CreateUIObject($"LPill_{i}", listContainer.transform);
                var pR = pillGo.GetComponent<RectTransform>();
                pR.anchorMin = new Vector2(0f, 1f); pR.anchorMax = new Vector2(1f, 1f);
                pR.pivot = new Vector2(0.5f, 1f);
                pR.anchoredPosition = new Vector2(0f, cy);
                pR.sizeDelta = new Vector2(0f, rowH);
                _loadPills[i] = pillGo.AddComponent<Image>(); _loadPills[i].color = Color.clear;

                var barGo = CreateUIObject($"LBar_{i}", listContainer.transform);
                var bR = barGo.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0f, 1f); bR.anchorMax = new Vector2(0f, 1f);
                bR.pivot = new Vector2(0f, 1f);
                bR.anchoredPosition = new Vector2(0f, cy);
                bR.sizeDelta = new Vector2(4f, rowH);
                _loadBars[i] = barGo.AddComponent<Image>(); _loadBars[i].color = Color.clear;

                var txtGo = CreateUIObject($"LText_{i}", listContainer.transform);
                var tR = txtGo.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(1f, 1f);
                tR.pivot = new Vector2(0f, 1f);
                tR.anchoredPosition = new Vector2(12f, cy);
                tR.sizeDelta = new Vector2(-12f, rowH);
                var tmp = txtGo.AddComponent<TextMeshProUGUI>();
                tmp.text = ""; tmp.fontSize = 17f;
                tmp.alignment = TextAlignmentOptions.Left; tmp.color = TextNormal;
                tmp.enableWordWrapping = false;
                _loadTexts[i] = tmp;

                // Click + hover
                var hitGo = CreateUIObject($"LHit_{i}", listContainer.transform);
                var hR = hitGo.GetComponent<RectTransform>();
                hR.anchorMin = new Vector2(0f, 1f); hR.anchorMax = new Vector2(1f, 1f);
                hR.pivot = new Vector2(0.5f, 1f);
                hR.anchoredPosition = new Vector2(0f, cy);
                hR.sizeDelta = new Vector2(0f, rowH);
                var hitImg = hitGo.AddComponent<Image>(); hitImg.color = Color.clear;
                var btn = hitGo.AddComponent<Button>(); btn.targetGraphic = hitImg;
                int cap = i;
                btn.onClick.AddListener(() => { _loadSel = _loadScrollOffset + cap; UpdateLoadGameVisuals(); });
                var trig = hitGo.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { _loadSel = _loadScrollOffset + cap; UpdateLoadGameVisuals(); });
                trig.triggers.Add(enter);
            }

            // Right column: details
            var detailContainer = CreateUIObject("SaveDetails", panel.transform);
            var dcR = detailContainer.GetComponent<RectTransform>();
            dcR.anchorMin = new Vector2(listW + 0.02f, 0.08f); dcR.anchorMax = new Vector2(0.98f, 0.86f);
            dcR.pivot = new Vector2(0f, 1f); dcR.sizeDelta = Vector2.zero;
            dcR.anchoredPosition = Vector2.zero;

            var detGo = CreateUIObject("DetailText", detailContainer.transform);
            var detR = detGo.GetComponent<RectTransform>();
            detR.anchorMin = Vector2.zero; detR.anchorMax = Vector2.one;
            detR.sizeDelta = Vector2.zero; detR.anchoredPosition = Vector2.zero;
            _loadDetailText = detGo.AddComponent<TextMeshProUGUI>();
            _loadDetailText.fontSize = 16f;
            _loadDetailText.alignment = TextAlignmentOptions.TopLeft;
            _loadDetailText.color = TextNormal;
            _loadDetailText.text = "Select a save.";

            // Action buttons
            float btnY = 0.02f; float btnH = 32f;
            AddLoadActionButton(panel.transform, "Load", new Vector2(listW + 0.04f, btnY),
                new Vector2(listW + 0.22f, btnY), btnH,
                new Color(0.24f, 0.47f, 0.2f, 1f), LoadSelectedSave);
            AddLoadActionButton(panel.transform, "Delete", new Vector2(listW + 0.26f, btnY),
                new Vector2(listW + 0.44f, btnY), btnH,
                new Color(0.47f, 0.2f, 0.2f, 1f), DeleteSelectedSave);

            return panel;
        }

        private void AddLoadActionButton(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax, float h,
            Color bg, UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject($"LoadBtn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorMin.x, 0f);
            rt.anchorMax = new Vector2(anchorMax.x, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 36f);
            rt.sizeDelta = new Vector2(0f, h);
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

        // ── Load game input ──────────────────────────────────────────────────

        private void HandleLoadGameInput()
        {
            if ((_cancel != null && _cancel.WasPerformedThisFrame())
                || Valkur.Core.Input.InputCompat.CancelPressed())
            { GoBack(); return; }

            if (_loadSaves.Count == 0) return;

            if ((_navUp != null && _navUp.WasPerformedThisFrame())
                || Valkur.Core.Input.InputCompat.NavUpPressed())
            {
                _loadSel = Mathf.Max(0, _loadSel - 1);
                EnsureLoadScrollVisible();
                UpdateLoadGameVisuals();
            }
            else if ((_navDown != null && _navDown.WasPerformedThisFrame())
                     || Valkur.Core.Input.InputCompat.NavDownPressed())
            {
                _loadSel = Mathf.Min(_loadSaves.Count - 1, _loadSel + 1);
                EnsureLoadScrollVisible();
                UpdateLoadGameVisuals();
            }
            else if ((_confirm != null && _confirm.WasPerformedThisFrame())
                     || Valkur.Core.Input.InputCompat.ConfirmPressed())
            {
                LoadSelectedSave();
            }
            else if (Valkur.Core.Input.KeyboardInputManager.WasDeletePressedThisFrame())
            {
                DeleteSelectedSave();
            }
        }

        private void EnsureLoadScrollVisible()
        {
            if (_loadSel < _loadScrollOffset) _loadScrollOffset = _loadSel;
            if (_loadSel >= _loadScrollOffset + LOAD_VISIBLE_ROWS)
                _loadScrollOffset = _loadSel - LOAD_VISIBLE_ROWS + 1;
        }

        // ── Load game data ───────────────────────────────────────────────────

        private void RefreshLoadGamePanel()
        {
            _loadSaves = SaveFileManager.ListSaves();
            _loadSel = 0;
            _loadScrollOffset = 0;
            UpdateLoadGameVisuals();
        }

        private void UpdateLoadGameVisuals()
        {
            if (_loadPills == null) return;

            for (int i = 0; i < LOAD_VISIBLE_ROWS; i++)
            {
                int dataIdx = _loadScrollOffset + i;
                bool hasData = dataIdx < _loadSaves.Count;
                bool selected = dataIdx == _loadSel;

                _loadPills[i].color = selected && hasData ? PillColor  : Color.clear;
                _loadBars[i].color  = selected && hasData ? AccentGold : Color.clear;
                _loadTexts[i].color = selected && hasData ? TextSelected : TextNormal;
                if (hasData)
                {
                    var sv = _loadSaves[dataIdx];
                    string display = sv.isAutoSave
                        ? $"<color=#FFC800>{SaveFileManager.AUTOSAVE_DISPLAY}</color>"
                        : sv.fileName;
                    _loadTexts[i].text = $"{display}  <color=#808080><size=12>{sv.timestamp}</size></color>";
                }
                else _loadTexts[i].text = "";
            }

            // Update details
            if (_loadDetailText != null)
            {
                if (_loadSaves.Count == 0)
                {
                    _loadDetailText.text = "No saved games.";
                }
                else if (_loadSel >= 0 && _loadSel < _loadSaves.Count)
                {
                    var info = _loadSaves[_loadSel];
                    _loadDetailText.text =
                        $"<color=#FFC800>File:</color> {info.fileName}\n\n" +
                        $"<color=#FFC800>Date:</color> {info.timestamp}\n\n" +
                        $"<color=#FFC800>Schema:</color> {info.schemaVersion}\n\n" +
                        $"<color=#FFC800>Path:</color>\n<size=13>{info.path}</size>";
                }
            }
        }

        // ── Load / Delete actions ────────────────────────────────────────────

        private void LoadSelectedSave()
        {
            if (_loadSel < 0 || _loadSel >= _loadSaves.Count) return;
            var info = _loadSaves[_loadSel];
            Debug.Log($"[PauseMenu] Loading save: {info.path}");
            if (SaveService.Instance != null && SaveService.Instance.Load(info.path))
            {
                ClosePause();
            }
            else
            {
                Debug.LogWarning($"[PauseMenu] Failed to load save: {info.path}");
            }
        }

        private void DeleteSelectedSave()
        {
            if (_loadSel < 0 || _loadSel >= _loadSaves.Count) return;
            var info = _loadSaves[_loadSel];
            Debug.Log($"[PauseMenu] Deleting save: {info.path}");
            SaveFileManager.DeleteSave(info.path);
            RefreshLoadGamePanel();
        }
    }
}
