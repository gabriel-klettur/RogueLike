using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Save;
using Valkur.UI.Loading;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Load / Delete actions ─────────────────────────────────────────────────

        private void MMLoadSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            if (info.isCorrupted)
            {
                Debug.LogWarning($"[MainMenu] Cannot load corrupted save: {info.fileName}");
                return;
            }
            Debug.Log($"[MainMenu] Loading save: {info.path}");
            PendingSaveLoad.Path        = info.path;
            PendingSaveLoad.PlayerClass = info.playerClass;
            TransitionAudioToGame();
            LoadingScreenController.Show(gameplaySceneName);
        }

        private void MMDeleteSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            Debug.Log($"[MainMenu] Deleting save: {info.path}");
            SaveFileManager.DeleteSave(info.path);
            RefreshMMLoadPanel();
            RebuildMenuPanel();
        }

        // ── Rename flow ───────────────────────────────────────────────────────────

        private void BeginRenameSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            if (info.isCorrupted)
            {
                Debug.LogWarning("[MainMenu] Cannot rename corrupted save.");
                return;
            }
            if (info.isAutoSave)
            {
                Debug.LogWarning("[MainMenu] The Auto-Save entry cannot be renamed.");
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

        private void CancelRename()
        {
            if (_mmRenameInput != null) _mmRenameInput.DeactivateInputField();
            SetLoadMode(LoadPanelMode.List);
        }

        private void CommitRename()
        {
            if (!TryGetSelectedSave(out var info)) { CancelRename(); return; }
            string newName = _mmRenameInput != null ? _mmRenameInput.text : null;
            string sanitized = SaveFileManager.SanitizeSaveName(newName);
            if (sanitized == null)
            {
                if (_mmRenameError != null) _mmRenameError.text = "Invalid name.";
                return;
            }
            if (string.Equals(sanitized, info.fileName, System.StringComparison.OrdinalIgnoreCase))
            {
                CancelRename();
                return;
            }
            string newPath = SaveFileManager.RenameSave(info.path, sanitized);
            if (newPath == null)
            {
                if (_mmRenameError != null) _mmRenameError.text = "Could not rename (duplicate name?).";
                return;
            }
            _mmLoadRuns = SaveFileManager.ListSavesByRun();
            for (int ri = 0; ri < _mmLoadRuns.Count; ri++)
            {
                var grp = _mmLoadRuns[ri];
                for (int si = 0; si < grp.saves.Count; si++)
                {
                    if (string.Equals(grp.saves[si].path, newPath,
                                      System.StringComparison.OrdinalIgnoreCase))
                    { _mmLoadRunSel = ri; _mmLoadSaveSel = si; break; }
                }
            }
            EnsureMMLoadScroll();
            CancelRename();
        }

        // ── Delete confirmation flow ──────────────────────────────────────────────

        private void RequestDeleteSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            if (_mmConfirmText != null)
                _mmConfirmText.text = $"Delete the save\n<b>{info.fileName}</b>?\nThis action cannot be undone.";
            _mmConfirmSel = 0;
            UpdateConfirmVisuals();
            SetLoadMode(LoadPanelMode.ConfirmDelete);
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

        // ── Mode switching ────────────────────────────────────────────────────────

        private void SetLoadMode(LoadPanelMode mode)
        {
            _mmLoadMode = mode;
            if (_mmRenameOverlay  != null) _mmRenameOverlay.SetActive(mode == LoadPanelMode.Rename);
            if (_mmConfirmOverlay != null) _mmConfirmOverlay.SetActive(mode == LoadPanelMode.ConfirmDelete);
        }

        // ── Sub-panel builders ────────────────────────────────────────────────────

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
            ttmp.text = "Rename Save"; ttmp.fontSize = 22f;
            ttmp.alignment = TextAlignmentOptions.Center;
            ttmp.color = AccentGold; ttmp.fontStyle = FontStyles.Bold;

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
            phTMP.text = "Save name..."; phTMP.fontSize = 18f;
            phTMP.color = new Color(1f, 1f, 1f, 0.35f); phTMP.fontStyle = FontStyles.Italic;
            phTMP.alignment = TextAlignmentOptions.Left;

            _mmRenameInput = fieldGo.AddComponent<TMP_InputField>();
            _mmRenameInput.textViewport = taR;
            _mmRenameInput.textComponent = txTMP;
            _mmRenameInput.placeholder = phTMP;
            _mmRenameInput.lineType = TMP_InputField.LineType.SingleLine;
            _mmRenameInput.characterLimit = 64;
            _mmRenameInput.onSubmit.AddListener(_ => CommitRename());

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

            BuildOverlayButton(box.transform, "Cancel", new Vector2(0.5f, 0f),
                new Vector2(-110f, 60f), new Vector2(180f, 38f),
                new Color(0.30f, 0.30f, 0.30f, 1f), CancelRename);
            BuildOverlayButton(box.transform, "OK", new Vector2(0.5f, 0f),
                new Vector2( 110f, 60f), new Vector2(180f, 38f),
                new Color(0.24f, 0.47f, 0.20f, 1f), CommitRename);

            var hintGo = CreateUIObject("Hint", box.transform);
            var hr = hintGo.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0.5f, 0f); hr.anchoredPosition = new Vector2(0f, 14f);
            hr.sizeDelta = new Vector2(0f, 22f);
            var htmp = hintGo.AddComponent<TextMeshProUGUI>();
            htmp.text = "Enter Confirm  |  Esc Cancel";
            htmp.fontSize = 13f;
            htmp.alignment = TextAlignmentOptions.Center;
            htmp.color = VersionCol;

            _mmRenameOverlay.SetActive(false);
        }

        private void BuildDeleteConfirmOverlay(Transform parent)
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

            // Two buttons: Cancel (0) / Delete (1)
            _mmConfirmPills = new Image[2];
            _mmConfirmTexts = new TextMeshProUGUI[2];
            string[] labels = { "Cancel", "Delete" };
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
            htmp.text = "<- -> Choose  |  Enter Confirm  |  Esc Cancel";
            htmp.fontSize = 13f;
            htmp.alignment = TextAlignmentOptions.Center;
            htmp.color = VersionCol;

            _mmConfirmOverlay.SetActive(false);
        }
    }
}
