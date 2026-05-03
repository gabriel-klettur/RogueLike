using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Catalog of all light presets. Used by WorldLightLoader and the lighting editor.
    /// </summary>
    [CreateAssetMenu(fileName = "LightPresetCatalog", menuName = "Valkur/Lighting/Light Preset Catalog")]
    public class LightPresetCatalog : ScriptableObject
    {
        [Tooltip("All available light presets.")]
        public List<LightPresetDefinition> presets = new List<LightPresetDefinition>();

        private Dictionary<string, LightPresetDefinition> _lookup;

        public LightPresetDefinition GetByKey(string key)
        {
            if (_lookup == null) RebuildLookup();
            _lookup.TryGetValue(key, out var preset);
            return preset;
        }

        public void RebuildLookup()
        {
            _lookup = new Dictionary<string, LightPresetDefinition>();
            foreach (var p in presets)
            {
                if (p != null && !string.IsNullOrEmpty(p.presetKey))
                    _lookup[p.presetKey] = p;
            }
        }

#if UNITY_EDITOR
        private void OnValidate() => _lookup = null;
#endif
    }
}
