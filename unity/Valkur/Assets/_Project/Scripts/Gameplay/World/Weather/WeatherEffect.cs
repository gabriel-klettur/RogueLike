using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Base class for every weather visual: a stack of camera-following
    /// <see cref="WeatherLayer"/> depth slices plus an optional looping audio bed.
    ///
    /// Subclasses supply the art in <see cref="BuildLayers"/> and may push their slices
    /// around in <see cref="LayoutForViewport"/> and <see cref="OnTick"/>. Everything the
    /// three effects have in common lives here, and each of those pieces was previously
    /// missing or duplicated three ways:
    ///
    ///   • <b>Depth.</b> An effect is a LIST of systems, not one. See <see cref="WeatherLayer"/>.
    ///   • <b>Density.</b> Activation (<see cref="_fade"/>) and how hard it is falling
    ///     (<see cref="_level"/>) are separate scalars, so raising a live weather from Light
    ///     to Heavy ramps its density without restarting the fade — and turning it off fades
    ///     out from wherever it was.
    ///   • <b>Day/night.</b> Particle materials are unlit, so the URP Global Light 2D the
    ///     cycle drives reaches none of these quads. Without folding the tint into the start
    ///     colour, midnight rain renders at noon brightness over a world at a few percent of
    ///     it. Same reasoning, and the same channel floor, as <c>ParticleEmitter.AmbientLight</c>.
    ///   • <b>Materials.</b> Through <c>ParticleMaterialCache</c>, on the URP particle shader.
    ///     Each effect used to build its own <c>Material</c> from <c>Particles/Standard Unlit</c>
    ///     — a built-in-pipeline shader — and assign it to <c>renderer.material</c>, which
    ///     clones it again per renderer and breaks SRP batching on top of the wrong shader.
    ///
    /// Lifecycle: Awake builds and starts every layer at zero emission; <see cref="SetIntensity"/>
    /// raises it; the fade runs both ways from whatever value it is at.
    /// </summary>
    public abstract partial class WeatherEffect : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField, Tooltip("Master volume of the looping audio bed, before the player's " +
                                 "ambient slider and the live fade.")]
        private float _audioMasterVolume = 0.32f;

        [SerializeField, Tooltip("Seconds for the effect to fade fully in or out. Weather that " +
                                 "snaps on reads as a toggle; weather that takes a couple of " +
                                 "seconds reads as a front moving in.")]
        private float _fadeSeconds = 2.2f;

        [SerializeField, Tooltip("Extra world units beyond the visible viewport that emitters " +
                                 "spawn from, so nothing pops into existence at the screen edge.")]
        private float _viewportMargin = 2.5f;

        // ── layers ───────────────────────────────────────────────────────────────────

        /// <summary>The depth slices, far to near in the order the subclass created them.</summary>
        protected readonly List<WeatherLayer> Layers = new List<WeatherLayer>();

        // ── live state ───────────────────────────────────────────────────────────────

        private Camera _trackedCamera;
        private AudioSource _audioSrc;

        private WeatherIntensity _intensity = WeatherIntensity.Off;
        private float _targetLevel;   // density the level asks for, 0..1
        private float _level;         // smoothed density
        private float _fade;          // activation envelope, 0..1

        private Color _ambient = Color.white;
        private float _ambientTimer;

        private float _halfW = 10f;
        private float _halfH = 5f;
        private bool  _laidOut;

        private bool  _built;
        private float _appliedDensity = -1f;
        private float _appliedFade    = -1f;
        private Color _appliedAmbient = Color.white;

        // ── ambient tint ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Per-channel floor on the day/night multiplier. Weather is the layer between the
        /// player and everything else: multiplied all the way to the cycle's literal night
        /// keyframe it stops separating from the ground it falls over, and precipitation you
        /// cannot see at night reads as the effect having switched itself off. Slightly
        /// higher than the vegetation floor for that reason — falling water catches the sky,
        /// which is always the brightest thing left after dark.
        /// </summary>
        private const float AmbientChannelFloor = 0.34f;

        /// <summary>Seconds between day/night re-reads. The tint only moves during the two ramps.</summary>
        private const float AmbientTickSeconds = 0.4f;

        // ── public API ───────────────────────────────────────────────────────────────

        /// <summary>The level this effect was last asked for.</summary>
        public WeatherIntensity Level => _intensity;

        /// <summary>True while the effect is at any level above Off.</summary>
        public bool IsActive => _intensity != WeatherIntensity.Off;

        /// <summary>
        /// Live density, 0..1 — the level scalar times the activation fade. This is what the
        /// grade and the wind field are driven from, so both ramp with the particles instead
        /// of snapping when a toggle is clicked.
        /// </summary>
        public float Density => Mathf.Clamp01(_level * _fade);

        /// <summary>Half the live viewport width, in world units.</summary>
        protected float HalfWidth => _halfW;

        /// <summary>Half the live viewport height, in world units.</summary>
        protected float HalfHeight => _halfH;

        /// <summary>Extra world units emitters are pushed beyond the visible edge.</summary>
        protected float ViewportMargin => _viewportMargin;

        /// <summary>The activation envelope, 0..1. Alpha rides this; density rides <see cref="Density"/>.</summary>
        protected float FadeAlpha => _fade;

        /// <summary>What <see cref="Activate"/> turns the weather on at.</summary>
        protected virtual WeatherIntensity DefaultIntensity => WeatherIntensity.Medium;

        /// <summary>Set the level. Off fades the effect out; anything else fades it in.</summary>
        public void SetIntensity(WeatherIntensity level)
        {
            _intensity   = level;
            _targetLevel = level.ToScalar();
            if (level != WeatherIntensity.Off && _audioSrc != null && !_audioSrc.isPlaying)
                _audioSrc.Play();
        }

        /// <summary>Turn the weather on at <see cref="DefaultIntensity"/>.</summary>
        public void Activate()
        {
            if (IsActive) return;
            SetIntensity(DefaultIntensity);
        }

        /// <summary>Fade the weather out. The audio bed stops once its volume reaches zero.</summary>
        public void Deactivate() => SetIntensity(WeatherIntensity.Off);

        // ── lifecycle ────────────────────────────────────────────────────────────────

        protected virtual void Awake() => EnsureBuilt();

        /// <summary>
        /// Build the depth slices and the audio bed, once.
        ///
        /// Public and idempotent rather than folded into <see cref="Awake"/>, because Unity
        /// does NOT call Awake on a component added in Edit Mode — only in Play Mode, or on a
        /// type marked <c>[ExecuteAlways]</c>. Marking this hierarchy ExecuteAlways to satisfy
        /// EditMode tests would be the wrong trade by a wide margin: the frame loop would then
        /// run in the editor scene, spawning weather particles into whatever the author has
        /// open. So the tests call this instead, and get exactly the object Play Mode builds.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            BuildLayers();

            foreach (var layer in Layers)
            {
                layer.SetRate(0f);
                layer.SetTint(Color.white, 0f);
                layer.System.Play();
            }

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
            float dt = Time.deltaTime;

            TrackCamera();
            AdvanceEnvelopes(dt);
            RefreshAmbient(dt);
            ApplyToLayers();
            ApplyAudio();

            OnTick(dt);
        }

        // ── camera + viewport ────────────────────────────────────────────────────────

        private void TrackCamera()
        {
            if (_trackedCamera == null) _trackedCamera = Camera.main;
            if (_trackedCamera == null) return;

            Vector3 p = _trackedCamera.transform.position;
            p.z = transform.position.z;
            transform.position = p;

            if (!_trackedCamera.orthographic) return;

            float halfH = _trackedCamera.orthographicSize;
            float halfW = halfH * Mathf.Max(0.0001f, _trackedCamera.aspect);

            // Re-laying out costs a shape write and a lifetime write per layer, and the ortho
            // size is constant for the whole session unless the window is resized — so gate
            // on a real change rather than paying it every frame. The first frame always
            // passes, because _laidOut starts false.
            if (_laidOut &&
                Mathf.Abs(halfW - _halfW) < 0.01f &&
                Mathf.Abs(halfH - _halfH) < 0.01f)
                return;

            _halfW   = halfW;
            _halfH   = halfH;
            _laidOut = true;
            LayoutForViewport(halfW, halfH);
        }

        // ── envelopes ────────────────────────────────────────────────────────────────

        private void AdvanceEnvelopes(float dt)
        {
            float fadeStep = dt / Mathf.Max(0.05f, _fadeSeconds);
            _fade = Mathf.MoveTowards(_fade, _targetLevel > 0f ? 1f : 0f, fadeStep);

            // The level ramps faster than the activation fade: switching Light to Heavy is a
            // change of degree the player just asked for, while turning weather on is an event
            // the world should take a moment to commit to.
            _level = Mathf.MoveTowards(_level, _targetLevel, fadeStep * 2f);
        }

        private void RefreshAmbient(float dt)
        {
            _ambientTimer -= dt;
            if (_ambientTimer > 0f) return;
            _ambientTimer = AmbientTickSeconds;

            var cycle = DayNightCycle.Instance;
            if (cycle == null) { _ambient = Color.white; return; }

            Color c = cycle.CurrentColor;
            _ambient = new Color(
                Mathf.Max(c.r, AmbientChannelFloor),
                Mathf.Max(c.g, AmbientChannelFloor),
                Mathf.Max(c.b, AmbientChannelFloor),
                1f);
        }

        // ── apply ────────────────────────────────────────────────────────────────────

        private void ApplyToLayers()
        {
            float density = Density;

            // The epsilon is what makes the steady state free, but zero has to be exact: a
            // fade that stopped 0.001 short of the threshold would leave every layer emitting
            // a fraction of a particle per second forever after the weather was turned off.
            bool densityMoved = Mathf.Abs(density - _appliedDensity) > 0.0015f
                             || (density <= 0f && _appliedDensity != 0f);
            bool fadeMoved    = Mathf.Abs(_fade - _appliedFade) > 0.0015f;
            bool ambientMoved = Mathf.Abs(_ambient.r - _appliedAmbient.r) > 0.004f
                             || Mathf.Abs(_ambient.g - _appliedAmbient.g) > 0.004f
                             || Mathf.Abs(_ambient.b - _appliedAmbient.b) > 0.004f;

            // Subclasses whose rate is modulated per frame (wind gusts) opt into an
            // unconditional rate write; everything else is free in the steady state.
            bool forceRate = RateIsPerFrame && density > 0.001f;

            if (densityMoved || forceRate)
            {
                float areaScale = ViewportAreaScale();
                for (int i = 0; i < Layers.Count; i++)
                {
                    var layer = Layers[i];
                    float rate = layer.BaseRate * density * RateMultiplier(layer);
                    if (layer.RateScalesWithViewportArea) rate *= areaScale;
                    layer.SetRate(rate);
                }
                _appliedDensity = density;
            }

            if (fadeMoved || ambientMoved)
            {
                for (int i = 0; i < Layers.Count; i++)
                    Layers[i].SetTint(_ambient, _fade);
                _appliedFade    = _fade;
                _appliedAmbient = _ambient;
            }
        }

        /// <summary>
        /// How much bigger the visible world is than the viewport the base rates were authored
        /// against. Only layers that spawn ACROSS the visible area use it — see
        /// <see cref="WeatherLayer.RateScalesWithViewportArea"/>.
        /// </summary>
        private float ViewportAreaScale()
        {
            float area = (_halfW * 2f) * (_halfH * 2f);
            return Mathf.Clamp(area / WeatherLayer.ReferenceArea, 0.25f, 4f);
        }

        private void ApplyAudio()
        {
            if (_audioSrc == null) return;

            // Weather is ambience, so it rides the player's ambient slider rather than SFX.
            var settings = GameSettings.Instance;
            float userVolume = settings != null ? Mathf.Clamp01(settings.ambientVolume) : 1f;
            if (!WeatherManager.AudioEnabled) userVolume = 0f;

            _audioSrc.volume = _audioMasterVolume * userVolume * _fade
                             * Mathf.Lerp(0.55f, 1f, _level) * AudioVolumeMultiplier();
            _audioSrc.pitch  = AudioPitch();

            if (_audioSrc.volume <= 0.0005f && _audioSrc.isPlaying) _audioSrc.Stop();
        }

        // ── subclass extension points ────────────────────────────────────────────────

        /// <summary>
        /// Create and configure every depth slice, through <see cref="CreateLayer"/>. Called
        /// once from Awake, before anything is played.
        /// </summary>
        protected abstract void BuildLayers();

        /// <summary>
        /// Position and size each slice's emitter for the live viewport, and set any lifetime
        /// derived from it. Called on the first frame and again whenever the viewport changes.
        /// </summary>
        protected virtual void LayoutForViewport(float halfW, float halfH) { }

        /// <summary>Per-frame hook for gusts, lightning and anything else time-varying.</summary>
        protected virtual void OnTick(float deltaTime) { }

        /// <summary>Extra per-layer emission multiplier, applied on top of the live density.</summary>
        protected virtual float RateMultiplier(WeatherLayer layer) => 1f;

        /// <summary>
        /// True when <see cref="RateMultiplier"/> varies continuously, so the rate write
        /// cannot be gated on the density having moved.
        /// </summary>
        protected virtual bool RateIsPerFrame => false;

        /// <summary>Playback pitch of the audio bed. Wind rides its gust envelope with it.</summary>
        protected virtual float AudioPitch() => 1f;

        /// <summary>
        /// Extra volume multiplier on the bed, on top of the fade and the level. Wind uses it
        /// so the bed swells with the gust the particles are already swelling with — a bed at
        /// constant level under visibly surging air is the tell that the two are unrelated.
        /// </summary>
        protected virtual float AudioVolumeMultiplier() => 1f;

        /// <summary>The looping bed, or null for a silent weather.</summary>
        protected virtual AudioClip ResolveAudioClip() => null;

    }
}
