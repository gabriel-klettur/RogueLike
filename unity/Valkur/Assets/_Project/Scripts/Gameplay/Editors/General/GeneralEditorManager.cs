using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.Services;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// Top-level launcher panel toggled with <c>ESC</c>. Lists every other
    /// runtime editor (Tile, Buildings, Items, …), the diagnostic overlays
    /// (Combat Ranges, Debug HUD), and session actions (Save / Load /
    /// Options / Quit) as clickable buttons. Each button delegates to the
    /// existing system — the launcher never duplicates editor logic.
    ///
    /// Implements <see cref="GameEditorManager.IGameEditor"/> so it
    /// participates in the standard exclusivity contract: opening any other
    /// editor through the launcher auto-closes the launcher; pressing ESC
    /// again toggles the launcher back off.
    /// </summary>
    public partial class GeneralEditorManager
        : SingletonMonoBehaviour<GeneralEditorManager>, GameEditorManager.IGameEditor
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
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (!EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.OpenGeneralEditor))
                return;

            // Pause menu owns ESC for sub-screen navigation while it's open.
            // Suppressing here also covers the "ESC closes pause" UX without
            // double-firing into a launcher toggle.
            var pause = ServiceLocator.Get<IPauseMenuService>();
            if (pause != null && pause.IsOpen) return;

            var mgr = GameEditorManager.Instance;
            if (mgr == null) return;

            // We are the active editor → toggle off (ESC again closes us).
            if (_isActive)
            {
                mgr.ToggleExclusive(this);
                return;
            }

            // Anything else (no editor active, or a different editor active) →
            // open the launcher. GameEditorManager.OpenExclusive auto-closes
            // the previous editor first, so the per-press UX is uniform:
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
        }

        public void Deactivate()
        {
            _isActive = false;
            SetPanelVisible(false);
            // Notify the manager so its own _activeEditor pointer clears even
            // when Deactivate is invoked outside ToggleExclusive (e.g. through
            // the close button or a "ClosesLauncher" entry click).
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
        }

        // BuildUI / SetPanelVisible / RefreshActiveStates implemented in the
        // .UI.cs partial so the lifecycle file stays focused on contract.
        partial void BuildUI();
        partial void SetPanelVisible(bool visible);
        partial void RefreshActiveStates();
    }
}
