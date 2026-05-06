using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Live-preview feature for the Boss Editor.
    ///
    /// The "Live preview" toggle button in the menu bar spawns a configured boss
    /// in front of the player using <see cref="BossEditorPreviewSandbox"/>. The
    /// sandbox wires the full choreography pipeline (BossPhaseController,
    /// BossConfigurator, BossBeatChoreographer, BossCueDispatcher) so the boss
    /// attacks in sync with the chart while the editor is open.
    ///
    /// Preview music: if no track is playing when the toggle is turned ON and the
    /// selected chart has a recognized track id, PlayMusic is called via
    /// <see cref="IAudioService"/>.
    ///
    /// The preview is torn down:
    ///   • When the toggle is turned OFF.
    ///   • When <see cref="Deactivate"/> is called (editor close).
    ///   • When <see cref="SelectBoss"/> picks a different boss.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── State ──────────────────────────────────────────────────────────────

        private bool                    _livePreviewOn;
        private BossEditorPreviewSandbox _sandbox;

        // Menu-bar button image + label — kept for highlight toggling.
        private Image           _previewBtnImg;
        private TextMeshProUGUI _previewBtnTmp;

        // ── Public toggle ──────────────────────────────────────────────────────

        internal void ToggleLivePreview()
        {
            if (_livePreviewOn)
                StopLivePreview();
            else
                StartLivePreview();
        }

        // Called by SelectBoss so the preview stays consistent with the
        // selected boss.
        internal void OnBossSelectionChangedPreview()
        {
            if (_livePreviewOn) StartLivePreview(); // respawn with new boss
        }

        // ── Start / stop ───────────────────────────────────────────────────────

        private void StartLivePreview()
        {
            if (_selectedBoss == null)
            {
                SetStatus("Select a boss first to use Live Preview.");
                return;
            }

            EnsureSandbox();
            _sandbox.Spawn(_selectedBoss, GetSpellCatalog());
            _livePreviewOn = true;

            // Attempt to start preview music if no track is playing.
            TryStartPreviewMusic();

            SetStatus($"Live preview ON — '{_selectedBoss.name}' spawned.");
            RefreshPreviewButton();
        }

        private void StopLivePreview()
        {
            if (_sandbox != null) _sandbox.Teardown();
            _livePreviewOn = false;
            SetStatus("Live preview OFF.");
            RefreshPreviewButton();
        }

        // ── Called from Deactivate (editor close) ─────────────────────────────

        internal void DeactivateLivePreview()
        {
            StopLivePreview();
        }

        // ── Sandbox helper ─────────────────────────────────────────────────────

        private void EnsureSandbox()
        {
            if (_sandbox != null) return;
            // Attach to the editor's own GameObject so it is cleaned up with the editor.
            _sandbox = gameObject.AddComponent<BossEditorPreviewSandbox>();
        }

        // ── Music helper ───────────────────────────────────────────────────────

        private void TryStartPreviewMusic()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;
            if (audio.IsMusicPlaying) return;

            if (_selectedChart == null || string.IsNullOrWhiteSpace(_selectedChart.musicTrackId))
                return;

            var catalog = GetAudioCatalog();
            if (catalog == null) return;

            MusicTrackEntry track = catalog.GetTrack(_selectedChart.musicTrackId);
            if (track?.clip == null) return;

            audio.PlayMusic(track.clip);
            Debug.Log($"[BossEditorPreview] Started track '{_selectedChart.musicTrackId}' for preview.");
        }

        // ── Button wiring (called from BossEditorUIBuilder) ────────────────────

        internal void SetPreviewButtonRefs(Image img, TextMeshProUGUI tmp)
        {
            _previewBtnImg = img;
            _previewBtnTmp = tmp;
            RefreshPreviewButton();
        }

        private void RefreshPreviewButton()
        {
            if (_previewBtnImg == null) return;
            // Use the canonical ApplyMenuBtnStyle so the Live Preview button matches
            // the dropdown highlight treatment (MENU_BTN_OPEN / MENU_BTN_NORMAL).
            EditorUIHelpers.ApplyMenuBtnStyle(_previewBtnImg, _previewBtnTmp, _livePreviewOn);
            if (_previewBtnTmp != null)
                _previewBtnTmp.text = _livePreviewOn ? "Stop Preview" : "Live Preview";
        }
    }
}
