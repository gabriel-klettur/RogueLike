using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Boot;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// The loading screen. Two phases:
    ///   Phase 1 (0 % -> 40 %) : <see cref="SceneManager.LoadSceneAsync"/>
    ///   Phase 2 (40 % -> 100%) : the gameplay boot sequence, stage by stage.
    ///
    /// <para><b>What an audit changed here, and why each one is not cosmetic.</b></para>
    ///
    /// <list type="bullet">
    ///   <item><b>The watchdog is a HEARTBEAT, not a deadline.</b> It used to be a flat
    ///   15 s from scene activation — 15 seconds of clock, not 15 seconds without
    ///   progress — so on any machine slower than the developer's the loading screen
    ///   faded out WHILE the boot was still running. It now rearms on every stage
    ///   report and only fires when nothing has advanced, which is the one question a
    ///   watchdog should be asking: not "is this slow" but "is this dead".</item>
    ///
    ///   <item><b>A dead boot ends on a screen, not in a broken world.</b> Fifty of the
    ///   fifty-five boot steps had no exception guard; one throw killed the coroutine,
    ///   the ready signal never arrived, and the old watchdog faded out anyway,
    ///   depositing the player in a half-built world with nothing said. Failures are
    ///   now collected by <see cref="BootTimeline"/> and shown, with a way back to the
    ///   menu.</item>
    ///
    ///   <item><b><see cref="Show"/> is idempotent.</b> It used to mint a second
    ///   GameObject on a second call — two concurrent <c>LoadSceneAsync</c> of the same
    ///   scene and two screens — and because the reporter hook was an ASSIGNMENT rather
    ///   than a <c>+=</c>, the newer screen silenced the older one, which then sat there
    ///   with its own watchdog running. The <c>_instance</c> field existed for exactly
    ///   this and was never read.</item>
    ///
    ///   <item><b>Gameplay input is blocked until the boot finishes.</b> From the frame
    ///   <c>allowSceneActivation</c> flips, the gameplay scene runs its Update while the
    ///   remaining stages register their systems — so the player could walk, click and
    ///   cast into a world that was still assembling itself.</item>
    ///
    ///   <item><b>It knows when there is no phase 2.</b> Every scene now routes through
    ///   here (see <c>SceneTransitionManager.LoadSceneHandler</c>), and the main menu has
    ///   no boot sequence to narrate - so a scene that stays silent for both
    ///   <see cref="Phase2GraceSeconds"/> AND <see cref="Phase2GraceFrames"/> is treated
    ///   as ready instead of being held for the gameplay minimum.</item>
    /// </list>
    /// </summary>
    public class LoadingScreenController : MonoBehaviour
    {
        // ── Layout constants ─────────────────────────────────────────────────
        private const float BAR_WIDTH_RATIO  = 0.60f;
        private const float BAR_HEIGHT_PX    = 30f;
        private const float BAR_Y_RATIO      = 0.80f;
        private const float BAR_BORDER       = 2f;
        private const float BAR_PADDING      = 3f;
        private const float TEXT_OFFSET_Y    = 20f;

        // ── Pacing ───────────────────────────────────────────────────────────
        private const float LERP_SPEED       = 3.5f;
        private const float MIN_DISPLAY_TIME = 1.5f;
        private const float FADE_DURATION    = 0.45f;
        private const float DOTS_INTERVAL    = 0.4f;
        private const float TIP_INTERVAL     = 4.5f;
        private const int   FEED_CAPACITY    = 3;

        /// <summary>
        /// How long a scene may stay silent after activation before we conclude it has
        /// no boot sequence to narrate. Paired with a frame count below; both gates must pass.
        /// </summary>
        private const float Phase2GraceSeconds = 0.5f;

        /// <summary>
        /// And how many RENDERED FRAMES must pass as well. This is the half that
        /// matters, and it was found by measurement rather than reasoning: activating
        /// the gameplay scene is one enormous frame — Unity tears down the menu, awakes
        /// every object in MainGameplay and hands control back — so a seconds-only
        /// grace window expires INSIDE that single frame, before
        /// <c>GameplaySceneSetup.Start</c> has run its first line. Measured live, the
        /// screen concluded "this scene has nothing to say", faded out, and left the
        /// player standing in a world that was 27 % built.
        ///
        /// That is the exact failure this whole rewrite exists to remove, reintroduced
        /// by the heuristic meant to make the main menu feel quick — and counting frames
        /// alone did NOT fix it, because activating that scene genuinely spans several
        /// frames as well as several seconds. What fixed it was moving the START of the
        /// window from "activation permitted" to "activation finished"
        /// (<c>yield return asyncOp</c>), after which a scene with a boot sequence has
        /// already spoken. Both gates stay as a cushion, small enough that the menu is
        /// not held and large enough to absorb an ordering hiccup.
        /// </summary>
        private const int Phase2GraceFrames = 3;

        /// <summary>
        /// Pure decision so it can be tested without a scene: has the target scene
        /// stayed silent long enough, in BOTH time and frames, to be treated as having
        /// no boot sequence? Both gates must pass — waiting for the slower of the two
        /// is the conservative direction, and the cost of being wrong is asymmetric
        /// (an extra beat on the menu, against dropping the player into a half-built
        /// world).
        /// </summary>
        public static bool ShouldConcludeNoPhase2(int framesSinceActivation, float secondsSinceActivation)
            => framesSinceActivation >= Phase2GraceFrames &&
               secondsSinceActivation >= Phase2GraceSeconds;

        /// <summary>
        /// How a progress fraction is spelled. ONE implementation, because the fixture that
        /// guards "the label mirrors the fill" used to compare against the literal <c>"0%"</c> —
        /// so changing the typography (Spanish sets a space before the sign) put a correct label
        /// in the red for a reason that had nothing to do with the rule being guarded. Same
        /// defect as the menu rows that dispatched on their own English text.
        /// </summary>
        public static string FormatPercent(float fraction)
            => Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100f) + " %";

        /// <summary>Nothing has advanced for this long: say so, but keep waiting.</summary>
        private const float StallWarnSeconds = 12f;

        /// <summary>Still nothing: the boot is dead, not slow. Offer a way out.</summary>
        private const float StallFailSeconds = 35f;

        // ── Colours ──────────────────────────────────────────────────────────
        //
        // Read from MenuStyle, so the loading screen is the same game as the menu it comes out
        // of. What this replaces: a white 2 px border, a black interior and a fill of PURE GREEN
        // (0, 200, 0) — the most generic progress bar there is — sitting in the middle of a
        // painting of a dragon.

        private static Valkur.Data.MenuStyle Style => Valkur.Data.MenuStyle.Active;

        private static Color BarFillColor => Style.Gold;
        private static Color BarWarnColor => new Color(0.96f, 0.70f, 0.24f, 1f);
        private static Color BarFailColor => Style.Danger;
        private static Color TextColor => Style.TextPrimary;
        private static readonly Color FallbackBg = Color.black;

        // ── The single live screen ───────────────────────────────────────────
        private static LoadingScreenController _instance;

        /// <summary>True while a loading screen is up. Read by the router's guard.</summary>
        public static bool IsShowing => _instance != null;

        // ── Runtime state ────────────────────────────────────────────────────
        private string  _targetScene;
        private float   _targetProgress;
        private float   _displayedProgress;
        private float   _startTime;
        private bool    _fadingOut;
        private bool    _sawPhase2;
        private bool    _sceneActivated;
        private bool    _finished;
        private float   _lastProgressTime;
        private float   _activationTime;
        private int     _activationFrame;
        private bool    _stallAnnounced;
        private bool    _errorShown;
        private bool    _blockedInput;

        // Animated dots
        private float  _lastSparkProgress;
        private float  _dotsTimer;
        private int    _dotsCount;
        private string _baseMessage = LoadingText.Preparing;

        private readonly System.Collections.Generic.Queue<string> _stageHistory =
            new System.Collections.Generic.Queue<string>(FEED_CAPACITY);

        private float _tipTimer;
        private int   _tipIndex = -1;

        // UI references
        private Image           _barFill;
        private Image           _barSpark;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _pctText;
        private TextMeshProUGUI _feedText;
        private TextMeshProUGUI _tipText;
        private CanvasGroup     _cg;
        private GameObject      _errorPanel;
        private TextMeshProUGUI _errorBody;
        private Transform       _canvasRoot;

        // ── Entry point ──────────────────────────────────────────────────────

        /// <summary>
        /// Show the loading screen and load <paramref name="targetScene"/>.
        /// Idempotent: a second call while one is already up is refused and logged
        /// rather than starting a second concurrent load of the same scene.
        /// </summary>
        public static bool Show(string targetScene)
        {
            if (string.IsNullOrWhiteSpace(targetScene))
            {
                Debug.LogError("[LoadingScreen] Se pidio cargar una escena sin nombre.");
                return false;
            }

            if (_instance != null)
            {
                Debug.LogWarning($"[LoadingScreen] Ya hay una carga en curso hacia " +
                                 $"'{_instance._targetScene}'; se ignora '{targetScene}'.");
                return false;
            }

            var go = new GameObject("[LoadingScreen]");
            DontDestroyOnLoad(go);
            var ctrl = go.AddComponent<LoadingScreenController>();
            ctrl._targetScene = targetScene;
            _instance = ctrl;
            return true;
        }

        // ── MonoBehaviour ────────────────────────────────────────────────────

        private void Awake() { _instance = this; }

        private void Start()
        {
            _startTime = Time.unscaledTime;
            _lastProgressTime = _startTime;
            BuildUI();

            // Subscribe via the Core relay — Gameplay reports without referencing UI.
            LoadingReporter.OnStageProgress = OnStageReport;
            LoadingReporter.OnGameplayReady = OnGameplayReady;

            BlockGameplayInput(true);
            StartCoroutine(LoadSceneAsync());
            StartCoroutine(WatchForStall());
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            LoadingReporter.Clear();
            BlockGameplayInput(false);
        }

        /// <summary>
        /// Nothing else can hold the input block during a scene load — chat and the
        /// console cannot be open across an activation — so this owns the flag
        /// outright for the length of the boot and gives it back on teardown.
        /// </summary>
        private void BlockGameplayInput(bool blocked)
        {
            if (blocked == _blockedInput) return;
            _blockedInput = blocked;
            Valkur.Core.Input.InputBlocker.SetBootBlocked(blocked);
        }

        private void Update()
        {
            if (_fadingOut) return;

            _displayedProgress = Mathf.Lerp(_displayedProgress, _targetProgress,
                Time.unscaledDeltaTime * LERP_SPEED);
            ApplyProgress(_displayedProgress);

            _dotsTimer += Time.unscaledDeltaTime;
            if (_dotsTimer >= DOTS_INTERVAL)
            {
                _dotsTimer = 0f;
                _dotsCount = (_dotsCount + 1) % 4;
                if (_statusText != null && !_errorShown)
                    _statusText.text = _baseMessage + new string('.', _dotsCount);
            }

            _tipTimer += Time.unscaledDeltaTime;
            if (_tipTimer >= TIP_INTERVAL)
            {
                _tipTimer = 0f;
                AdvanceTip();
            }
        }

        private void AdvanceTip()
        {
            var tips = LoadingText.Tips;
            if (tips == null || tips.Length == 0 || _tipText == null) return;
            _tipIndex = (_tipIndex + 1) % tips.Length;
            _tipText.text = tips[_tipIndex];
        }

        // ── Stage reporting (Phase 2) ────────────────────────────────────────

        private void OnStageReport(string message, float gamePhaseProgress)
        {
            if (_finished) return;

            _sawPhase2 = true;
            _lastProgressTime = Time.unscaledTime;
            if (_stallAnnounced) ClearStallWarning();

            // Promote the OUTGOING stage to the feed before swapping the message in.
            if (!string.IsNullOrEmpty(_baseMessage) &&
                _baseMessage != LoadingText.BuildingWorld &&
                _baseMessage != LoadingText.LoadingRes &&
                _baseMessage != LoadingText.Preparing)
            {
                if (_stageHistory.Count >= FEED_CAPACITY) _stageHistory.Dequeue();
                _stageHistory.Enqueue(_baseMessage);
                RefreshFeed();
            }

            _baseMessage    = message;
            float target    = 0.4f + Mathf.Clamp01(gamePhaseProgress) * 0.6f;

            // Monotonic by construction: a bar that goes backwards reads as a bug even
            // when the number behind it is right.
            if (target > _targetProgress) _targetProgress = target;
        }

        private void OnGameplayReady()
        {
            if (_finished) return;
            _finished = true;

            if (BootTimeline.AnyFailed)
            {
                ShowErrorPanel(false);
                return;
            }
            if (!_fadingOut) StartCoroutine(FadeAndDestroy());
        }

        private void RefreshFeed()
        {
            if (_feedText == null) return;
            var sb = new System.Text.StringBuilder();
            int idx = 0;
            int count = _stageHistory.Count;
            foreach (var stage in _stageHistory)
            {
                float a = Mathf.Lerp(0.30f, 0.90f, count <= 1 ? 1f : idx / (float)(count - 1));
                int alphaHex = Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                sb.Append("<color=#FFFFFF").Append(alphaHex.ToString("X2")).Append('>')
                  .Append("» ").Append(stage).Append("</color>");
                if (idx < count - 1) sb.Append('\n');
                idx++;
            }
            _feedText.text = sb.ToString();
        }

        // ── Scene loading (Phase 1: 0 % -> 40 %) ─────────────────────────────

        private IEnumerator LoadSceneAsync()
        {
            yield return null; // render the first frame at 0 % before loading begins

            Time.timeScale = 1f;
            EntityRegistry.Clear();
            GameEvents.Clear();

            // A scene missing from Build Settings makes LoadSceneAsync return NULL, and
            // the shipped code dereferenced it on the very next line — an NRE with the
            // loading screen already up and no way out of it.
            if (!Application.CanStreamedLevelBeLoaded(_targetScene))
            {
                FailHard($"La escena '{_targetScene}' no esta en Build Settings.");
                yield break;
            }

            var asyncOp = SceneManager.LoadSceneAsync(_targetScene);
            if (asyncOp == null)
            {
                FailHard($"Unity rechazo la carga de la escena '{_targetScene}'.");
                yield break;
            }

            asyncOp.allowSceneActivation = false;
            _baseMessage = LoadingText.LoadingRes;

            while (asyncOp.progress < 0.9f)
            {
                float p = (asyncOp.progress / 0.9f) * 0.4f;
                if (p > _targetProgress) _targetProgress = p;
                _lastProgressTime = Time.unscaledTime;
                yield return null;
            }

            _targetProgress = 0.4f;
            _baseMessage    = LoadingText.BuildingWorld;
            yield return new WaitForSecondsRealtime(0.25f);

            // Disable the DontDestroyOnLoad EventSystem so its OnDisable removes it
            // from EventSystem.m_EventSystems BEFORE the new scene's EventSystem
            // OnEnable runs — otherwise both are briefly enabled and uGUI logs
            // "There can be only one active Event System."
            Valkur.Core.Input.PersistentEventSystem.Pause();

            asyncOp.allowSceneActivation = true;

            // Wait for the activation to COMPLETE, not merely to be permitted. Unity
            // runs the incoming scene's Awake / OnEnable / Start DURING activation and
            // only reports isDone afterwards, so by the time this line resumes a scene
            // that has a boot sequence has already reported its first stage — and the
            // grace window below becomes a cushion rather than a bet.
            //
            // Stamping it at allowSceneActivation instead is what shipped first, and it
            // measured the activation itself as silence: MainGameplay takes several
            // frames and more than a second to come up, so the screen concluded "this
            // scene has nothing to say" and faded out with the boot at 15 %. Counting
            // frames as well as seconds did not save it, because the activation really
            // does span frames. The signal was wrong, not the threshold.
            yield return asyncOp;

            _sceneActivated  = true;
            _activationTime  = Time.unscaledTime;
            _activationFrame = Time.frameCount;
            _lastProgressTime = _activationTime;
        }

        // ── Liveness ─────────────────────────────────────────────────────────

        /// <summary>
        /// The heartbeat. Three distinct outcomes, and the shipped code collapsed all
        /// three into "fade out after fifteen seconds whatever happened":
        ///
        /// <list type="bullet">
        ///   <item>a scene with no boot sequence goes quiet immediately and is simply
        ///   done;</item>
        ///   <item>a boot that is merely SLOW keeps reporting, so the timer keeps
        ///   rearming and the screen stays up for as long as it needs;</item>
        ///   <item>a boot that is DEAD stops reporting, and only that case ends in an
        ///   error panel.</item>
        /// </list>
        /// </summary>
        private IEnumerator WatchForStall()
        {
            while (!_finished && !_fadingOut && !_errorShown)
            {
                float now = Time.unscaledTime;

                // A scene that never narrates itself is not stalled — it is a light
                // scene (the menu) with nothing to say.
                if (_sceneActivated && !_sawPhase2 &&
                    ShouldConcludeNoPhase2(Time.frameCount - _activationFrame, now - _activationTime))
                {
                    _finished = true;
                    _targetProgress = 1f;
                    if (!_fadingOut) StartCoroutine(FadeAndDestroy());
                    yield break;
                }

                float silent = now - _lastProgressTime;

                if (silent >= StallFailSeconds)
                {
                    FailHard($"El arranque lleva {Mathf.RoundToInt(silent)} s sin avanzar " +
                             $"en '{_baseMessage}'.");
                    yield break;
                }

                if (silent >= StallWarnSeconds && !_stallAnnounced)
                    ShowStallWarning();

                yield return null;
            }
        }

        private void ShowStallWarning()
        {
            _stallAnnounced = true;
            if (_barFill != null) _barFill.color = BarWarnColor;
            if (_tipText != null)
            {
                _tipText.color = new Color(1f, 0.85f, 0.5f, 0.95f);
                _tipText.text  = LoadingText.Timeout + " — " + LoadingText.TimeoutHint;
            }
            Debug.LogWarning($"[LoadingScreen] Sin avance desde hace {StallWarnSeconds:F0} s " +
                             $"en '{_baseMessage}'.");
        }

        private void ClearStallWarning()
        {
            _stallAnnounced = false;
            if (_barFill != null) _barFill.color = BarFillColor;
            if (_tipText != null)
            {
                _tipText.color = new Color(1f, 1f, 1f, 0.65f);
                AdvanceTip();
            }
        }

        /// <summary>
        /// The boot cannot finish. Show what happened and give the player a door;
        /// never fade out into a world that was never built.
        /// </summary>
        private void FailHard(string reason)
        {
            Debug.LogError($"[LoadingScreen] {reason}");
            _finished = true;
            ShowErrorPanel(true, reason);
        }

        // ── Fade out ─────────────────────────────────────────────────────────

        private IEnumerator FadeAndDestroy()
        {
            if (_fadingOut) yield break;

            _targetProgress = 1f;

            // The minimum display is for the gameplay boot, where it stops the screen
            // strobing past. A light scene has nothing to show and is not held.
            if (_sawPhase2)
            {
                float elapsed = Time.unscaledTime - _startTime;
                if (elapsed < MIN_DISPLAY_TIME)
                    yield return new WaitForSecondsRealtime(MIN_DISPLAY_TIME - elapsed);
            }

            _fadingOut         = true;
            _displayedProgress = 1f;
            ApplyProgress(1f);
            if (_statusText != null) _statusText.text = LoadingText.Ready;

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

        // ── Progress ─────────────────────────────────────────────────────────

        private void ApplyProgress(float p)
        {
            p = Mathf.Clamp01(p);
            if (_barFill != null) _barFill.fillAmount = p;
            if (_pctText  != null) _pctText.text = FormatPercent(p);
            if (_barSpark != null)
            {
                var rt = (RectTransform)_barSpark.transform;
                var area = (RectTransform)_barSpark.transform.parent;
                rt.anchoredPosition = new Vector2(area.rect.width * p, 0f);
                // Lit only while the fill is actually moving. A spark that sits on a stalled bar
                // says the opposite of what it is for.
                float delta = Mathf.Abs(p - _lastSparkProgress);
                _lastSparkProgress = p;
                float want = delta > 0.0008f ? 1f : 0f;
                var c = _barSpark.color;
                c.a = Mathf.MoveTowards(c.a, want, Time.unscaledDeltaTime * 4f);
                _barSpark.color = c;
            }
        }

        // 1x1 white sprite — Image.Type.Filled needs a sprite to honour fillAmount.
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Domain Reload is off: both survive a Play session and point at destroyed
            // objects on the next one.
            _instance = null;
            _whiteSprite = null;
        }

        // ── UI construction ──────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("LoadingCanvas");
            canvasGo.transform.SetParent(transform);
            _canvasRoot = canvasGo.transform;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            Valkur.Core.UI.HudLayout.ApplyScaler(scaler);

            canvasGo.AddComponent<GraphicRaycaster>();
            _cg = canvasGo.AddComponent<CanvasGroup>();

            BuildBackground(canvasGo);

            float refW = 1600f, refH = 800f;
            float barW = refW * BAR_WIDTH_RATIO;
            float barY = refH * (1f - BAR_Y_RATIO);

            BuildBar(canvasGo, barW, barY);
            BuildLabels(canvasGo, barW, barY);

            UILayerHelper.SetUILayerRecursive(canvasGo);

            // Image.Type.Filled REQUIRES a sprite to honour fillAmount. Without one the
            // bar renders as a solid rect and looks 100 % full from the first frame.
            if (_barFill == null || _barFill.sprite == null)
            {
                Debug.LogError("[LoadingScreen] _barFill.sprite es null; la barra se veria " +
                               "llena al 100 % desde el primer frame. Se fuerza el fallback.");
                if (_barFill != null) _barFill.sprite = GetWhiteSprite();
            }
        }

        private void BuildBackground(GameObject canvasGo)
        {
            // RectMask2D on the container + AspectRatioFitter.EnvelopeParent on the
            // image: "cover" behaviour, so the art is never stretched — a wider window
            // crops top and bottom, a taller one crops the sides.
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
                return;
            }

            var bgTex = Resources.Load<Texture2D>("UI/Loading/background_ini");
            if (bgTex != null)
            {
                // FullRect. The default is SpriteMeshType.Tight, which traces the alpha outline
                // of the whole 1536 x 1024 region: measured on this very texture, 21.22 ms
                // against 0.026 ms — and the outline is read by nothing.
                bgImg.sprite = Sprite.Create(bgTex, new Rect(0, 0, bgTex.width, bgTex.height),
                                             new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                fitter.aspectRatio = (float)bgTex.width / Mathf.Max(1, bgTex.height);
                return;
            }

            // Solid black fallback — drop the fitter so an aspectRatio of 0 cannot NaN
            // the layout.
            bgImg.color = FallbackBg;
            bgImg.preserveAspect = false;
            Destroy(fitter);
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            Debug.LogWarning("[LoadingScreen] No se encontro background_ini en Resources/UI/Loading/.");
        }

        private void BuildBar(GameObject canvasGo, float barW, float barY)
        {
            var art = Valkur.UI.MainMenu.MenuArt.Get();

            // A grooved track and a filled bar from the menu's own atlas, with a chamfer and a
            // lit top edge. What this replaces is a white rectangle around a black rectangle
            // around a pure-green rectangle.
            var barOuter = new GameObject("BarOuter", typeof(RectTransform));
            barOuter.transform.SetParent(canvasGo.transform, false);
            var trackImg = barOuter.AddComponent<Image>();
            trackImg.sprite = art.SliderTrack;
            trackImg.type = Image.Type.Sliced;
            trackImg.color = Color.white;
            trackImg.raycastTarget = false;
            trackImg.pixelsPerUnitMultiplier = 1f;

            var barOuterRt = barOuter.GetComponent<RectTransform>();
            barOuterRt.anchorMin        = new Vector2(0.5f, 0f);
            barOuterRt.anchorMax        = new Vector2(0.5f, 0f);
            barOuterRt.pivot            = new Vector2(0.5f, 0f);
            barOuterRt.anchoredPosition = new Vector2(0f, barY);
            barOuterRt.sizeDelta        = new Vector2(barW, BAR_HEIGHT_PX);

            float pad = BAR_BORDER + BAR_PADDING;
            var fillArea = new GameObject("BarFillArea", typeof(RectTransform));
            fillArea.transform.SetParent(barOuter.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = Vector2.zero; fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(pad, pad);
            fillAreaRt.offsetMax = new Vector2(-pad, -pad);

            var fillGo = new GameObject("BarFill", typeof(RectTransform));
            fillGo.transform.SetParent(fillArea.transform, false);
            _barFill = fillGo.AddComponent<Image>();
            _barFill.sprite     = art.SliderFill;
            _barFill.color      = BarFillColor;
            _barFill.type       = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillOrigin = 0;
            _barFill.fillAmount = 0f;
            _barFill.raycastTarget = false;
            _barFill.pixelsPerUnitMultiplier = 1f;
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;

            // A spark that rides the leading edge. It is the one thing on the screen that makes
            // the number MOVING visible rather than merely different from a second ago.
            var sparkGo = new GameObject("BarSpark", typeof(RectTransform));
            sparkGo.transform.SetParent(fillArea.transform, false);
            _barSpark = sparkGo.AddComponent<Image>();
            _barSpark.sprite = art.MoteGlow;
            _barSpark.type = Image.Type.Simple;
            _barSpark.raycastTarget = false;
            _barSpark.color = new Color(1f, 1f, 1f, 0f);
            var sparkRt = sparkGo.GetComponent<RectTransform>();
            sparkRt.anchorMin = new Vector2(0f, 0.5f);
            sparkRt.anchorMax = new Vector2(0f, 0.5f);
            sparkRt.pivot = new Vector2(0.5f, 0.5f);
            sparkRt.sizeDelta = new Vector2(26f, BAR_HEIGHT_PX + 10f);
        }

        private void BuildLabels(GameObject canvasGo, float barW, float barY)
        {
            var art = Valkur.UI.MainMenu.MenuArt.Get();

            // A soft floor under everything that is written on the art. Without it the status,
            // the feed and the tip are white type straight on a painting, so whether they can be
            // read depends on which part of the picture they land on — measured on the shipped
            // screen, the status line sat on the dragon's fire.
            var plateGo = new GameObject("TextPlate", typeof(RectTransform));
            plateGo.transform.SetParent(canvasGo.transform, false);
            var plate = plateGo.AddComponent<Image>();
            plate.sprite = art.SoftPlate;
            plate.type = Image.Type.Simple;
            plate.color = new Color(0f, 0f, 0f, 0.68f);
            plate.raycastTarget = false;
            var plateRt = plateGo.GetComponent<RectTransform>();
            plateRt.anchorMin = new Vector2(0.5f, 0f);
            plateRt.anchorMax = new Vector2(0.5f, 0f);
            plateRt.pivot = new Vector2(0.5f, 0.5f);
            plateRt.anchoredPosition = new Vector2(0f, barY + BAR_HEIGHT_PX * 0.5f);
            plateRt.sizeDelta = new Vector2(barW * 1.45f, 300f);

            var pctGo = new GameObject("BarPercent", typeof(RectTransform));
            pctGo.transform.SetParent(canvasGo.transform, false);
            _pctText = pctGo.AddComponent<TextMeshProUGUI>();
            _pctText.fontSize  = 15f;
            _pctText.color     = Style.Gold;
            _pctText.alignment = TextAlignmentOptions.Left;
            _pctText.text      = FormatPercent(0f);
            _pctText.raycastTarget = false;
            var pctRt = pctGo.GetComponent<RectTransform>();
            pctRt.anchorMin        = new Vector2(0.5f, 0f);
            pctRt.anchorMax        = new Vector2(0.5f, 0f);
            pctRt.pivot            = new Vector2(0f, 0f);
            pctRt.anchoredPosition = new Vector2(barW * 0.5f + 12f, barY);
            pctRt.sizeDelta        = new Vector2(70f, BAR_HEIGHT_PX);

            var textGo = new GameObject("StatusText", typeof(RectTransform));
            textGo.transform.SetParent(canvasGo.transform, false);
            _statusText = textGo.AddComponent<TextMeshProUGUI>();
            _statusText.fontSize  = 19f;
            _statusText.color     = TextColor;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.text      = LoadingText.Preparing;
            _statusText.raycastTarget = false;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin        = new Vector2(0.5f, 0f);
            textRt.anchorMax        = new Vector2(0.5f, 0f);
            textRt.pivot            = new Vector2(0.5f, 0f);
            textRt.anchoredPosition = new Vector2(0f, barY + BAR_HEIGHT_PX + TEXT_OFFSET_Y);
            textRt.sizeDelta        = new Vector2(barW, 30f);

            var feedGo = new GameObject("ActivityFeed", typeof(RectTransform));
            feedGo.transform.SetParent(canvasGo.transform, false);
            _feedText = feedGo.AddComponent<TextMeshProUGUI>();
            _feedText.fontSize             = 12f;
            // Dim. It is boot telemetry, not content: at full white it was the second brightest
            // thing on the screen and read as something the player was supposed to follow.
            _feedText.color                = Style.TextMuted;
            _feedText.alignment            = TextAlignmentOptions.MidlineRight;
            _feedText.richText             = true;
            _feedText.text                 = string.Empty;
            _feedText.enableWordWrapping   = false;
            _feedText.overflowMode         = TextOverflowModes.Ellipsis;
            _feedText.raycastTarget        = false;
            var feedRt = feedGo.GetComponent<RectTransform>();
            feedRt.anchorMin        = new Vector2(0.5f, 0f);
            feedRt.anchorMax        = new Vector2(0.5f, 0f);
            feedRt.pivot            = new Vector2(1f,   0f);
            feedRt.anchoredPosition = new Vector2(barW * 0.5f, barY + BAR_HEIGHT_PX + TEXT_OFFSET_Y + 30f);
            feedRt.sizeDelta        = new Vector2(barW * 0.5f, 60f);

            var tipGo = new GameObject("LoadingTip", typeof(RectTransform));
            tipGo.transform.SetParent(canvasGo.transform, false);
            _tipText = tipGo.AddComponent<TextMeshProUGUI>();
            _tipText.fontSize  = 15f;
            _tipText.color     = Style.TextDim;
            _tipText.alignment = TextAlignmentOptions.Center;
            _tipText.fontStyle = FontStyles.Italic;
            _tipText.text      = string.Empty;
            _tipText.enableWordWrapping = true;
            _tipText.raycastTarget = false;
            var tipRt = tipGo.GetComponent<RectTransform>();
            tipRt.anchorMin        = new Vector2(0.5f, 0f);
            tipRt.anchorMax        = new Vector2(0.5f, 0f);
            tipRt.pivot            = new Vector2(0.5f, 1f);
            tipRt.anchoredPosition = new Vector2(0f, barY - 22f);
            tipRt.sizeDelta        = new Vector2(barW, 56f);
            AdvanceTip();
        }

        // ── The error surface ────────────────────────────────────────────────

        /// <summary>
        /// What the player sees when the boot could not finish. Two buttons, because
        /// the two failures are different: a boot that ran to the end with some steps
        /// broken leaves a playable-ish world worth entering, while one that never got
        /// there does not — so "Continuar" is offered only in the first case.
        /// </summary>
        private void ShowErrorPanel(bool fatal, string reason = null)
        {
            if (_errorShown || _canvasRoot == null) return;
            _errorShown = true;
            _fadingOut  = false;

            if (_barFill != null) _barFill.color = BarFailColor;
            if (_statusText != null) _statusText.text = LoadingText.Failed;
            if (_tipText != null) _tipText.text = string.Empty;

            _errorPanel = new GameObject("BootErrorPanel");
            _errorPanel.transform.SetParent(_canvasRoot, false);
            var panelImg = _errorPanel.AddComponent<Image>();
            panelImg.color = new Color(0.05f, 0.05f, 0.06f, 0.93f);
            var panelRt = _errorPanel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot     = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(900f, 380f);
            panelRt.anchoredPosition = new Vector2(0f, 60f);

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_errorPanel.transform, false);
            _errorBody = bodyGo.AddComponent<TextMeshProUGUI>();
            _errorBody.fontSize  = 15f;
            _errorBody.color     = new Color(1f, 0.92f, 0.9f, 1f);
            _errorBody.alignment = TextAlignmentOptions.TopLeft;
            _errorBody.text      = BuildFailureText(reason);
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.offsetMin = new Vector2(28f, 74f);
            bodyRt.offsetMax = new Vector2(-28f, -22f);

            MakeButton(_errorPanel.transform, "Volver al menu", new Vector2(-150f, 26f), () =>
            {
                BlockGameplayInput(false);
                LoadingReporter.Clear();
                _instance = null;
                Destroy(gameObject);
                SceneTransitionManager.LoadSceneImmediate("MainMenu");
            });

            if (!fatal)
            {
                MakeButton(_errorPanel.transform, "Continuar igualmente", new Vector2(150f, 26f), () =>
                {
                    _errorShown = false;
                    Destroy(_errorPanel);
                    StartCoroutine(FadeAndDestroy());
                });
            }
        }

        private string BuildFailureText(string reason)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(reason)) sb.AppendLine(reason).AppendLine();

            var failures = BootTimeline.Failures;
            if (failures != null && failures.Count > 0)
            {
                sb.Append("Pasos que fallaron (").Append(failures.Count).Append("):").AppendLine();
                int shown = 0;
                foreach (var f in failures)
                {
                    if (shown++ >= 8) { sb.AppendLine("  ..."); break; }
                    sb.Append("  · ").AppendLine(f);
                }
                sb.AppendLine();
            }
            sb.Append(LoadingText.FailedHint);
            return sb.ToString();
        }

        private void MakeButton(Transform parent, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.19f, 0.21f, 1f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 38f);
            rt.anchoredPosition = anchoredPos;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            // Image + TMP on the SAME GameObject throws; the label is always a child.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = 15f;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        }
    }
}
