using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Editors;
using Valkur.Core.Input;
using Valkur.Core.Services;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// Top-level launcher panel toggled with <c>ESC</c> — since the F-row was retired, the
    /// only way into any of the sixteen runtime editors. Lists them, the diagnostic overlays
    /// (Combat Ranges, Debug HUD, Save Log) and the session actions (Save / Load / Options /
    /// Quit) as buttons. Each button delegates to the existing system — the launcher never
    /// duplicates editor logic.
    ///
    /// Implements <see cref="GameEditorManager.IGameEditor"/> so it participates in the
    /// standard exclusivity contract: opening any other editor through the launcher
    /// auto-closes the launcher; pressing ESC again toggles the launcher back off.
    ///
    /// Escape has more than one reader. An overlay that closes on it (Save Log, character
    /// sheet, the launcher's own confirm dialog) claims it through
    /// <see cref="EscapeOwnership"/> while it is up, and the toggle below yields to that
    /// claim — before it did, one press closed the overlay AND toggled the launcher behind it.
    /// </summary>
    public partial class GeneralEditorManager
        : SingletonMonoBehaviour<GeneralEditorManager>,
          GameEditorManager.IGameEditor,
          IProvidesWorkspaceState
    {
        public string EditorName => "General";
        public bool IsActive => _isActive;

        private bool _isActive;
        private bool _uiBuilt;

        private IReadOnlyList<GeneralEditorEntry> _entries;

        protected override void OnSingletonAwake()
        {
            _entries = GeneralEditorRegistry.BuildEntries();
            GameEditorManager.EnsureInstance().Register(this);
            BuildUI();
            SetPanelVisible(false);
        }

        protected override void OnDestroy()
        {
            ReleaseConfirmEscapeClaim();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            // The confirm dialog owns Escape for as long as it is up: the press cancels it
            // and must not also reach the toggle below.
            if (IsConfirmOpen)
            {
                if (KeyboardInputManager.WasEscapePressedThisFrame()) CancelConfirm();
                return;
            }

            if (!EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.OpenGeneralEditor))
                return;

            // Pause menu owns ESC for sub-screen navigation while it's open.
            // Suppressing here also covers the "ESC closes pause" UX without
            // double-firing into a launcher toggle.
            var pause = ServiceLocator.Get<IPauseMenuService>();
            if (pause != null && pause.IsOpen) return;

            // An overlay that closes on Escape has claimed it. Before this check, with the
            // Save Log open, one press closed the log AND closed the launcher it was
            // opened from.
            if (EscapeOwnership.IsClaimed) return;

            var mgr = GameEditorManager.Instance;
            if (mgr == null) return;

            // We are the active editor → toggle off (ESC again closes us).
            if (_isActive)
            {
                mgr.ToggleExclusive(this);
                return;
            }

            // An editor opened FROM another editor leaves a one-shot return target behind it:
            // the Selection tool double-clicks a building and lands in the Buildings editor,
            // and Escape there should undo that step rather than dump the author at the
            // launcher with their selection stranded one screen back. Consumed, so the NEXT
            // Escape behaves exactly as it always has.
            if (mgr.TryConsumeReturnTarget(out var back))
            {
                mgr.OpenExclusive(back);
                return;
            }

            // Anything else (no editor active, or a different editor active) →
            // open the launcher. GameEditorManager.OpenExclusive auto-closes the
            // previous editor first, so the per-press UX is uniform:
            //   gameplay  ── ESC ─►  launcher
            //   any editor ─ ESC ─►  editor closes + launcher opens
            //   launcher  ── ESC ─►  back to gameplay
            // Per-editor ESC handlers (modal cancel, RMB cancel, etc.) still
            // run in the same frame; their internal cleanup is idempotent
            // with the subsequent Deactivate triggered here.
            mgr.OpenExclusive(this);
        }

        public void Activate()
        {
            _isActive = true;
            SetPanelVisible(true);
            RefreshActiveStates();
            FocusFirstEntry();
        }

        public void Deactivate()
        {
            _isActive = false;
            ReleaseFocus();
            SetPanelVisible(false);
            // Notify the manager so its own _activeEditor pointer clears even
            // when Deactivate is invoked outside ToggleExclusive (e.g. through
            // the close button or a "ClosesLauncher" entry click).
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
        }

        // Implemented in the .UI.cs partial so the lifecycle file stays focused on contract.
        partial void BuildUI();
        partial void SetPanelVisible(bool visible);
        partial void RefreshActiveStates();
        partial void FocusFirstEntry();
        partial void ReleaseFocus();
    }
}
