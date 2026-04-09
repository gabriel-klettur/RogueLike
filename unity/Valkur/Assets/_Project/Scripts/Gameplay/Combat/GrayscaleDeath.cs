using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Applies a grayscale tint to a SpriteRenderer on death.
    /// Mirrors Python's death_tint_system (GRAY=(100,100,100)).
    /// Attach to entities — auto-triggers via Health death event.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class GrayscaleDeath : MonoBehaviour
    {
        [SerializeField, Tooltip("Color to tint sprite on death.")]
        private Color deathTint = new Color(100f/255f, 100f/255f, 100f/255f, 1f);

        [SerializeField, Tooltip("Tint fade-in speed (seconds).")]
        private float fadeSpeed = 0.5f;

        private SpriteRenderer _sr;
        private Color _originalColor;
        private bool _dying;
        private float _t;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _originalColor = _sr.color;
        }

        /// <summary>Call when entity dies to begin grayscale fade.</summary>
        public void TriggerDeath()
        {
            _dying = true;
            _t = 0f;
        }

        private void Update()
        {
            if (!_dying) return;
            _t += Time.deltaTime / fadeSpeed;
            _sr.color = Color.Lerp(_originalColor, deathTint, Mathf.Clamp01(_t));
        }

        /// <summary>Reset to original color (e.g. on respawn).</summary>
        public void ResetTint()
        {
            _dying = false;
            if (_sr != null) _sr.color = _originalColor;
        }
    }
}
