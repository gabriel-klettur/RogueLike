using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Breathing scale animation for the XP orb. Applies a sinusoidal
    /// multiplier to <c>localScale</c> so the orb gently pulses, drawing
    /// the player's eye without competing with combat VFX.
    ///
    /// Pure transform-driven: no allocation per frame, no Update on the
    /// physics object. Survives object-pool re-use because Awake captures
    /// the base scale only once and <see cref="ResetForReuse"/> lets pools
    /// rewind state on rent.
    /// </summary>
    public class XpOrbPulse : MonoBehaviour
    {
        [Tooltip("Peak scale offset added on top of the base scale (0.12 = ±12%).")]
        [Range(0f, 0.5f)] [SerializeField] private float amplitude = 0.12f;

        [Tooltip("Pulses per second.")]
        [Min(0.1f)] [SerializeField] private float frequency = 1.4f;

        [Tooltip("Phase offset in seconds — randomised on Awake so a cluster of " +
                 "orbs doesn't pulse in lock-step.")]
        [SerializeField] private float phaseOffset;

        private Vector3 _baseScale;
        private float   _elapsed;
        private bool    _initialized;

        private void Awake() => EnsureInitialized();

        // Lazy init lets EditMode tests (where Awake doesn't reliably fire on
        // AddComponent) drive the pulse via Tick without a captured-as-zero
        // base scale.
        private void EnsureInitialized()
        {
            if (_initialized) return;
            _baseScale = transform.localScale;
            if (_baseScale == Vector3.zero) _baseScale = Vector3.one;
            phaseOffset = Random.Range(0f, 1f / frequency);
            _initialized = true;
        }

        private void Update()
        {
            EnsureInitialized();
            _elapsed += Time.deltaTime;
            ApplyScaleAt(_elapsed);
        }

        /// <summary>Test seam — drive the animation deterministically.</summary>
        public void Tick(float deltaTime)
        {
            EnsureInitialized();
            _elapsed += deltaTime;
            ApplyScaleAt(_elapsed);
        }

        /// <summary>Reset accumulated time so a pooled orb starts fresh.</summary>
        public void ResetForReuse()
        {
            EnsureInitialized();
            _elapsed = 0f;
            transform.localScale = _baseScale;
        }

        private void ApplyScaleAt(float t)
        {
            float k = 1f + Mathf.Sin((t + phaseOffset) * frequency * Mathf.PI * 2f) * amplitude;
            transform.localScale = _baseScale * k;
        }
    }
}
