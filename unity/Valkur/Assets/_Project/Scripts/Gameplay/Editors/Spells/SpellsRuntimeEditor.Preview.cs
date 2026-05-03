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
    /// and wires the 4-direction selector buttons.
    ///
    /// Behavior:
    ///   • Auto-play on panel open if a spell is selected.
    ///   • Selection change in the picker updates the loop in real time.
    ///   • Direction buttons (N/S/E/W) override the cast vector — applied next cycle.
    ///   • Audio is muted while the panel is open (via AudioListener.volume cache),
    ///     restored on close.
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

            // Hover probe lets Update detect wheel-zoom only when the cursor is
            // actually over the preview surface.
            if (_uiRefs.ViewPreviewArea != null && _previewHoverProbe == null)
                _previewHoverProbe = _uiRefs.ViewPreviewArea.gameObject.AddComponent<SpellsViewHoverProbe>();
        }

        /// <summary>
        /// Called every frame from <see cref="SpellsRuntimeEditor"/>.Update while the
        /// View panel is open. Zooms the preview when the user spins the mouse wheel
        /// over the preview surface.
        /// </summary>
        private void TickPreviewInput()
        {
            if (_previewService == null || _previewHoverProbe == null) return;
            if (!_previewHoverProbe.IsHovered) return;

            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(wheel) > 0.01f)
                _previewService.ZoomBy(wheel * WHEEL_ZOOM_SENSITIVITY);
        }

        private void SetPreviewDirection(Vector2 dir)
        {
            _previewDirection = dir.normalized;
            _previewService?.SetDirection(_previewDirection);
            UpdateViewStatus();
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
