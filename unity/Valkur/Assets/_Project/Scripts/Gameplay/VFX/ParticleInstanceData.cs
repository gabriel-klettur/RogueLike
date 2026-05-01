using System;
using UnityEngine;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Legacy plain-data model for one entry in <c>particles_instances.json</c> (v1 format).
    /// Used by <c>ParticlesEditorWindow</c> (Unity Editor window) for its in-memory list.
    ///
    /// For runtime persistence, prefer <see cref="ParticleInstanceRecord"/> (returned by
    /// <see cref="ParticleInstanceSerializer.Deserialize"/>) and
    /// <see cref="PersistedParticleInstance"/> (attached to spawned GameObjects).
    /// </summary>
    [Serializable]
    public class ParticleInstanceData
    {
        /// <summary>Numeric id (v1 format). Not stable — use <see cref="PersistedParticleInstance.StableGuid"/> instead.</summary>
        public int id;

        /// <summary>Preset key. Python: preset_id.</summary>
        public string preset_id;

        /// <summary>Zone name string linking to ZoneManager. Python: zone.</summary>
        public string zone;

        /// <summary>X pixel offset from zone origin. Python: rel_x.</summary>
        public int rel_x;

        /// <summary>Y pixel offset from zone origin (Pygame Y-down). Python: rel_y.</summary>
        public int rel_y;

        /// <summary>Optional visual scale multiplier. Python: scale_multiplier.</summary>
        public float scale_multiplier = 1f;
    }
}
