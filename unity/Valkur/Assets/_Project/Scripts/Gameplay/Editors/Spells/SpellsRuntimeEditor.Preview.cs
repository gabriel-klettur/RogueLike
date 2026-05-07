using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — VIEW panel: live, looping spell preview rendered into an
    /// off-screen RenderTexture. Owns the lifecycle of <see cref="SpellPreviewService"/>
    /// and wires the 4-direction selector buttons, zoom controls, and the transport
    /// row (play/pause, speed, frame scrubber).
    ///
    /// Behavior:
    ///   • Auto-play on panel open if a spell is selected.
    ///   • Selection change in the picker updates the loop in real time.
    ///   • Direction buttons (N/S/E/W) override the cast vector — applied next cycle.
    ///   • Audio is muted while the panel is open (via AudioListener.volume cache),
    ///     restored on close.
    ///   • Transport row: Play/Pause toggle, speed selector (0.25x / 0.5x / 1x),
    ///     prev/next frame step, and a scrubber slider with frame counter.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private SpellPreviewService _previewService;
        private GameObject          _previewStageRoot;
        private Vector2             _previewDirection = Vector2.right;
        private bool                _audioMuteApplied;
        private float               _audioVolumeCache;
        private SpellsViewHoverProbe _previewHoverProbe;
        // Wheel sensitivity: MouseInputManager.GetMouseWheelDelta() returns the
        // legacy-style ±120 per notch (the manager normalises the InputSystem path
        // to match), so 1/120 ≈ 0.0083 makes one wheel notch ≈ one ZoomIn/Out click.
        private const float WHEEL_ZOOM_SENSITIVITY = 1f / 120f;

        // Highlights for speed buttons: active speed gets BTN_ACTIVE colour.
        private static readonly Color SPEED_BTN_ACTIVE  = new Color(0.90f, 0.76f, 0.38f, 1f);   // amber
        private static readonly Color SPEED_BTN_NORMAL  = new Color(0.22f, 0.22f, 0.28f, 1f);

        // ── UI wiring ─────────────────────────────────────────────────────

        private void WireViewPanel()
        {
            if (_uiRefs.ViewDirNBtn != null)
                _uiRefs.ViewDirNBtn.onClick.AddListener(() => SetPreviewDirection(Vector2.up));
            if (_uiRefs.ViewDirSBtn != null)
                _uiRefs.ViewDirSBtn.onClick.AddListener(() => SetPreviewDirection(Vector2.down));
            if (_uiRefs.ViewDirEBtn != null)
                _uiRefs.ViewDirEBtn.onClick.AddListener(() => SetPreviewDirection(Vector2.right));
            if (_uiRefs.ViewDirWBtn != null)
                _uiRefs.ViewDirWBtn.onClick.AddListener(() => SetPreviewDirection(Vector2.left));

            if (_uiRefs.ViewZoomInBtn != null)
                _uiRefs.ViewZoomInBtn.onClick.AddListener(() => _previewService?.ZoomIn());
            if (_uiRefs.ViewZoomOutBtn != null)
                _uiRefs.ViewZoomOutBtn.onClick.AddListener(() => _previewService?.ZoomOut());

            // ── Transport row ─────────────────────────────────────────────

            if (_uiRefs.ViewPlayPauseBtn != null)
                _uiRefs.ViewPlayPauseBtn.onClick.AddListener(OnTransportPlayPause);

            if (_uiRefs.ViewSpeed025Btn != null)
                _uiRefs.ViewSpeed025Btn.onClick.AddListener(() => SetTransportSlow(0.25f));
            if (_uiRefs.ViewSpeed05Btn != null)
                _uiRefs.ViewSpeed05Btn.onClick.AddListener(() => SetTransportSlow(0.5f));
            if (_uiRefs.ViewSpeed1Btn != null)
                _uiRefs.ViewSpeed1Btn.onClick.AddListener(SetTransportLive);

            if (_uiRefs.ViewPrevFrameBtn != null)
                _uiRefs.ViewPrevFrameBtn.onClick.AddListener(() => _previewService?.StepFrame(-1));
            if (_uiRefs.ViewNextFrameBtn != null)
                _uiRefs.ViewNextFrameBtn.onClick.AddListener(() => _previewService?.StepFrame(+1));

            if (_uiRefs.ViewFrameSlider != null)
            {
                _uiRefs.ViewFrameSlider.onValueChanged.AddListener(OnScrubberMoved);
                _uiRefs.ViewFrameSlider.interactable = false;  // read-only while Live
            }

            // Character overlay toggle
            if (_uiRefs.ViewCharacterToggleBtn != null)
                _uiRefs.ViewCharacterToggleBtn.onClick.AddListener(OnToggleCharacter);

            // Hover probe lets Update detect wheel-zoom only when the cursor is
            // actually over the preview surface.
            if (_uiRefs.ViewPreviewArea != null && _previewHoverProbe == null)
                _previewHoverProbe = _uiRefs.ViewPreviewArea.gameObject.AddComponent<SpellsViewHoverProbe>();
        }

        /// <summary>
        /// Called every frame from <see cref="SpellsRuntimeEditor"/>.Update while the
        /// View panel is open. Drives wheel-zoom and pumps the transport-row display
        /// (texture push, slider, frame counter, button labels).
        /// </summary>
        private void TickPreviewInput()
        {
            if (_previewService == null) return;

            // Wheel zoom only while hovering the preview surface.
            if (_previewHoverProbe != null && _previewHoverProbe.IsHovered)
            {
                float wheel = MouseInputManager.GetMouseWheelDelta();
                if (Mathf.Abs(wheel) > 0.01f)
                    _previewService.ZoomBy(wheel * WHEEL_ZOOM_SENSITIVITY);
            }

            // Push the correct texture (live RT or captured frame) to the RawImage.
            if (_uiRefs.ViewRawImage != null)
                _uiRefs.ViewRawImage.texture = _previewService.GetDisplayTexture();

            // Update transport-row controls each frame (cheap reads, no GC).
            RefreshTransportUI();
            RefreshCharacterToggleVisual();
        }

        private void SetPreviewDirection(Vector2 dir)
        {
            _previewDirection = dir.normalized;
            _previewService?.SetDirection(_previewDirection);
            UpdateViewStatus();
        }

        // ── Transport callbacks ────────────────────────────────────────────

        private void OnTransportPlayPause()
        {
            if (_previewService == null) return;
            var mode = _previewService.CurrentTransport;
            if (mode == SpellPreviewService.TransportMode.Paused)
            {
                // Resume: go back to whatever speed was active before pausing.
                // If 1x, go Live; otherwise Slow at the same speed.
                if (Mathf.Approximately(_previewService.PlaybackSpeed, 1f))
                    SetTransportLive();
                else
                    SetTransportSlow(_previewService.PlaybackSpeed);
            }
            else
            {
                // Pause from Live or Slow.
                _previewService.SetTransport(SpellPreviewService.TransportMode.Paused,
                                             _previewService.PlaybackSpeed);
            }
        }

        private void SetTransportLive()
        {
            _previewService?.SetTransport(SpellPreviewService.TransportMode.Live, 1f);
            if (_uiRefs.ViewFrameSlider != null) _uiRefs.ViewFrameSlider.interactable = false;
        }

        private void SetTransportSlow(float speed)
        {
            _previewService?.SetTransport(SpellPreviewService.TransportMode.Slow, speed);
            if (_uiRefs.ViewFrameSlider != null) _uiRefs.ViewFrameSlider.interactable = true;
        }

        private void OnScrubberMoved(float value)
        {
            if (_previewService == null) return;
            // Only seek when the user is actually dragging — ignore programmatic updates
            // pushed with SetValueWithoutNotify.
            var mode = _previewService.CurrentTransport;
            if (mode == SpellPreviewService.TransportMode.Paused ||
                mode == SpellPreviewService.TransportMode.Slow)
            {
                _previewService.SeekToFraction(value);
            }
        }

        /// <summary>
        /// Pushes transport state to the UI controls every frame while the View
        /// panel is open. Uses SetValueWithoutNotify on the slider to prevent the
        /// onValueChanged callback from firing during programmatic updates.
        /// </summary>
        private void RefreshTransportUI()
        {
            if (_previewService == null) return;

            var mode     = _previewService.CurrentTransport;
            int total    = _previewService.CapturedFrameCount;
            int current  = _previewService.DisplayedFrame;
            float speed  = _previewService.PlaybackSpeed;

            // Play / Pause label.
            if (_uiRefs.ViewPlayPauseBtnLabel != null)
                _uiRefs.ViewPlayPauseBtnLabel.text = (mode == SpellPreviewService.TransportMode.Paused)
                    ? "Play" : "Pause";

            // Speed button highlights.
            bool live = mode == SpellPreviewService.TransportMode.Live;
            bool slow = mode != SpellPreviewService.TransportMode.Live;
            if (_uiRefs.ViewSpeed025BtnImg != null)
                _uiRefs.ViewSpeed025BtnImg.color = (slow && Mathf.Approximately(speed, 0.25f))
                    ? SPEED_BTN_ACTIVE : SPEED_BTN_NORMAL;
            if (_uiRefs.ViewSpeed05BtnImg != null)
                _uiRefs.ViewSpeed05BtnImg.color  = (slow && Mathf.Approximately(speed, 0.5f))
                    ? SPEED_BTN_ACTIVE : SPEED_BTN_NORMAL;
            if (_uiRefs.ViewSpeed1BtnImg != null)
                _uiRefs.ViewSpeed1BtnImg.color   = live
                    ? SPEED_BTN_ACTIVE : SPEED_BTN_NORMAL;

            // Prev / Next interactable only in Paused or Slow.
            bool canStep = mode != SpellPreviewService.TransportMode.Live;
            if (_uiRefs.ViewPrevFrameBtn != null) _uiRefs.ViewPrevFrameBtn.interactable = canStep;
            if (_uiRefs.ViewNextFrameBtn != null) _uiRefs.ViewNextFrameBtn.interactable = canStep;

            // Slider interactable only in Paused or Slow.
            if (_uiRefs.ViewFrameSlider != null)
            {
                _uiRefs.ViewFrameSlider.interactable = canStep;
                float frac = total > 1 ? (float)current / (total - 1) : 0f;
                _uiRefs.ViewFrameSlider.SetValueWithoutNotify(frac);
            }

            // Frame counter label.
            if (_uiRefs.ViewFrameCounterLabel != null)
            {
                if (total == 0)
                    _uiRefs.ViewFrameCounterLabel.text = "Frame -- / --";
                else
                    _uiRefs.ViewFrameCounterLabel.text = $"Frame {current + 1} / {total}";
            }
        }

        // ── Character toggle ──────────────────────────────────────────────

        private void OnToggleCharacter()
        {
            if (_previewService == null) return;
            _previewService.SetShowCharacter(!_previewService.ShowCharacter);
            RefreshCharacterToggleVisual();
        }

        /// <summary>
        /// Mirrors the pattern of <see cref="RefreshTransportUI"/>: reads the current
        /// <c>ShowCharacter</c> state from <see cref="SpellPreviewService"/> and updates
        /// the toggle button's label and background colour accordingly.
        /// Amber (SPEED_BTN_ACTIVE) when ON, dark grey (SPEED_BTN_NORMAL) when OFF.
        /// </summary>
        private void RefreshCharacterToggleVisual()
        {
            if (_previewService == null) return;
            bool on = _previewService.ShowCharacter;

            if (_uiRefs.ViewCharacterToggleBtnImg != null)
                _uiRefs.ViewCharacterToggleBtnImg.color = on ? SPEED_BTN_ACTIVE : SPEED_BTN_NORMAL;

            if (_uiRefs.ViewCharacterToggleLabel != null)
            {
                _uiRefs.ViewCharacterToggleLabel.text  = on ? "Character: ON" : "Character: OFF";
                _uiRefs.ViewCharacterToggleLabel.color = on
                    ? new Color(0.10f, 0.08f, 0.04f, 1f)   // dark text on amber
                    : new Color(0.60f, 0.60f, 0.68f, 1f);  // muted on dark
            }
        }

        // ── Lifecycle hooks called from SpellsRuntimeEditor.UI.cs ─────────

        private void OnViewPanelOpened()
        {
            EnsurePreviewService();
            ApplyAudioMute(true);
            BindPreviewTexture();
            PushSelectedSpellToPreview();
            _previewService?.Open();
            UpdateViewStatus();
            // Sync the toggle visual with the reset state (ShowCharacter=false after Open()).
            RefreshCharacterToggleVisual();
        }

        private void OnViewPanelClosed()
        {
            _previewService?.Close();
            ApplyAudioMute(false);
            UpdateViewStatus();
        }

        private void NotifyPreviewSelectionChanged()
        {
            if (_previewService == null) return;
            if (!_openDropdowns.Contains("view")) return;
            PushSelectedSpellToPreview();
            UpdateViewStatus();
        }

        // ── Preview service plumbing ──────────────────────────────────────

        private void EnsurePreviewService()
        {
            if (_previewService != null) return;
            if (_previewStageRoot == null)
            {
                _previewStageRoot = new GameObject("SpellPreviewStage");
                _previewStageRoot.transform.SetParent(transform, false);
            }
            _previewService = new SpellPreviewService();
            _previewService.Initialize(_previewStageRoot.transform);
            _previewService.SetDirection(_previewDirection);
        }

        private void BindPreviewTexture()
        {
            if (_uiRefs.ViewRawImage == null || _previewService == null) return;
            _uiRefs.ViewRawImage.texture = _previewService.GetPreviewTexture();
        }

        private void PushSelectedSpellToPreview()
        {
            if (_previewService == null) return;
            SpellDefinition spell = null;
            if (!string.IsNullOrEmpty(_selectedKey) && _catalog != null)
                _catalog.TryGet(_selectedKey, out spell);
            _previewService.SetSelectedSpell(spell);
        }

        private void UpdateViewStatus()
        {
            if (_uiRefs.ViewSpellNameTmp == null && _uiRefs.ViewStatusTmp == null) return;

            SpellDefinition spell = null;
            if (!string.IsNullOrEmpty(_selectedKey) && _catalog != null)
                _catalog.TryGet(_selectedKey, out spell);

            if (_uiRefs.ViewSpellNameTmp != null)
            {
                _uiRefs.ViewSpellNameTmp.text = spell == null
                    ? "(no spell selected)"
                    : (string.IsNullOrEmpty(spell.displayName) ? spell.spellKey : spell.displayName);
            }

            if (_uiRefs.ViewStatusTmp != null)
            {
                if (spell == null)
                {
                    _uiRefs.ViewStatusTmp.text = "select a spell to preview";
                }
                else
                {
                    string dirLabel = DirectionLabel(_previewDirection);
                    string note = (spell.type == SpellType.Projectile
                                   && (_previewService == null || !_previewService.HasProjectilePrefab))
                        ? "  [no projectile prefab — projectile preview disabled]"
                        : "";
                    _uiRefs.ViewStatusTmp.text = $"looping  •  dir: {dirLabel}{note}";
                }
            }
        }

        private static string DirectionLabel(Vector2 d)
        {
            if (d == Vector2.up)    return "N";
            if (d == Vector2.down)  return "S";
            if (d == Vector2.left)  return "W";
            if (d == Vector2.right) return "E";
            return $"({d.x:F2}, {d.y:F2})";
        }

        // ── Audio mute (AudioListener.volume cache/restore) ───────────────

        private void ApplyAudioMute(bool mute)
        {
            if (mute)
            {
                if (_audioMuteApplied) return;
                _audioVolumeCache = AudioListener.volume;
                AudioListener.volume = 0f;
                _audioMuteApplied = true;
            }
            else
            {
                if (!_audioMuteApplied) return;
                AudioListener.volume = _audioVolumeCache;
                _audioMuteApplied = false;
            }
        }

        // ── Forwarded lifecycle from main partial ─────────────────────────

        private void ShutdownPreview()
        {
            if (_previewService != null)
            {
                _previewService.Shutdown();
                _previewService = null;
            }
            if (_previewStageRoot != null)
            {
                Valkur.Core.SafeDestroy.Of(_previewStageRoot);
                _previewStageRoot = null;
            }
            ApplyAudioMute(false);
        }
    }
}
