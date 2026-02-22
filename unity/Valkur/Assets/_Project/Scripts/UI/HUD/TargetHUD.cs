using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Shows a target panel at the top-center of the screen.
    /// Triggers:
    ///   1. Mouse hover over an NPC (via MouseTargetDetector)
    ///   2. Player hits an NPC (via MeleeCombat.OnHitTarget)
    /// Displays: name, FSM state, HP bar, HP text.
    /// Persists while hovering; fades after displayDuration if triggered by hit.
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
        [SerializeField] private float displayDuration = 4f;
        [SerializeField] private float fadeSpeed = 6f;
        [SerializeField] private Color hpColor = new Color(0.86f, 0.24f, 0.24f, 1f);

        private Health _targetHealth;
        private GameObject _targetGo;
        private float _lastInteractionTime = -999f;
        private bool _isHovering;

        /// <summary>
        /// Wire UI element references at runtime (replaces reflection-based field injection).
        /// </summary>
        public void SetUIReferences(CanvasGroup group, TextMeshProUGUI name, TextMeshProUGUI state,
                                     Image hpFillImg, TextMeshProUGUI hpLabel)
        {
            panelGroup = group;
            nameText = name;
            stateText = state;
            hpFill = hpFillImg;
            hpText = hpLabel;
        }

        private void Awake()
        {
            if (panelGroup != null)
                panelGroup.alpha = 0f;
        }

        private void Update()
        {
            // Show while hovering, or for displayDuration after a hit
            bool shouldShow = _targetHealth != null &&
                (_isHovering || Time.time - _lastInteractionTime < displayDuration);

            float targetAlpha = shouldShow ? 1f : 0f;
            if (panelGroup != null)
                panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, targetAlpha, Time.deltaTime * fadeSpeed);

            // Live-update state label and HP while visible
            if (_targetHealth != null && shouldShow)
            {
                UpdateBar(_targetHealth.CurrentHp, _targetHealth.MaxHp);
                UpdateStateLabel();
            }
        }

        /// <summary>
        /// Called by MouseTargetDetector when the hovered entity changes.
        /// Pass null to clear the hover target.
        /// </summary>
        public void SetHoverTarget(GameObject target)
        {
            if (target == null)
            {
                _isHovering = false;
                return;
            }

            var health = target.GetComponent<Health>();
            if (health == null || health.IsDead)
            {
                _isHovering = false;
                return;
            }

            _isHovering = true;
            SetTarget(target, health);
        }

        /// <summary>
        /// Called when the player damages an entity. Shows the target panel with a timer.
        /// </summary>
        public void ShowTarget(GameObject target)
        {
            if (target == null) return;

            var health = target.GetComponent<Health>();
            if (health == null) return;

            _lastInteractionTime = Time.time;
            SetTarget(target, health);
        }

        private void SetTarget(GameObject target, Health health)
        {
            // Skip if same target
            if (_targetGo == target && _targetHealth == health) return;

            // Unsubscribe from previous target
            if (_targetHealth != null)
                _targetHealth.OnHpChanged -= OnTargetHpChanged;

            _targetGo = target;
            _targetHealth = health;
            _targetHealth.OnHpChanged += OnTargetHpChanged;

            // Update name
            if (nameText != null)
            {
                string rawName = target.name.Replace("(Clone)", "").Trim();
                nameText.text = rawName;
            }

            UpdateStateLabel();
            UpdateBar(health.CurrentHp, health.MaxHp);
        }

        private void UpdateStateLabel()
        {
            if (stateText == null || _targetGo == null) return;

            var brain = _targetGo.GetComponent<FSMMonsterBrain>();
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

        private void OnTargetHpChanged(int current, int max)
        {
            _lastInteractionTime = Time.time;
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
