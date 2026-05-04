using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Base class for every weather visual: a camera-following
    /// <see cref="ParticleSystem"/> + an optional looping audio bed. Subclasses
    /// only need to populate the particle config in <see cref="ConfigureParticles"/>
    /// and the (optional) audio clip in <see cref="ResolveAudioClip"/>.
    ///
    /// All concrete effects share the same lifecycle (Start → idle, Activate →
    /// fade in, Deactivate → fade out, OnDestroy → release) so the
    /// <see cref="WeatherManager"/> can stack / combine them without per-type
    /// special casing.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public abstract class WeatherEffect : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField, Tooltip("Master volume for the looping audio bed. " +
                                  "Multiplied with the live fade so a player can keep ambient clearly under combat SFX.")]
        protected float _audioMasterVolume = 0.40f;

        [SerializeField, Tooltip("Seconds for emission rate + audio to ramp up when activated, and back down on deactivate.")]
        protected float _fadeSeconds = 1.5f;

        protected ParticleSystem _ps;
        protected ParticleSystem.MainModule _main;
        protected ParticleSystem.EmissionModule _emission;
        protected float _baseEmissionRate;
        protected AudioSource _audioSrc;
        protected Camera _trackedCamera;

        // Fade animation state — single timer; activation polarity flips _targetActive.
        protected bool  _targetActive;
        protected float _fadeAlpha;     // 0..1, 1 = fully active

        public bool IsActive => _targetActive;

        protected virtual void Awake()
        {
            _ps       = GetComponent<ParticleSystem>();
            _main     = _ps.main;
            _emission = _ps.emission;
            ConfigureParticles();
            _baseEmissionRate = _emission.rateOverTime.constant;
            // Start emission off — Activate() raises it.
            var emit = _emission;
            emit.rateOverTime = 0f;
            _ps.Play();

            var clip = ResolveAudioClip();
            if (clip != null)
            {
                _audioSrc = gameObject.AddComponent<AudioSource>();
                _audioSrc.clip                  = clip;
                _audioSrc.loop                  = true;
                _audioSrc.spatialBlend          = 0f;
                _audioSrc.volume                = 0f;
                _audioSrc.priority              = 220;
                _audioSrc.bypassEffects         = true;
                _audioSrc.bypassListenerEffects = true;
                _audioSrc.bypassReverbZones     = true;
                _audioSrc.playOnAwake           = false;
            }
        }

        protected virtual void Update()
        {
            if (_trackedCamera == null) _trackedCamera = Camera.main;
            if (_trackedCamera != null)
            {
                Vector3 p = _trackedCamera.transform.position;
                p.z = transform.position.z;
                transform.position = p;

                // Resize the emission box to the live viewport so particles
                // always cover the player's full visible area regardless of
                // ortho zoom. Without this, the spawn box stayed at the
                // designer-time literal (36 wu) and most particles spawned
                // off-screen, dying before they ever became visible.
                if (_trackedCamera.orthographic)
                {
                    float halfH = _trackedCamera.orthographicSize;
                    float halfW = halfH * Mathf.Max(0.0001f, _trackedCamera.aspect);
                    UpdateEmissionForViewport(halfW, halfH);
                }
            }

            // Smooth fade toward the target activation level so flipping the
            // weather doesn't pop emission/audio harshly.
            float target = _targetActive ? 1f : 0f;
            if (!Mathf.Approximately(_fadeAlpha, target))
            {
                float step = (target > _fadeAlpha ? 1f : -1f) * (Time.deltaTime / Mathf.Max(0.01f, _fadeSeconds));
                _fadeAlpha = Mathf.Clamp01(_fadeAlpha + step);
                ApplyFade();
            }

            // Subclasses may want per-frame tweaks (e.g. wind gusts).
            OnTick();
        }

        /// <summary>
        /// Subclasses override to position + size their emission box and tune
        /// particle lifetime so the falling / blowing trail covers the entire
        /// visible area (plus a small margin) at any zoom level.
        /// </summary>
        /// <param name="halfW">Half the viewport width in world units.</param>
        /// <param name="halfH">Half the viewport height in world units.</param>
        protected virtual void UpdateEmissionForViewport(float halfW, float halfH) { }

        public void Activate()
        {
            if (_targetActive) return;
            _targetActive = true;
            if (_audioSrc != null && !_audioSrc.isPlaying) _audioSrc.Play();
        }

        public void Deactivate()
        {
            if (!_targetActive) return;
            _targetActive = false;
            // Audio source keeps playing while it fades, then is cut in
            // ApplyFade() once volume reaches zero.
        }

        protected virtual void ApplyFade()
        {
            var emit = _emission;
            emit.rateOverTime = _baseEmissionRate * _fadeAlpha;
            if (_audioSrc != null)
            {
                _audioSrc.volume = _audioMasterVolume * _fadeAlpha;
                if (_fadeAlpha <= 0.001f && _audioSrc.isPlaying) _audioSrc.Stop();
            }
        }

        // Subclass extension points.
        protected abstract void ConfigureParticles();
        protected virtual AudioClip ResolveAudioClip() => null;
        protected virtual void OnTick() { }
    }
}
