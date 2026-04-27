using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
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
    public sealed class MusicPlayerHUD : MonoBehaviour
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
        [SerializeField, Tooltip("Vertical lift to leave room for toasts above.")]
        private float bottomLift = 0f;
        [SerializeField, Tooltip("Fade widget out when no track is playing. Default off so the player is always visible (mirrors HP/MP HUD on the bottom-left).")]
        private bool hideWhenIdle = false;

        private Canvas _canvas;
        private RectTransform _rt;
        private CanvasGroup _cg;
        private Image _bg;
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
        // Minimize: collapses the whole widget to a tiny pill with a single restore icon.
        // Available in both simple and expanded modes — toggling restores the previous mode.
        private bool _isMinimized;
        private Button _minimizeBtn;
        private Image _minimizeIcon;
        private GameObject _resizeHandle;
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
        private const string PrefKeyMinimized  = "valkur.musichud.minimized";
        private const string PrefKeyAmplitude  = "valkur.musichud.amplitude";
        private const string PrefKeyVolume     = "valkur.musichud.volume";
        // Per-track tempo overrides (tap-tempo persistence).
        // Keys are prefixed with the track id so each song keeps its own calibration.
        private const string PrefKeyTempoBpmFmt    = "valkur.musichud.tempo.{0}.bpm";
        private const string PrefKeyTempoOffsetFmt = "valkur.musichud.tempo.{0}.offset";

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
            if (PlayerPrefs.HasKey(PrefKeyMinimized))
                _isMinimized = PlayerPrefs.GetInt(PrefKeyMinimized) != 0;
            // Defaults if nothing persisted yet.
            _simpleW   = PlayerPrefs.GetFloat(PrefKeyWSimple,   320f);
            _simpleH   = PlayerPrefs.GetFloat(PrefKeyHSimple,   78f);
            _expandedW = PlayerPrefs.GetFloat(PrefKeyWExpanded, 320f);
            _expandedH = PlayerPrefs.GetFloat(PrefKeyHExpanded, 320f);
            if (PlayerPrefs.HasKey(PrefKeyAmplitude))
                _waveformAmplitude = Mathf.Clamp(PlayerPrefs.GetFloat(PrefKeyAmplitude), 0.25f, 6f);
            if (PlayerPrefs.HasKey(PrefKeyVolume))
                _volumeBeforeMute = Mathf.Clamp(PlayerPrefs.GetFloat(PrefKeyVolume), 0f, 1f);
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
                // Restore persisted volume BEFORE syncing the slider so the AudioManager
                // already plays at the saved level when the game starts.
                float savedVol = PlayerPrefs.GetFloat(PrefKeyVolume, 0.7f);
                _audio.SetMusicVolume(savedVol);
                if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(savedVol);
                // Catch-up: a track may already be playing when the HUD enables for
                // the first time (HUD spawned mid-song). Re-apply any saved tempo
                // override for that track so the user's calibration sticks.
                ApplySavedTempoOverride(_audio.CurrentTrackId);
            }
        }

        private void OnDisable()
        {
            if (_audio != null) _audio.OnTrackChanged -= HandleTrackChanged;
            _audio = null;
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
                float target = (hideWhenIdle && !active) ? 0f : 1f;
                _cg.alpha = Mathf.MoveTowards(_cg.alpha, target, Time.unscaledDeltaTime * 4f);
            }

            if (!active)
            {
                // Idle state: keep widget visible but show placeholder data.
                if (_title != null) _title.text = "\u266A No music";
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
                _audio.SetMusicVolume(0f);
                _volumeSlider.SetValueWithoutNotify(0f);
            }
            else
            {
                float v = _volumeBeforeMute > 0.05f ? _volumeBeforeMute : 0.7f;
                _audio.SetMusicVolume(v);
                _volumeSlider.SetValueWithoutNotify(v);
                PlayerPrefs.SetFloat(PrefKeyVolume, v);
                PlayerPrefs.Save();
            }
        }

        private void OnVolumeChanged(float v)
        {
            _audio?.SetMusicVolume(v);
            // Persist non-zero volumes so the next session starts at the same level.
            if (v > 0.001f)
            {
                PlayerPrefs.SetFloat(PrefKeyVolume, v);
                PlayerPrefs.Save();
            }
        }

        // ── UI Build ────────────────────────────────────────────────────────
        private void BuildUI()
        {
            // Ensure RectTransform exists BEFORE we touch any Canvas / Image.
            // (RequireComponent on the class guarantees this when the component is added.)
            _rt = gameObject.GetComponent<RectTransform>();
            if (_rt == null) _rt = gameObject.AddComponent<RectTransform>();

            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                var canvasGo = new GameObject("MusicHUDCanvas", typeof(RectTransform));
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 150;
                canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasGo.AddComponent<GraphicRaycaster>();
                transform.SetParent(canvasGo.transform, false);
            }

            _rt.anchorMin = new Vector2(1f, 0f);
            _rt.anchorMax = new Vector2(1f, 0f);
            _rt.pivot     = new Vector2(1f, 0f);
            _rt.anchoredPosition = new Vector2(-edgeInset, edgeInset + bottomLift);
            // Inner sizeDelta is fixed to the design size; the widget is resized
            // by changing localScale so all children scale uniformly with it.
            _rt.sizeDelta = new Vector2(BaseW, CurrentBaseH);
            ApplyScaleFromSize();

            _bg = gameObject.AddComponent<Image>();
            _bg.color = new Color(0.06f, 0.06f, 0.08f, 0.85f);

            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = hideWhenIdle ? 0f : 1f; // start visible by default
            _cg.blocksRaycasts = true;
            _cg.interactable = true;

            // Simple-mode content container: anchored to the BOTTOM of the root
            // and fixed at BaseHSimple px tall. The expanded mode adds a spectrum
            // panel ABOVE this container (the root grows upward, pivot is bottom-right).
            _simpleContent = NewChild("SimpleContent");
            var scRt = _simpleContent.GetComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0f, 0f);
            scRt.anchorMax = new Vector2(1f, 0f);
            scRt.pivot     = new Vector2(0.5f, 0f);
            scRt.anchoredPosition = new Vector2(0f, 0f);
            scRt.sizeDelta = new Vector2(0f, BaseHSimple);

            // Metronome dot
            var dotGo = NewChild("Metronome", _simpleContent.transform);
            var dotRt = dotGo.GetComponent<RectTransform>();
            dotRt.anchorMin = new Vector2(0f, 1f);
            dotRt.anchorMax = new Vector2(0f, 1f);
            dotRt.pivot     = new Vector2(0.5f, 0.5f);
            dotRt.anchoredPosition = new Vector2(20f, -12f);
            dotRt.sizeDelta = new Vector2(12f, 12f);
            _metronome = dotGo.AddComponent<Image>();
            _metronome.color = new Color(0.6f, 0.8f, 1f, 1f);
            _metronome.sprite = BuildCircleSprite();
            _metronome.raycastTarget = false;

            // Title (top-left)
            _title = NewLabel("Title", 14, FontStyles.Bold, new Color(1f, 0.95f, 0.85f), _simpleContent.transform);
            var titleRt = _title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot     = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(34f, -4f);
            titleRt.sizeDelta = new Vector2(-44f, 18f);
            _title.alignment = TextAlignmentOptions.Left;
            _title.enableWordWrapping = false;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _title.text = "—";

            // Meta (BPM · TS)
            _meta = NewLabel("Meta", 10, FontStyles.Normal, new Color(0.75f, 0.85f, 1f, 0.95f), _simpleContent.transform);
            var metaRt = _meta.rectTransform;
            metaRt.anchorMin = new Vector2(0f, 1f);
            metaRt.anchorMax = new Vector2(0f, 1f);
            metaRt.pivot     = new Vector2(0f, 1f);
            metaRt.anchoredPosition = new Vector2(34f, -22f);
            metaRt.sizeDelta = new Vector2(180f, 12f);
            _meta.alignment = TextAlignmentOptions.Left;
            _meta.text = "no tempo";

            // Beat counter (top-right)
            _beatCounter = NewLabel("BeatCounter", 10, FontStyles.Bold, new Color(1f, 1f, 1f, 0.95f), _simpleContent.transform);
            var bcRt = _beatCounter.rectTransform;
            bcRt.anchorMin = new Vector2(1f, 1f);
            bcRt.anchorMax = new Vector2(1f, 1f);
            bcRt.pivot     = new Vector2(1f, 1f);
            bcRt.anchoredPosition = new Vector2(-8f, -4f);
            bcRt.sizeDelta = new Vector2(150f, 14f);
            _beatCounter.alignment = TextAlignmentOptions.Right;
            _beatCounter.text = "—";

            // Time label
            _timeLabel = NewLabel("Time", 10, FontStyles.Normal, new Color(0.85f, 0.85f, 0.85f, 0.85f), _simpleContent.transform);
            var tlRt = _timeLabel.rectTransform;
            tlRt.anchorMin = new Vector2(1f, 1f);
            tlRt.anchorMax = new Vector2(1f, 1f);
            tlRt.pivot     = new Vector2(1f, 1f);
            tlRt.anchoredPosition = new Vector2(-8f, -22f);
            tlRt.sizeDelta = new Vector2(120f, 12f);
            _timeLabel.alignment = TextAlignmentOptions.Right;
            _timeLabel.text = "0:00 / 0:00";

            // Progress bar (just under meta line, just above buttons; no dead space)
            var barBgGo = NewChild("ProgressBg", _simpleContent.transform);
            var barBgRt = barBgGo.GetComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0f, 1f);
            barBgRt.anchorMax = new Vector2(1f, 1f);
            barBgRt.pivot     = new Vector2(0f, 1f);
            // Give the bar more click area than its visible thickness so it's draggable.
            barBgRt.anchoredPosition = new Vector2(8f, -36f);
            barBgRt.sizeDelta = new Vector2(-16f, 12f);
            var bgImg = barBgGo.AddComponent<Image>();
            bgImg.sprite = BuildSolidSprite();
            bgImg.type = Image.Type.Sliced;
            // Transparent click hit area (visible track is the inner Image below).
            bgImg.color = new Color(1f, 1f, 1f, 0.001f);
            bgImg.raycastTarget = true;
            var seekHandler = barBgGo.AddComponent<ProgressBarSeekHandler>();
            seekHandler.Init(this, barBgRt);

            // Visible thin track inside the larger hit area.
            var trackGo = NewChild("Track", barBgGo.transform);
            var tRt = trackGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0.5f);
            tRt.anchorMax = new Vector2(1f, 0.5f);
            tRt.pivot     = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = Vector2.zero;
            tRt.sizeDelta = new Vector2(0f, 3f);
            var trackImg = trackGo.AddComponent<Image>();
            trackImg.sprite = BuildSolidSprite();
            trackImg.color = new Color(1f, 1f, 1f, 0.18f);
            trackImg.raycastTarget = false;

            var fillGo = NewChild("ProgressFill", trackGo.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            fillRt.anchoredPosition = Vector2.zero;
            _progressFill = fillGo.AddComponent<Image>();
            _progressFill.sprite = BuildSolidSprite();
            _progressFill.color = new Color(0.95f, 0.78f, 0.25f, 0.95f);
            _progressFill.type = Image.Type.Filled;
            _progressFill.fillMethod = Image.FillMethod.Horizontal;
            _progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _progressFill.fillAmount = 0f;
            _progressFill.raycastTarget = false;

            // Transport buttons (bottom row)
            float btnY = 5f;
            _prevBtn   = BuildIconButton("PrevBtn",   SpritePrev,    8f,   btnY, OnPrevClicked,   out _,           _simpleContent.transform);
            _playBtn   = BuildIconButton("PlayBtn",   SpritePlay,    36f,  btnY, OnPlayClicked,   out _playIcon,   _simpleContent.transform);
            _nextBtn   = BuildIconButton("NextBtn",   SpriteNext,    64f,  btnY, OnNextClicked,   out _,           _simpleContent.transform);
            _muteBtn   = BuildIconButton("MuteBtn",   SpriteSpeaker, 100f, btnY, OnMuteClicked,   out _muteIcon,   _simpleContent.transform);
            _expandBtn = BuildIconButton("ExpandBtn", SpriteChevronUp, 128f, btnY, OnExpandClicked, out _expandIcon, _simpleContent.transform);

            // Volume slider (right of buttons)
            BuildVolumeSlider(160f, btnY + 5f);

            // Spectrum panel (expanded mode only) — sits above the simple content.
            BuildSpectrumPanel();

            // Resize grip (top-left corner) — drag to resize
            BuildResizeHandle();
            // Minimize button (top-right corner) — collapses to a tiny restore pill.
            BuildMinimizeButton();

            // Apply initial expand state (built collapsed by default; toggle if persisted).
            ApplyExpandedState();
            // Apply minimized state AFTER expand so the cached size is correct.
            ApplyMinimizedState();

            // Re-clamp to current screen so a previously-saved size that no longer
            // fits the resolution is shrunk back into view on first show.
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            ApplySize(widgetWidth, widgetHeight);
        }

        private void BuildResizeHandle()
        {
            var go = NewChild("ResizeHandle");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(2f, -2f);
            rt.sizeDelta = new Vector2(18f, 18f);

            // Transparent hit area so the entire 18x18 receives drag events.
            var hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;

            // Triangle glyph on top.
            var glyph = NewLabel("Glyph", 18, FontStyles.Bold, new Color(1f, 0.95f, 0.85f, 0.9f));
            glyph.transform.SetParent(go.transform, false);
            var gr = glyph.rectTransform;
            gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one;
            gr.sizeDelta = Vector2.zero; gr.anchoredPosition = Vector2.zero;
            glyph.alignment = TextAlignmentOptions.TopLeft;
            glyph.text = "◤"; // BLACK UPPER LEFT TRIANGLE
            glyph.raycastTarget = false;

            var handler = go.AddComponent<ResizeHandle>();
            handler.Init(this);
            _resizeHandle = go;
        }

        // ── Minimize button ─────────────────────────────────────────────────
        // Sits at the top-right corner of the root and is ALWAYS visible (even
        // while minimized) so the user can always restore the player. Available
        // in both simple and expanded modes per user request.
        private void BuildMinimizeButton()
        {
            var go = NewChild("MinimizeBtn");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-2f, -2f);
            rt.sizeDelta = new Vector2(20f, 20f);

            var bgImg = go.AddComponent<Image>();
            bgImg.sprite = BuildRoundedRectSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(1f, 1f, 1f, 0.10f);

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(1f, 0.95f, 0.55f, 1f);
            colors.pressedColor     = new Color(0.95f, 0.78f, 0.25f, 1f);
            colors.selectedColor    = new Color(1f, 1f, 1f, 1f);
            colors.disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            colors.colorMultiplier  = 1f;
            colors.fadeDuration     = 0.10f;
            btn.colors = colors;
            btn.targetGraphic = bgImg;
            btn.onClick.AddListener(OnMinimizeClicked);
            _minimizeBtn = btn;

            var iconGo = NewChild("MinimizeIcon", go.transform);
            var ir = iconGo.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0.5f, 0.5f);
            ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.pivot     = new Vector2(0.5f, 0.5f);
            ir.anchoredPosition = Vector2.zero;
            ir.sizeDelta = new Vector2(12f, 12f);
            _minimizeIcon = iconGo.AddComponent<Image>();
            _minimizeIcon.sprite = SpriteMinus;
            _minimizeIcon.color = new Color(1f, 1f, 1f, 0.95f);
            _minimizeIcon.raycastTarget = false;
            _minimizeIcon.preserveAspect = true;
        }

        private void OnMinimizeClicked()
        {
            // Going INTO minimized: cache the current footprint so restore comes back
            // to the exact same size the user was using.
            if (!_isMinimized) CacheSizeForCurrentMode();
            _isMinimized = !_isMinimized;
            ApplyMinimizedState();
            PlayerPrefs.SetInt(PrefKeyMinimized, _isMinimized ? 1 : 0);
            PlayerPrefs.Save();
        }

        // Tiny pill size shown when minimized: just the title text + restore button.
        private const float MinimizedW = 28f;
        private const float MinimizedH = 28f;

        private void ApplyMinimizedState()
        {
            // Hide everything except the minimize button (which becomes "restore").
            if (_simpleContent != null)  _simpleContent.SetActive(!_isMinimized);
            if (_spectrumPanel != null)  _spectrumPanel.SetActive(!_isMinimized && _isExpanded);
            if (_resizeHandle != null)   _resizeHandle.SetActive(!_isMinimized);

            if (_minimizeIcon != null)
                _minimizeIcon.sprite = _isMinimized ? SpriteChevronUp : SpriteMinus;

            if (_isMinimized)
            {
                // Shrink root to a small square showing only the restore button.
                if (_rt != null) _rt.sizeDelta = new Vector2(MinimizedW, MinimizedH);
                if (_rt != null) _rt.localScale = Vector3.one;
            }
            else
            {
                // Restore the cached per-mode size.
                widgetWidth  = _isExpanded ? _expandedW : _simpleW;
                widgetHeight = _isExpanded ? _expandedH : _simpleH;
                ApplyExpandedState();
            }
        }

        // ── Spectrum panel (expanded mode) ──────────────────────────────────

        private void BuildSpectrumPanel()
        {
            _specSamples  = new float[FftSize];
            _specSmoothed = new float[SpecBars];
            _specBars     = new Image[SpecBars];

            _spectrumPanel = NewChild("SpectrumPanel");
            var spRt = _spectrumPanel.GetComponent<RectTransform>();
            // Top of root: occupies the area above the simple content.
            spRt.anchorMin = new Vector2(0f, 0f);
            spRt.anchorMax = new Vector2(1f, 1f);
            spRt.pivot     = new Vector2(0.5f, 1f);
            // Sit between top of root and top of simple content (BaseHSimple px from bottom).
            spRt.offsetMin = new Vector2(8f, BaseHSimple);
            spRt.offsetMax = new Vector2(-8f, -8f);

            var bg = _spectrumPanel.AddComponent<Image>();
            bg.sprite = BuildRoundedRectSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.02f, 0.02f, 0.04f, 0.65f);
            bg.raycastTarget = false;

            // Beat dot row at the very top: BeatsPerBar markers, light up on the active beat.
            // The row is also interactive: clicks = tap-tempo; horizontal drag = BPM fine-tune,
            // vertical drag = first-beat offset fine-tune. See BeatDotsTapHandler.
            var dotsRoot = NewChild("BeatDots", _spectrumPanel.transform);
            var drRt = dotsRoot.GetComponent<RectTransform>();
            drRt.anchorMin = new Vector2(0f, 1f);
            drRt.anchorMax = new Vector2(1f, 1f);
            drRt.pivot     = new Vector2(0.5f, 1f);
            drRt.anchoredPosition = new Vector2(0f, -2f);
            // Make the strip tall enough to be a comfortable click/drag target.
            drRt.sizeDelta = new Vector2(0f, 18f);
            // Invisible-but-raycastable backplate so the whole strip catches input.
            var dotsBg = dotsRoot.AddComponent<Image>();
            dotsBg.color = new Color(1f, 1f, 1f, 0.001f);
            dotsBg.raycastTarget = true;
            var tapHandler = dotsRoot.AddComponent<BeatDotsTapHandler>();
            tapHandler.Init(this);

            // Build up to 16 dots; we only show BeatsPerBar of them per Update().
            const int MaxBeatDots = 16;
            _beatDots = new Image[MaxBeatDots];
            for (int i = 0; i < MaxBeatDots; i++)
            {
                var d = NewChild("Dot" + i, dotsRoot.transform);
                var dRt = d.GetComponent<RectTransform>();
                dRt.anchorMin = new Vector2(0f, 0.5f);
                dRt.anchorMax = new Vector2(0f, 0.5f);
                dRt.pivot     = new Vector2(0.5f, 0.5f);
                dRt.sizeDelta = new Vector2(8f, 8f);
                var di = d.AddComponent<Image>();
                di.sprite = BuildCircleSprite();
                di.color = new Color(1f, 1f, 1f, 0.25f);
                di.raycastTarget = false;
                _beatDots[i] = di;
            }

            // Full-song waveform overview (Ableton-style) sits between beat dots and bars.
            // It shows peaks per pixel column with bar/beat grid lines baked in,
            // plus a moving playhead so the user can visually align BPM with audio peaks.
            _waveformPanel = NewChild("Waveform", _spectrumPanel.transform);
            var wfRt = _waveformPanel.GetComponent<RectTransform>();
            wfRt.anchorMin = new Vector2(0f, 1f);
            wfRt.anchorMax = new Vector2(1f, 1f);
            wfRt.pivot     = new Vector2(0.5f, 1f);
            // Top-pinned: 4 px below the beat-dot row (which occupies top 18 px), 100 px tall.
            wfRt.offsetMin = new Vector2(6f, -122f);  // bottom = top - 122
            wfRt.offsetMax = new Vector2(-6f, -22f);  // top    = top - 22

            var wfBg = _waveformPanel.AddComponent<Image>();
            wfBg.color = new Color(0.03f, 0.03f, 0.06f, 0.9f);
            // Mouse-wheel zoom on the waveform area.
            wfBg.raycastTarget = true;
            var zoomHandler = _waveformPanel.AddComponent<WaveformZoomHandler>();
            zoomHandler.Init(this);

            _waveformImage = NewChild("WaveformImage", _waveformPanel.transform).AddComponent<Image>();
            var wiRt = _waveformImage.rectTransform;
            wiRt.anchorMin = Vector2.zero;
            wiRt.anchorMax = Vector2.one;
            wiRt.offsetMin = Vector2.zero;
            wiRt.offsetMax = Vector2.zero;
            _waveformImage.preserveAspect = false;
            _waveformImage.raycastTarget = false;
            _waveformImage.color = Color.white;

            // Playhead: 2 px vertical line that slides with playback.
            var phGo = NewChild("Playhead", _waveformPanel.transform);
            _waveformPlayhead = phGo.GetComponent<RectTransform>();
            _waveformPlayhead.anchorMin = new Vector2(0f, 0f);
            _waveformPlayhead.anchorMax = new Vector2(0f, 1f);
            _waveformPlayhead.pivot     = new Vector2(0.5f, 0.5f);
            _waveformPlayhead.sizeDelta = new Vector2(2f, 0f);
            _waveformPlayhead.anchoredPosition = Vector2.zero;
            var phImg = phGo.AddComponent<Image>();
            phImg.color = new Color(1f, 0.35f, 0.35f, 0.95f);
            phImg.raycastTarget = false;

            // Bars row sits below the waveform.
            var barsRoot = NewChild("Bars", _spectrumPanel.transform);
            var brRt = barsRoot.GetComponent<RectTransform>();
            brRt.anchorMin = new Vector2(0f, 0f);
            brRt.anchorMax = new Vector2(1f, 1f);
            brRt.offsetMin = new Vector2(6f, 6f);
            brRt.offsetMax = new Vector2(-6f, -126f); // leave 18 (dots) + 4 + 100 (waveform) + 4

            for (int i = 0; i < SpecBars; i++)
            {
                var b = NewChild("Bar" + i, barsRoot.transform);
                var bRt = b.GetComponent<RectTransform>();
                // Each bar: anchored to bottom of bars area, evenly distributed across X.
                float xMin = i / (float)SpecBars;
                float xMax = (i + 1) / (float)SpecBars;
                bRt.anchorMin = new Vector2(xMin, 0f);
                bRt.anchorMax = new Vector2(xMax, 0f);
                bRt.pivot     = new Vector2(0.5f, 0f);
                bRt.offsetMin = new Vector2(1f, 0f);
                bRt.offsetMax = new Vector2(-1f, 2f); // initial tiny height
                var bi = b.AddComponent<Image>();
                bi.sprite = BuildSolidSprite();
                bi.type = Image.Type.Sliced;
                bi.color = new Color(0.95f, 0.78f, 0.25f, 0.95f);
                bi.raycastTarget = false;
                _specBars[i] = bi;
            }
        }

        private void OnExpandClicked()
        {
            // Don't allow expand-toggle while minimized — the restore button is the
            // only meaningful action in that state.
            if (_isMinimized) return;
            // Persist the size of the OUTGOING mode so each layout remembers its own footprint.
            CacheSizeForCurrentMode();
            _isExpanded = !_isExpanded;
            // Adopt the size cached for the INCOMING mode.
            widgetWidth  = _isExpanded ? _expandedW : _simpleW;
            widgetHeight = _isExpanded ? _expandedH : _simpleH;
            ApplyExpandedState();
            PlayerPrefs.SetInt(PrefKeyExpanded, _isExpanded ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void ApplyExpandedState()
        {
            if (_rt != null) _rt.sizeDelta = new Vector2(BaseW, CurrentBaseH);
            if (_spectrumPanel != null) _spectrumPanel.SetActive(_isExpanded);
            if (_expandIcon != null)    _expandIcon.sprite = _isExpanded ? SpriteChevronDown : SpriteChevronUp;
            // Re-clamp current pixel size in the new vertical reference frame.
            ApplySize(widgetWidth, widgetHeight);
            PersistSize();
        }

        private void CacheSizeForCurrentMode()
        {
            if (_isExpanded) { _expandedW = widgetWidth; _expandedH = widgetHeight; }
            else             { _simpleW   = widgetWidth; _simpleH   = widgetHeight; }
        }

        // ── Spectrum data update ────────────────────────────────────────────
        private void UpdateSpectrum(bool playing)
        {
            if (!_isExpanded || _specBars == null || _specSamples == null) return;

            bool got = playing && _audio != null && _audio.GetMusicSpectrumData(_specSamples);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 240f);
            float fall = 2.2f * dt; // smoothed bar fall-off rate

            // Map FFT bins to log-spaced bars: low bins occupy more screen than high bins,
            // matching how the human ear perceives frequency.
            int N = _specSamples.Length;
            for (int i = 0; i < SpecBars; i++)
            {
                float t0 = (float)i / SpecBars;
                float t1 = (float)(i + 1) / SpecBars;
                int lo = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow(N, t0)), 1, N - 1);
                int hi = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow(N, t1)), lo + 1, N);

                float v = 0f;
                if (got)
                {
                    for (int k = lo; k < hi; k++) v += _specSamples[k];
                    v /= Mathf.Max(1, hi - lo);
                    // Logarithmic compression so quiet detail is visible.
                    v = Mathf.Clamp01(Mathf.Log10(1f + v * 800f) * 0.5f);
                }

                if (v > _specSmoothed[i]) _specSmoothed[i] = v;
                else                       _specSmoothed[i] = Mathf.Max(0f, _specSmoothed[i] - fall);

                var bar = _specBars[i];
                if (bar == null) continue;
                var rt = bar.rectTransform;
                // Height as percentage of bar area, anchored bottom.
                rt.anchorMax = new Vector2(rt.anchorMax.x, _specSmoothed[i]);
                // Color shifts from gold → magenta with intensity.
                bar.color = Color.Lerp(new Color(0.45f, 0.65f, 1f, 0.9f),
                                       new Color(1f, 0.4f, 0.6f, 0.95f),
                                       _specSmoothed[i]);
            }

            // Beat dot row: light up the active beat in the bar.
            if (_beatDots != null)
            {
                int bpb = (_clock != null && _clock.IsActive) ? Mathf.Max(1, _clock.BeatsPerBar) : 4;
                bpb = Mathf.Min(bpb, _beatDots.Length);
                int active = (_clock != null && _clock.IsActive) ? _clock.CurrentBeatInBar : -1;
                float spacing = 1f / Mathf.Max(1, bpb);
                for (int i = 0; i < _beatDots.Length; i++)
                {
                    var dot = _beatDots[i];
                    if (dot == null) continue;
                    bool used = i < bpb;
                    dot.gameObject.SetActive(used);
                    if (!used) continue;
                    var dRt = dot.rectTransform;
                    dRt.anchorMin = new Vector2((i + 0.5f) * spacing, 0.5f);
                    dRt.anchorMax = dRt.anchorMin;
                    if (i == active && playing)
                    {
                        bool downbeat = (i == 0);
                        dot.color = downbeat ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(1f, 1f, 1f, 1f);
                        float pulse = 1f + (1f - (_clock != null ? _clock.BeatPhase01 : 0f)) * 0.5f;
                        dRt.localScale = new Vector3(pulse, pulse, 1f);
                    }
                    else
                    {
                        dot.color = (i == 0) ? new Color(1f, 0.85f, 0.3f, 0.45f) : new Color(1f, 1f, 1f, 0.30f);
                        dRt.localScale = Vector3.one;
                    }
                }
            }

            // Full-song waveform: rebuild on track change, update playhead every frame.
            UpdateWaveform(playing);
        }

        // ── Waveform overview (Ableton-style) ──────────────────────────────
        private void UpdateWaveform(bool playing)
        {
            if (_waveformImage == null || _audio == null) return;

            var clip = _audio.CurrentMusicClip;
            float bpm    = _audio.CurrentTrackBpm;
            float offset = _audio.CurrentTrackBeatOffsetSec;
            int   bpb    = Mathf.Max(1, _audio.CurrentTrackBeatsPerBar);

            bool needsRebuild = clip != null && (
                clip != _cachedWaveformClip ||
                !Mathf.Approximately(bpm,    _cachedWaveformBpm) ||
                !Mathf.Approximately(offset, _cachedWaveformOffset) ||
                bpb != _cachedWaveformBeatsPerBar);

            if (needsRebuild)
                RebuildWaveformTexture(clip, bpm, offset, bpb);

            // Move the playhead to the current playback position.
            if (clip != null && _waveformPlayhead != null)
            {
                float duration = clip.length;
                float t = _audio.CurrentMusicTime;
                float p = duration > 0f ? Mathf.Clamp01(t / duration) : 0f;
                _waveformPlayhead.anchorMin = new Vector2(p, 0f);
                _waveformPlayhead.anchorMax = new Vector2(p, 1f);
                _waveformPlayhead.gameObject.SetActive(true);

                // Progressive streaming-clip waveform: sample output, write column.
                if (playing && _waveformProgressive && _waveformWorkPixels != null && duration > 0f)
                    BakeProgressiveWaveformColumn(p);
            }
            else if (_waveformPlayhead != null)
            {
                _waveformPlayhead.gameObject.SetActive(false);
            }
        }

        private void BakeProgressiveWaveformColumn(float position01)
        {
            if (_waveformOutputBuf == null) _waveformOutputBuf = new float[256];
            if (!_audio.GetMusicOutputData(_waveformOutputBuf)) return;

            // Peak amplitude in this audio buffer.
            float peak = 0f;
            for (int i = 0; i < _waveformOutputBuf.Length; i++)
            {
                float a = _waveformOutputBuf[i];
                if (a < 0f) a = -a;
                if (a > peak) peak = a;
            }
            peak = Mathf.Clamp01(peak * 1.4f * _waveformAmplitude); // gentle gain + user zoom

            int col = Mathf.Clamp(Mathf.FloorToInt(position01 * WaveformTexW), 0, WaveformTexW - 1);
            if (_waveformColumnPeak == null) _waveformColumnPeak = new float[WaveformTexW];

            // Snap to the bar grid so the progressive output renders as discrete
            // bars (matching the bulk path style: 3 px bar + 1 px gap).
            const int BarW = 3;
            const int GapW = 1;
            int stride = BarW + GapW;
            int barStart = (col / stride) * stride;
            int barEnd   = Mathf.Min(barStart + BarW, WaveformTexW);

            // Accumulate (max) peak for this bar group.
            if (peak > _waveformColumnPeak[barStart]) _waveformColumnPeak[barStart] = peak;

            int midY = WaveformTexH / 2;
            float halfH = WaveformTexH * 0.45f;
            int span = Mathf.Max(1, (int)(_waveformColumnPeak[barStart] * halfH));
            var waveC = new Color32(220, 180, 90, 255);

            // Restore baseline + draw the symmetric peak across the whole bar width.
            int y0 = Mathf.Clamp(midY - span, 0, WaveformTexH - 1);
            int y1 = Mathf.Clamp(midY + span, 0, WaveformTexH - 1);
            for (int x = barStart; x < barEnd; x++)
            {
                for (int y = 0; y < WaveformTexH; y++)
                    _waveformWorkPixels[y * WaveformTexW + x] = _waveformGridPixels[y * WaveformTexW + x];
                for (int y = y0; y <= y1; y++)
                    _waveformWorkPixels[y * WaveformTexW + x] = waveC;
            }

            // Throttle GPU upload — apply once per ~33 ms or when column advances.
            _waveformDirtyTimer += Time.unscaledDeltaTime;
            if (col != _waveformLastCol || _waveformDirtyTimer >= 0.033f)
            {
                _waveformLastCol = col;
                _waveformDirtyTimer = 0f;
                _waveformTex.SetPixels32(_waveformWorkPixels);
                _waveformTex.Apply(false, false);
            }
        }

        private void RebuildWaveformTexture(AudioClip clip, float bpm, float offsetSec, int beatsPerBar)
        {
            if (clip == null) return;

            if (_waveformTex == null)
            {
                _waveformTex = new Texture2D(WaveformTexW, WaveformTexH, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }

            // Build the static baseline first: bg + center axis + bar/beat grid.
            var grid = new Color32[WaveformTexW * WaveformTexH];
            var bgC   = new Color32(8, 8, 16, 230);
            var axisC = new Color32(40, 40, 60, 200);
            for (int i = 0; i < grid.Length; i++) grid[i] = bgC;
            int midY = WaveformTexH / 2;
            for (int x = 0; x < WaveformTexW; x++) grid[midY * WaveformTexW + x] = axisC;

            float duration = clip.length;
            if (bpm > 0f && duration > 0f)
            {
                float spb = 60f / bpm;
                int beatCount = Mathf.Max(0, Mathf.FloorToInt((duration - offsetSec) / spb)) + 1;
                int safeBpb = Mathf.Max(1, beatsPerBar);
                // If beats are too dense (less than ~4 px apart), only draw downbeats.
                float pxPerBeat = WaveformTexW * (spb / duration);
                bool drawOffBeats = pxPerBeat >= 4f;
                var downbeatC = new Color32(255, 220, 80, 220);
                var beatC     = new Color32(140, 140, 180, 110);
                for (int b = 0; b < beatCount; b++)
                {
                    float ts = offsetSec + b * spb;
                    if (ts < 0f || ts > duration) continue;
                    int x = Mathf.Clamp((int)(ts / duration * WaveformTexW), 0, WaveformTexW - 1);
                    bool downbeat = (b % safeBpb) == 0;
                    if (!downbeat && !drawOffBeats) continue;
                    var c = downbeat ? downbeatC : beatC;
                    for (int y = 0; y < WaveformTexH; y++)
                    {
                        int i = y * WaveformTexW + x;
                        if (downbeat) grid[i] = c;
                        else
                        {
                            var prev = grid[i];
                            float ca = c.a / 255f;
                            byte r  = (byte)(prev.r * (1f - ca) + c.r * ca);
                            byte g2 = (byte)(prev.g * (1f - ca) + c.g * ca);
                            byte bl = (byte)(prev.b * (1f - ca) + c.b * ca);
                            grid[i] = new Color32(r, g2, bl, 255);
                        }
                    }
                }
            }

            _waveformGridPixels = grid;
            _waveformWorkPixels = new Color32[grid.Length];
            System.Array.Copy(grid, _waveformWorkPixels, grid.Length);
            if (_waveformColumnPeak == null || _waveformColumnPeak.Length != WaveformTexW)
                _waveformColumnPeak = new float[WaveformTexW];
            else
                System.Array.Clear(_waveformColumnPeak, 0, _waveformColumnPeak.Length);
            _waveformLastCol = -1;
            _waveformDirtyTimer = 0f;

            // Streaming clips can't use GetData → fall back to progressive sampling.
            // CompressedInMemory + DecompressOnLoad both support GetData.
            _waveformProgressive = clip.loadType == AudioClipLoadType.Streaming;

            if (!_waveformProgressive)
            {
                int channels = Mathf.Max(1, clip.channels);
                int totalPerChannel = clip.samples;
                float[] data = null;
                if (totalPerChannel > 0)
                {
                    try
                    {
                        data = new float[totalPerChannel * channels];
                        if (!clip.GetData(data, 0)) data = null;
                    }
                    catch
                    {
                        data = null;
                    }
                }

                if (data != null)
                {
                    var waveC = new Color32(220, 180, 90, 255);
                    float halfH = WaveformTexH * 0.45f;
                    float amp = _waveformAmplitude;
                    // Bar-style rendering (like the spectrum below): paint groups of
                    // BarW columns and skip GapW between them, so the waveform reads
                    // as discrete bars instead of a solid silhouette.
                    const int BarW = 3;
                    const int GapW = 1;
                    int stride = BarW + GapW;
                    for (int gx = 0; gx < WaveformTexW; gx += stride)
                    {
                        // Aggregate peak across the bar's pixel-column span.
                        long lo = (long)gx * totalPerChannel / WaveformTexW;
                        long hi = (long)Mathf.Min(gx + BarW, WaveformTexW) * totalPerChannel / WaveformTexW;
                        if (hi <= lo) hi = lo + 1;
                        float vMin = 0f, vMax = 0f;
                        for (long s = lo; s < hi; s++)
                        {
                            int idx = (int)s * channels;
                            float v = data[idx];
                            if (v < vMin) vMin = v;
                            if (v > vMax) vMax = v;
                        }
                        vMin = Mathf.Clamp(vMin * amp, -1f, 1f);
                        vMax = Mathf.Clamp(vMax * amp, -1f, 1f);
                        int y0 = Mathf.Clamp(midY + (int)(vMin * halfH), 0, WaveformTexH - 1);
                        int y1 = Mathf.Clamp(midY + (int)(vMax * halfH), 0, WaveformTexH - 1);
                        int xEnd = Mathf.Min(gx + BarW, WaveformTexW);
                        for (int x = gx; x < xEnd; x++)
                            for (int y = y0; y <= y1; y++)
                                _waveformWorkPixels[y * WaveformTexW + x] = waveC;
                    }
                }
            }

            _waveformTex.SetPixels32(_waveformWorkPixels);
            _waveformTex.Apply(false, false);
            if (_waveformSprite == null)
                _waveformSprite = Sprite.Create(_waveformTex, new Rect(0, 0, WaveformTexW, WaveformTexH),
                                                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _waveformImage.sprite = _waveformSprite;
            _waveformImage.SetMaterialDirty();

            _cachedWaveformClip        = clip;
            _cachedWaveformBpm         = bpm;
            _cachedWaveformOffset      = offsetSec;
            _cachedWaveformBeatsPerBar = beatsPerBar;
        }

        // Called by the ResizeHandle while the user drags.
        // Width and height are independent: the user can stretch the widget
        // freely on either axis without the other one following.
        internal void ApplySize(float w, float h)
        {
            float wn = Mathf.Clamp(w, minSize.x, maxSize.x);
            float hn = Mathf.Clamp(h, minSize.y, maxSize.y);

            // Hard clamp to the actual on-screen rectangle so the widget never
            // overflows the top/left of the screen (especially in expanded mode).
            // CRITICAL: widget sizes are in CANVAS units, but Screen.* / pixelRect
            // are in SCREEN pixels. Convert via canvas.scaleFactor so the clamp
            // is correct under any CanvasScaler setting.
            float screenW = Screen.width;
            float screenH = Screen.height;
            if (_canvas != null)
            {
                var pr = _canvas.pixelRect;
                if (pr.width  > 0f) screenW = pr.width;
                if (pr.height > 0f) screenH = pr.height;
            }
            float canvasScale = (_canvas != null && _canvas.scaleFactor > 0.0001f) ? _canvas.scaleFactor : 1f;
            // Available room for the widget expressed in CANVAS units.
            float availW = Mathf.Max(minSize.x, screenW / canvasScale - edgeInset * 2f);
            float availH = Mathf.Max(minSize.y, screenH / canvasScale - edgeInset * 2f - bottomLift);
            wn = Mathf.Min(wn, availW);
            hn = Mathf.Min(hn, availH);

            widgetWidth  = wn;
            widgetHeight = hn;
            ApplyScaleFromSize();
        }

        private void ApplyScaleFromSize()
        {
            if (_rt == null) return;
            // Independent X / Y stretch so the user can freely change aspect ratio.
            float sx = widgetWidth  / BaseW;
            float sy = widgetHeight / CurrentBaseH;
            _rt.localScale = new Vector3(sx, sy, 1f);
        }

        // Called by the ResizeHandle on EndDrag to persist the choice.
        internal void PersistSize()
        {
            // Always cache the size for the mode we're currently in, then write
            // the per-mode keys so the simple and expanded layouts stay independent.
            CacheSizeForCurrentMode();
            PlayerPrefs.SetFloat(PrefKeyWSimple,   _simpleW);
            PlayerPrefs.SetFloat(PrefKeyHSimple,   _simpleH);
            PlayerPrefs.SetFloat(PrefKeyWExpanded, _expandedW);
            PlayerPrefs.SetFloat(PrefKeyHExpanded, _expandedH);
            PlayerPrefs.Save();
        }

        internal Vector2 CurrentSize => new Vector2(widgetWidth, widgetHeight);

        /// <summary>Called by ProgressBarSeekHandler when the user clicks/drags the yellow bar.</summary>
        internal void SeekToFraction(float frac01)
        {
            if (_audio == null) return;
            var clip = _audio.CurrentMusicClip;
            if (clip == null) return;
            float t = Mathf.Clamp01(frac01) * clip.length;
            _audio.SeekMusic(t);
            // Update the visible fill immediately so the drag feels responsive.
            if (_progressFill != null) _progressFill.fillAmount = Mathf.Clamp01(frac01);
        }

        /// <summary>Called by WaveformZoomHandler when the user scrolls the wheel over the waveform.</summary>
        internal void AdjustWaveformAmplitude(float factor)
        {
            float prev = _waveformAmplitude;
            _waveformAmplitude = Mathf.Clamp(_waveformAmplitude * factor, 0.25f, 6f);
            if (Mathf.Approximately(prev, _waveformAmplitude)) return;
            PlayerPrefs.SetFloat(PrefKeyAmplitude, _waveformAmplitude);
            PlayerPrefs.Save();
            // Force a full waveform repaint with the new amplitude.
            // For bulk-mode (non-streaming) clips this re-bakes the static peaks.
            // For progressive (streaming) clips it scales subsequent peaks; existing
            // peaks are scaled in-place so the viewer reflects the new zoom now.
            if (_audio != null)
            {
                var clip = _audio.CurrentMusicClip;
                if (clip != null)
                {
                    if (_waveformProgressive)
                    {
                        // Rescale already-painted peak columns visually.
                        if (_waveformColumnPeak != null && _waveformWorkPixels != null && _waveformGridPixels != null)
                        {
                            int midY = WaveformTexH / 2;
                            float halfH = WaveformTexH * 0.45f;
                            float scaleK = _waveformAmplitude / Mathf.Max(0.0001f, prev);
                            var waveC = new Color32(220, 180, 90, 255);
                            const int BarW = 3;
                            const int GapW = 1;
                            int stride = BarW + GapW;
                            for (int gx = 0; gx < WaveformTexW; gx += stride)
                            {
                                _waveformColumnPeak[gx] = Mathf.Clamp01(_waveformColumnPeak[gx] * scaleK);
                                int span = Mathf.Max(0, (int)(_waveformColumnPeak[gx] * halfH));
                                int y0 = Mathf.Clamp(midY - span, 0, WaveformTexH - 1);
                                int y1 = Mathf.Clamp(midY + span, 0, WaveformTexH - 1);
                                int xEnd = Mathf.Min(gx + BarW, WaveformTexW);
                                for (int x = gx; x < xEnd; x++)
                                {
                                    for (int y = 0; y < WaveformTexH; y++)
                                        _waveformWorkPixels[y * WaveformTexW + x] = _waveformGridPixels[y * WaveformTexW + x];
                                    if (span > 0)
                                        for (int y = y0; y <= y1; y++)
                                            _waveformWorkPixels[y * WaveformTexW + x] = waveC;
                                }
                            }
                            _waveformTex.SetPixels32(_waveformWorkPixels);
                            _waveformTex.Apply(false, false);
                        }
                    }
                    else
                    {
                        // Force a clean rebuild for bulk clips.
                        _cachedWaveformClip = null;
                        UpdateWaveform(true);
                    }
                }
            }
        }

        // ── Tap-tempo / live tempo override ──────────────────────────────────
        // We store *music-time* timestamps (CurrentMusicTime) instead of wall-clock
        // time so the BPM estimate is robust against pause/seek and matches the
        // beat clock 1:1. Tap intervals are also folded over candidate beat-multiples
        // ({1, 1/2, 2, 1/3, 3, 1/4, 4}) so the user can tap on any subdivision
        // (eighths, half-notes…) and we still recover the song's true beat period.
        private readonly System.Collections.Generic.List<float> _tapTimes = new System.Collections.Generic.List<float>(16);
        private const float TapResetSec = 2.0f; // gap longer than this resets the buffer

        /// <summary>
        /// Called by <see cref="BeatDotsTapHandler"/> on each pointer-up that wasn't a drag.
        /// Builds a rolling music-time tap window, derives a robust BPM via the median
        /// inter-tap interval, snaps the latest tap to the nearest beat boundary so the
        /// downbeat counter stays continuous, persists the result per-track, and fires
        /// <see cref="MusicBeatClock.OverrideTempo"/> so the change applies to the rest
        /// of the song.
        /// </summary>
        internal void RegisterBeatTap()
        {
            if (_audio == null) return;
            if (_clock == null) _clock = MusicBeatClock.Instance;
            if (_clock == null) return;

            // Music time is the right reference: ignores pause/scale and matches the clock.
            float now = _audio.CurrentMusicTime;
            if (_tapTimes.Count > 0 && now - _tapTimes[_tapTimes.Count - 1] > TapResetSec)
                _tapTimes.Clear();
            _tapTimes.Add(now);
            const int MaxTaps = 12;
            if (_tapTimes.Count > MaxTaps) _tapTimes.RemoveRange(0, _tapTimes.Count - MaxTaps);
            if (_tapTimes.Count < 2) return;

            // Median inter-tap interval (robust to one mistimed tap).
            int n = _tapTimes.Count - 1;
            var ipi = new float[n];
            for (int i = 0; i < n; i++) ipi[i] = _tapTimes[i + 1] - _tapTimes[i];
            System.Array.Sort(ipi);
            float medianIpi = (n % 2 == 1) ? ipi[n / 2] : 0.5f * (ipi[n / 2 - 1] + ipi[n / 2]);
            if (medianIpi <= 0.05f) return; // impossibly fast taps

            // Try multiple beat-multiples and keep the BPM closest (in log space) to the
            // existing one — lets the user tap halves/quarters/eighths interchangeably.
            float[] mults = { 1f, 0.5f, 2f, 1f / 3f, 3f, 0.25f, 4f };
            float currentBpm = _clock.Bpm > 0f ? _clock.Bpm : 120f;
            float bestBpm = currentBpm;
            float bestErr = float.MaxValue;
            for (int i = 0; i < mults.Length; i++)
            {
                float secPerBeat = medianIpi * mults[i];
                float candidate  = 60f / secPerBeat;
                if (candidate < 40f || candidate > 240f) continue;
                float err = Mathf.Abs(Mathf.Log(candidate / currentBpm));
                if (err < bestErr) { bestErr = err; bestBpm = candidate; }
            }

            // Snap the latest tap to the nearest beat: choose offset so the rounded
            // beat index k = round((tap - prevOffset) / secPerBeat) lands exactly on
            // the tap. Keeps the bar/beat counter continuous instead of jumping.
            float secPerBeat2 = 60f / bestBpm;
            float prevOffset  = _clock.FirstBeatOffsetSec;
            float relTap      = now - prevOffset;
            int   k           = Mathf.Max(0, Mathf.RoundToInt(relTap / secPerBeat2));
            float newOffset   = now - k * secPerBeat2;
            while (newOffset < 0f)           newOffset += secPerBeat2;
            while (newOffset >= secPerBeat2) newOffset -= secPerBeat2;

            _clock.OverrideTempo(bestBpm, newOffset);

            // Only persist once we have a stable estimate (>=3 taps) so a single
            // accidental click doesn't permanently retune the track.
            if (_tapTimes.Count >= 3)
            {
                string trackId = _audio.CurrentTrackId;
                if (!string.IsNullOrEmpty(trackId))
                {
                    PlayerPrefs.SetFloat(string.Format(PrefKeyTempoBpmFmt,    trackId), bestBpm);
                    PlayerPrefs.SetFloat(string.Format(PrefKeyTempoOffsetFmt, trackId), newOffset);
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>
        /// Called by <see cref="BeatDotsTapHandler"/> while the user drags. Horizontal delta
        /// fine-tunes BPM (right = faster), vertical delta fine-tunes the first-beat offset.
        /// Persisted per-track so manual tweaks survive across plays.
        /// </summary>
        internal void AdjustTempoByDrag(Vector2 deltaPixels)
        {
            if (_clock == null) _clock = MusicBeatClock.Instance;
            if (_clock == null || _clock.Bpm <= 0f) return;
            float bpm = Mathf.Clamp(_clock.Bpm + deltaPixels.x * 0.2f, 40f, 240f);
            float off = Mathf.Max(0f, _clock.FirstBeatOffsetSec - deltaPixels.y * 0.005f);
            _clock.OverrideTempo(bpm, off);
            string trackId = _audio != null ? _audio.CurrentTrackId : null;
            if (!string.IsNullOrEmpty(trackId))
            {
                // PlayerPrefs writes are coalesced; one Save() per drag burst is enough,
                // but Unity's Save is cheap so we just write each tick.
                PlayerPrefs.SetFloat(string.Format(PrefKeyTempoBpmFmt,    trackId), bpm);
                PlayerPrefs.SetFloat(string.Format(PrefKeyTempoOffsetFmt, trackId), off);
            }
        }

        private void BuildVolumeSlider(float x, float y)
        {
            var sliderGo = NewChild("VolumeSlider", _simpleContent.transform);
            var slRt = sliderGo.GetComponent<RectTransform>();
            slRt.anchorMin = new Vector2(0f, 0f);
            slRt.anchorMax = new Vector2(1f, 0f);
            slRt.pivot     = new Vector2(0f, 0f);
            slRt.anchoredPosition = new Vector2(x, y);
            slRt.sizeDelta = new Vector2(-(x + 8f), 6f);
            _volumeSlider = sliderGo.AddComponent<Slider>();
            _volumeSlider.minValue = 0f;
            _volumeSlider.maxValue = 1f;
            _volumeSlider.value = 0.7f;
            _volumeSlider.transition = Selectable.Transition.None;

            var sliderBg = NewChild("Bg", sliderGo.transform);
            var sBgRt = sliderBg.GetComponent<RectTransform>();
            sBgRt.anchorMin = Vector2.zero; sBgRt.anchorMax = Vector2.one;
            sBgRt.sizeDelta = Vector2.zero; sBgRt.anchoredPosition = Vector2.zero;
            var sBgImg = sliderBg.AddComponent<Image>();
            sBgImg.sprite = BuildRoundedRectSprite();
            sBgImg.type = Image.Type.Sliced;
            sBgImg.color = new Color(1f, 1f, 1f, 0.18f);

            var fillArea = NewChild("FillArea", sliderGo.transform);
            var faRt = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
            faRt.sizeDelta = Vector2.zero; faRt.anchoredPosition = Vector2.zero;
            var sFill = NewChild("Fill", fillArea.transform);
            var sfRt = sFill.GetComponent<RectTransform>();
            sfRt.anchorMin = Vector2.zero; sfRt.anchorMax = Vector2.one;
            sfRt.sizeDelta = Vector2.zero; sfRt.anchoredPosition = Vector2.zero;
            var sFillImg = sFill.AddComponent<Image>();
            sFillImg.sprite = BuildRoundedRectSprite();
            sFillImg.type = Image.Type.Sliced;
            sFillImg.color = new Color(0.95f, 0.78f, 0.25f, 0.95f);
            _volumeSlider.fillRect = sfRt;

            var handleArea = NewChild("HandleArea", sliderGo.transform);
            var haRt = handleArea.GetComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
            haRt.sizeDelta = Vector2.zero; haRt.anchoredPosition = Vector2.zero;
            var sHandle = NewChild("Handle", handleArea.transform);
            var sHRt = sHandle.GetComponent<RectTransform>();
            sHRt.sizeDelta = new Vector2(10f, 10f);
            sHRt.anchorMin = new Vector2(0.5f, 0.5f);
            sHRt.anchorMax = new Vector2(0.5f, 0.5f);
            var sHImg = sHandle.AddComponent<Image>();
            sHImg.color = new Color(1f, 0.97f, 0.85f, 1f);
            sHImg.sprite = BuildCircleSprite();
            _volumeSlider.handleRect = sHRt;
            _volumeSlider.targetGraphic = sHImg;
            _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        }

        private Button BuildIconButton(string label, Sprite icon, float x, float y,
                                       UnityEngine.Events.UnityAction onClick,
                                       out Image iconImg, Transform parent = null)
        {
            var go = NewChild(label, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot     = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(26f, 22f);

            // Rounded background — gives the button a polished, modern look.
            var bgImg = go.AddComponent<Image>();
            bgImg.sprite = BuildRoundedRectSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(1f, 1f, 1f, 0.10f);

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(1f, 0.95f, 0.55f, 1f);
            colors.pressedColor     = new Color(0.95f, 0.78f, 0.25f, 1f);
            colors.selectedColor    = new Color(1f, 1f, 1f, 1f);
            colors.disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            colors.colorMultiplier  = 1f;
            colors.fadeDuration     = 0.10f;
            btn.colors = colors;
            btn.targetGraphic = bgImg;
            btn.onClick.AddListener(onClick);

            // Icon child centered inside the button.
            var iconGo = NewChild(label + "Icon", go.transform);
            var ir = iconGo.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0.5f, 0.5f);
            ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.pivot     = new Vector2(0.5f, 0.5f);
            ir.anchoredPosition = Vector2.zero;
            ir.sizeDelta = new Vector2(14f, 14f);
            iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.color = new Color(1f, 1f, 1f, 0.95f);
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;

            return btn;
        }

        private GameObject NewChild(string label, Transform parent = null)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private TextMeshProUGUI NewLabel(string label, int size, FontStyles style, Color color, Transform parent = null)
        {
            var go = NewChild(label, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        // 32×32 white circle sprite (cached, used by metronome dot + slider handle).
        private static Sprite _circleCache;
        private static Sprite _solidCache;
        private static Sprite BuildSolidSprite()
        {
            if (_solidCache != null) return _solidCache;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            _solidCache = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _solidCache;
        }
        private static Sprite BuildCircleSprite()
        {
            if (_circleCache != null) return _circleCache;
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[N * N];
            float r = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d);
                pixels[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _circleCache = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return _circleCache;
        }

        // ── Procedural icon sprites ─────────────────────────────────────────
        // Cached per-icon. Each is rendered into a small RGBA32 texture with
        // antialiased edges so the icons look crisp at any scale.
        private static Sprite _sPlay, _sPause, _sPrev, _sNext, _sSpeaker, _sSpeakerMute, _sRoundRect, _sChevUp, _sChevDown;

        private static Sprite SpritePlay        { get { if (_sPlay        == null) _sPlay        = BuildPlaySprite();        return _sPlay; } }
        private static Sprite SpritePause       { get { if (_sPause       == null) _sPause       = BuildPauseSprite();       return _sPause; } }
        private static Sprite SpritePrev        { get { if (_sPrev        == null) _sPrev        = BuildPrevNextSprite(true);  return _sPrev; } }
        private static Sprite SpriteNext        { get { if (_sNext        == null) _sNext        = BuildPrevNextSprite(false); return _sNext; } }
        private static Sprite SpriteSpeaker     { get { if (_sSpeaker     == null) _sSpeaker     = BuildSpeakerSprite(false); return _sSpeaker; } }
        private static Sprite SpriteSpeakerMute { get { if (_sSpeakerMute == null) _sSpeakerMute = BuildSpeakerSprite(true);  return _sSpeakerMute; } }
        private static Sprite SpriteChevronUp   { get { if (_sChevUp      == null) _sChevUp      = BuildChevronSprite(true);  return _sChevUp; } }
        private static Sprite SpriteChevronDown { get { if (_sChevDown    == null) _sChevDown    = BuildChevronSprite(false); return _sChevDown; } }
        private static Sprite _sMinus;
        private static Sprite SpriteMinus       { get { if (_sMinus       == null) _sMinus       = BuildMinusSprite();        return _sMinus; } }

        private const int IcoN = 32;

        private static Sprite BuildRoundedRectSprite()
        {
            if (_sRoundRect != null) return _sRoundRect;
            const int N = 16;
            const int R = 4;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x < R ? R - x : (x >= N - R ? x - (N - R - 1) : 0);
                float dy = y < R ? R - y : (y >= N - R ? y - (N - R - 1) : 0);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(R - d);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            // Border = R so Image.Type.Sliced preserves the rounded corners at any size.
            _sRoundRect = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f),
                                        100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
            return _sRoundRect;
        }

        private static Sprite BuildPlaySprite()
        {
            var px = NewIconBuffer();
            // Right-pointing triangle, slightly inset.
            FillTriangleAA(px, IcoN, new Vector2(8f, 5f), new Vector2(8f, IcoN - 5f), new Vector2(IcoN - 6f, IcoN * 0.5f));
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildPauseSprite()
        {
            var px = NewIconBuffer();
            FillRect(px, IcoN, 8, 6, 5, IcoN - 12);
            FillRect(px, IcoN, 8, 6, IcoN - 13, IcoN - 12);
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildPrevNextSprite(bool prev)
        {
            var px = NewIconBuffer();
            if (prev)
            {
                FillRect(px, IcoN, 3, 6, 5, IcoN - 10);
                // Left triangle
                FillTriangleAA(px, IcoN, new Vector2(IcoN - 6f, 5f), new Vector2(IcoN - 6f, IcoN - 5f), new Vector2(8f, IcoN * 0.5f));
            }
            else
            {
                FillRect(px, IcoN, 3, 6, IcoN - 8, IcoN - 10);
                FillTriangleAA(px, IcoN, new Vector2(6f, 5f), new Vector2(6f, IcoN - 5f), new Vector2(IcoN - 8f, IcoN * 0.5f));
            }
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildSpeakerSprite(bool muted)
        {
            var px = NewIconBuffer();
            // Speaker body (small box on left + horn triangle)
            FillRect(px, IcoN, 7, 6, 12, 7);                                    // body
            FillTriangleAA(px, IcoN, new Vector2(7f, 16f), new Vector2(20f, 6f), new Vector2(20f, IcoN - 6f)); // horn
            if (!muted)
            {
                // Two sound arcs on the right.
                DrawArc(px, IcoN, new Vector2(20f, 16f), 5f, 6f, -45f, 45f);
                DrawArc(px, IcoN, new Vector2(20f, 16f), 9f, 10f, -45f, 45f);
            }
            else
            {
                // Diagonal cross on the right.
                DrawLineAA(px, IcoN, new Vector2(22f, 9f),  new Vector2(IcoN - 4f, IcoN - 9f), 1.4f);
                DrawLineAA(px, IcoN, new Vector2(IcoN - 4f, 9f), new Vector2(22f, IcoN - 9f), 1.4f);
            }
            return SpriteFromBuffer(px);
        }

        private static Sprite BuildChevronSprite(bool pointUp)
        {
            // A double chevron (two stacked V shapes) so it reads as "expand" / "collapse".
            var px = NewIconBuffer();
            float cx = IcoN * 0.5f;
            float w = 8f;     // half-width of the chevron arms
            float t = 1.6f;   // line thickness
            // y positions for the two stacked Vs (one above the other)
            float y1 = pointUp ? 11f : 21f;
            float y2 = pointUp ? 19f : 13f;
            float dy = pointUp ? 5f  : -5f; // arm tip drops downward in down-chevron
            // First V
            DrawLineAA(px, IcoN, new Vector2(cx - w, y1 + dy), new Vector2(cx, y1), t);
            DrawLineAA(px, IcoN, new Vector2(cx,     y1),       new Vector2(cx + w, y1 + dy), t);
            // Second V
            DrawLineAA(px, IcoN, new Vector2(cx - w, y2 + dy), new Vector2(cx, y2), t);
            DrawLineAA(px, IcoN, new Vector2(cx,     y2),       new Vector2(cx + w, y2 + dy), t);
            return SpriteFromBuffer(px);
        }

        // A simple horizontal bar — universal "minimize / hide" affordance.
        private static Sprite BuildMinusSprite()
        {
            var px = NewIconBuffer();
            float cy = IcoN * 0.5f;
            DrawLineAA(px, IcoN, new Vector2(7f, cy), new Vector2(IcoN - 7f, cy), 2.4f);
            return SpriteFromBuffer(px);
        }

        // ── Pixel buffer helpers ────────────────────────────────────────────
        private static Color32[] NewIconBuffer() => new Color32[IcoN * IcoN];

        private static Sprite SpriteFromBuffer(Color32[] px)
        {
            var tex = new Texture2D(IcoN, IcoN, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, IcoN, IcoN), new Vector2(0.5f, 0.5f));
        }

        private static void FillRect(Color32[] px, int N, int w, int h, int x0, int y0)
        {
            for (int y = y0; y < y0 + h && y < N; y++)
            for (int x = x0; x < x0 + w && x < N; x++)
                if (x >= 0 && y >= 0) px[y * N + x] = new Color32(255, 255, 255, 255);
        }

        private static void FillTriangleAA(Color32[] px, int N, Vector2 a, Vector2 b, Vector2 c)
        {
            float minX = Mathf.Floor(Mathf.Min(a.x, b.x, c.x)) - 1f;
            float maxX = Mathf.Ceil (Mathf.Max(a.x, b.x, c.x)) + 1f;
            float minY = Mathf.Floor(Mathf.Min(a.y, b.y, c.y)) - 1f;
            float maxY = Mathf.Ceil (Mathf.Max(a.y, b.y, c.y)) + 1f;
            for (int y = (int)Mathf.Max(0, minY); y <= (int)Mathf.Min(N - 1, maxY); y++)
            for (int x = (int)Mathf.Max(0, minX); x <= (int)Mathf.Min(N - 1, maxX); x++)
            {
                // Compute signed distance to triangle (approx via barycentric).
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (PointInTriangle(p, a, b, c))
                    px[y * N + x] = new Color32(255, 255, 255, 255);
            }
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = Cross(p - a, b - a);
            float s2 = Cross(p - b, c - b);
            float s3 = Cross(p - c, a - c);
            return (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static void DrawLineAA(Color32[] px, int N, Vector2 a, Vector2 b, float thickness)
        {
            int minX = Mathf.Max(0, (int)(Mathf.Min(a.x, b.x) - thickness - 1));
            int maxX = Mathf.Min(N - 1, (int)(Mathf.Max(a.x, b.x) + thickness + 1));
            int minY = Mathf.Max(0, (int)(Mathf.Min(a.y, b.y) - thickness - 1));
            int maxY = Mathf.Min(N - 1, (int)(Mathf.Max(a.y, b.y) + thickness + 1));
            Vector2 ab = b - a; float ablen2 = ab.sqrMagnitude;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ablen2));
                Vector2 q = a + ab * t;
                float d = Vector2.Distance(p, q);
                float aA = Mathf.Clamp01(thickness - d);
                if (aA > 0)
                {
                    var prev = px[y * N + x];
                    byte na = (byte)Mathf.Max(prev.a, aA * 255);
                    px[y * N + x] = new Color32(255, 255, 255, na);
                }
            }
        }

        private static void DrawArc(Color32[] px, int N, Vector2 c, float rIn, float rOut, float angMinDeg, float angMaxDeg)
        {
            int minX = Mathf.Max(0, (int)(c.x - rOut - 1));
            int maxX = Mathf.Min(N - 1, (int)(c.x + rOut + 1));
            int minY = Mathf.Max(0, (int)(c.y - rOut - 1));
            int maxY = Mathf.Min(N - 1, (int)(c.y + rOut + 1));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - c.x, dy = y + 0.5f - c.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < rIn || d > rOut) continue;
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (ang < angMinDeg || ang > angMaxDeg) continue;
                float a = Mathf.Min(d - rIn, rOut - d);
                a = Mathf.Clamp01(a);
                var prev = px[y * N + x];
                byte na = (byte)Mathf.Max(prev.a, a * 255);
                px[y * N + x] = new Color32(255, 255, 255, na);
            }
        }
    }

    /// <summary>
    /// Drag handler for the top-left resize grip of <see cref="MusicPlayerHUD"/>.
    /// Because the widget is anchored bottom-right (pivot 1,0), moving the
    /// pointer left/up grows the widget; right/down shrinks it.
    /// </summary>
    internal sealed class ResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private MusicPlayerHUD _owner;
        private Vector2 _startSize;
        private Vector2 _startPointer;

        public void Init(MusicPlayerHUD owner) { _owner = owner; }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_owner == null) return;
            _startSize = _owner.CurrentSize;
            _startPointer = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_owner == null) return;
            float scale = 1f;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.0001f) scale = canvas.scaleFactor;

            Vector2 delta = (e.position - _startPointer) / scale;
            // Bottom-right anchored: drag LEFT (negative x) → wider; UP (positive y) → taller.
            float newW = _startSize.x - delta.x;
            float newH = _startSize.y + delta.y;
            _owner.ApplySize(newW, newH);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_owner != null) _owner.PersistSize();
        }
    }

    /// <summary>
    /// Click + drag the progress bar to seek the music. Uses the bar's RectTransform
    /// to convert pointer X into a 0..1 fraction, then asks the HUD to seek.
    /// </summary>
    internal sealed class ProgressBarSeekHandler : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private MusicPlayerHUD _owner;
        private RectTransform _rt;

        public void Init(MusicPlayerHUD owner, RectTransform rt) { _owner = owner; _rt = rt; }

        public void OnPointerDown(PointerEventData e) => Seek(e);
        public void OnDrag(PointerEventData e)        => Seek(e);
        public void OnPointerUp(PointerEventData e)   => Seek(e);

        private void Seek(PointerEventData e)
        {
            if (_owner == null || _rt == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var local))
                return;
            // Bar is left-pivot (0,1) with full width; local.x is 0..rect.width.
            float w = _rt.rect.width;
            if (w <= 0f) return;
            float frac = Mathf.Clamp01(local.x / w);
            _owner.SeekToFraction(frac);
        }
    }

    /// <summary>
    /// Mouse-wheel zoom on the waveform area. Each scroll step multiplies the
    /// vertical amplitude by ~1.2 (or 1/1.2 down). Persisted via PlayerPrefs.
    /// </summary>
    internal sealed class WaveformZoomHandler : MonoBehaviour, IScrollHandler
    {
        private MusicPlayerHUD _owner;
        public void Init(MusicPlayerHUD owner) { _owner = owner; }
        public void OnScroll(PointerEventData e)
        {
            if (_owner == null) return;
            float dy = e.scrollDelta.y;
            if (Mathf.Abs(dy) < 0.001f) return;
            float factor = dy > 0f ? 1.2f : (1f / 1.2f);
            _owner.AdjustWaveformAmplitude(factor);
        }
    }

    /// <summary>
    /// Beat-dots strip input handler. Distinguishes:
    ///  • Click (no drag) → tap-tempo: each click contributes to a rolling BPM estimate.
    ///  • Drag             → live tempo nudge: horizontal = BPM, vertical = first-beat offset.
    /// </summary>
    internal sealed class BeatDotsTapHandler : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler
    {
        private MusicPlayerHUD _owner;
        private bool _isDragging;
        private const float DragThresholdPx = 4f;
        private Vector2 _downPos;

        public void Init(MusicPlayerHUD owner) { _owner = owner; }

        public void OnPointerDown(PointerEventData e)
        {
            _isDragging = false;
            _downPos = e.position;
        }

        public void OnPointerUp(PointerEventData e)
        {
            // Only count as a tap if the pointer didn't move enough to be a drag.
            if (_isDragging) { _isDragging = false; return; }
            if ((e.position - _downPos).sqrMagnitude > DragThresholdPx * DragThresholdPx) return;
            _owner?.RegisterBeatTap();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _isDragging = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_owner == null) return;
            float scale = 1f;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.0001f) scale = canvas.scaleFactor;
            Vector2 deltaPx = e.delta / scale;
            _owner.AdjustTempoByDrag(deltaPx);
        }
    }
}
