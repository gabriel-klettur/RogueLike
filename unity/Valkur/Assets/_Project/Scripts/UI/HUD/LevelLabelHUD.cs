using TMPro;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Bottom-right "Lvl N" readout. Reads the bound player's current
    /// <see cref="Experience.Level"/> at bind time and refreshes via
    /// <see cref="GameEvents.OnLevelUp"/> filtered to the bound entity.
    /// Event-driven only — no per-frame work.
    /// </summary>
    public sealed class LevelLabelHUD : MonoBehaviour
    {
        private TextMeshProUGUI _label;
        private GameObject _boundEntity;

        public void Bind(Health playerHealth, TextMeshProUGUI label)
        {
            _label = label;
            _boundEntity = playerHealth != null ? playerHealth.gameObject : null;

            GameEvents.OnLevelUp += OnLevelUp;

            if (_boundEntity != null)
            {
                var xp = _boundEntity.GetComponent<Experience>();
                if (xp != null) ApplyLevel(xp.Level);
            }
        }

        private void OnDestroy()
        {
            GameEvents.OnLevelUp -= OnLevelUp;
        }

        private void OnLevelUp(GameObject entity, int newLevel)
        {
            if (entity != _boundEntity) return;
            ApplyLevel(newLevel);
        }

        private void ApplyLevel(int level)
        {
            if (_label == null) return;
            _label.text = $"Lvl {level}";
        }
    }
}
