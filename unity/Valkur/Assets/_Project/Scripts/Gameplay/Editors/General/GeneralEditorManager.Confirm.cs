using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// The launcher's confirm dialog, for the one action in it that cannot be undone:
    /// leaving the session. It lives on its OWN canvas rather than the launcher's, because
    /// every Game-section entry hides the launcher before it runs (so the pause menu and
    /// the scene transition never stack over it) and a dialog on that canvas would vanish
    /// with it. Cancel reopens the launcher, the same way the Map Backups browser does.
    /// </summary>
    public partial class GeneralEditorManager
    {
        private const int CONFIRM_CANVAS_ORDER = 120;

        private Canvas          _confirmCanvas;
        private GameObject      _confirmRoot;
        private TextMeshProUGUI _confirmMessage;
        private Button          _confirmOk;
        private Button          _confirmCancel;
        private Action          _pendingConfirm;

        public bool IsConfirmOpen => _confirmRoot != null && _confirmRoot.activeSelf;

        /// <summary>
        /// Ask before running <paramref name="onConfirm"/>. Escape and Cancel both dismiss
        /// and bring the launcher back; Confirm runs the action with the dialog already gone.
        /// </summary>
        public void Confirm(string message, Action onConfirm)
        {
            EnsureConfirmDialog();
            _pendingConfirm      = onConfirm;
            _confirmMessage.text = message;
            _confirmRoot.SetActive(true);
            // The dialog reads Escape itself (see Update); the claim keeps every OTHER
            // reader — the character sheet, the Save Log — from acting on the same press.
            EscapeOwnership.Claim(_confirmRoot);
        }

        private void EnsureConfirmDialog()
        {
            if (_confirmRoot != null) return;

            _confirmCanvas = EditorUIHelpers.CreateEditorCanvas("GeneralEditorConfirmCanvas", CONFIRM_CANVAS_ORDER);
            var (root, message, ok, cancel) = UIConfirmDialog.Make(_confirmCanvas.transform, "General Editor");
            _confirmRoot    = root;
            _confirmMessage = message;
            _confirmOk      = ok;
            _confirmCancel  = cancel;
            _confirmOk.onClick.AddListener(AcceptConfirm);
            _confirmCancel.onClick.AddListener(CancelConfirm);
        }

        private void AcceptConfirm()
        {
            var action = _pendingConfirm;
            HideConfirm();
            action?.Invoke();
        }

        private void CancelConfirm()
        {
            HideConfirm();
            var mgr = GameEditorManager.Instance;
            if (mgr != null) mgr.OpenExclusive(this);
        }

        private void HideConfirm()
        {
            _pendingConfirm = null;
            if (_confirmRoot != null) _confirmRoot.SetActive(false);
            ReleaseConfirmEscapeClaim();
        }

        private void ReleaseConfirmEscapeClaim()
        {
            if (_confirmRoot != null) EscapeOwnership.Release(_confirmRoot);
        }
    }
}
