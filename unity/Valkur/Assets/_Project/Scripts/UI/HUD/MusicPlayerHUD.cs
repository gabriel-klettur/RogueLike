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
        private Vector2 maxSize = new Vector2(720f, 320f);

        // Reference design size used as the basis for uniform scaling.
        // Everything (fonts, buttons, slider, icons) is laid out for this size
        // and then scaled via RectTransform.localScale so the look stays proportional.
        private const float BaseW = 320f;
        private const float BaseH = 78f;
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

        private IAudioService _audio;
        private MusicBeatClock _clock;
        private float _flashTimer;
        private float _volumeBeforeMute = 0.7f;

        // ── Lifecycle ───────────────────────────────────────────────────────
        private const string PrefKeyW = "valkur.musichud.width";
        private const string PrefKeyH = "valkur.musichud.height";

        private void Awake()
        {
            // Restore last user-chosen size before building UI.
            if (PlayerPrefs.HasKey(PrefKeyW)) widgetWidth  = PlayerPrefs.GetFloat(PrefKeyW);
            if (PlayerPrefs.HasKey(PrefKeyH)) widgetHeight = PlayerPrefs.GetFloat(PrefKeyH);
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
                if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(_audio.MusicVolume);
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
            }
        }

        private void OnVolumeChanged(float v) { _audio?.SetMusicVolume(v); }

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
            _rt.sizeDelta = new Vector2(BaseW, BaseH);
            ApplyScaleFromSize();

            _bg = gameObject.AddComponent<Image>();
            _bg.color = new Color(0.06f, 0.06f, 0.08f, 0.85f);

            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = hideWhenIdle ? 0f : 1f; // start visible by default
            _cg.blocksRaycasts = true;
            _cg.interactable = true;

            // Metronome dot
            var dotGo = NewChild("Metronome");
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
            _title = NewLabel("Title", 14, FontStyles.Bold, new Color(1f, 0.95f, 0.85f));
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
            _meta = NewLabel("Meta", 10, FontStyles.Normal, new Color(0.75f, 0.85f, 1f, 0.95f));
            var metaRt = _meta.rectTransform;
            metaRt.anchorMin = new Vector2(0f, 1f);
            metaRt.anchorMax = new Vector2(0f, 1f);
            metaRt.pivot     = new Vector2(0f, 1f);
            metaRt.anchoredPosition = new Vector2(34f, -22f);
            metaRt.sizeDelta = new Vector2(180f, 12f);
            _meta.alignment = TextAlignmentOptions.Left;
            _meta.text = "no tempo";

            // Beat counter (top-right)
            _beatCounter = NewLabel("BeatCounter", 10, FontStyles.Bold, new Color(1f, 1f, 1f, 0.95f));
            var bcRt = _beatCounter.rectTransform;
            bcRt.anchorMin = new Vector2(1f, 1f);
            bcRt.anchorMax = new Vector2(1f, 1f);
            bcRt.pivot     = new Vector2(1f, 1f);
            bcRt.anchoredPosition = new Vector2(-8f, -4f);
            bcRt.sizeDelta = new Vector2(150f, 14f);
            _beatCounter.alignment = TextAlignmentOptions.Right;
            _beatCounter.text = "—";

            // Time label
            _timeLabel = NewLabel("Time", 10, FontStyles.Normal, new Color(0.85f, 0.85f, 0.85f, 0.85f));
            var tlRt = _timeLabel.rectTransform;
            tlRt.anchorMin = new Vector2(1f, 1f);
            tlRt.anchorMax = new Vector2(1f, 1f);
            tlRt.pivot     = new Vector2(1f, 1f);
            tlRt.anchoredPosition = new Vector2(-8f, -22f);
            tlRt.sizeDelta = new Vector2(120f, 12f);
            _timeLabel.alignment = TextAlignmentOptions.Right;
            _timeLabel.text = "0:00 / 0:00";

            // Progress bar (just under meta line, just above buttons; no dead space)
            var barBgGo = NewChild("ProgressBg");
            var barBgRt = barBgGo.GetComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0f, 1f);
            barBgRt.anchorMax = new Vector2(1f, 1f);
            barBgRt.pivot     = new Vector2(0f, 1f);
            barBgRt.anchoredPosition = new Vector2(8f, -40f);
            barBgRt.sizeDelta = new Vector2(-16f, 3f);
            var bgImg = barBgGo.AddComponent<Image>();
            bgImg.sprite = BuildSolidSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(1f, 1f, 1f, 0.18f);
            bgImg.raycastTarget = false;

            var fillGo = NewChild("ProgressFill", barBgGo.transform);
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
            _prevBtn = BuildIconButton("PrevBtn", SpritePrev, 8f,  btnY, OnPrevClicked, out _);
            _playBtn = BuildIconButton("PlayBtn", SpritePlay, 36f, btnY, OnPlayClicked, out _playIcon);
            _nextBtn = BuildIconButton("NextBtn", SpriteNext, 64f, btnY, OnNextClicked, out _);
            _muteBtn = BuildIconButton("MuteBtn", SpriteSpeaker, 100f, btnY, OnMuteClicked, out _muteIcon);

            // Volume slider (right of buttons)
            BuildVolumeSlider(132f, btnY + 5f);

            // Resize grip (top-left corner) — drag to resize
            BuildResizeHandle();
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
        }

        // Called by the ResizeHandle while the user drags.
        internal void ApplySize(float w, float h)
        {
            // Use the larger of the two normalized deltas as a uniform scale so
            // the user can drag in any direction and the widget grows proportionally.
            float wn = Mathf.Clamp(w, minSize.x, maxSize.x);
            float hn = Mathf.Clamp(h, minSize.y, maxSize.y);
            float scale = Mathf.Max(wn / BaseW, hn / BaseH);
            // Final clamped scale derived from the global min/max bounds.
            float minScale = Mathf.Max(minSize.x / BaseW, minSize.y / BaseH);
            float maxScale = Mathf.Min(maxSize.x / BaseW, maxSize.y / BaseH);
            scale = Mathf.Clamp(scale, minScale, maxScale);

            widgetWidth  = BaseW * scale;
            widgetHeight = BaseH * scale;
            ApplyScaleFromSize();
        }

        private void ApplyScaleFromSize()
        {
            if (_rt == null) return;
            float scale = widgetWidth / BaseW;
            _rt.localScale = new Vector3(scale, scale, 1f);
        }

        // Called by the ResizeHandle on EndDrag to persist the choice.
        internal void PersistSize()
        {
            PlayerPrefs.SetFloat(PrefKeyW, widgetWidth);
            PlayerPrefs.SetFloat(PrefKeyH, widgetHeight);
            PlayerPrefs.Save();
        }

        internal Vector2 CurrentSize => new Vector2(widgetWidth, widgetHeight);

        private void BuildVolumeSlider(float x, float y)
        {
            var sliderGo = NewChild("VolumeSlider");
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
                                       out Image iconImg)
        {
            var go = NewChild(label);
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

        private TextMeshProUGUI NewLabel(string label, int size, FontStyles style, Color color)
        {
            var go = NewChild(label);
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
        private static Sprite _sPlay, _sPause, _sPrev, _sNext, _sSpeaker, _sSpeakerMute, _sRoundRect;

        private static Sprite SpritePlay        { get { if (_sPlay        == null) _sPlay        = BuildPlaySprite();        return _sPlay; } }
        private static Sprite SpritePause       { get { if (_sPause       == null) _sPause       = BuildPauseSprite();       return _sPause; } }
        private static Sprite SpritePrev        { get { if (_sPrev        == null) _sPrev        = BuildPrevNextSprite(true);  return _sPrev; } }
        private static Sprite SpriteNext        { get { if (_sNext        == null) _sNext        = BuildPrevNextSprite(false); return _sNext; } }
        private static Sprite SpriteSpeaker     { get { if (_sSpeaker     == null) _sSpeaker     = BuildSpeakerSprite(false); return _sSpeaker; } }
        private static Sprite SpriteSpeakerMute { get { if (_sSpeakerMute == null) _sSpeakerMute = BuildSpeakerSprite(true);  return _sSpeakerMute; } }

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
}
