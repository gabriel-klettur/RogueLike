using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a single particle effect preset.
    /// Maps to one entry in Python's data/particles/particles.json.
    ///
    /// Create via: Assets > Create > Valkur > Particles > Particle Preset
    /// Or import from Python JSON via: Valkur > Particles > Import Presets from Python JSON
    /// </summary>
    [CreateAssetMenu(fileName = "NewParticlePreset", menuName = "Valkur/Particles/Particle Preset")]
    public class ParticlePresetDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique key matching Python particles.json entry id.")]
        [SerializeField] public string id;

        [Tooltip("Human-readable label. Python: name.")]
        [SerializeField] public string displayName;

        [Tooltip("Category string. Python: type (e.g. 'aura', 'dash', 'explosion').")]
        [SerializeField] public string type;

        [Header("VFX Parameters")]
        [Tooltip("Visual effect configuration. Mirrors Python vfx.particles block.")]
        [SerializeField] public ParticleVfxParams vfx = new ParticleVfxParams();
    }
}
