using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;
using Valkur.UI.Loading;

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
        private TextMeshProUGUI _mmLoadTargetLabel; // "Operará sobre: <save>" hint above action buttons

        // ── Sub-modes (rename / delete confirm) ──────────────────────────────
        private enum LoadPanelMode { List, Rename, ConfirmDelete }
        private LoadPanelMode _mmLoadMode = LoadPanelMode.List;

        // Rename overlay
        private GameObject       _mmRenameOverlay;
        private TMP_InputField   _mmRenameInput;
        private TextMeshProUGUI  _mmRenameError;

        // Confirm-delete overlay
        private GameObject       _mmConfirmOverlay;
        private TextMeshProUGUI  _mmConfirmText;
        private int              _mmConfirmSel; // 0 = Cancelar, 1 = Borrar
        private Image[]          _mmConfirmPills;
        private TextMeshProUGUI[] _mmConfirmTexts;

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
            hintTMP.text = "W/S Navegar  |  Enter Cargar  |  F2 Renombrar  |  Supr Borrar  |  Esc Volver";
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
                btn.onClick.AddListener(() =>
                {
                    int idx = _mmLoadScroll + cap;
                    if (idx < 0 || idx >= _mmLoadSaves.Count) return;
                    _mmLoadSel = idx; UpdateMMLoadVisuals();
                });
                var trig = hitGo.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ =>
                {
                    int idx = _mmLoadScroll + cap;
                    if (idx < 0 || idx >= _mmLoadSaves.Count) return;
                    _mmLoadSel = idx; UpdateMMLoadVisuals();
                });
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

            // Target-save indicator (sits just above the action button row so the
            // user always sees which slot the next click will affect).
            var targetGo = CreateUIObject("MMTargetLabel", panel.transform);
            var targetRt = targetGo.GetComponent<RectTransform>();
            targetRt.anchorMin = new Vector2(listW + 0.04f, 0f);
            targetRt.anchorMax = new Vector2(0.98f,         0f);
            targetRt.pivot = new Vector2(0.5f, 0f);
            targetRt.anchoredPosition = new Vector2(0f, 76f); // just above button row at y=36
            targetRt.sizeDelta = new Vector2(0f, 22f);
            _mmLoadTargetLabel = targetGo.AddComponent<TextMeshProUGUI>();
            _mmLoadTargetLabel.fontSize = 14f;
            _mmLoadTargetLabel.alignment = TextAlignmentOptions.Center;
            _mmLoadTargetLabel.color = AccentGold;
            _mmLoadTargetLabel.text = "";
            _mmLoadTargetLabel.raycastTarget = false;

            // Action buttons (3 columns: Cargar / Renombrar / Borrar)
            AddMMLoadButton(panel.transform, "Cargar", new Vector2(listW + 0.04f, 0f),
                new Vector2(listW + 0.18f, 0f),
                new Color(0.24f, 0.47f, 0.2f, 1f), MMLoadSelectedSave);
            AddMMLoadButton(panel.transform, "Renombrar", new Vector2(listW + 0.20f, 0f),
                new Vector2(listW + 0.34f, 0f),
                new Color(0.30f, 0.40f, 0.55f, 1f), BeginRenameSelectedSave);
            AddMMLoadButton(panel.transform, "Borrar", new Vector2(listW + 0.36f, 0f),
                new Vector2(listW + 0.50f, 0f),
                new Color(0.47f, 0.2f, 0.2f, 1f), RequestDeleteSelectedSave);

            BuildRenameOverlay(_mmLoadOverlay.transform);
            BuildDeleteConfirmOverlay(_mmLoadOverlay.transform);

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

        /// <summary>
        /// Generic overlay button placed by absolute pivot/anchor inside an
        /// overlay panel. Used for the Rename overlay's Cancelar/Aceptar pair so
        /// every action is reachable with the mouse (keyboard parity unchanged).
        /// </summary>
        private void BuildOverlayButton(Transform parent, string label,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size, Color bg,
            UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject($"OverlayBtn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>(); img.color = bg;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(action);

            var lblGo = CreateUIObject("Label", go.transform);
            var lblR  = lblGo.GetComponent<RectTransform>();
            lblR.anchorMin = Vector2.zero; lblR.anchorMax = Vector2.one;
            lblR.sizeDelta = Vector2.zero; lblR.anchoredPosition = Vector2.zero;
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 16f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white; tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
        }

        // ── Input ────────────────────────────────────────────────────────────

        private void HandleMMLoadInput()
        {
            switch (_mmLoadMode)
            {
                case LoadPanelMode.Rename:        HandleRenameInput();        return;
                case LoadPanelMode.ConfirmDelete: HandleConfirmDeleteInput(); return;
            }

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
                RequestDeleteSelectedSave();
            }
            else if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            {
                BeginRenameSelectedSave();
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
            // Preserve selection across refresh by file path so rename / delete
            // do not silently jump the cursor back to slot 0 (which would make
            // it look like "the autosave I clicked is no longer selected").
            string previouslySelectedPath = (_mmLoadSel >= 0 && _mmLoadSel < _mmLoadSaves.Count)
                ? _mmLoadSaves[_mmLoadSel].path
                : null;

            _mmLoadSaves = SaveFileManager.ListSaves();
            _mmLoadSel = 0;
            if (!string.IsNullOrEmpty(previouslySelectedPath))
            {
                for (int i = 0; i < _mmLoadSaves.Count; i++)
                {
                    if (string.Equals(_mmLoadSaves[i].path, previouslySelectedPath,
                                      System.StringComparison.OrdinalIgnoreCase))
                    { _mmLoadSel = i; break; }
                }
            }
            _mmLoadScroll = 0;
            EnsureMMLoadScroll();
            SetLoadMode(LoadPanelMode.List);
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
                if (hasData)
                {
                    var s = _mmLoadSaves[dataIdx];
                    _mmLoadTexts[i].text = s.isCorrupted
                        ? $"<color=#FF6666>[Corrupta]</color> {s.fileName}"
                        : s.fileName;
                }
                else
                {
                    _mmLoadTexts[i].text = "";
                }
            }

            // Target indicator above the action buttons. Always reflects what
            // Cargar / Renombrar / Borrar will operate on, so the user never
            // has to guess which row is "active".
            if (_mmLoadTargetLabel != null)
            {
                if (_mmLoadSel >= 0 && _mmLoadSel < _mmLoadSaves.Count)
                    _mmLoadTargetLabel.text = $"Operará sobre: <b>{_mmLoadSaves[_mmLoadSel].fileName}</b>";
                else
                    _mmLoadTargetLabel.text = "";
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
                    if (info.isCorrupted)
                    {
                        _mmLoadDetailText.text =
                            "<color=#FF6666><b>Partida corrupta</b></color>\n\n" +
                            $"<color=#FFC800>Archivo:</color> {info.fileName}\n\n" +
                            "Esta partida no se puede cargar.\n" +
                            "Puedes borrarla con <b>Supr</b>.";
                    }
                    else
                    {
                        string cls  = FormatClassName(info.playerClass);
                        string zone = string.IsNullOrEmpty(info.currentZone) ? "—" : info.currentZone;
                        string hp   = info.maxHp > 0 ? $"{info.hp}/{info.maxHp}" : "—";
                        _mmLoadDetailText.text =
                            $"<color=#FFC800>Clase:</color> {cls}\n" +
                            $"<color=#FFC800>Zona:</color>  {zone}\n\n" +
                            $"<color=#FFC800>Nivel:</color>  {info.level}     " +
                            $"<color=#FFC800>XP:</color>  {info.experience}\n" +
                            $"<color=#FFC800>HP:</color>    {hp}\n\n" +
                            $"<color=#FFC800>Guardado:</color> {info.timestamp}\n\n" +
                            $"<color=#808080><size=13>{info.fileName}</size></color>";
                    }
                }
            }
        }

        // ── Actions ──────────────────────────────────────────────────────────

        private void MMLoadSelectedSave()
        {
            if (_mmLoadSel < 0 || _mmLoadSel >= _mmLoadSaves.Count) return;
            var info = _mmLoadSaves[_mmLoadSel];
            if (info.isCorrupted)
            {
                Debug.LogWarning($"[MainMenu] Cannot load corrupted save: {info.fileName}");
                return;
            }
            Debug.Log($"[MainMenu] Loading save: {info.path}");
            PendingSaveLoad.Path = info.path;
            TransitionAudioToGame();
            LoadingScreenController.Show(gameplaySceneName);
        }

        private void MMDeleteSelectedSave()
        {
            if (_mmLoadSel < 0 || _mmLoadSel >= _mmLoadSaves.Count) return;
            var info = _mmLoadSaves[_mmLoadSel];
            Debug.Log($"[MainMenu] Deleting save: {info.path}");
            SaveFileManager.DeleteSave(info.path);
            RefreshMMLoadPanel();
            // Rebuild main menu so "Continuar" disappears when no saves remain
            RebuildMenuPanel();
        }

        // ── Rename flow ──────────────────────────────────────────────────────

        private void BeginRenameSelectedSave()
        {
            if (_mmLoadSel < 0 || _mmLoadSel >= _mmLoadSaves.Count) return;
            var info = _mmLoadSaves[_mmLoadSel];
            if (info.isCorrupted)
            {
                Debug.LogWarning("[MainMenu] Cannot rename corrupted save.");
                return;
            }
            if (_mmRenameInput != null)
            {
                _mmRenameInput.text = info.fileName;
                _mmRenameInput.Select();
                _mmRenameInput.ActivateInputField();
            }
            if (_mmRenameError != null) _mmRenameError.text = "";
            SetLoadMode(LoadPanelMode.Rename);
        }

        private void HandleRenameInput()
        {
            // Esc cancels
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelRename();
                return;
            }
            // Enter confirms (when input field has focus, Enter inserts newline by default
            // for multiline fields — TMP_InputField single-line fires onSubmit instead)
            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame ||
                 Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                CommitRename();
            }
        }

        private void CancelRename()
        {
            if (_mmRenameInput != null) _mmRenameInput.DeactivateInputField();
            SetLoadMode(LoadPanelMode.List);
        }

        private void CommitRename()
        {
            if (_mmLoadSel < 0 || _mmLoadSel >= _mmLoadSaves.Count) { CancelRename(); return; }
            var info = _mmLoadSaves[_mmLoadSel];
            string newName = _mmRenameInput != null ? _mmRenameInput.text : null;
            string sanitized = SaveFileManager.SanitizeSaveName(newName);
            if (sanitized == null)
            {
                if (_mmRenameError != null) _mmRenameError.text = "Nombre inválido.";
                return;
            }
            if (string.Equals(sanitized, info.fileName, System.StringComparison.OrdinalIgnoreCase))
            {
                CancelRename(); // no change
                return;
            }
            string newPath = SaveFileManager.RenameSave(info.path, sanitized);
            if (newPath == null)
            {
                if (_mmRenameError != null) _mmRenameError.text = "No se pudo renombrar (¿nombre duplicado?).";
                return;
            }
            // Re-list and try to keep the renamed slot selected
            _mmLoadSaves = SaveFileManager.ListSaves();
            for (int i = 0; i < _mmLoadSaves.Count; i++)
            {
                if (string.Equals(_mmLoadSaves[i].path, newPath, System.StringComparison.OrdinalIgnoreCase))
                { _mmLoadSel = i; break; }
            }
            EnsureMMLoadScroll();
            CancelRename();
        }

        // ── Delete confirmation flow ─────────────────────────────────────────

        private void RequestDeleteSelectedSave()
        {
            if (_mmLoadSel < 0 || _mmLoadSel >= _mmLoadSaves.Count) return;
            var info = _mmLoadSaves[_mmLoadSel];
            if (_mmConfirmText != null)
                _mmConfirmText.text = $"¿Borrar la partida\n<b>{info.fileName}</b>?\nEsta acción no se puede deshacer.";
            _mmConfirmSel = 0; // default to Cancelar
            UpdateConfirmVisuals();
            SetLoadMode(LoadPanelMode.ConfirmDelete);
        }

        private void HandleConfirmDeleteInput()
        {
            if (_cancelAction.WasPerformedThisFrame())
            { SetLoadMode(LoadPanelMode.List); return; }

            if (_navLeftAction.WasPerformedThisFrame() || _navRightAction.WasPerformedThisFrame())
            { _mmConfirmSel = 1 - _mmConfirmSel; UpdateConfirmVisuals(); }

            if (_confirmAction.WasPerformedThisFrame())
            {
                if (_mmConfirmSel == 1) MMDeleteSelectedSave();
                else SetLoadMode(LoadPanelMode.List);
            }
        }

        private void UpdateConfirmVisuals()
        {
            if (_mmConfirmPills == null) return;
            for (int i = 0; i < _mmConfirmPills.Length; i++)
            {
                bool sel = i == _mmConfirmSel;
                _mmConfirmPills[i].color = sel ? PillColor    : new Color(1f, 1f, 1f, 0.04f);
                _mmConfirmTexts[i].color = sel ? TextSelected : TextNormal;
                _mmConfirmTexts[i].fontStyle = sel ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ── Mode switching ───────────────────────────────────────────────────

        private void SetLoadMode(LoadPanelMode mode)
        {
            _mmLoadMode = mode;
            if (_mmRenameOverlay  != null) _mmRenameOverlay.SetActive(mode == LoadPanelMode.Rename);
            if (_mmConfirmOverlay != null) _mmConfirmOverlay.SetActive(mode == LoadPanelMode.ConfirmDelete);
        }

        // ── Builders for sub-panels ──────────────────────────────────────────

        private void BuildRenameOverlay(Transform parent)
        {
            _mmRenameOverlay = CreateUIObject("MMRenameOverlay", parent);
            StretchFull(_mmRenameOverlay);
            _mmRenameOverlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var box = CreateUIObject("MMRenameBox", _mmRenameOverlay.transform);
            var br = box.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0.5f, 0.5f); br.anchorMax = new Vector2(0.5f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(520f, 260f);
            box.AddComponent<Image>().color = PanelBg;

            var titleGo = CreateUIObject("Title", box.transform);
            var tr = titleGo.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f); tr.anchoredPosition = new Vector2(0f, -14f);
            tr.sizeDelta = new Vector2(0f, 36f);
            var ttmp = titleGo.AddComponent<TextMeshProUGUI>();
            ttmp.text = "Renombrar partida"; ttmp.fontSize = 22f;
            ttmp.alignment = TextAlignmentOptions.Center;
            ttmp.color = AccentGold; ttmp.fontStyle = FontStyles.Bold;

            // Input field background
            var fieldGo = CreateUIObject("Field", box.transform);
            var fr = fieldGo.GetComponent<RectTransform>();
            fr.anchorMin = new Vector2(0.5f, 0.5f); fr.anchorMax = new Vector2(0.5f, 0.5f);
            fr.pivot = new Vector2(0.5f, 0.5f); fr.anchoredPosition = new Vector2(0f, 30f);
            fr.sizeDelta = new Vector2(460f, 40f);
            fieldGo.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);

            var textArea = CreateUIObject("TextArea", fieldGo.transform);
            var taR = textArea.GetComponent<RectTransform>();
            taR.anchorMin = Vector2.zero; taR.anchorMax = Vector2.one;
            taR.offsetMin = new Vector2(10f, 6f); taR.offsetMax = new Vector2(-10f, -6f);
            textArea.AddComponent<RectMask2D>();

            var textGo = CreateUIObject("Text", textArea.transform);
            var txR = textGo.GetComponent<RectTransform>();
            txR.anchorMin = Vector2.zero; txR.anchorMax = Vector2.one;
            txR.sizeDelta = Vector2.zero;
            var txTMP = textGo.AddComponent<TextMeshProUGUI>();
            txTMP.fontSize = 18f; txTMP.color = TextNormal;
            txTMP.alignment = TextAlignmentOptions.Left;

            var phGo = CreateUIObject("Placeholder", textArea.transform);
            var phR = phGo.GetComponent<RectTransform>();
            phR.anchorMin = Vector2.zero; phR.anchorMax = Vector2.one;
            phR.sizeDelta = Vector2.zero;
            var phTMP = phGo.AddComponent<TextMeshProUGUI>();
            phTMP.text = "Nombre de la partida..."; phTMP.fontSize = 18f;
            phTMP.color = new Color(1f, 1f, 1f, 0.35f); phTMP.fontStyle = FontStyles.Italic;
            phTMP.alignment = TextAlignmentOptions.Left;

            _mmRenameInput = fieldGo.AddComponent<TMP_InputField>();
            _mmRenameInput.textViewport = taR;
            _mmRenameInput.textComponent = txTMP;
            _mmRenameInput.placeholder = phTMP;
            _mmRenameInput.lineType = TMP_InputField.LineType.SingleLine;
            _mmRenameInput.characterLimit = 64;
            _mmRenameInput.onSubmit.AddListener(_ => CommitRename());

            // Error / hint line (between field and buttons)
            var errGo = CreateUIObject("Error", box.transform);
            var er = errGo.GetComponent<RectTransform>();
            er.anchorMin = new Vector2(0f, 0.5f); er.anchorMax = new Vector2(1f, 0.5f);
            er.pivot = new Vector2(0.5f, 0.5f); er.anchoredPosition = new Vector2(0f, -10f);
            er.sizeDelta = new Vector2(0f, 22f);
            _mmRenameError = errGo.AddComponent<TextMeshProUGUI>();
            _mmRenameError.fontSize = 14f;
            _mmRenameError.alignment = TextAlignmentOptions.Center;
            _mmRenameError.color = new Color(1f, 0.45f, 0.45f, 1f);
            _mmRenameError.text = "";

            // Mouse-clickable buttons (Cancelar / Aceptar) — keyboard parity: Esc / Enter
            BuildOverlayButton(box.transform, "Cancelar", new Vector2(0.5f, 0f),
                new Vector2(-110f, 60f), new Vector2(180f, 38f),
                new Color(0.30f, 0.30f, 0.30f, 1f), CancelRename);
            BuildOverlayButton(box.transform, "Aceptar",  new Vector2(0.5f, 0f),
                new Vector2( 110f, 60f), new Vector2(180f, 38f),
                new Color(0.24f, 0.47f, 0.20f, 1f), CommitRename);

            var hintGo = CreateUIObject("Hint", box.transform);
            var hr = hintGo.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0.5f, 0f); hr.anchoredPosition = new Vector2(0f, 14f);
            hr.sizeDelta = new Vector2(0f, 22f);
            var htmp = hintGo.AddComponent<TextMeshProUGUI>();
            htmp.text = "Enter Confirmar  |  Esc Cancelar";
            htmp.fontSize = 13f;
            htmp.alignment = TextAlignmentOptions.Center;
            htmp.color = VersionCol;

            _mmRenameOverlay.SetActive(false);
        }        private void BuildDeleteConfirmOverlay(Transform parent)
        {
            _mmConfirmOverlay = CreateUIObject("MMConfirmOverlay", parent);
            StretchFull(_mmConfirmOverlay);
            _mmConfirmOverlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var box = CreateUIObject("MMConfirmBox", _mmConfirmOverlay.transform);
            var br = box.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0.5f, 0.5f); br.anchorMax = new Vector2(0.5f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(540f, 220f);
            box.AddComponent<Image>().color = PanelBg;

            var msgGo = CreateUIObject("Msg", box.transform);
            var mr = msgGo.GetComponent<RectTransform>();
            mr.anchorMin = new Vector2(0f, 0.35f); mr.anchorMax = new Vector2(1f, 1f);
            mr.offsetMin = new Vector2(20f, 0f); mr.offsetMax = new Vector2(-20f, -16f);
            _mmConfirmText = msgGo.AddComponent<TextMeshProUGUI>();
            _mmConfirmText.fontSize = 18f;
            _mmConfirmText.alignment = TextAlignmentOptions.Center;
            _mmConfirmText.color = TextNormal;

            // Two buttons: Cancelar (0) / Borrar (1)
            _mmConfirmPills = new Image[2];
            _mmConfirmTexts = new TextMeshProUGUI[2];
            string[] labels = { "Cancelar", "Borrar" };
            float[]  xPos   = { 0.25f, 0.75f };
            for (int i = 0; i < 2; i++)
            {
                int cap = i;
                var btnGo = CreateUIObject($"BtnConfirm_{i}", box.transform);
                var btnR  = btnGo.GetComponent<RectTransform>();
                btnR.anchorMin = new Vector2(xPos[i], 0f); btnR.anchorMax = new Vector2(xPos[i], 0f);
                btnR.pivot = new Vector2(0.5f, 0f); btnR.anchoredPosition = new Vector2(0f, 22f);
                btnR.sizeDelta = new Vector2(180f, 40f);
                _mmConfirmPills[i] = btnGo.AddComponent<Image>();
                _mmConfirmPills[i].color = new Color(1f, 1f, 1f, 0.04f);
                var btn = btnGo.AddComponent<Button>(); btn.targetGraphic = _mmConfirmPills[i];
                btn.onClick.AddListener(() =>
                {
                    _mmConfirmSel = cap; UpdateConfirmVisuals();
                    if (cap == 1) MMDeleteSelectedSave();
                    else SetLoadMode(LoadPanelMode.List);
                });

                var lblGo = CreateUIObject("Lbl", btnGo.transform);
                var lblR = lblGo.GetComponent<RectTransform>();
                lblR.anchorMin = Vector2.zero; lblR.anchorMax = Vector2.one;
                lblR.sizeDelta = Vector2.zero;
                var lblTMP = lblGo.AddComponent<TextMeshProUGUI>();
                lblTMP.text = labels[i]; lblTMP.fontSize = 18f;
                lblTMP.alignment = TextAlignmentOptions.Center;
                lblTMP.color = TextNormal; lblTMP.raycastTarget = false;
                _mmConfirmTexts[i] = lblTMP;
            }

            var hintGo = CreateUIObject("Hint", box.transform);
            var hr = hintGo.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0.5f, 0f); hr.anchoredPosition = new Vector2(0f, 4f);
            hr.sizeDelta = new Vector2(0f, 20f);
            var htmp = hintGo.AddComponent<TextMeshProUGUI>();
            htmp.text = "← → Elegir  |  Enter Confirmar  |  Esc Cancelar";
            htmp.fontSize = 13f;
            htmp.alignment = TextAlignmentOptions.Center;
            htmp.color = VersionCol;

            _mmConfirmOverlay.SetActive(false);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string FormatClassName(string key)
        {
            if (string.IsNullOrEmpty(key)) return "—";
            return char.ToUpperInvariant(key[0]) + key.Substring(1).ToLowerInvariant();
        }
    }
}
