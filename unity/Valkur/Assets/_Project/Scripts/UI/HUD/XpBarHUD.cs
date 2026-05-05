using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Bottom-center XP bar with smooth fill, level label and XP/Next text.
    /// Mirrors Python's <c>ExperienceRenderSystem</c> (50% screen width, centered).
    ///
    /// Subscribes to the player's <see cref="Experience"/> events when bound;
    /// also listens to <see cref="GameEvents.OnLevelUp"/> as a safety net so it
    /// catches level-ups produced by sources other than direct XP gain (level
    /// commands, gifts, retroactive grants).
    /// </summary>
    public class XpBarHUD : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Image fill;
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI label;

        [Header("Colors")]
        [SerializeField] private Color fillColor    = new Color(0.31f, 0.55f, 1f, 1f);
        [SerializeField] private Color levelUpFlash = new Color(1f, 0.92f, 0.35f, 1f);

        [Header("Settings")]
        [SerializeField] private float smoothSpeed   = 8f;
        [SerializeField] private float flashDuration = 0.6f;

        private Experience _xp;
        private GameObject _boundEntity;
        private float _targetFill;
        private float _flashTimer;

        public bool IsBound => _xp != null;
        public float DisplayedFill => fill != null ? fill.fillAmount : 0f;
        public float TargetFill    => _targetFill;
        public Experience BoundExperience => _xp;

        public void SetUIReferences(Image fillImg, Image bgImg, TextMeshProUGUI labelText)
        {
            fill = fillImg;
            background = bgImg;
            label = labelText;
        }

        public void Bind(Experience experience)
        {
            UnbindCurrent();

            _xp = experience;
            if (_xp == null) return;

            _boundEntity = _xp.gameObject;
            _xp.OnXpGained += OnXpGained;
            _xp.OnLevelUp  += OnLevelUp;
            GameEvents.OnLevelUp += OnGlobalLevelUp;

            RefreshAll();
        }

        private void UnbindCurrent()
        {
            if (_xp != null)
            {
                _xp.OnXpGained -= OnXpGained;
                _xp.OnLevelUp  -= OnLevelUp;
            }
            GameEvents.OnLevelUp -= OnGlobalLevelUp;
            _xp = null;
            _boundEntity = null;
        }

        private void OnDestroy() => UnbindCurrent();

        private void OnXpGained(int _) => RefreshAll();
        private void OnLevelUp(int _)  { RefreshAll(); FlashLevelUp(); }
        private void OnGlobalLevelUp(GameObject entity, int _)
        {
            if (entity == _boundEntity) FlashLevelUp();
        }

        private void Update()
        {
            if (fill == null) return;

            fill.fillAmount = Mathf.Lerp(fill.fillAmount, _targetFill, Time.deltaTime * smoothSpeed);

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_flashTimer / flashDuration);
                fill.color = Color.Lerp(fillColor, levelUpFlash, t);
            }
            else if (fill.color != fillColor)
            {
                fill.color = fillColor;
            }
        }

        /// <summary>Public for tests — drives the lerp deterministically.</summary>
        public void Tick(float deltaTime)
        {
            if (fill == null) return;
            fill.fillAmount = Mathf.Lerp(fill.fillAmount, _targetFill, deltaTime * smoothSpeed);
            if (_flashTimer > 0f) _flashTimer = Mathf.Max(0f, _flashTimer - deltaTime);
        }

        public void RefreshAll()
        {
            if (_xp == null)
            {
                _targetFill = 0f;
                if (label != null) label.text = "Lvl 0   0/0";
                return;
            }

            _targetFill = Mathf.Clamp01(_xp.NormalizedProgress);
            if (label != null)
            {
                int curr = _xp.XpInCurrentLevel;
                int next = Mathf.Max(1, _xp.XpForNextLevel - _xp.XpRequiredForLevel(_xp.Level));
                label.text = $"Lvl {_xp.Level}   {curr}/{next}";
            }
        }

        private void FlashLevelUp() => _flashTimer = flashDuration;

        /// <summary>Test seam — force the bar's displayed fill to a value.</summary>
        public void SnapToTarget()
        {
            if (fill != null) fill.fillAmount = _targetFill;
        }
    }
}
