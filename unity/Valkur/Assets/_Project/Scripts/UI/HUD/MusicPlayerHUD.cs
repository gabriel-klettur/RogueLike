using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.UIKit;
using Valkur.Infrastructure;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Persistent now-playing widget anchored to the bottom-right corner.
    /// Shows the zone's current track, BPM/time-signature, Bar/Beat counter,
    /// a metronome pulse, a progress bar, and transport controls
    /// (Prev / Play-Pause / Next + volume slider + mute).
    ///
    /// Listens to <see cref="IAudioService.OnTrackChanged"/>, so when
    /// <c>OnZoneChanged</c> picks a track, the widget updates automatically.
    ///
    /// Self-builds its UI; just AddComponent on a GameObject.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed partial class MusicPlayerHUD : MonoBehaviour
    {
        [SerializeField, Tooltip("Width of the widget in pixels.")]
        private float widgetWidth = 320f;
        [SerializeField, Tooltip("Height of the widget in pixels.")]
        private float widgetHeight = 118f;
        [SerializeField, Tooltip("Minimum size while resizing (px).")]
        private Vector2 minSize = new Vector2(140f, 52f);
        [SerializeField, Tooltip("Maximum size while resizing (px).")]
        private Vector2 maxSize = new Vector2(720f, 720f);

        // Reference design size used as the basis for uniform scaling.
        // Everything (fonts, buttons, slider, icons) is laid out for this size
        // and then scaled via RectTransform.localScale so the look stays proportional.
        private const float BaseW = 320f;
        private const float HeaderH = 18f;
        // Simple mode is the compact transport-only widget.
        // Expanded mode adds a spectrum analyzer + beat-dot row above it,
        // so the user can visually align the configured BPM with the actual audio peaks.
        private const float BaseHSimple   = 78f;
        // Expanded mode hosts (top→bottom): beat-dot row, full-song waveform with
        // bar/beat grid + playhead, and the realtime spectrum bar analyzer.
        private const float BaseHExpanded = 320f;
        private float CurrentBaseH => _isExpanded ? BaseHExpanded : BaseHSimple;
        [SerializeField, Tooltip("Edge inset from screen border.")]
        private float edgeInset = 16f;
        [SerializeField, Tooltip("Vertical lift to leave room for minimized HUD buttons and toasts below.")]
        private float bottomLift = 88f;
        [SerializeField, Tooltip("Fade widget out when no track is playing. Default off so the player is always visible (mirrors HP/MP HUD on the bottom-left).")]
        private bool hideWhenIdle = false;

        private Canvas _canvas;
        private RectTransform _rt;
        private CanvasGroup _cg;
        private Image _bg;
        private GameObject _headerBar;
        private Image _headerBg;
        private Image _progressFill;
        private Image _metronome;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _meta;
        private TextMeshProUGUI _beatCounter;
        private TextMeshProUGUI _timeLabel;
        private Image _playIcon;
        private Button _playBtn;
        private Button _prevBtn;
        private Button _nextBtn;
        private Button _muteBtn;
        private Image _muteIcon;
        private Slider _volumeSlider;

        // Expanded-mode widgets (built once, hidden in simple mode).
        private bool _isExpanded;
        private GameObject _simpleContent;
        private GameObject _spectrumPanel;
        private Image _expandIcon;
        private Button _expandBtn;
        // Close: hides the whole panel. The HUDIconBar's "music" icon brings
        // it back. There is intentionally NO in-place "minimized pill" mode —
        // the bar IS the minimized state, shared with the other HUD buttons.
        // Default is HIDDEN: on a fresh launch the panel stays closed and the
        // user opens it by clicking the music icon in the bar. Persisted state
        // (PrefKeyHidden) overrides this default after the first toggle.
        private bool _panelHidden = true;
        private Button _closeBtn;
        private Image _closeIcon;
        private Image _closeBg;
        private RectTransform _closeBtnRt;
        private GameObject _resizeHandle;
        [SerializeField]
        [Tooltip("Sprite shown in the persistent HUD icon bar to re-open the player. " +
                 "Assign Assets/_Project/Art/UI/music_player_button.png in the Inspector. " +
                 "If left empty the sprite is loaded automatically in the Editor.")]
        private Sprite _barIconSprite;

        // Path used for automatic editor-side loading (no Resources folder needed).
        private const string BarIconSpritePath = "Assets/_Project/Art/UI/music_player_button.png";

        private Sprite GetBarIconSprite()
        {
            if (_barIconSprite != null) return _barIconSprite;
#if UNITY_EDITOR
            _barIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(BarIconSpritePath);
            if (_barIconSprite != null) return _barIconSprite;
#endif
            return null;
        }
        private Image[] _specBars;
        private float[] _specSmoothed;
        private float[] _specSamples;
        private Image[] _beatDots;
        private const int SpecBars = 64;
        private const int FftSize = 256; // power of two, gives 256 frequency bins

        // Full-song waveform (Ableton-style overview) — built once per AudioClip.
        private GameObject _waveformPanel;
        private Image _waveformImage;
        private RectTransform _waveformPlayhead;
        private AudioClip _cachedWaveformClip;
        private float _cachedWaveformBpm;
        private float _cachedWaveformOffset;
        private int _cachedWaveformBeatsPerBar;
        private Texture2D _waveformTex;
        private Sprite _waveformSprite;
        private const int WaveformTexW = 512;
        private const int WaveformTexH = 96;
        // Progressive waveform: for streaming clips that don't support GetData,
        // we sample GetOutputData each frame and bake peaks at the playhead column.
        private float[] _waveformOutputBuf;
        private float[] _waveformColumnPeak;   // accumulated peak per column, 0..1
        private Color32[] _waveformGridPixels; // baseline (bg + grid + axis) — re-stamped every refresh
        private Color32[] _waveformWorkPixels; // current frame buffer (grid + waveform peaks so far)
        private bool _waveformProgressive;
        private int _waveformLastCol = -1;
        private float _waveformDirtyTimer;
        // User-controlled vertical zoom on the waveform (1.0 = baseline, 0.25..6.0).
        // Adjustable via mouse wheel over the waveform panel.
        private float _waveformAmplitude = 1f;
        // Track screen size changes so we can re-clamp the widget on resolution change.
        private int _lastScreenW, _lastScreenH;

        private IAudioService _audio;
        private MusicBeatClock _clock;
        private float _flashTimer;
        private float _volumeBeforeMute = 0.7f;

        // ── Lifecycle ───────────────────────────────────────────────────────
        // Sizes are stored separately for simple vs expanded mode so each layout
        // remembers its own preferred footprint.
        private const string PrefKeyWSimple    = "valkur.musichud.simple.width";
        private const string PrefKeyHSimple    = "valkur.musichud.simple.height";
        private const string PrefKeyWExpanded  = "valkur.musichud.expanded.width";
        private const string PrefKeyHExpanded  = "valkur.musichud.expanded.height";
        private const string PrefKeyExpanded   = "valkur.musichud.expanded";
        private const string PrefKeyHidden     = "valkur.musichud.hidden";
        private const string PrefKeyAmplitude  = "valkur.musichud.amplitude";
        // Volume is NOT stored in PlayerPrefs — it lives in GameSettings.musicVolume so
        // the slider here, the PauseMenu sounds panel and the MainMenu sounds panel
        // all read/write the same source. Changing volume from any of them persists
        // globally across menu and gameplay.

        // ID used to register the music button in the persistent HUDIconBar.
        private const string BarButtonId = "music";
        // Per-track tempo overrides (tap-tempo persistence).
        // Keys are prefixed with the track id so each song keeps its own calibration.
        private const string PrefKeyTempoBpmFmt    = "valkur.musichud.tempo.{0}.bpm";
        private const string PrefKeyTempoOffsetFmt = "valkur.musichud.tempo.{0}.offset";

        // Tap-tempo rolling buffer — music-time stamps, shared with Interaction partial.
        private readonly System.Collections.Generic.List<float> _tapTimes = new System.Collections.Generic.List<float>(16);
        private const float TapResetSec = 2.0f; // gap longer than this resets the buffer

        // Cached per-mode sizes so toggling expand/collapse restores the prior footprint.
        private float _simpleW   = 320f;
        private float _simpleH   = 78f;
        private float _expandedW = 320f;
        private float _expandedH = 320f;

        private void Awake()
        {
            // Restore last user-chosen sizes (per-mode) + expand state before building UI.
            if (PlayerPrefs.HasKey(PrefKeyExpanded))
                _isExpanded = PlayerPrefs.GetInt(PrefKeyExpanded) != 0;
            if (PlayerPrefs.HasKey(PrefKeyHidden))
                _panelHidden = PlayerPrefs.GetInt(PrefKeyHidden) != 0;
            // Defaults if nothing persisted yet.
            _simpleW   = PlayerPrefs.GetFloat(PrefKeyWSimple,   320f);
            _simpleH   = PlayerPrefs.GetFloat(PrefKeyHSimple,   78f);
            _expandedW = PlayerPrefs.GetFloat(PrefKeyWExpanded, 320f);
            _expandedH = PlayerPrefs.GetFloat(PrefKeyHExpanded, 320f);
            if (PlayerPrefs.HasKey(PrefKeyAmplitude))
                _waveformAmplitude = Mathf.Clamp(PlayerPrefs.GetFloat(PrefKeyAmplitude), 0.25f, 6f);
            // Seed the pre-mute volume from the unified GameSettings so unmute
            // restores whatever the user last had — even if it was set from the
            // main-menu or pause-menu sounds panel.
            _volumeBeforeMute = Mathf.Clamp01(GameSettings.Instance.musicVolume);
            if (_volumeBeforeMute <= 0.001f) _volumeBeforeMute = 0.7f;
            // Adopt the size belonging to the current mode.
            widgetWidth  = _isExpanded ? _expandedW : _simpleW;
            widgetHeight = _isExpanded ? _expandedH : _simpleH;
            widgetWidth  = Mathf.Clamp(widgetWidth,  minSize.x, maxSize.x);
            widgetHeight = Mathf.Clamp(widgetHeight, minSize.y, maxSize.y);
            BuildUI();
        }

        private void OnEnable()
        {
            _audio = ServiceLocator.Get<IAudioService>();
            if (_audio != null)
            {
                _audio.OnTrackChanged += HandleTrackChanged;
                // AudioManager already initialised its runtime volume from
                // GameSettings.musicVolume in OnSingletonAwake, and PauseMenu's
                // sound panel calls ApplySettings() whenever the user tweaks it.
                // Just reflect that current value in the slider.
                if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(_audio.MusicVolume);
                // Catch-up: a track may already be playing when the HUD enables for
                // the first time (HUD spawned mid-song). Re-apply any saved tempo
                // override for that track so the user's calibration sticks.
                ApplySavedTempoOverride(_audio.CurrentTrackId);
            }
            // Safety: re-apply visibility on enable to guarantee the CanvasGroup is
            // in the right state on the very first frame even if Awake ran before
            // the Canvas was fully initialized.
            ApplyPanelVisibility();

            RegisterBarButton();
        }

        private void OnDisable()
        {
            if (_audio != null) _audio.OnTrackChanged -= HandleTrackChanged;
            _audio = null;

            // Tray button is owned by the HUDIconBar singleton; remove on disable
            // so the icon doesn't dangle if this widget is destroyed.
            var bar = HUDIconBar.Instance;
            if (bar != null) bar.Unregister(BarButtonId);
        }

        private void RegisterBarButton()
        {
            var bar = HUDIconBar.Instance;
            if (bar == null) return;
            // order=2 keeps inventory(0) → spells(1) → music(2) left-to-right.
            bar.Register(BarButtonId, GetBarIconSprite(), TogglePanel, order: 2);
        }

        private void TogglePanel()
        {
            _panelHidden = !_panelHidden;
            ApplyPanelVisibility();
            PlayerPrefs.SetInt(PrefKeyHidden, _panelHidden ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void HandleTrackChanged(string id, string title, float bpm, int beatsPerBar)
        {
            _flashTimer = 0.6f;
            string key = _audio != null ? _audio.CurrentTrackKey : null;
            UpdateStaticLabels(title, bpm, beatsPerBar, key);
            // New song = clean tap-tempo buffer.
            _tapTimes.Clear();
            // If the user has previously calibrated this track via tap-tempo,
            // re-apply that override so the live BPM/offset persist across plays.
            ApplySavedTempoOverride(id);
        }

        private void ApplySavedTempoOverride(string trackId)
        {
            if (string.IsNullOrEmpty(trackId)) return;
            string bpmKey = string.Format(PrefKeyTempoBpmFmt, trackId);
            string offKey = string.Format(PrefKeyTempoOffsetFmt, trackId);
            if (!PlayerPrefs.HasKey(bpmKey) || !PlayerPrefs.HasKey(offKey)) return;
            float bpm = PlayerPrefs.GetFloat(bpmKey);
            float off = PlayerPrefs.GetFloat(offKey);
            if (bpm <= 0f) return;
            if (_clock == null) _clock = MusicBeatClock.Instance;
            // Defer one frame so MusicBeatClock has time to sync to the new track
            // (its own HandleTrackChanged also fires on the same event).
            StartCoroutine(ApplyOverrideNextFrame(bpm, off));
        }

        private System.Collections.IEnumerator ApplyOverrideNextFrame(float bpm, float off)
        {
            yield return null;
            if (_clock == null) _clock = MusicBeatClock.Instance;
            _clock?.OverrideTempo(bpm, off);
        }

        private void Update()
        {
            if (_audio == null)
            {
                _audio = ServiceLocator.Get<IAudioService>();
                if (_audio != null)
                {
                    _audio.OnTrackChanged += HandleTrackChanged;
                    if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(_audio.MusicVolume);
                }
            }
            if (_clock == null) _clock = MusicBeatClock.Instance;

            // Re-clamp size when the screen resolution changes so the widget always fits.
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
            {
                _lastScreenW = Screen.width;
                _lastScreenH = Screen.height;
                ApplySize(widgetWidth, widgetHeight);
            }

            bool playing = _audio != null && _audio.IsMusicPlaying;
            bool paused  = _audio != null && _audio.IsMusicPaused;
            bool active  = playing || paused;

            if (_cg != null)
            {
                // Respect _panelHidden: when the user closed the panel via the
                // close button (or it starts closed by default), Update must NOT
                // bring alpha back to 1 every frame — that would leave the panel
                // visually visible while blocksRaycasts is false, looking like
                // "buttons don't work".
                float target = (_panelHidden || (hideWhenIdle && !active)) ? 0f : 1f;
                _cg.alpha = Mathf.MoveTowards(_cg.alpha, target, Time.unscaledDeltaTime * 4f);
            }

            if (!active)
            {
                // Idle state: keep widget visible but show placeholder data.
                if (_title != null) _title.text = "♪ No music";
                if (_meta != null) _meta.text = "— BPM · —/—";
                if (_beatCounter != null) _beatCounter.text = "—";
                if (_timeLabel != null) _timeLabel.text = "0:00 / 0:00";
                if (_progressFill != null) _progressFill.fillAmount = 0f;
                if (_metronome != null)
                {
                    _metronome.transform.localScale = Vector3.one;
                    _metronome.color = new Color(0.4f, 0.4f, 0.4f, 0.6f);
                }
                if (_playIcon != null) _playIcon.sprite = SpritePlay;
                if (_muteIcon != null && _audio != null)
                    _muteIcon.sprite = _audio.MusicVolume <= 0.001f ? SpriteSpeakerMute : SpriteSpeaker;
                if (_prevBtn != null) _prevBtn.interactable = false;
                if (_nextBtn != null) _nextBtn.interactable = false;
                if (_playBtn != null) _playBtn.interactable = false;
                if (_bg != null) _bg.color = new Color(0.06f, 0.06f, 0.08f, 0.85f);
                UpdateSpectrum(false);
                return;
            }

            if (_playBtn != null) _playBtn.interactable = true;

            if (_title != null && _title.text == "—" && _audio != null)
                UpdateStaticLabels(_audio.CurrentTrackTitle, _audio.CurrentTrackBpm, _audio.CurrentTrackBeatsPerBar, _audio.CurrentTrackKey);

            // Progress bar + time
            float total = 0f;
            var clip = _audio.CurrentMusicClip;
            if (clip != null) total = clip.length;
            float t = _audio.CurrentMusicTime;
            float p = total > 0f ? Mathf.Clamp01(t / total) : 0f;
            if (_progressFill != null) _progressFill.fillAmount = p;
            if (_timeLabel != null) _timeLabel.text = $"{FormatTime(t)} / {FormatTime(total)}";

            // Beat counter & metronome pulse
            if (_clock != null && _clock.IsActive && playing)
            {
                if (_beatCounter != null)
                    _beatCounter.text = $"Bar {_clock.CurrentBar + 1} · Beat {_clock.CurrentBeatInBar + 1}/{_clock.BeatsPerBar}";

                if (_metronome != null)
                {
                    float phase = _clock.BeatPhase01;
                    float pulse = Mathf.Lerp(1.6f, 1f, phase);
                    _metronome.transform.localScale = new Vector3(pulse, pulse, 1f);
                    bool downbeat = _clock.CurrentBeatInBar == 0;
                    _metronome.color = Color.Lerp(
                        downbeat ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(0.6f, 0.8f, 1f, 1f),
                        new Color(0.4f, 0.4f, 0.4f, 0.6f),
                        phase);
                }
            }
            else
            {
                if (_beatCounter != null) _beatCounter.text = paused ? "paused" : "—";
                if (_metronome != null) _metronome.transform.localScale = Vector3.one;
            }

            if (_playIcon != null) _playIcon.sprite = (playing && !paused) ? SpritePause : SpritePlay;
            if (_muteIcon != null && _audio != null)
                _muteIcon.sprite = _audio.MusicVolume <= 0.001f ? SpriteSpeakerMute : SpriteSpeaker;

            bool canSkip = _audio != null && _audio.HasActivePlaylist;
            if (_prevBtn != null) _prevBtn.interactable = canSkip;
            if (_nextBtn != null) _nextBtn.interactable = canSkip;

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_flashTimer / 0.6f);
                if (_bg != null)
                    _bg.color = Color.Lerp(new Color(0.06f, 0.06f, 0.08f, 0.85f),
                                           new Color(0.95f, 0.78f, 0.25f, 0.85f), k);
            }
            else if (_bg != null)
            {
                _bg.color = new Color(0.06f, 0.06f, 0.08f, 0.85f);
            }

            UpdateSpectrum(playing && !paused);
        }

        private void UpdateStaticLabels(string title, float bpm, int beatsPerBar, string key = null)
        {
            if (_title != null)
                _title.text = string.IsNullOrEmpty(title) ? "—" : "♪ " + title;
            if (_meta != null)
            {
                if (bpm > 0f)
                {
                    string baseMeta = $"{bpm:0} BPM · {Mathf.Max(1, beatsPerBar)}/4";
                    _meta.text = string.IsNullOrEmpty(key) ? baseMeta : baseMeta + " · " + key;
                }
                else
                {
                    _meta.text = string.IsNullOrEmpty(key) ? "no tempo" : "no tempo · " + key;
                }
            }
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds)) seconds = 0f;
            int total = Mathf.FloorToInt(seconds);
            return $"{total / 60:D1}:{total % 60:D2}";
        }

        // ── Button handlers ─────────────────────────────────────────────────
        private void OnPlayClicked()
        {
            if (_audio == null) return;
            if (_audio.IsMusicPaused) _audio.ResumeMusic();
            else if (_audio.IsMusicPlaying) _audio.PauseMusic();
        }

        private void OnPrevClicked() { _audio?.SkipToPreviousTrack(); }
        private void OnNextClicked() { _audio?.SkipToNextTrack(); }

        private void OnMuteClicked()
        {
            if (_audio == null || _volumeSlider == null) return;
            if (_audio.MusicVolume > 0.001f)
            {
                _volumeBeforeMute = _audio.MusicVolume;
                ApplyAndPersistVolume(0f);
                _volumeSlider.SetValueWithoutNotify(0f);
            }
            else
            {
                float v = _volumeBeforeMute > 0.05f ? _volumeBeforeMute : 0.7f;
                ApplyAndPersistVolume(v);
                _volumeSlider.SetValueWithoutNotify(v);
            }
        }

        private void OnVolumeChanged(float v)
        {
            ApplyAndPersistVolume(v);
        }

        // Single point where the music volume is mutated. Writes to GameSettings
        // (the unified source of truth shared with the menu sounds panels) and
        // also pushes the value to the running AudioManager so it takes effect
        // immediately. Save() is debounced via GameSettings' own JSON write — cheap.
        private void ApplyAndPersistVolume(float v)
        {
            v = Mathf.Clamp01(v);
            var gs = GameSettings.Instance;
            gs.musicVolume = v;
            _audio?.SetMusicVolume(v);
            gs.Save();
        }

        private void OnCloseClicked()
        {
            // Hide the whole panel. The HUDIconBar's "music" button toggles
            // visibility back on (it calls TogglePanel).
            _panelHidden = true;
            ApplyPanelVisibility();
            PlayerPrefs.SetInt(PrefKeyHidden, 1);
            PlayerPrefs.Save();
        }

        // Fixed close-button footprint (no longer transitions between sizes).
        private const float CloseBtnSize  = 20f;
        private const float CloseIconSize = 12f;

        private void ApplyRootAnchorPosition()
        {
            if (_rt == null) return;
            _rt.anchoredPosition = new Vector2(-edgeInset, edgeInset + bottomLift);
        }

        private void ApplyPanelVisibility()
        {
            // Whole-panel show/hide via CanvasGroup. When hidden, the panel is
            // invisible AND non-interactive — clicks fall through to the bar.
            if (_cg != null)
            {
                _cg.alpha           = _panelHidden ? 0f : 1f;
                _cg.blocksRaycasts  = !_panelHidden;
                _cg.interactable    = !_panelHidden;
            }
            ApplyRootAnchorPosition();
        }
    }
}
