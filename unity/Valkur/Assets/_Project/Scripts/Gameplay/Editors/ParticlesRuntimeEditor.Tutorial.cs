using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Tutorial overlay (Prev / Next / Close, mirrors Buildings) ──────────

        private void BuildTutorial()
        {
            _tutorialRoot = EditorUIHelpers.MakePanel("Tutorial", _root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 240f));
            var vlg = _tutorialRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 14);
            vlg.spacing = 8f; vlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeTitleBar(_tutorialRoot.transform, "PARTICLES TUTORIAL");

            _tutorialStepLabel = EditorUIHelpers.AddLabel(_tutorialRoot.transform, "", 14f);
            _tutorialStepLabel.fontStyle = FontStyles.Bold;
            _tutorialStepLabel.color     = UITheme.ACCENT;

            var bodyGo = EditorUIHelpers.CreateUI("Body", _tutorialRoot.transform);
            bodyGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _tutorialBodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
            _tutorialBodyTmp.fontSize           = 12f;
            _tutorialBodyTmp.color              = UITheme.TEXT_PRIMARY;
            _tutorialBodyTmp.alignment          = TextAlignmentOptions.TopLeft;
            _tutorialBodyTmp.enableWordWrapping = true;

            var nav = EditorUIHelpers.CreateUI("Nav", _tutorialRoot.transform);
            nav.AddComponent<LayoutElement>().preferredHeight = 32f;
            var hlg = nav.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(nav.transform, "Prev",  () => StepTutorial(-1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Next",  () => StepTutorial(+1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Close", () => _tutorialRoot.SetActive(false), 28f, 12f);

            _tutorialStep = 0;
            RefreshTutorial();
            _tutorialRoot.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorialRoot == null) return;
            bool show = !_tutorialRoot.activeSelf;
            _tutorialRoot.SetActive(show);
            if (show) { _tutorialRoot.transform.SetAsLastSibling(); RefreshTutorial(); }
        }

        private void StepTutorial(int delta)
        {
            _tutorialStep = (_tutorialStep + delta + TUTORIAL_STEPS.Length) % TUTORIAL_STEPS.Length;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_tutorialStepLabel == null) return;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _tutorialStepLabel.text = $"{title}   ({_tutorialStep + 1}/{TUTORIAL_STEPS.Length})";
            _tutorialBodyTmp.text   = body;
        }

        // ── Confirm-delete modal ───────────────────────────────────────────────

        private void BuildConfirmModal()
        {
            _confirmModal = EditorUIHelpers.MakePanel("ConfirmModal", _root.transform,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var bgImg = _confirmModal.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 140f / 255f);

            var inner = EditorUIHelpers.MakePanel("Inner", _confirmModal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 200f));
            var vlg = inner.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 12f; vlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeTitleBar(inner.transform, "CONFIRM DELETE");

            _confirmText = EditorUIHelpers.AddLabel(inner.transform, "?", 13f);
            _confirmText.color     = UITheme.TEXT_PRIMARY;
            _confirmText.alignment = TextAlignmentOptions.MidlineLeft;
            _confirmText.richText  = true;

            var btnRow = EditorUIHelpers.CreateUI("Btns", inner.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeDangerButton(btnRow.transform, "Eliminar",
                () => { var cb = _pendingConfirmYes; HideConfirm(); cb?.Invoke(); }, 32f);
            EditorUIHelpers.MakeButton(btnRow.transform, "Cancelar",
                () => HideConfirm(), 32f, 12f);

            _confirmModal.SetActive(false);
        }

        private void ShowConfirm(string text, System.Action onYes)
        {
            if (_confirmModal == null) { onYes?.Invoke(); return; }
            _confirmText.text = text;
            _pendingConfirmYes = onYes;
            _confirmModal.SetActive(true);
            _confirmModal.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            _pendingConfirmYes = null;
            if (_confirmModal != null) _confirmModal.SetActive(false);
        }
    }
}
