using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// Self-contained loading screen that matches the Python LoadingScreen:
    ///   - Full-screen background image (background_ini.png, stretched to fill)
    ///   - White bordered progress bar (60% width, 80% Y) with green fill
    ///   - White status text centered above the bar
    ///
    /// Usage: LoadingScreenController.Show("MainGameplay")
    /// Creates a DontDestroyOnLoad Canvas, async-loads the target scene,
    /// updates the progress bar, then self-destructs.
    /// </summary>
    public class LoadingScreenController : MonoBehaviour
    {
        // ── Python-matching constants ──────────────────────────────
        private const float BAR_WIDTH_RATIO  = 0.60f;   // 60% of screen width
        private const float BAR_HEIGHT_PX    = 30f;
        private const float BAR_Y_RATIO      = 0.80f;   // 80% from top
        private const float BAR_PADDING      = 3f;
        private const float BAR_BORDER       = 2f;
        private const float TEXT_OFFSET_Y    = 20f;      // px above bar center

        private static readonly Color BarBorderColor = Color.white;
        private static readonly Color BarFillColor   = new Color(0f, 200f / 255f, 0f, 1f); // (0, 200, 0)
        private static readonly Color TextColor      = Color.white;
        private static readonly Color FallbackBg     = Color.black;

        // ── Loading phase messages (Spanish, matching Python order) ─
        private static readonly string[] PhaseMessages =
        {
            "Pantalla, reloj y fuente",
            "Mundo (sin estado)",
            "Cargando estado de mundo",
            "Inicializando audio",
            "Cargando mapa",
            "Cargando edificios",
            "Inicializando ECS",
            "Cargando catálogo de ítems",
            "Cargando Z-layer",
            "Cargando editores",
            "Cargando minimapa",
            "Inicializando renderizador",
            "Inicializando menú",
        };

        // ── UI references ──────────────────────────────────────────
        private Image _barFill;
        private TextMeshProUGUI _statusText;
        private string _targetScene;

        /// <summary>
        /// Entry point. Call this instead of SceneTransitionManager.LoadScene
        /// to get a visual loading screen during scene transition.
        /// </summary>
        public static void Show(string targetScene)
        {
            var go = new GameObject("[LoadingScreen]");
            DontDestroyOnLoad(go);
            var ctrl = go.AddComponent<LoadingScreenController>();
            ctrl._targetScene = targetScene;
        }

        private void Start()
        {
            BuildUI();
            StartCoroutine(LoadSceneAsync());
        }

        // ────────────────────────────────────────────────────────────
        //  UI Construction (mirrors Python LoadingScreen.__init__)
        // ────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            float sw = Screen.width;
            float sh = Screen.height;

            // Canvas (Screen Space – Overlay, highest sort order)
            var canvasGo = new GameObject("LoadingCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1600f, 800f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var canvasRt = canvasGo.GetComponent<RectTransform>();

            // ── Background image ───────────────────────────────────
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImage = bgGo.AddComponent<Image>();
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            // Load background sprite from Resources
            var bgSprite = Resources.Load<Sprite>("UI/Loading/background_ini");
            if (bgSprite != null)
            {
                bgImage.sprite = bgSprite;
                bgImage.preserveAspect = false; // stretch to fill, like Python
            }
            else
            {
                // Try loading as Texture2D (in case import settings are default)
                var bgTex = Resources.Load<Texture2D>("UI/Loading/background_ini");
                if (bgTex != null)
                {
                    bgSprite = Sprite.Create(bgTex,
                        new Rect(0, 0, bgTex.width, bgTex.height),
                        new Vector2(0.5f, 0.5f));
                    bgImage.sprite = bgSprite;
                    bgImage.preserveAspect = false;
                }
                else
                {
                    bgImage.color = FallbackBg;
                    Debug.LogWarning("[LoadingScreen] background_ini not found in Resources/UI/Loading/. Using black fallback.");
                }
            }

            // Reference resolution for layout
            float refW = 1600f;
            float refH = 800f;

            // ── Progress bar outer border ──────────────────────────
            float barW = refW * BAR_WIDTH_RATIO;     // 960
            float barY = refH * (1f - BAR_Y_RATIO);  // 160 from bottom in ref coords

            var barOuterGo = new GameObject("BarOuter");
            barOuterGo.transform.SetParent(canvasGo.transform, false);
            var barOuterImg = barOuterGo.AddComponent<Image>();
            barOuterImg.color = Color.clear;
            var barOuterRt = barOuterGo.GetComponent<RectTransform>();
            barOuterRt.anchorMin = new Vector2(0.5f, 0f);
            barOuterRt.anchorMax = new Vector2(0.5f, 0f);
            barOuterRt.pivot = new Vector2(0.5f, 0f);
            barOuterRt.anchoredPosition = new Vector2(0f, barY);
            barOuterRt.sizeDelta = new Vector2(barW, BAR_HEIGHT_PX);

            // White border via Outline or a child frame approach – use a simple Image + child
            var borderGo = new GameObject("BarBorder");
            borderGo.transform.SetParent(barOuterGo.transform, false);
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.color = BarBorderColor;
            var borderRt = borderGo.GetComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = Vector2.zero;
            borderRt.offsetMax = Vector2.zero;

            // Inner black background (inside border)
            var innerBgGo = new GameObject("BarInnerBg");
            innerBgGo.transform.SetParent(barOuterGo.transform, false);
            var innerBgImg = innerBgGo.AddComponent<Image>();
            innerBgImg.color = FallbackBg;
            var innerBgRt = innerBgGo.GetComponent<RectTransform>();
            innerBgRt.anchorMin = Vector2.zero;
            innerBgRt.anchorMax = Vector2.one;
            innerBgRt.offsetMin = new Vector2(BAR_BORDER, BAR_BORDER);
            innerBgRt.offsetMax = new Vector2(-BAR_BORDER, -BAR_BORDER);

            // Green fill bar (inside padding)
            var fillGo = new GameObject("BarFill");
            fillGo.transform.SetParent(barOuterGo.transform, false);
            _barFill = fillGo.AddComponent<Image>();
            _barFill.color = BarFillColor;
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f); // width controlled by anchorMax.x
            fillRt.pivot = new Vector2(0f, 0.5f);
            float pad = BAR_BORDER + BAR_PADDING;
            fillRt.offsetMin = new Vector2(pad, pad);
            fillRt.offsetMax = new Vector2(-pad, -pad);

            // ── Status text ────────────────────────────────────────
            var textGo = new GameObject("StatusText");
            textGo.transform.SetParent(canvasGo.transform, false);
            _statusText = textGo.AddComponent<TextMeshProUGUI>();
            _statusText.fontSize = 18f;
            _statusText.color = TextColor;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.text = "";
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.5f, 0f);
            textRt.anchorMax = new Vector2(0.5f, 0f);
            textRt.pivot = new Vector2(0.5f, 0f);
            textRt.anchoredPosition = new Vector2(0f, barY + BAR_HEIGHT_PX + TEXT_OFFSET_Y);
            textRt.sizeDelta = new Vector2(barW, 30f);

            // Start at 0% progress
            SetProgress(0f, "Cargando...");
        }

        // ────────────────────────────────────────────────────────────
        //  Scene loading coroutine
        // ────────────────────────────────────────────────────────────
        private IEnumerator LoadSceneAsync()
        {
            // Clean state before loading (same as SceneTransitionManager.LoadScene)
            Time.timeScale = 1f;
            EntityRegistry.Clear();
            GameEvents.Clear();

            var async = SceneManager.LoadSceneAsync(_targetScene);
            async.allowSceneActivation = false;

            int phaseCount = PhaseMessages.Length;
            int lastPhase = -1;

            // Unity async loading goes 0 → 0.9 while loading, then waits for activation
            while (async.progress < 0.9f)
            {
                // Map Unity progress (0..0.9) to our 0..1 range
                float progress = Mathf.Clamp01(async.progress / 0.9f);

                // Pick a phase message based on progress
                int phase = Mathf.Min(Mathf.FloorToInt(progress * phaseCount), phaseCount - 1);
                if (phase != lastPhase)
                {
                    lastPhase = phase;
                }
                SetProgress(progress, PhaseMessages[Mathf.Max(0, phase)]);
                yield return null;
            }

            // Loading is done (0.9), show 100% briefly before activating
            SetProgress(1f, PhaseMessages[phaseCount - 1]);
            yield return new WaitForSecondsRealtime(0.3f);

            // Activate the scene
            async.allowSceneActivation = true;

            // Wait one frame for scene activation
            yield return null;

            // Self-destruct
            Destroy(gameObject);
        }

        // ────────────────────────────────────────────────────────────
        //  Progress update
        // ────────────────────────────────────────────────────────────
        private void SetProgress(float progress, string message)
        {
            progress = Mathf.Clamp01(progress);

            if (_barFill != null)
            {
                var rt = _barFill.rectTransform;
                float pad = BAR_BORDER + BAR_PADDING;
                // Set anchorMax.x to represent fill percentage within the padded area
                rt.anchorMax = new Vector2(progress, 1f);
                rt.offsetMax = new Vector2(-pad, -pad);
            }

            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }
    }
}
