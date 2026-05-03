using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Tints every <see cref="SpriteRenderer"/> in the player's hierarchy to a
    /// translucent black silhouette while the player is a spirit, and animates
    /// a small vertical bob so the player visibly "floats". Restores the
    /// original colors on revive.
    ///
    /// Uses <see cref="MaterialPropertyBlock"/> so we never instantiate a
    /// per-renderer material clone (which would leak in EditMode tests under
    /// the LogAssert.ignoreFailingMessages rule and would also defeat the
    /// SRP batcher).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSpiritVisuals : MonoBehaviour
    {
        [Header("Spirit Look")]
        [SerializeField, Tooltip("Fully tinted color while spirit. Pure opaque black = pitch silhouette.")]
        private Color spiritColor = new Color(0f, 0f, 0f, 1f);

        [SerializeField, Tooltip("How fast the color blends in/out (seconds).")]
        private float colorFadeDuration = 0.6f;

        [Header("Bob")]
        [SerializeField, Tooltip("Vertical float amplitude in world units.")]
        private float bobAmplitude = 0.08f;

        [SerializeField, Tooltip("Bob frequency in Hz.")]
        private float bobFrequency = 1.4f;

        private static readonly int s_ColorId = Shader.PropertyToID("_Color");

        private struct CachedRenderer
        {
            public SpriteRenderer Renderer;
            public Color OriginalColor;
        }

        private readonly List<CachedRenderer> _renderers = new List<CachedRenderer>();
        private MaterialPropertyBlock _mpb;

        private bool _active;
        private float _t;
        private float _bobAccumulator;
        private float _baseLocalY;
        private bool _baseCaptured;
        private Transform _bobTarget;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            // Bob a child sprite transform if available — bobbing the player root
            // would fight the Rigidbody2D every frame and break physics interpolation.
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.transform != transform)
                _bobTarget = sr.transform;
        }

        private void OnEnable()
        {
            CacheRenderers();
        }

        private void Update()
        {
            if (!_active) return;

            _t = Mathf.Min(_t + Time.deltaTime, colorFadeDuration);
            float k = colorFadeDuration > 0f ? _t / colorFadeDuration : 1f;
            ApplyTint(k);

            _bobAccumulator += Time.deltaTime;
            if (_baseCaptured && _bobTarget != null)
            {
                float bob = Mathf.Sin(_bobAccumulator * bobFrequency * Mathf.PI * 2f) * bobAmplitude * k;
                var lp = _bobTarget.localPosition;
                lp.y = _baseLocalY + bob;
                _bobTarget.localPosition = lp;
            }
        }

        /// <summary>Begin the spirit fade-in. Idempotent.</summary>
        public void Activate()
        {
            if (_active) return;
            _active = true;
            _t = 0f;
            _bobAccumulator = 0f;
            CacheRenderers();
            if (_bobTarget == null)
            {
                var sr = GetComponentInChildren<SpriteRenderer>();
                if (sr != null && sr.transform != transform)
                    _bobTarget = sr.transform;
            }
            if (!_baseCaptured && _bobTarget != null)
            {
                _baseLocalY = _bobTarget.localPosition.y;
                _baseCaptured = true;
            }
        }

        /// <summary>Restore original colors and zero out the bob offset.</summary>
        public void Deactivate()
        {
            _active = false;
            _t = 0f;

            for (int i = 0; i < _renderers.Count; i++)
            {
                var entry = _renderers[i];
                if (entry.Renderer == null) continue;
                entry.Renderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(s_ColorId, entry.OriginalColor);
                entry.Renderer.SetPropertyBlock(_mpb);
                entry.Renderer.color = entry.OriginalColor;
            }

            if (_baseCaptured && _bobTarget != null)
            {
                var lp = _bobTarget.localPosition;
                lp.y = _baseLocalY;
                _bobTarget.localPosition = lp;
            }
        }

        private void CacheRenderers()
        {
            _renderers.Clear();
            var found = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            for (int i = 0; i < found.Length; i++)
            {
                _renderers.Add(new CachedRenderer
                {
                    Renderer = found[i],
                    OriginalColor = found[i].color,
                });
            }
        }

        private void ApplyTint(float k)
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                var entry = _renderers[i];
                if (entry.Renderer == null) continue;
                Color blended = Color.Lerp(entry.OriginalColor, spiritColor, k);
                entry.Renderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(s_ColorId, blended);
                entry.Renderer.SetPropertyBlock(_mpb);
                entry.Renderer.color = blended;
            }
        }
    }
}
