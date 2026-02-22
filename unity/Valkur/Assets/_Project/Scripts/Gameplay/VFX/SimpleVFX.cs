using UnityEngine;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Lightweight VFX component that plays a timed visual effect (scale + fade).
    /// Maps to Python's ParticleComponent lifecycle (age, lifespan, size_over_life, alpha_over_life).
    /// 
    /// Supports auto-despawn back to VFXManager pool.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SimpleVFX : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private float _duration;
        private float _elapsed;
        private float _scale;
        private Color _color;
        private string _poolKey;
        private VFXManager _manager;
        private bool _playing;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Start the VFX animation. Called by VFXManager after spawning.
        /// </summary>
        public void Play(Color color, float duration, float scale, string poolKey, VFXManager manager)
        {
            _color = color;
            _duration = Mathf.Max(0.01f, duration);
            _scale = scale;
            _poolKey = poolKey;
            _manager = manager;
            _elapsed = 0f;
            _playing = true;

            transform.localScale = Vector3.one * _scale;
            if (_sr != null)
                _sr.color = _color;
        }

        private void Update()
        {
            if (!_playing) return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            // Scale: grow slightly then shrink
            float scaleCurve = EvaluateScaleCurve(t);
            transform.localScale = Vector3.one * _scale * scaleCurve;

            // Alpha: fade out
            float alpha = 1f - t;
            if (_sr != null)
                _sr.color = new Color(_color.r, _color.g, _color.b, _color.a * alpha);

            if (_elapsed >= _duration)
            {
                _playing = false;
                ReturnToPool();
            }
        }

        private float EvaluateScaleCurve(float t)
        {
            // Quick expand (0->0.3) then slow shrink (0.3->1.0)
            if (t < 0.3f)
                return Mathf.Lerp(0.5f, 1.2f, t / 0.3f);
            return Mathf.Lerp(1.2f, 0.1f, (t - 0.3f) / 0.7f);
        }

        private void ReturnToPool()
        {
            if (_manager != null && !string.IsNullOrEmpty(_poolKey))
            {
                _manager.Despawn(_poolKey, gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            _playing = false;
        }
    }
}
