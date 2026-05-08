using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Screen-space HUD showing player HP and MP bars with text.
    /// Mirrors Python's HUDStatsRenderSystem (bottom-left) + HealthBarSystem + ManaBarRenderSystem.
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Health playerHealth;

        [Header("HP Bar")]
        [SerializeField] private Image hpFill;
        [SerializeField] private Image hpBackground;
        [SerializeField] private TextMeshProUGUI hpText;

        [Header("MP Bar")]
        [SerializeField] private Image mpFill;
        [SerializeField] private Image mpBackground;
        [SerializeField] private TextMeshProUGUI mpText;

        [Header("Colors")]
        [SerializeField] private Color hpColor = new Color(0.2f, 0.9f, 0.2f, 1f);
        [SerializeField] private Color hpLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        [SerializeField] private Color mpColor = new Color(0.31f, 0.47f, 1f, 1f);
        [SerializeField] private Color bgColor = new Color(0.16f, 0.16f, 0.16f, 0.85f);

        [Header("Settings")]
        [SerializeField] private float lowHpThreshold = 0.25f;
        [SerializeField] private float smoothSpeed = 8f;

        private float _targetHpFill = 1f;
        private float _targetMpFill = 1f;
        private int _currentMp = 100;
        private int _maxMp = 100;

        /// <summary>
        /// Wire UI element references at runtime (replaces reflection-based field injection).
        /// </summary>
        public void SetUIReferences(Image hpFillImg, Image hpBgImg, TextMeshProUGUI hpLabel,
                                     Image mpFillImg, Image mpBgImg, TextMeshProUGUI mpLabel)
        {
            hpFill = hpFillImg;
            hpBackground = hpBgImg;
            hpText = hpLabel;
            mpFill = mpFillImg;
            mpBackground = mpBgImg;
            mpText = mpLabel;
        }

        public void Initialize(Health health)
        {
            if (playerHealth != null)
                playerHealth.OnHpChanged -= OnHpChanged;

            playerHealth = health;

            if (playerHealth != null)
            {
                playerHealth.OnHpChanged += OnHpChanged;
                OnHpChanged(playerHealth.CurrentHp, playerHealth.MaxHp);
            }

            UpdateMpDisplay();
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
                playerHealth.OnHpChanged -= OnHpChanged;
        }

        private void Update()
        {
            if (hpFill != null)
            {
                hpFill.fillAmount = Mathf.Lerp(hpFill.fillAmount, _targetHpFill, Time.deltaTime * smoothSpeed);

                float ratio = hpFill.fillAmount;
                hpFill.color = ratio <= lowHpThreshold ? hpLowColor : hpColor;
            }

            if (mpFill != null)
                mpFill.fillAmount = Mathf.Lerp(mpFill.fillAmount, _targetMpFill, Time.deltaTime * smoothSpeed);
        }

        private void OnHpChanged(int current, int max)
        {
            _targetHpFill = max > 0 ? (float)current / max : 0f;

            if (hpText != null)
                hpText.text = $"{current}/{max}";
        }

        public void SetMana(int current, int max)
        {
            _currentMp = current;
            _maxMp = max;
            _targetMpFill = max > 0 ? (float)current / max : 0f;
            UpdateMpDisplay();
        }

        private void UpdateMpDisplay()
        {
            if (mpText != null)
                mpText.text = $"{_currentMp}/{_maxMp}";
        }
    }
}
