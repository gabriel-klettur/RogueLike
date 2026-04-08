using System.Collections;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Configures and drives a Unity ParticleSystem based on a ParticlePresetDefinition.
    ///
    /// Maps to Python's individual emitter systems:
    ///   HealingAuraEmitterSystem, DashEmitterSystem, ArcaneFlameEmitterSystem,
    ///   SlashEmitterSystem, LightningEmitterSystem, LaserBeamEmitterSystem, etc.
    ///
    /// Place on any GameObject.  A child ParticleSystem is created automatically.
    /// For lightning kind, a LineRenderer is created instead of a ParticleSystem.
    ///
    /// Coordinate conventions (inherited from ParticlePresetImporter):
    ///   All numeric fields in ParticleVfxParams are in Unity world-units / seconds.
    /// </summary>
    public partial class ParticleEmitter : MonoBehaviour
    {
        private const float UNITY_GRAVITY = 9.81f;

        [Header("Preset")]
        [SerializeField, Tooltip("Particle preset to apply. Drives kind, speed, colors, shapes, etc.")]
        private ParticlePresetDefinition _preset;

        [SerializeField, Tooltip("Scale multiplier for sizes and radii. Python: scale_multiplier.")]
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

            EnsureParticleSystem();
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
    }
}
