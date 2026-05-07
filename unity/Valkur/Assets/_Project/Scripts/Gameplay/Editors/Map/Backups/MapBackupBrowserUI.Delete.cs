using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valkur.Gameplay.MapEditor.Backups
{
    public partial class MapBackupBrowserUI
    {
        // ── Delete (three-stage + type-to-confirm) ───────────────────────────────

        private void OnDeleteClicked()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            _delStage = 1;
            _delPrompt.text =
                $"<b>Stage 1 of 3.</b>\n\n" +
                $"You are about to delete the snapshot:\n" +
                $"<b>{_selectedId}</b>\n\n" +
                $"This action cannot be undone. Proceed?";
            _delConfirmLabel.text = "Continue";
            _delInput.gameObject.SetActive(false);
            _delConfirmBtn.interactable = true;
            _delDialog.SetActive(true);
        }

        private void OnDeleteConfirmClicked()
        {
            if (_delStage == 1)
            {
                _delStage = 2;
                _delPrompt.text =
                    $"<b>Stage 2 of 3.</b>\n\n" +
                    $"Snapshots are written to your local AppData and there is no\n" +
                    $"versioned history once they're gone. Are you absolutely sure?";
                _delConfirmLabel.text = "I understand — continue";
                _delConfirmBtn.interactable = true;
                return;
            }
            if (_delStage == 2)
            {
                _delStage = 3;
                _delPrompt.text =
                    $"<b>Stage 3 of 3.</b>\n\n" +
                    $"Type the phrase <b>{DESTROY_PHRASE}</b>\n" +
                    $"exactly (case-sensitive) to enable the destroy button.";
                _delConfirmLabel.text = "Destroy";
                _delConfirmBtn.interactable = false;
                _delInput.text = "";
                _delInput.gameObject.SetActive(true);
                EventSystem.current?.SetSelectedGameObject(_delInput.gameObject);
                return;
            }
            // Stage 3 → only fires when the input text matches exactly.
            if (_delInput.text != DESTROY_PHRASE) return;

            string id = _selectedId;
            bool ok = _store.DeleteBackup(id);
            CloseDeleteDialog();
            SetStatus(ok ? $"Destroyed snapshot '{id}'." : $"Delete failed for '{id}'.");
            _selectedId = null;
            RefreshList();
        }

        private void CloseDeleteDialog()
        {
            _delStage = 0;
            if (_delDialog != null) _delDialog.SetActive(false);
        }

        private void BuildDeleteDialog(Transform canvas)
        {
            _delDialog = MakeStretch("DeleteDialog", canvas);
            _delDialog.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            var box = MakeRect("Box", _delDialog.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            box.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 320f);
            box.AddComponent<Image>().color = PanelBg;
            var ol = box.AddComponent<Outline>();
            ol.effectColor = new Color(1f, 0.32f, 0.36f, 1f);
            ol.effectDistance = new Vector2(2f, 2f);

            var title = AddText(box.transform, "DELETE BACKUP", 18f, new Color(1f, 0.42f, 0.42f, 1f),
                                TextAlignmentOptions.Center, FontStyles.Bold);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -16f);
            titleRt.sizeDelta = new Vector2(-32f, 28f);

            _delPrompt = AddText(box.transform, "", 14f, TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            var promptRt = _delPrompt.rectTransform;
            promptRt.anchorMin = new Vector2(0f, 0f); promptRt.anchorMax = new Vector2(1f, 1f);
            promptRt.offsetMin = new Vector2(20f, 90f);
            promptRt.offsetMax = new Vector2(-20f, -56f);
            _delPrompt.enableWordWrapping = true;

            // Type-to-confirm input (only visible at stage 3)
            var inputGo = new GameObject("DestroyPhraseInput", typeof(RectTransform));
            inputGo.transform.SetParent(box.transform, false);
            var inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0f, 0f); inputRt.anchorMax = new Vector2(1f, 0f);
            inputRt.pivot = new Vector2(0.5f, 0f);
            inputRt.anchoredPosition = new Vector2(0f, 60f);
            inputRt.sizeDelta = new Vector2(-32f, 32f);
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.06f, 0.07f, 0.09f, 1f);

            _delInput = inputGo.AddComponent<TMP_InputField>();
            _delInput.targetGraphic = inputBg;
            _delInput.lineType = TMP_InputField.LineType.SingleLine;
            _delInput.characterLimit = 80;

            var textArea = MakeRect("TextArea", inputGo.transform, new Vector2(0f, 0f), new Vector2(1f, 1f));
            textArea.GetComponent<RectTransform>().offsetMin = new Vector2(8f, 4f);
            textArea.GetComponent<RectTransform>().offsetMax = new Vector2(-8f, -4f);
            textArea.AddComponent<RectMask2D>();

            var inputText = AddText(textArea.transform, "", 14f, TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            var itRt = inputText.rectTransform;
            itRt.anchorMin = Vector2.zero; itRt.anchorMax = Vector2.one;
            itRt.offsetMin = Vector2.zero; itRt.offsetMax = Vector2.zero;

            var placeholder = AddText(textArea.transform, $"Type '{DESTROY_PHRASE}' here…", 13f,
                                      new Color(0.4f, 0.43f, 0.5f, 1f),
                                      TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            var phRt = placeholder.rectTransform;
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero; phRt.offsetMax = Vector2.zero;

            _delInput.textViewport     = textArea.GetComponent<RectTransform>();
            _delInput.textComponent    = inputText;
            _delInput.placeholder      = placeholder;
            _delInput.onValueChanged.AddListener(s =>
            {
                if (_delStage == 3 && _delConfirmBtn != null)
                    _delConfirmBtn.interactable = (s == DESTROY_PHRASE);
            });

            // Buttons row
            var btnRow = MakeRect("Buttons", box.transform, new Vector2(0f, 0f), new Vector2(1f, 0f));
            var brRt = btnRow.GetComponent<RectTransform>();
            brRt.pivot = new Vector2(0.5f, 0f);
            brRt.anchoredPosition = new Vector2(0f, 14f);
            brRt.sizeDelta = new Vector2(-32f, 36f);

            var cancelBtn = AddButton(btnRow.transform, "Cancel", 140f, 36f, BtnNormal, BtnHover, CloseDeleteDialog);
            var cnRt = cancelBtn.GetComponent<RectTransform>();
            cnRt.anchorMin = new Vector2(0f, 0.5f); cnRt.anchorMax = new Vector2(0f, 0.5f);
            cnRt.pivot = new Vector2(0f, 0.5f);
            cnRt.anchoredPosition = new Vector2(0f, 0f);

            _delConfirmBtn = AddButton(btnRow.transform, "Continue", 220f, 36f, BtnDanger, BtnDangerH, OnDeleteConfirmClicked);
            _delConfirmLabel = _delConfirmBtn.GetComponentInChildren<TextMeshProUGUI>();
            var ccRt = _delConfirmBtn.GetComponent<RectTransform>();
            ccRt.anchorMin = new Vector2(1f, 0.5f); ccRt.anchorMax = new Vector2(1f, 0.5f);
            ccRt.pivot = new Vector2(1f, 0.5f);
            ccRt.anchoredPosition = new Vector2(0f, 0f);

            _delDialog.SetActive(false);
        }
    }
}
