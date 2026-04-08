using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Displays the player's combo count — a counter that fades in/out with the combo state.
    /// Mirrors Python's ComboBarRenderSystem (center-screen counter with glow).
    ///
    /// Wire: ComboHUD sits on a Canvas child. PlayerHUD or HUDBootstrap calls Initialize().
    /// </summary>
    public class ComboHUD : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Root panel that fades in when combo is active")]
        [SerializeField] private CanvasGroup panel;

        [Tooltip("Label showing the combo number (e.g. '7x')")]
        [SerializeField] private TextMeshProUGUI comboLabel;

        [Tooltip("Optional fill bar showing time remaining in combo window")]
        [SerializeField] private Image windowBar;

        [Header("Colors")]
        [SerializeField] private Color normalColor   = new Color(1f, 0.85f, 0.3f, 1f); // gold
        [SerializeField] private Color breakColor    = new Color(0.9f, 0.3f, 0.1f, 1f); // orange-red
        [SerializeField] private Color highComboColor = new Color(1f, 0.4f, 0.1f, 1f); // hot orange

        [Header("Thresholds")]
        [Tooltip("Combo count at which color shifts to highComboColor")]
        [SerializeField] private int highComboThreshold = 10;

        [Header("Animation")]
        [SerializeField] private float fadeSpeed = 5f;
        [SerializeField] private float punchScale = 1.3f;
        [SerializeField] private float punchDecay = 8f;

        private ComboCounter _combo;
        private float _currentScale = 1f;
        private float _targetAlpha = 0f;

        // ── Init ──────────────────────────────────────────────────────────

        public void Initialize(ComboCounter combo)
        {
            if (_combo != null)
            {
                _combo.OnComboChanged -= HandleComboChanged;
                _combo.OnComboReset   -= HandleComboReset;
            }

            _combo = combo;

            if (_combo != null)
            {
                _combo.OnComboChanged += HandleComboChanged;
                _combo.OnComboReset   += HandleComboReset;
            }

            if (panel != null) panel.alpha = 0f;
        }

        // ── Update ────────────────────────────────────────────────────────

        private void Update()
        {
            // Fade panel in/out
            if (panel != null)
            {
                bool active = _combo != null && _combo.IsActive;
                _targetAlpha = active ? 1f : 0f;
                panel.alpha = Mathf.Lerp(panel.alpha, _targetAlpha, Time.deltaTime * fadeSpeed);
            }

            // Punch scale decay
            _currentScale = Mathf.Lerp(_currentScale, 1f, Time.deltaTime * punchDecay);
            if (comboLabel != null)
                comboLabel.transform.localScale = Vector3.one * _currentScale;

            // Window bar fill
            if (windowBar != null && _combo != null && _combo.IsActive)
            {
                float elapsed = _combo.WindowEnd - Time.time;
                float maxWindow = 2f; // approximate; actual window varies
                windowBar.fillAmount = Mathf.Clamp01(elapsed / maxWindow);
            }
            else if (windowBar != null)
            {
                windowBar.fillAmount = 0f;
            }
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void HandleComboChanged(int count)
        {
            if (comboLabel != null)
                comboLabel.text = $"{count}x";

            // Update label color
            if (comboLabel != null)
            {
                comboLabel.color = count >= highComboThreshold ? highComboColor : normalColor;
            }

            // Punch scale
            _currentScale = punchScale;
        }

        private void HandleComboReset(int finalCount)
        {
            if (comboLabel != null)
            {
                comboLabel.text = $"{finalCount}x";
                if (_combo != null && _combo.IsBreakFlashing)
                    comboLabel.color = breakColor;
            }
        }

        private void OnDestroy()
        {
            if (_combo != null)
            {
                _combo.OnComboChanged -= HandleComboChanged;
                _combo.OnComboReset   -= HandleComboReset;
            }
        }
    }
}
