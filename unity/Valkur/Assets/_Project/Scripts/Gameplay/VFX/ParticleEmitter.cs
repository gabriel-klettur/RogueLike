using System.Collections;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Configures and drives a Unity ParticleSystem based on a ParticlePresetDefinition.
    ///
    /// Place on any GameObject.  A child ParticleSystem is created automatically.
    /// For lightning kind, a LineRenderer is created instead of a ParticleSystem.
    ///
    /// Coordinate conventions:
    ///   All numeric fields in ParticleVfxParams are in Unity world-units / seconds.
    /// </summary>
    public partial class ParticleEmitter : MonoBehaviour
    {
        private const float UNITY_GRAVITY = 9.81f;

        [Header("Preset")]
        [SerializeField, Tooltip("Particle preset to apply. Drives kind, speed, colors, shapes, etc.")]
        private ParticlePresetDefinition _preset;

        [SerializeField, Tooltip("Scale multiplier for sizes and radii.")]
        [Range(0.01f, 10f)]
        private float _scaleMultiplier = 1f;

        [SerializeField, Tooltip("Play automatically on Start.")]
        private bool _playOnAwake = true;

        // Runtime components
        private ParticleSystem _ps;
        private LineRenderer _lr;
        private Coroutine _lightningCoroutine;
        private Coroutine _burstLoopCoroutine;

        // ------------------------------------------------------------------ lifecycle

        private void Start()
        {
            if (_preset != null && _playOnAwake)
                ApplyPreset(_preset, _scaleMultiplier);
        }

        // Resume playback whenever the GameObject is re-enabled (e.g. by the
        // ParticleInstancesLoader's viewport culling). Without this, an emitter
        // that gets SetActive(false) shortly after spawn never plays again when
        // it re-enters the camera frustum — looping presets appear "static".
        private void OnEnable()
        {
            // Play() is idempotent — calling it on an already-playing system is a
            // no-op. We don't gate on _ps.isPlaying because that flag is unreliable
            // right after a SetActive(false→true) cycle.
            if (_ps != null && _preset != null)
            {
                // The child may have been deactivated by a burst's stopAction; Play()
                // would be silently ignored while it is.
                if (!_ps.gameObject.activeSelf) _ps.gameObject.SetActive(true);
                _ps.Play();
            }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Apply a preset, rebuilding the particle system from scratch.
        /// Can be called at runtime to hot-swap the effect.
        /// Maps to Python's ParticlePresetRenderSystem resolving a ParticlePresetComponent.
        /// </summary>
        public void ApplyPreset(ParticlePresetDefinition preset, float scaleMultiplier = 1f)
        {
            _playOnAwake = false; // prevent double-apply when called programmatically before Start()
            _preset = preset;
            _scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);

            string kind = preset.vfx.kind ?? "";

            if (kind == "lightning")
            {
                SetupLightning(preset.vfx);
                return;
            }

            // Leaving the lightning path has to be explicit. AnimateLightning is a
            // while(true) coroutine that keeps re-enabling the LineRenderer forever, so
            // an emitter reused across presets (the editor's preview emitter is reused
            // for every selection) would keep drawing the old bolt on top of every
            // preset chosen afterwards.
            TeardownLightning();

            EnsureParticleSystem();
            // A finished burst sets stopAction = Disable, which deactivates the child
            // holding the ParticleSystem. Play() on an inactive GameObject is a no-op,
            // so without this the emitter is dead for good after its first one-shot.
            if (!_ps.gameObject.activeSelf) _ps.gameObject.SetActive(true);
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureParticleSystem(preset.vfx, _scaleMultiplier);

            if (IsBurstWithInterval(kind) && preset.vfx.burstIntervalSeconds > 0f)
            {
                // Repeating burst (e.g. explosion placed as ambient effect)
                if (_burstLoopCoroutine != null) StopCoroutine(_burstLoopCoroutine);
                _burstLoopCoroutine = StartCoroutine(BurstLoop(preset.vfx.burstIntervalSeconds));
            }
            else
            {
                _ps.Play();
            }
        }

        /// <summary>
        /// Stops new particle emission while letting already-alive particles finish
        /// their natural lifespan (no clear). Used by short-lived "trail" emitters
        /// — e.g. the dash trail emitter that travels from origin to destination
        /// and must stop spawning new dust the instant it arrives, instead of
        /// pooling particles at the endpoint until VFXManager destroys the GO.
        /// Also halts the repeating-burst coroutine if one is running.
        /// </summary>
        public void StopEmitting()
        {
            if (_burstLoopCoroutine != null)
            {
                StopCoroutine(_burstLoopCoroutine);
                _burstLoopCoroutine = null;
            }
            if (_ps != null)
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// Inverse of <see cref="StopEmitting"/>: resume emitting using the preset
        /// already applied. Idempotent — calling on a system that's already playing
        /// is a no-op. For burst-with-interval presets the repeating coroutine is
        /// re-started so the cadence resumes; for plain continuous emitters this
        /// just re-plays the underlying ParticleSystem. Used by long-lived togglers
        /// like <see cref="Valkur.Gameplay.ManaRegenAura"/> that switch the effect
        /// on and off without rebuilding the emitter.
        /// </summary>
        public void StartEmitting()
        {
            if (_ps == null) return;
            if (_preset != null
                && IsBurstWithInterval(_preset.vfx.kind ?? "")
                && _preset.vfx.burstIntervalSeconds > 0f
                && _burstLoopCoroutine == null)
            {
                _burstLoopCoroutine = StartCoroutine(BurstLoop(_preset.vfx.burstIntervalSeconds));
            }
            _ps.Play();
        }

        /// <summary>
        /// Override the underlying ParticleSystem's continuous emission rate.
        /// Used when a preset's authored rate is too low for a short-lived
        /// motion-driven emitter (e.g. the dash trail, which travels start→end
        /// in ~0.18 s — at the preset's stock 10/s only 1-2 particles drop along
        /// the path; bumping the rate while moving gives a continuous wake).
        /// No-op if the ParticleSystem has not been built yet.
        /// </summary>
        public void SetEmissionRate(float ratePerSecond)
        {
            if (_ps == null) return;
            var emission = _ps.emission;
            emission.rateOverTime = Mathf.Max(0f, ratePerSecond);
        }
    }
}
