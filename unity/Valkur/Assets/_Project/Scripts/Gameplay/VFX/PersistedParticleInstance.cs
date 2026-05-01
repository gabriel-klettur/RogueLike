using System;
using UnityEngine;

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

        // ── Public read-only API ─────────────────────────────────────────────────

        /// <summary>Particle preset id. Matches ParticlePresetDefinition.id.</summary>
        public string PresetId => _presetId;

        /// <summary>Stable GUID string (N format, no hyphens). Assigned once in <see cref="Initialize"/>.</summary>
        public string StableGuid => _stableGuid;

        /// <summary>Visual scale multiplier applied to the emitter.</summary>
        public float ScaleMultiplier => _scaleMultiplier;

        // ── Initialisation ───────────────────────────────────────────────────────

        /// <summary>
        /// Initialises the component with a preset id and scale.
        /// Assigns a new random GUID. Call once immediately after AddComponent.
        /// </summary>
        public void Initialize(string presetId, float scaleMultiplier = 1f)
        {
            _presetId = presetId;
            _scaleMultiplier = scaleMultiplier;
            _stableGuid = Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Restores identity from persisted data (loader path).
        /// Pass the GUID from the JSON so round-trips preserve it.
        /// </summary>
        public void Restore(string presetId, string stableGuid, float scaleMultiplier = 1f)
        {
            _presetId = presetId;
            _stableGuid = string.IsNullOrEmpty(stableGuid)
                ? Guid.NewGuid().ToString("N")
                : stableGuid;
            _scaleMultiplier = scaleMultiplier;
        }

        /// <summary>Updates the scale multiplier without changing the GUID or preset id.</summary>
        public void SetScaleMultiplier(float value)
        {
            _scaleMultiplier = value;
        }
    }
}
