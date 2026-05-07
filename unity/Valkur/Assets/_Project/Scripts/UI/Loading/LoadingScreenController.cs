using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// Realistic two-phase loading screen:
    ///   Phase 1 (0 % â†’ 40 %) : Unity SceneManager.LoadSceneAsync
    ///   Phase 2 (40 % â†’ 100%) : GameplaySceneSetup stage-by-stage callbacks
    ///
    /// Visual improvements:
    ///   - Smooth lerped progress (no jumps)
    ///   - Image.Type.Filled bar (correct at 0 %)
    ///   - Percentage label next to the bar
    ///   - Animated trailing dots on status text
    ///   - Canvas fade-out before destroy
    ///   - Guaranteed minimum display time (1.5 s)
    ///
    /// Usage: LoadingScreenController.Show("MainGameplay")
    /// GameplaySceneSetup calls ReportStage() / ReportGameplayReady().
    /// </summary>
    public class LoadingScreenController : MonoBehaviour
    {
        // â”€â”€ Layout constants (match Python LoadingScreen) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private const float BAR_WIDTH_RATIO  = 0.60f;
        private const float BAR_HEIGHT_PX    = 30f;
        private const float BAR_Y_RATIO      = 0.80f;
        private const float BAR_BORDER       = 2f;
        private const float BAR_PADDING      = 3f;
        private const float TEXT_OFFSET_Y    = 20f;

        // â”€â”€ Polish constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private const float LERP_SPEED       = 3.5f;
        private const float MIN_DISPLAY_TIME = 1.5f;
        private const float FADE_DURATION    = 0.45f;
        private const float DOTS_INTERVAL    = 0.4f;
        private const float TIP_INTERVAL     = 4.5f;
        private const int   FEED_CAPACITY    = 3;

        // â”€â”€ Colors â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static readonly Color BarBorderColor = Color.white;
        private static readonly Color BarFillColor   = new Color(0f, 200f / 255f, 0f, 1f);
        private static readonly Color TextColor      = Color.white;
        private static readonly Color FallbackBg     = Color.black;

        // â”€â”€ Static interface for GameplaySceneSetup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static LoadingScreenController _instance;

        // â”€â”€ Runtime state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private string  _targetScene;
        private float   _targetProgress;
        private float   _displayedProgress;
        private float   _startTime;
        private bool    _fadingOut;

        // Animated dots
        private float  _dotsTimer;
        private int    _dotsCount;
        private string _baseMessage = "Loading";

        // Stage history (FIFO of last N completed stages for the activity feed)
        private readonly System.Collections.Generic.Queue<string> _stageHistory =
            new System.Collections.Generic.Queue<string>(FEED_CAPACITY);

        // Rotating tips
        private float _tipTimer;
        private int   _tipIndex = -1;

        // UI references
        private Image           _barFill;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _pctText;
        private TextMeshProUGUI _feedText;
        private TextMeshProUGUI _tipText;
        private CanvasGroup     _cg;

        // ── Loading tips (rotate every TIP_INTERVAL while the screen is up) ──
        // Short, gameplay-relevant lines that give the user something to read
        // while the bar fills. Order doesn't matter — the index advances
        // forward on a timer.
        private static readonly string[] LoadingTips = new[]
        {
            "Tip: Hold the left mouse button to keep attacking.",
            "Tip: Press F1–F12 to open the in-game editors at any time.",
            "Tip: F10 opens the Buildings editor — paint per-cell collisions there.",
            "Tip: F8 is the Tile editor; Ctrl + click to flood-fill a region.",
            "Tip: Mouse-wheel over stacked buildings to cycle the active selection.",
            "Tip: F12 lets you author boss FSMs without recompiling.",
            "Tip: Saves rotate 5 deep — a corruption can be rolled back.",
            "Tip: Permadeath is on; your soul drops on death.",
            "Tip: Spells can be remapped from the F4 in-game editor.",
            "Tip: The day/night cycle changes monster spawn behaviour.",
        };

        // â”€â”€ Entry point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static void Show(string targetScene)
        {
            var go = new GameObject("[LoadingScreen]");
            DontDestroyOnLoad(go);
            var ctrl = go.AddComponent<LoadingScreenController>();
            ctrl._targetScene = targetScene;
        }

        // â”€â”€ MonoBehaviour â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void Awake() { _instance = this; }

        private void Start()
        {
            _startTime = Time.unscaledTime;
            BuildUI();
            // Subscribe via Core relay â€” Gameplay can call without a UI assembly reference
            LoadingReporter.OnStageProgress = OnStageReport;
            LoadingReporter.OnGameplayReady = () =>
            {
                if (!_fadingOut) StartCoroutine(FadeAndDestroy());
            };
            StartCoroutine(LoadSceneAsync());
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            LoadingReporter.Clear();
        }

        private void Update()
        {
            if (_fadingOut) return;

            // Smooth progress bar
            _displayedProgress = Mathf.Lerp(_displayedProgress, _targetProgress,
                Time.unscaledDeltaTime * LERP_SPEED);
            ApplyProgress(_displayedProgress);

            // Animated dots
            _dotsTimer += Time.unscaledDeltaTime;
            if (_dotsTimer >= DOTS_INTERVAL)
            {
                _dotsTimer = 0f;
                _dotsCount = (_dotsCount + 1) % 4;
                if (_statusText != null)
                    _statusText.text = _baseMessage + new string('.', _dotsCount);
            }

            // Rotating tips — give the user something to read on long stages.
            _tipTimer += Time.unscaledDeltaTime;
            if (_tipTimer >= TIP_INTERVAL)
            {
                _tipTimer = 0f;
                AdvanceTip();
            }
        }

        private void AdvanceTip()
        {
            if (LoadingTips == null || LoadingTips.Length == 0 || _tipText == null) return;
            _tipIndex = (_tipIndex + 1) % LoadingTips.Length;
            _tipText.text = LoadingTips[_tipIndex];
        }

        // â”€â”€ Stage reporting (Phase 2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void OnStageReport(string message, float gamePhaseProgress)
        {
            // Promote the OUTGOING stage to the activity feed before swapping
            // the message in. The current message represents work that just
            // finished; the new one is what's about to run.
            if (!string.IsNullOrEmpty(_baseMessage) && _baseMessage != "Initializing world" &&
                _baseMessage != "Loading resources" && _baseMessage != "Loading")
            {
                if (_stageHistory.Count >= FEED_CAPACITY) _stageHistory.Dequeue();
                _stageHistory.Enqueue(_baseMessage);
                RefreshFeed();
            }

            _baseMessage    = message;
            _targetProgress = 0.4f + Mathf.Clamp01(gamePhaseProgress) * 0.6f;
        }

        private void RefreshFeed()
        {
            if (_feedText == null) return;
            // Render newest stage at the bottom (closest to the active label),
            // older stages above with progressively lower alpha so the feed
            // visually fades upward.
            var sb = new System.Text.StringBuilder();
            int idx = 0;
            int count = _stageHistory.Count;
            foreach (var stage in _stageHistory)
            {
                // alpha 0.30 → 0.60 → 0.90 from oldest to newest (3-line cap).
                float a = Mathf.Lerp(0.30f, 0.90f, count <= 1 ? 1f : idx / (float)(count - 1));
                int alphaHex = Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                sb.Append("<color=#FFFFFF").Append(alphaHex.ToString("X2")).Append('>')
                  .Append("» ").Append(stage).Append("</color>");
                if (idx < count - 1) sb.Append('\n');
                idx++;
            }
            _feedText.text = sb.ToString();
        }

        // â”€â”€ Scene loading coroutine (Phase 1: 0%â†’40%) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private IEnumerator LoadSceneAsync()
        {
            yield return null; // Render first frame at 0% before loading begins

            Time.timeScale = 1f;
            EntityRegistry.Clear();
            GameEvents.Clear();

            var asyncOp = SceneManager.LoadSceneAsync(_targetScene);
            asyncOp.allowSceneActivation = false;
            _baseMessage = "Loading resources";

            while (asyncOp.progress < 0.9f)
            {
                _targetProgress = (asyncOp.progress / 0.9f) * 0.4f;
                yield return null;
            }

            _targetProgress = 0.4f;
            _baseMessage    = "Initializing world";
            yield return new WaitForSecondsRealtime(0.5f); // allow bar to animate toward 40%

            // Disable the DontDestroyOnLoad EventSystem so its OnDisable removes it
            // from EventSystem.m_EventSystems BEFORE the new scene's EventSystem
            // OnEnable runs. Without this, both components are briefly enabled and
            // Unity logs "There can be only one active Event System." even though
            // RuntimeInputBootstrap.OnSceneLoaded destroys the duplicate one frame
            // later. PersistentEventSystem.Ensure() re-enables ours after cleanup.
            Valkur.Core.Input.PersistentEventSystem.Pause();

            asyncOp.allowSceneActivation = true;
            StartCoroutine(FadeWatchdog(15f)); // safety net if Phase 2 never completes
            // Phase 2 is driven by GameplaySceneSetup â†’ ReportStage / ReportGameplayReady
        }

        // â”€â”€ Fade out and destroy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private IEnumerator FadeAndDestroy()
        {
            if (_fadingOut) yield break; // guard against watchdog + normal double-invocation

            // Point Update() toward 100% but keep lerp running so bar fills smoothly
            _targetProgress = 1f;

            // Wait for minimum display time — bar lerps toward 100% during this window
            float elapsed = Time.unscaledTime - _startTime;
            if (elapsed < MIN_DISPLAY_TIME)
                yield return new WaitForSecondsRealtime(MIN_DISPLAY_TIME - elapsed);

            // Freeze lerp, snap bar to full, show ready text, then fade canvas
            _fadingOut         = true;
            _displayedProgress = 1f;
            ApplyProgress(1f);
            if (_statusText != null) _statusText.text = "Ready!";

            if (_cg != null)
            {
                float t = 0f;
                while (t < FADE_DURATION)
                {
                    _cg.alpha = 1f - t / FADE_DURATION;
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                _cg.alpha = 0f;
            }

            Destroy(gameObject);
        }

        // Watchdog: force fade-out if Phase 2 never fires (e.g. GameplaySceneSetup crash)
        private IEnumerator FadeWatchdog(float timeoutSeconds)
        {
            yield return new WaitForSecondsRealtime(timeoutSeconds);
            if (!_fadingOut)
            {
                Debug.LogWarning("[LoadingScreen] Phase 2 timed out — forcing fade-out.");
                StartCoroutine(FadeAndDestroy());
            }
        }

        // â”€â”€ Apply progress to bar and percentage label â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void ApplyProgress(float p)
        {
            p = Mathf.Clamp01(p);
            if (_barFill != null) _barFill.fillAmount = p;
            if (_pctText  != null) _pctText.text = $"{Mathf.RoundToInt(p * 100f)}%";
        }

        // 1Ã—1 white sprite cache â€” required by Image.Type.Filled to honour fillAmount
        private static Sprite _whiteSprite;
        private static Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "LoadingScreenWhite1x1" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "LoadingScreenWhiteSprite";
            return _whiteSprite;
        }

        // â”€â”€ UI construction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void BuildUI()
        {
            var canvasGo = new GameObject("LoadingCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(1600f, 800f);
            scaler.matchWidthOrHeight   = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            _cg = canvasGo.AddComponent<CanvasGroup>();

            // ── Background ────────────────────────────────────────────────
            // Mirrors the main-menu carousel: outer container uses RectMask2D
            // so anything that overflows past the canvas gets clipped, then
            // the inner Image uses AspectRatioFitter.EnvelopeParent ("cover"
            // mode) to fill the canvas while preserving the source aspect.
            // Result: art is never stretched / squashed — wider windows crop
            // the top/bottom margins, taller windows crop the left/right.
            var bgContainer = new GameObject("Background_Container");
            bgContainer.transform.SetParent(canvasGo.transform, false);
            var bgContainerRt = bgContainer.AddComponent<RectTransform>();
            bgContainerRt.anchorMin = Vector2.zero; bgContainerRt.anchorMax = Vector2.one;
            bgContainerRt.offsetMin = Vector2.zero; bgContainerRt.offsetMax = Vector2.zero;
            bgContainer.AddComponent<RectMask2D>();

            var bgGo  = new GameObject("Background");
            bgGo.transform.SetParent(bgContainer.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            var bgRt  = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bgRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bgRt.pivot            = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = Vector2.zero;
            bgImg.preserveAspect  = true;

            var fitter = bgGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            var bgSprite = Resources.Load<Sprite>("UI/Loading/background_ini");
            if (bgSprite != null)
            {
                bgImg.sprite = bgSprite;
                fitter.aspectRatio = bgSprite.texture != null
                    ? (float)bgSprite.texture.width / Mathf.Max(1, bgSprite.texture.height)
                    : (float)bgSprite.rect.width / Mathf.Max(1f, bgSprite.rect.height);
            }
            else
            {
                var bgTex = Resources.Load<Texture2D>("UI/Loading/background_ini");
                if (bgTex != null)
                {
                    bgImg.sprite = Sprite.Create(bgTex,
                        new Rect(0, 0, bgTex.width, bgTex.height), new Vector2(0.5f, 0.5f));
                    fitter.aspectRatio = (float)bgTex.width / Mathf.Max(1, bgTex.height);
                }
                else
                {
                    // Fallback: solid black, no aspect fitter needed (drop the
                    // component so an aspectRatio of 0 doesn't NaN the layout).
                    bgImg.color = FallbackBg;
                    bgImg.preserveAspect = false;
                    Destroy(fitter);
                    bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
                    bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
                    Debug.LogWarning("[LoadingScreen] background_ini not found in Resources/UI/Loading/.");
                }
            }

            // â”€â”€ Progress bar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            float refW = 1600f, refH = 800f;
            float barW = refW * BAR_WIDTH_RATIO;
            float barY = refH * (1f - BAR_Y_RATIO);

            // Outer border
            var barOuter = new GameObject("BarOuter");
            barOuter.transform.SetParent(canvasGo.transform, false);
            barOuter.AddComponent<Image>().color = BarBorderColor;
            var barOuterRt = barOuter.GetComponent<RectTransform>();
            barOuterRt.anchorMin        = new Vector2(0.5f, 0f);
            barOuterRt.anchorMax        = new Vector2(0.5f, 0f);
            barOuterRt.pivot            = new Vector2(0.5f, 0f);
            barOuterRt.anchoredPosition = new Vector2(0f, barY);
            barOuterRt.sizeDelta        = new Vector2(barW, BAR_HEIGHT_PX);

            // Inner black background
            var innerBg = new GameObject("BarBg");
            innerBg.transform.SetParent(barOuter.transform, false);
            innerBg.AddComponent<Image>().color = FallbackBg;
            var innerRt = innerBg.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero; innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(BAR_BORDER, BAR_BORDER);
            innerRt.offsetMax = new Vector2(-BAR_BORDER, -BAR_BORDER);

            // Fill area (inset by border + padding)
            float pad = BAR_BORDER + BAR_PADDING;
            var fillArea = new GameObject("BarFillArea");
            fillArea.transform.SetParent(barOuter.transform, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>(); // plain GO has no RT; must add explicitly
            fillAreaRt.anchorMin = Vector2.zero; fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(pad, pad);
            fillAreaRt.offsetMax = new Vector2(-pad, -pad);

            // Filled Image â€” correct at all fill amounts (0% included)
            var fillGo = new GameObject("BarFill");
            fillGo.transform.SetParent(fillArea.transform, false);
            _barFill = fillGo.AddComponent<Image>();
            // Image.Type.Filled REQUIRES a sprite â€” without one it renders as a solid
            // rect ignoring fillAmount (the bar would always look 100% full).
            _barFill.sprite     = GetWhiteSprite();
            _barFill.color      = BarFillColor;
            _barFill.type       = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillOrigin = 0; // left-to-right
            _barFill.fillAmount = 0f;
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;

            // â”€â”€ Percentage label (right of bar) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var pctGo = new GameObject("BarPercent");
            pctGo.transform.SetParent(canvasGo.transform, false);
            _pctText = pctGo.AddComponent<TextMeshProUGUI>();
            _pctText.fontSize  = 14f;
            _pctText.color     = TextColor;
            _pctText.alignment = TextAlignmentOptions.Left;
            _pctText.text      = "0%";
            var pctRt = pctGo.GetComponent<RectTransform>();
            pctRt.anchorMin        = new Vector2(0.5f, 0f);
            pctRt.anchorMax        = new Vector2(0.5f, 0f);
            pctRt.pivot            = new Vector2(0f, 0f);
            pctRt.anchoredPosition = new Vector2(barW * 0.5f + 8f, barY);
            pctRt.sizeDelta        = new Vector2(54f, BAR_HEIGHT_PX);

            // â”€â”€ Status text (above bar) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var textGo = new GameObject("StatusText");
            textGo.transform.SetParent(canvasGo.transform, false);
            _statusText = textGo.AddComponent<TextMeshProUGUI>();
            _statusText.fontSize  = 18f;
            _statusText.color     = TextColor;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.text      = "Loading...";
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin        = new Vector2(0.5f, 0f);
            textRt.anchorMax        = new Vector2(0.5f, 0f);
            textRt.pivot            = new Vector2(0.5f, 0f);
            textRt.anchoredPosition = new Vector2(0f, barY + BAR_HEIGHT_PX + TEXT_OFFSET_Y);
            textRt.sizeDelta        = new Vector2(barW, 30f);

            // ── Activity feed (above the status line, right-aligned, faded) ──
            // Shows the last FEED_CAPACITY completed stages so the user has
            // visible momentum even when an individual stage label sits for
            // a moment. Right-aligned to leave the centre uncluttered.
            var feedGo = new GameObject("ActivityFeed");
            feedGo.transform.SetParent(canvasGo.transform, false);
            _feedText = feedGo.AddComponent<TextMeshProUGUI>();
            _feedText.fontSize             = 12f;
            _feedText.color                = TextColor; // alpha is per-line via rich text
            _feedText.alignment            = TextAlignmentOptions.MidlineRight;
            _feedText.richText             = true;
            _feedText.text                 = string.Empty;
            _feedText.enableWordWrapping   = false;
            _feedText.overflowMode         = TextOverflowModes.Ellipsis;
            var feedRt = feedGo.GetComponent<RectTransform>();
            feedRt.anchorMin               = new Vector2(0.5f, 0f);
            feedRt.anchorMax               = new Vector2(0.5f, 0f);
            feedRt.pivot                   = new Vector2(1f,   0f);
            feedRt.anchoredPosition        = new Vector2(barW * 0.5f, barY + BAR_HEIGHT_PX + TEXT_OFFSET_Y + 28f);
            feedRt.sizeDelta               = new Vector2(barW * 0.5f, 60f);

            // ── Rotating tips (below the bar, centered, dim) ────────────────
            // A single line that cycles through gameplay tips every TIP_INTERVAL
            // seconds. Sits well below the bar so it doesn't crowd the live
            // status. Shown immediately so the user sees the first tip on
            // frame 1 instead of waiting TIP_INTERVAL for the first rotation.
            var tipGo = new GameObject("LoadingTip");
            tipGo.transform.SetParent(canvasGo.transform, false);
            _tipText = tipGo.AddComponent<TextMeshProUGUI>();
            _tipText.fontSize  = 14f;
            _tipText.color     = new Color(1f, 1f, 1f, 0.65f);
            _tipText.alignment = TextAlignmentOptions.Center;
            _tipText.fontStyle = FontStyles.Italic;
            _tipText.text      = string.Empty;
            var tipRt = tipGo.GetComponent<RectTransform>();
            tipRt.anchorMin        = new Vector2(0.5f, 0f);
            tipRt.anchorMax        = new Vector2(0.5f, 0f);
            tipRt.pivot            = new Vector2(0.5f, 1f);
            tipRt.anchoredPosition = new Vector2(0f, barY - 16f);
            tipRt.sizeDelta        = new Vector2(barW, 26f);
            AdvanceTip();

            UILayerHelper.SetUILayerRecursive(canvasGo);

            // ── Defensive invariant ──────────────────────────────────────────
            // Image.Type.Filled REQUIRES a sprite to honour fillAmount. Without
            // one, the bar renders as a solid rect and looks 100 % full from
            // the very first frame regardless of progress. Guard against any
            // future refactor that drops the sprite assignment above.
            if (_barFill == null || _barFill.sprite == null)
            {
                Debug.LogError("[LoadingScreen] _barFill.sprite is null. The progress " +
                               "bar will appear 100% full because Image.Type.Filled " +
                               "ignores fillAmount without a sprite. Forcing a fallback.");
                if (_barFill != null) _barFill.sprite = GetWhiteSprite();
            }
        }
    }
}

