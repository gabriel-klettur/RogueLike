using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Full-screen radial vignette that tints the screen edges according to the
    /// current day phase. Sits behind the gameplay HUD (sortingOrder = 95) so
    /// HP bars, spell bar and chat overlay it cleanly.
    ///
    /// The vignette is at its strongest during dawn / dusk (warm, painterly
    /// edges) and softer during day / night, giving the player a peripheral cue
    /// that "the world is changing" without modifying the global Light2D.
    /// </summary>
    public sealed class DayNightVignetteOverlay : MonoBehaviour
    {
        // The vignette tint matches the live Light2D color and pulls its
        // strength from the per-phase vignetteAlpha that DayNightCycle
        // publishes through CurrentColor / CurrentVignetteAlpha. That keeps
        // a single source of truth — designers and the runtime "AJUSTES DE
        // FASE" panel only edit the cycle's phase look.
        private const float TINT_LERP_SPEED = 0.7f;  // Hz of color smoothing

        private Canvas _canvas;
        private Image  _vignette;
        private Sprite _vignetteSprite;
        private Color  _currentColor = Color.clear;

        private void Start()
        {
            BuildUI();
            // Snap to the current phase color on first frame so we don't fade in
            // from black when the editor enters Play.
            if (DayNightCycle.Instance != null)
                _currentColor = TargetFromCycle(DayNightCycle.Instance);
            if (_vignette != null) _vignette.color = _currentColor;
        }

        private void Update()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null || _vignette == null) return;

            // The "no filters" master switch — when the cycle's lighting is
            // disabled the vignette fades to fully transparent so the world
            // reads at native colors.
            Color target = cycle.LightingEnabled ? TargetFromCycle(cycle) : Color.clear;
            _currentColor = Color.Lerp(_currentColor, target,
                                       1f - Mathf.Exp(-TINT_LERP_SPEED * Time.deltaTime));
            _vignette.color = _currentColor;
        }

        // Convert the cycle's live phase look into a vignette color. The
        // vignette uses the SAME RGB as the global Light2D tint so the world's
        // edges read as a continuation of the global atmosphere; only the
        // alpha varies per phase.
        private static Color TargetFromCycle(DayNightCycle cycle)
        {
            var c = cycle.CurrentColor;
            return new Color(c.r, c.g, c.b, cycle.CurrentVignetteAlpha);
        }

        private void OnDestroy()
        {
            if (_vignetteSprite != null)
            {
                if (_vignetteSprite.texture != null) Destroy(_vignetteSprite.texture);
                Destroy(_vignetteSprite);
                _vignetteSprite = null;
            }
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("DayNightVignetteCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            // Below the gameplay HUD (HUDManager uses 100, clock 105) so HP/MP
            // and the clock sit on top of the vignette.
            _canvas.sortingOrder = 95;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            // Vignette doesn't need raycasts — explicitly skip the raycaster.

            var go  = new GameObject("Vignette", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin        = Vector2.zero;
            rt.anchorMax        = Vector2.one;
            rt.offsetMin        = Vector2.zero;
            rt.offsetMax        = Vector2.zero;

            _vignetteSprite = BuildVignetteSprite();
            _vignette       = go.AddComponent<Image>();
            _vignette.sprite        = _vignetteSprite;
            _vignette.type          = Image.Type.Simple;
            _vignette.preserveAspect = false;
            _vignette.raycastTarget = false;
            _vignette.color         = Color.clear;
        }

        // 64×64 radial gradient: transparent center, opaque corners. Stretched
        // across the whole screen by Image.Type=Simple. The non-linear ramp
        // (squared) keeps the center clean and concentrates intensity near the
        // edges — that's where the eye reads "warm light spill" without losing
        // gameplay readability.
        private static Sprite BuildVignetteSprite()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };
            var px  = new Color32[N * N];
            float cx = (N - 1) * 0.5f;
            float cy = (N - 1) * 0.5f;
            float maxD = Mathf.Sqrt(cx * cx + cy * cy);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - cx, dy = y - cy;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) / maxD;
                // Inner 35% completely transparent; falls off quickly outside that.
                float t = Mathf.SmoothStep(0.35f, 1.0f, d);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(t * t * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }
    }
}
