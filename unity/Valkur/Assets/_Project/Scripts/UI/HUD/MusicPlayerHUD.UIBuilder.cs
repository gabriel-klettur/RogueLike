using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
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
            ApplyRootAnchorPosition();
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

            BuildHeaderBar();

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
            dotRt.anchoredPosition = new Vector2(20f, -(HeaderH + 2f));
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
            titleRt.anchoredPosition = new Vector2(34f, -(HeaderH + 2f));
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
            metaRt.anchoredPosition = new Vector2(34f, -(HeaderH + 20f));
            metaRt.sizeDelta = new Vector2(180f, 12f);
            _meta.alignment = TextAlignmentOptions.Left;
            _meta.text = "no tempo";

            // Beat counter (top-right)
            _beatCounter = NewLabel("BeatCounter", 10, FontStyles.Bold, new Color(1f, 1f, 1f, 0.95f), _simpleContent.transform);
            var bcRt = _beatCounter.rectTransform;
            bcRt.anchorMin = new Vector2(1f, 1f);
            bcRt.anchorMax = new Vector2(1f, 1f);
            bcRt.pivot     = new Vector2(1f, 1f);
            bcRt.anchoredPosition = new Vector2(-8f, -(HeaderH + 2f));
            bcRt.sizeDelta = new Vector2(150f, 14f);
            _beatCounter.alignment = TextAlignmentOptions.Right;
            _beatCounter.text = "—";

            // Time label
            _timeLabel = NewLabel("Time", 10, FontStyles.Normal, new Color(0.85f, 0.85f, 0.85f, 0.85f), _simpleContent.transform);
            var tlRt = _timeLabel.rectTransform;
            tlRt.anchorMin = new Vector2(1f, 1f);
            tlRt.anchorMax = new Vector2(1f, 1f);
            tlRt.pivot     = new Vector2(1f, 1f);
            tlRt.anchoredPosition = new Vector2(-8f, -(HeaderH + 20f));
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
            barBgRt.anchoredPosition = new Vector2(8f, -(HeaderH + 24f));
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
            // Close button (top-right corner) — hides the panel; the HUDIconBar
            // music icon brings it back.
            BuildCloseButton();

            // Apply initial expand state (built collapsed by default; toggle if persisted).
            ApplyExpandedState();
            // Apply panel visibility so the CanvasGroup matches the persisted state
            // on the very first frame.
            ApplyPanelVisibility();

            // Re-clamp to current screen so a previously-saved size that no longer
            // fits the resolution is shrunk back into view on first show.
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            ApplySize(widgetWidth, widgetHeight);
        }

        private void BuildResizeHandle()
        {
            var go = NewChild("ResizeHandle", _headerBar != null ? _headerBar.transform : transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot     = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(4f, 0f);
            rt.sizeDelta = new Vector2(16f, 16f);

            // Subtle header-tile background so it reads as a menu control.
            var hit = go.AddComponent<Image>();
            hit.sprite = BuildRoundedRectSprite();
            hit.type = Image.Type.Sliced;
            hit.color = new Color(1f, 1f, 1f, 0.10f);
            hit.raycastTarget = true;

            // Triangle glyph on top.
            var glyph = NewLabel("Glyph", 14, FontStyles.Bold, new Color(1f, 0.95f, 0.85f, 0.9f));
            glyph.transform.SetParent(go.transform, false);
            var gr = glyph.rectTransform;
            gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one;
            gr.sizeDelta = Vector2.zero; gr.anchoredPosition = Vector2.zero;
            glyph.alignment = TextAlignmentOptions.Center;
            glyph.text = "/";
            glyph.raycastTarget = false;

            var handler = go.AddComponent<ResizeHandle>();
            handler.Init(this);
            _resizeHandle = go;
        }

        // ── Close button ────────────────────────────────────────────────────
        // Top-right corner of the player. Click → hide the whole panel.
        // Re-opens via the persistent HUDIconBar music icon.
        private void BuildCloseButton()
        {
            var go = NewChild("CloseBtn", _headerBar != null ? _headerBar.transform : transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot     = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-4f, 0f);
            rt.sizeDelta = new Vector2(CloseBtnSize, CloseBtnSize);
            _closeBtnRt = rt;

            var bgImg = go.AddComponent<Image>();
            bgImg.sprite = BuildRoundedRectSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(1f, 1f, 1f, 0.10f);
            _closeBg = bgImg;

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
            btn.onClick.AddListener(OnCloseClicked);
            _closeBtn = btn;

            var iconGo = NewChild("CloseIcon", go.transform);
            var ir = iconGo.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0.5f, 0.5f);
            ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.pivot     = new Vector2(0.5f, 0.5f);
            ir.anchoredPosition = Vector2.zero;
            ir.sizeDelta = new Vector2(CloseIconSize, CloseIconSize);
            _closeIcon = iconGo.AddComponent<Image>();
            _closeIcon.raycastTarget = false;
            _closeIcon.preserveAspect = true;
            _closeIcon.sprite = SpriteMinus;
            _closeIcon.color  = new Color(1f, 1f, 1f, 0.95f);
        }

        private void BuildHeaderBar()
        {
            _headerBar = NewChild("HeaderBar");
            var rt = _headerBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, HeaderH);

            _headerBg = _headerBar.AddComponent<Image>();
            _headerBg.sprite = BuildRoundedRectSprite();
            _headerBg.type = Image.Type.Sliced;
            _headerBg.color = new Color(1f, 1f, 1f, 0.06f);

            var divider = NewChild("HeaderDivider", _headerBar.transform);
            var dividerRt = divider.GetComponent<RectTransform>();
            dividerRt.anchorMin = new Vector2(0f, 0f);
            dividerRt.anchorMax = new Vector2(1f, 0f);
            dividerRt.pivot = new Vector2(0.5f, 0f);
            dividerRt.anchoredPosition = Vector2.zero;
            dividerRt.sizeDelta = new Vector2(-8f, 1f);
            var dividerImg = divider.AddComponent<Image>();
            dividerImg.sprite = BuildSolidSprite();
            dividerImg.color = new Color(1f, 1f, 1f, 0.10f);
            dividerImg.raycastTarget = false;
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
    }
}
