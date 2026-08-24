using System.Collections.Generic;
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

        [Header("Layers")]
        [Tooltip("Marks this preset as a SUB-LAYER of some composite rather than something " +
                 "placeable on its own. It keeps working exactly as before as a layer, as a " +
                 "spell's preset and as an already-placed instance — the only thing that " +
                 "changes is that the F1 picker gives it no placement tile. Set it on the " +
                 "presets that only ever appear inside another preset's layers list: placing " +
                 "the composite AND one of its layers beside it doubles that layer, and " +
                 "nothing in the UI would say so.")]
        [SerializeField] public bool layerOnly = false;

        [Tooltip("Optional child presets rendered by the same emitter, each as its own ParticleSystem, so one placed instance (or one spell slot) can be a stacked effect — additive light over alpha mass, fast sparks over slow haze. One level deep: a layer's own layers are ignored. Null entries, self-references and lightning-kind layers are skipped. Every layer is scaled by the emitter's scaleMultiplier exactly like the root vfx.")]
        [SerializeField] public List<ParticlePresetDefinition> layers = new List<ParticlePresetDefinition>();
    }
}
