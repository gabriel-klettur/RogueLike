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
        private string _baseMessage = "Cargando";

        // UI references
        private Image           _barFill;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _pctText;
        private CanvasGroup     _cg;

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
        }

        // â”€â”€ Stage reporting (Phase 2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void OnStageReport(string message, float gamePhaseProgress)
        {
            _baseMessage    = message;
            _targetProgress = 0.4f + Mathf.Clamp01(gamePhaseProgress) * 0.6f;
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
            _baseMessage = "Cargando recursos";

            while (asyncOp.progress < 0.9f)
            {
                _targetProgress = (asyncOp.progress / 0.9f) * 0.4f;
                yield return null;
            }

            _targetProgress = 0.4f;
            _baseMessage    = "Inicializando mundo";
            yield return new WaitForSecondsRealtime(0.5f); // allow bar to animate toward 40%

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
            if (_statusText != null) _statusText.text = "Listo!";

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

            // â”€â”€ Background â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var bgGo  = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            var bgRt  = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            var bgSprite = Resources.Load<Sprite>("UI/Loading/background_ini");
            if (bgSprite != null)
            {
                bgImg.sprite = bgSprite;
                bgImg.preserveAspect = false;
            }
            else
            {
                var bgTex = Resources.Load<Texture2D>("UI/Loading/background_ini");
                if (bgTex != null)
                {
                    bgImg.sprite = Sprite.Create(bgTex,
                        new Rect(0, 0, bgTex.width, bgTex.height), new Vector2(0.5f, 0.5f));
                    bgImg.preserveAspect = false;
                }
                else
                {
                    bgImg.color = FallbackBg;
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
            _statusText.text      = "Cargando...";
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin        = new Vector2(0.5f, 0f);
            textRt.anchorMax        = new Vector2(0.5f, 0f);
            textRt.pivot            = new Vector2(0.5f, 0f);
            textRt.anchoredPosition = new Vector2(0f, barY + BAR_HEIGHT_PX + TEXT_OFFSET_Y);
            textRt.sizeDelta        = new Vector2(barW, 30f);

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

