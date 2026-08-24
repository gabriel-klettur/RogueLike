using System;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Identity marker attached to every particle emitter that is owned by the
    /// persistence system (loader or editor).  The stable GUID persists through
    /// renames, reload cycles and undo/redo.
    ///
    /// The presence of this component is the canonical signal that a GameObject
    /// belongs to the loader/editor — preview emitters (PPrev_*) must NOT carry it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersistedParticleInstance : MonoBehaviour
    {
        [SerializeField, Tooltip("Particle preset id used to configure the emitter. Matches ParticlePresetDefinition.id.")]
        private string _presetId;

        [SerializeField, Tooltip("Stable GUID assigned once at spawn. Does not change on rename or reload.")]
        private string _stableGuid;

        [SerializeField, Tooltip("Visual scale multiplier applied when ApplyPreset is called. 1 = default.")]
        private float _scaleMultiplier = 1f;

        [SerializeField, Tooltip("Per-instance size overrides (emission width/height and reach), " +
                                 "as ratios of the preset's own values. All 1 = inherit. Folded " +
                                 "into the owned config once this instance has one.")]
        private ParticleInstanceOverrides _overrides = ParticleInstanceOverrides.None;

        [SerializeField, Tooltip("This placement's OWN particle configuration, copied from the " +
                                 "preset when it was placed. Null only for a record written " +
                                 "before copy-on-place, until the loader snapshots it.")]
        private ParticleInstanceConfig _config;

        // ── Public read-only API ─────────────────────────────────────────────────

        /// <summary>Particle preset id. Matches ParticlePresetDefinition.id.</summary>
        public string PresetId => _presetId;

        /// <summary>Stable GUID string (N format, no hyphens). Assigned once in <see cref="Initialize"/>.</summary>
        public string StableGuid => _stableGuid;

        /// <summary>Visual scale multiplier applied to the emitter.</summary>
        public float ScaleMultiplier => _scaleMultiplier;

        /// <summary>
        /// This placement's size overrides. Separate from <see cref="ScaleMultiplier"/>, which
        /// scales the whole effect uniformly: these resize the emission area per axis and the
        /// reach independently, which is what the F1 drag handles author.
        /// </summary>
        public ParticleInstanceOverrides Overrides => _overrides;

        /// <summary>
        /// This placement's own configuration — the copy of the preset it was born with and has
        /// owned ever since. Editing a preset does not reach it; the editor's "reapply preset"
        /// actions are what overwrite it on request.
        /// </summary>
        public ParticleInstanceConfig Config => _config;

        /// <summary>True once this placement owns its configuration.</summary>
        public bool HasOwnConfig => _config != null && !_config.IsEmpty;

        /// <summary>
        /// Hands this placement a configuration of its own. Does NOT rebuild the emitter — the
        /// caller drives that through <c>ParticleEmitter.ApplyConfig</c>, because a resize or a
        /// property edit updates both and only the emitter knows how to re-apply itself.
        /// </summary>
        public void SetConfig(ParticleInstanceConfig config)
        {
            _config = config;
            // The ratios were folded into the snapshot when it was taken; keeping them would
            // apply them a second time on the next rebuild.
            if (config != null && !config.IsEmpty) _overrides = ParticleInstanceOverrides.None;
        }

        // ── Initialisation ───────────────────────────────────────────────────────

        /// <summary>
        /// Initialises the component with a preset id and scale.
        /// Assigns a new random GUID. Call once immediately after AddComponent.
        /// </summary>
        public void Initialize(string presetId, float scaleMultiplier = 1f)
        {
            _presetId = presetId;
            _scaleMultiplier = scaleMultiplier;
            _overrides = ParticleInstanceOverrides.None;
            _stableGuid = Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Restores identity from persisted data (loader path).
        /// Pass the GUID from the JSON so round-trips preserve it.
        /// </summary>
        public void Restore(string presetId, string stableGuid, float scaleMultiplier = 1f)
            => Restore(presetId, stableGuid, scaleMultiplier, ParticleInstanceOverrides.None);

        /// <summary>Restores identity AND this placement's size overrides (loader path).</summary>
        public void Restore(string presetId, string stableGuid, float scaleMultiplier,
                            ParticleInstanceOverrides overrides)
        {
            _presetId = presetId;
            _stableGuid = string.IsNullOrEmpty(stableGuid)
                ? Guid.NewGuid().ToString("N")
                : stableGuid;
            _scaleMultiplier = scaleMultiplier;
            _overrides = overrides.Sanitized();
        }

        /// <summary>Updates the scale multiplier without changing the GUID or preset id.</summary>
        public void SetScaleMultiplier(float value)
        {
            _scaleMultiplier = value;
        }

        /// <summary>
        /// Records new size overrides on this placement. Does NOT rebuild the emitter — the
        /// caller drives that through <c>ParticleEmitter.SetOverrides</c>, because a drag
        /// updates both and only the emitter knows how to re-apply itself.
        /// </summary>
        public void SetOverrides(ParticleInstanceOverrides overrides)
        {
            _overrides = overrides.Sanitized();
        }
    }
}
