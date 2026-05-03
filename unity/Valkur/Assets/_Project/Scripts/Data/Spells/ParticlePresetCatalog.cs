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

        // Lazy O(1) lookup cache, rebuilt on first access or after mutation
        [System.NonSerialized] private Dictionary<string, ParticlePresetDefinition> _lookup;

        private Dictionary<string, ParticlePresetDefinition> Lookup
        {
            get
            {
                if (_lookup == null) RebuildLookup();
                return _lookup;
            }
        }

        private void RebuildLookup()
        {
            _lookup = new Dictionary<string, ParticlePresetDefinition>(presets.Count);
            foreach (var p in presets)
            {
                if (p != null && !string.IsNullOrEmpty(p.id))
                    _lookup[p.id] = p;
            }
        }

        /// <summary>
        /// Look up a preset by its id.  Returns null if not found.
        /// Matches Python's get_preset(preset_id) from particles_config.py.
        /// </summary>
        public ParticlePresetDefinition GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Lookup.TryGetValue(id, out var result) ? result : null;
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
            _lookup = null; // Invalidate cache
        }
    }
}
