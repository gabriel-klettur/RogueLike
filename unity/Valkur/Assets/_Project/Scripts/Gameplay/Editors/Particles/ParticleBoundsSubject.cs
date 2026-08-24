using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// What a resize drag is acting on: either a PRESET asset, or the blocks a placed emitter
    /// is actually running.
    ///
    /// The distinction became load-bearing with copy-on-place. A placement takes a copy of its
    /// preset when it is placed and owns it from then on, so by the time an author grabs a
    /// handle the asset may say something entirely different from the emitter in front of them.
    /// Every ratio a drag computes is taken against a base — the current emission extent, the
    /// current reach, the current lifetime travel — and taking that base from the wrong side
    /// makes the box jump away from the cursor on the first pixel.
    ///
    /// Both shapes answer the same four questions, so the drag arithmetic in
    /// <see cref="ParticleBoundsHandles"/> is written once against this and does not know which
    /// it is holding. The preset shape also keeps the older entry points working, and keeps the
    /// handle tests free of scene objects.
    /// </summary>
    public readonly struct ParticleBoundsSubject
    {
        private readonly ParticlePresetDefinition _preset;
        private readonly IReadOnlyList<ParticleVfxParams> _blocks;

        /// <summary>The instance's scale multiplier; every extent below is measured at it.</summary>
        public readonly float Scale;

        /// <summary>A subject reading a preset asset — a fresh placement, or a preview.</summary>
        public ParticleBoundsSubject(ParticlePresetDefinition preset, float scaleMultiplier)
        {
            _preset = preset;
            _blocks = null;
            Scale = Mathf.Max(0.01f, scaleMultiplier);
        }

        /// <summary>
        /// A subject reading the blocks an emitter is running — root first, then layers, which
        /// is the order <c>ParticleEmitter.EffectiveBlocks</c> reports and the order its systems
        /// are built in.
        /// </summary>
        public ParticleBoundsSubject(IReadOnlyList<ParticleVfxParams> blocks, float scaleMultiplier)
        {
            _preset = null;
            _blocks = blocks;
            Scale = Mathf.Max(0.01f, scaleMultiplier);
        }

        /// <summary>The subject for a placed emitter, whichever side it currently lives on.</summary>
        public static ParticleBoundsSubject Of(ParticleEmitter emitter)
        {
            if (emitter == null) return default;

            return emitter.HasOwnConfig
                ? new ParticleBoundsSubject(emitter.EffectiveBlocks, emitter.ScaleMultiplier)
                : new ParticleBoundsSubject(emitter.Preset, emitter.ScaleMultiplier);
        }

        public bool IsValid => _preset != null || (_blocks != null && _blocks.Count > 0);

        // ── The four questions a drag asks ───────────────────────────────────────

        /// <summary>Swept area under a candidate set of ratios.</summary>
        public ParticleFootprint Reach(ParticleInstanceOverrides overrides)
            => _blocks != null
                ? ParticleFootprint.OfBlocks(WithOverrides(overrides), Scale)
                : ParticleFootprint.Of(_preset, Scale, overrides);

        /// <summary>Emission area under a candidate set of ratios.</summary>
        public ParticleFootprint Emission(ParticleInstanceOverrides overrides)
            => _blocks != null
                ? ParticleFootprint.OfEmissionBlocks(WithOverrides(overrides), Scale)
                : ParticleFootprint.OfEmission(_preset, Scale, overrides);

        /// <summary>Raw, unpadded emission half-extents — the base of an emission drag.</summary>
        public Vector2 EmissionHalfExtents(ParticleInstanceOverrides overrides)
            => _blocks != null
                ? ParticleFootprint.EmissionHalfExtentsOfBlocks(WithOverrides(overrides), Scale)
                : ParticleFootprint.EmissionHalfExtents(_preset, Scale, overrides);

        /// <summary>Conservative lower bound on lifetime travel — what the motion floor reads.</summary>
        public float LifetimeTravel(ParticleInstanceOverrides overrides)
            => _blocks != null
                ? ParticleFootprint.LifetimeTravelOfBlocks(WithOverrides(overrides), Scale)
                : ParticleFootprint.LifetimeTravel(_preset, Scale, overrides);

        /// <summary>Signed position of one edge of the reach box, relative to the emitter.</summary>
        public float EdgePosition(ParticleInstanceOverrides overrides, ParticleBoundsEdge edge)
            => ParticleBoundsHandles.EdgeOf(Reach(overrides), edge);

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The blocks with a candidate set of ratios folded in. Allocates a small list per
        /// call, which a drag does a few dozen times a frame during the reach solve's
        /// bisection — measured against the alternative (threading the ratios through six
        /// footprint entry points) this is the version that stays readable, and a drag is not
        /// a hot loop.
        /// </summary>
        private IReadOnlyList<ParticleVfxParams> WithOverrides(ParticleInstanceOverrides overrides)
        {
            if (overrides.IsDefault) return _blocks;

            var applied = new List<ParticleVfxParams>(_blocks.Count);
            for (int i = 0; i < _blocks.Count; i++)
                applied.Add(ParticleOverrideApplier.Apply(_blocks[i], overrides));
            return applied;
        }
    }
}
