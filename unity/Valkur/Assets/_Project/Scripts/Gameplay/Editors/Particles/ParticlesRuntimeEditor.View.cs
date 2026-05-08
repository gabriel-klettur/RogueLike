using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Particles Editor — View panel logic.
    /// Handles play/pause, speed selection, zoom (buttons + mouse-wheel), preset-name
    /// binding and status text.
    /// </summary>
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // Mirror the Spells Editor wheel sensitivity constant:
        // MouseInputManager.GetMouseWheelDelta() returns the legacy-style ±120 per notch,
        // so 1/120 ≈ 0.0083 makes one notch roughly one ZoomIn/ZoomOut step.
        private const float PARTICLE_WHEEL_ZOOM_SENSITIVITY = 1f / 120f;

        private ParticlesViewHoverProbe _previewHoverProbe;

        // ── Wiring ────────────────────────────────────────────────────────────

        private void WireViewPanel()
        {
            if (_ui.ViewPlayPauseBtn  != null) _ui.ViewPlayPauseBtn.onClick.AddListener(OnPlayPauseClicked);
            if (_ui.ViewSpeed025Btn   != null) _ui.ViewSpeed025Btn.onClick.AddListener(() => OnSpeedSelected(0.25f));
            if (_ui.ViewSpeed05Btn    != null) _ui.ViewSpeed05Btn.onClick.AddListener(() => OnSpeedSelected(0.5f));
            if (_ui.ViewSpeed1Btn     != null) _ui.ViewSpeed1Btn.onClick.AddListener(() => OnSpeedSelected(1f));

            if (_ui.ViewZoomInBtn  != null) _ui.ViewZoomInBtn.onClick.AddListener(OnZoomIn);
            if (_ui.ViewZoomOutBtn != null) _ui.ViewZoomOutBtn.onClick.AddListener(OnZoomOut);

            // Attach the hover probe to the preview surface so wheel zoom is
            // gated to cursor-over-preview (mirrors SpellsViewHoverProbe pattern).
            if (_ui.ViewRawImage != null && _previewHoverProbe == null)
            {
                // The preview background Image is the parent of the RawImage.
                var previewBg = _ui.ViewRawImage.transform.parent;
                if (previewBg != null)
                    _previewHoverProbe = previewBg.gameObject.AddComponent<ParticlesViewHoverProbe>();
            }
        }

        // ── Transport callbacks ───────────────────────────────────────────────

        internal void OnPlayPauseClicked()
        {
            bool paused = _previewService.TogglePause();
            RefreshViewPanel();
        }

        internal void OnSpeedSelected(float multiplier)
        {
            _previewService.SetSpeedMultiplier(multiplier);
            RefreshViewPanel();
        }

        // ── Zoom callbacks ────────────────────────────────────────────────────

        private void OnZoomIn()
        {
            _previewService.ZoomIn();
            RefreshViewPanel();
        }

        private void OnZoomOut()
        {
            _previewService.ZoomOut();
            RefreshViewPanel();
        }

        /// <summary>
        /// Called every frame from Update() while the editor is active.
        /// Handles mouse-wheel zoom over the preview surface.
        /// </summary>
        private void TickViewPanelInput()
        {
            if (_previewHoverProbe == null || !_previewHoverProbe.IsHovered) return;

            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(wheel) < 0.01f) return;

            // Clamp to ±1 per frame so a very fast wheel doesn't slam to a limit.
            float clamped = Mathf.Clamp(wheel * PARTICLE_WHEEL_ZOOM_SENSITIVITY, -1f, 1f);
            if (clamped > 0f)
                _previewService.ZoomIn();
            else
                _previewService.ZoomOut();

            RefreshViewPanel();
        }

        // ── Panel refresh ─────────────────────────────────────────────────────

        /// <summary>
        /// Refresh the View panel to match the current selected preset and playback state.
        /// Call after SelectPreset(), OnPlayPauseClicked(), OnSpeedSelected(), zoom changes.
        /// </summary>
        internal void RefreshViewPanel()
        {
            // Preset name
            if (_ui.ViewPresetNameTmp != null)
            {
                var def = string.IsNullOrEmpty(_selectedPresetId) ? null
                          : _catalog?.GetById(_selectedPresetId);
                _ui.ViewPresetNameTmp.text = def != null
                    ? (def.displayName ?? def.id ?? "(no name)")
                    : "(no preset selected)";
            }

            // RT binding — point the RawImage at the large preview RT.
            if (_ui.ViewRawImage != null)
            {
                var largeTex = _previewService.GetLargePreviewTexture();
                bool has     = largeTex != null && !string.IsNullOrEmpty(_selectedPresetId);
                _ui.ViewRawImage.texture = has ? largeTex : null;
                _ui.ViewRawImage.color   = has ? Color.white : new Color(0.08f, 0.08f, 0.10f, 1f);
            }

            // Play/Pause button label
            if (_ui.ViewPlayPauseBtnLabel != null)
                _ui.ViewPlayPauseBtnLabel.text = _previewService.IsPaused ? "Play" : "Pause";

            // Speed button highlights
            float speed = _previewService.SpeedMultiplier;
            ApplySpeedBtnStyle(_ui.ViewSpeed025BtnImg, Mathf.Approximately(speed, 0.25f));
            ApplySpeedBtnStyle(_ui.ViewSpeed05BtnImg,  Mathf.Approximately(speed, 0.5f));
            ApplySpeedBtnStyle(_ui.ViewSpeed1BtnImg,   Mathf.Approximately(speed, 1.0f));

            // Status text — includes current zoom percentage.
            if (_ui.ViewStatusTmp != null)
            {
                float zoom    = _previewService.LargeOrthoZoom;
                int   zoomPct = Mathf.RoundToInt(zoom * 100f);
                string zoomStr = $"zoom {zoomPct}%";

                if (_previewService.IsPaused)
                    _ui.ViewStatusTmp.text = $"paused · {zoomStr}";
                else if (Mathf.Approximately(speed, 1.0f))
                    _ui.ViewStatusTmp.text = $"playing 1x · {zoomStr}";
                else
                    _ui.ViewStatusTmp.text = $"playing {speed:0.##}x · {zoomStr}";
            }
        }

        private static void ApplySpeedBtnStyle(Image img, bool active)
        {
            if (img == null) return;
            img.color = active ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
        }
    }
}
