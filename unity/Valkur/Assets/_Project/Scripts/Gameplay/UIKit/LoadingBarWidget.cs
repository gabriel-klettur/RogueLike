using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Reusable progress-bar widget. Builds the bar chrome (border + inner
    /// background + filled image + percentage label + status text) under
    /// any parent transform, tracks a target progress value, and lerps
    /// toward it on <see cref="Tick"/>.
    ///
    /// Used by both the boot-time <c>LoadingScreenController</c> and the
    /// F11 Map Editor's slot-switch overlay so the two surfaces look
    /// identical — only the surrounding background / tips / feed differ.
    /// Lives in Valkur.UIKit so any assembly that needs a progress bar
    /// (Gameplay, UI, Editor) can mount one without circular references.
    /// </summary>
    public sealed class LoadingBarWidget : MonoBehaviour
    {
        // ── Layout constants ──
        private const float BAR_HEIGHT_PX = 30f;
        private const float BAR_BORDER    = 2f;
        private const float BAR_PADDING   = 3f;
        private const float TEXT_OFFSET_Y = 20f;
        private const float DOTS_INTERVAL = 0.4f;
        private const float DEFAULT_LERP_SPEED = 3.5f;

        // ── Colors ──
        private static readonly Color BarBorderColor = Color.white;
        private static readonly Color BarFillColor   = new Color(0f, 200f / 255f, 0f, 1f);
        private static readonly Color BarInnerBg     = Color.black;
        private static readonly Color TextColor      = Color.white;

        private Image _barFill;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _pctText;

        private float _targetProgress;
        private float _displayedProgress;
        private float _lerpSpeed = DEFAULT_LERP_SPEED;

        // Animated trailing dots — same idle-feel as the boot loader.
        private float  _dotsTimer;
        private int    _dotsCount;
        private string _baseMessage = "Loading";

        public float DisplayedProgress => _displayedProgress;

        // 1×1 white sprite cache. Image.Type.Filled REQUIRES a sprite;
        // without one the bar always renders as if 100 % full because
        // fillAmount is silently ignored. Held statically so multiple
        // widgets share a single 4-byte texture.
        private static Sprite _whiteSprite;
        public static Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "LoadingBarWhite1x1" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "LoadingBarWhiteSprite";
            return _whiteSprite;
        }

        /// <summary>
        /// Build the bar chrome under <paramref name="parent"/> at the
        /// given anchored position (in <paramref name="parent"/>'s local
        /// space). <paramref name="barWidth"/> is the outer-border width;
        /// the fill area insets by border+padding.
        /// </summary>
        public static LoadingBarWidget Mount(Transform parent, Vector2 anchoredPos, float barWidth)
        {
            var go = new GameObject("LoadingBarWidget");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(barWidth, BAR_HEIGHT_PX + 60f);

            var widget = go.AddComponent<LoadingBarWidget>();
            widget.BuildChildren(barWidth);
            return widget;
        }

        private void BuildChildren(float barWidth)
        {
            // Outer border.
            var barOuter = new GameObject("BarOuter");
            barOuter.transform.SetParent(transform, false);
            barOuter.AddComponent<Image>().color = BarBorderColor;
            var outerRt = barOuter.GetComponent<RectTransform>();
            outerRt.anchorMin = new Vector2(0.5f, 0.5f);
            outerRt.anchorMax = new Vector2(0.5f, 0.5f);
            outerRt.pivot     = new Vector2(0.5f, 0.5f);
            outerRt.anchoredPosition = Vector2.zero;
            outerRt.sizeDelta = new Vector2(barWidth, BAR_HEIGHT_PX);

            // Inner black background.
            var innerBg = new GameObject("BarBg");
            innerBg.transform.SetParent(barOuter.transform, false);
            innerBg.AddComponent<Image>().color = BarInnerBg;
            var innerRt = innerBg.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero; innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(BAR_BORDER, BAR_BORDER);
            innerRt.offsetMax = new Vector2(-BAR_BORDER, -BAR_BORDER);

            // Fill area (inset by border + padding).
            float pad = BAR_BORDER + BAR_PADDING;
            var fillArea = new GameObject("BarFillArea");
            fillArea.transform.SetParent(barOuter.transform, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = Vector2.zero; fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(pad, pad);
            fillAreaRt.offsetMax = new Vector2(-pad, -pad);

            // Filled fill image — green progress.
            var fillGo = new GameObject("BarFill");
            fillGo.transform.SetParent(fillArea.transform, false);
            _barFill = fillGo.AddComponent<Image>();
            _barFill.sprite     = GetWhiteSprite();
            _barFill.color      = BarFillColor;
            _barFill.type       = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillOrigin = 0;
            _barFill.fillAmount = 0f;
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;

            // Percentage label (right of bar).
            var pctGo = new GameObject("BarPercent");
            pctGo.transform.SetParent(transform, false);
            _pctText = pctGo.AddComponent<TextMeshProUGUI>();
            _pctText.fontSize  = 14f;
            _pctText.color     = TextColor;
            _pctText.alignment = TextAlignmentOptions.Left;
            _pctText.text      = "0%";
            var pctRt = pctGo.GetComponent<RectTransform>();
            pctRt.anchorMin = new Vector2(0.5f, 0.5f);
            pctRt.anchorMax = new Vector2(0.5f, 0.5f);
            pctRt.pivot     = new Vector2(0f, 0.5f);
            pctRt.anchoredPosition = new Vector2(barWidth * 0.5f + 8f, 0f);
            pctRt.sizeDelta        = new Vector2(54f, BAR_HEIGHT_PX);

            // Status text (above bar).
            var textGo = new GameObject("StatusText");
            textGo.transform.SetParent(transform, false);
            _statusText = textGo.AddComponent<TextMeshProUGUI>();
            _statusText.fontSize  = 18f;
            _statusText.color     = TextColor;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.text      = "Loading...";
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.5f, 0.5f);
            textRt.anchorMax = new Vector2(0.5f, 0.5f);
            textRt.pivot     = new Vector2(0.5f, 0f);
            textRt.anchoredPosition = new Vector2(0f, BAR_HEIGHT_PX * 0.5f + TEXT_OFFSET_Y);
            textRt.sizeDelta        = new Vector2(barWidth, 30f);

            // Defensive invariant — Image.Type.Filled silently ignores
            // fillAmount when its sprite is null and renders as 100 % full
            // from frame 1, which is misleading at a glance. Re-assign
            // proactively if a future refactor drops the assignment above.
            if (_barFill == null || _barFill.sprite == null)
            {
                Debug.LogError("[LoadingBarWidget] _barFill.sprite is null. Forcing fallback.");
                if (_barFill != null) _barFill.sprite = GetWhiteSprite();
            }
        }

        // ── Public API ─────────────────────────────────────────────────

        public void SetTargetProgress(float progress01)
        {
            _targetProgress = Mathf.Clamp01(progress01);
        }

        public void SetStatus(string text)
        {
            _baseMessage = string.IsNullOrEmpty(text) ? "Loading" : text;
        }

        public void SnapToProgress(float progress01)
        {
            _targetProgress = _displayedProgress = Mathf.Clamp01(progress01);
            ApplyProgress(_displayedProgress);
        }

        public void Reset()
        {
            _targetProgress = _displayedProgress = 0f;
            _dotsTimer = 0f;
            _dotsCount = 0;
            ApplyProgress(0f);
        }

        public void SetLerpSpeed(float speed) => _lerpSpeed = Mathf.Max(0.01f, speed);

        /// <summary>
        /// Drive the lerp + dots animation. Pass
        /// <see cref="Time.unscaledDeltaTime"/> when the owning surface
        /// runs while gameplay is paused (overlays do).
        /// </summary>
        public void Tick(float deltaTime)
        {
            _displayedProgress = Mathf.Lerp(_displayedProgress, _targetProgress,
                deltaTime * _lerpSpeed);
            ApplyProgress(_displayedProgress);

            _dotsTimer += deltaTime;
            if (_dotsTimer >= DOTS_INTERVAL)
            {
                _dotsTimer = 0f;
                _dotsCount = (_dotsCount + 1) % 4;
                if (_statusText != null)
                    _statusText.text = _baseMessage + new string('.', _dotsCount);
            }
        }

        private void ApplyProgress(float p)
        {
            p = Mathf.Clamp01(p);
            if (_barFill != null) _barFill.fillAmount = p;
            if (_pctText  != null) _pctText.text = $"{Mathf.RoundToInt(p * 100f)}%";
        }
    }
}
