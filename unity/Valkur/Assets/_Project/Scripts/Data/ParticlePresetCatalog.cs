using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject catalog holding all particle effect presets.
    /// Analog to Python's PARTICLES dict from particles_config.py.
    ///
    /// A single catalog asset lives at:
    ///   Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset
    ///
    /// Reference this from ParticleInstancesLoader and ParticlesEditorWindow
    /// via Resources.Load or SerializeField injection.
    /// </summary>
    [CreateAssetMenu(fileName = "ParticlePresetCatalog", menuName = "Valkur/Particles/Particle Preset Catalog")]
    public class ParticlePresetCatalog : ScriptableObject
    {
        [SerializeField]
        [Tooltip("All particle presets, imported from Python's particles.json.")]
        private List<ParticlePresetDefinition> presets = new List<ParticlePresetDefinition>();

        public IReadOnlyList<ParticlePresetDefinition> Presets => presets;

        /// <summary>
        /// Look up a preset by its id.  Returns null if not found.
        /// Matches Python's get_preset(preset_id) from particles_config.py.
        /// </summary>
        public ParticlePresetDefinition GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in presets)
            {
                if (p != null && p.id == id)
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Editor-only helper to rebuild the catalog from a list of definitions.
        /// </summary>
        public void SetPresets(IEnumerable<ParticlePresetDefinition> defs)
        {
            presets.Clear();
            foreach (var d in defs)
            {
                if (d != null)
                    presets.Add(d);
            }
        }
    }
}
