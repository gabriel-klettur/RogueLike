using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Shows a target panel at the top-center of the screen when the player hits an enemy.
    /// Mirrors Python's TargetHudRenderSystem: name, state label, HP bar, HP text.
    /// Auto-hides after a configurable TTL since last hit.
    /// </summary>
    public class TargetHUD : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI stateText;
        [SerializeField] private Image hpFill;
        [SerializeField] private TextMeshProUGUI hpText;

        [Header("Settings")]
        [SerializeField] private float displayDuration = 3f;
        [SerializeField] private float fadeSpeed = 4f;
        [SerializeField] private Color hpColor = new Color(0.86f, 0.24f, 0.24f, 1f);

        private Health _targetHealth;
        private float _lastHitTime = -999f;
        private float _targetAlpha;

        private void Awake()
        {
            if (panelGroup != null)
                panelGroup.alpha = 0f;
        }

        private void Update()
        {
            bool shouldShow = Time.time - _lastHitTime < displayDuration && _targetHealth != null;
            _targetAlpha = shouldShow ? 1f : 0f;

            if (panelGroup != null)
                panelGroup.alpha = Mathf.Lerp(panelGroup.alpha, _targetAlpha, Time.deltaTime * fadeSpeed);

            if (_targetHealth != null && shouldShow)
                UpdateBar(_targetHealth.CurrentHp, _targetHealth.MaxHp);
        }

        /// <summary>
        /// Called when the player damages an entity. Updates the target panel.
        /// </summary>
        public void ShowTarget(GameObject target)
        {
            if (target == null) return;

            var health = target.GetComponent<Health>();
            if (health == null) return;

            // Unsubscribe from previous target
            if (_targetHealth != null)
                _targetHealth.OnHpChanged -= OnTargetHpChanged;

            _targetHealth = health;
            _targetHealth.OnHpChanged += OnTargetHpChanged;
            _lastHitTime = Time.time;

            if (nameText != null)
                nameText.text = target.name.Replace("(Clone)", "").Trim();

            if (stateText != null)
            {
                // Try to get FSM state label
                var brain = target.GetComponent<FSMMonsterBrain>();
                if (brain != null)
                {
                    string stateName = brain.CurrentStateName;
                    stateText.text = stateName;
                    stateText.gameObject.SetActive(!string.IsNullOrEmpty(stateName));
                }
                else
                {
                    stateText.gameObject.SetActive(false);
                }
            }

            UpdateBar(health.CurrentHp, health.MaxHp);
        }

        private void OnTargetHpChanged(int current, int max)
        {
            _lastHitTime = Time.time;
            UpdateBar(current, max);
        }

        private void UpdateBar(int current, int max)
        {
            if (hpFill != null)
            {
                float ratio = max > 0 ? (float)current / max : 0f;
                hpFill.fillAmount = ratio;
                hpFill.color = hpColor;
            }

            if (hpText != null)
                hpText.text = $"{Mathf.Max(0, current)} / {max}";
        }

        private void OnDestroy()
        {
            if (_targetHealth != null)
                _targetHealth.OnHpChanged -= OnTargetHpChanged;
        }
    }
}
