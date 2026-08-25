using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a light preset (Torch, Lamp, Magic, etc.).
    /// Maps to Python's data/light/presets.json entries.
    /// </summary>
    [CreateAssetMenu(fileName = "NewLightPreset", menuName = "Valkur/Lighting/Light Preset")]
    public class LightPresetDefinition : ScriptableObject
    {
        [Tooltip("Unique preset key matching Python preset name (e.g. 'Torch').")]
        public string presetKey;

        [Tooltip("Outer light radius in PIXELS on the buildings grid (PPU 32). WorldLightLoader " +
                  "divides by 32 to get world units, so 500 px = 15.6 world units. The old tooltip " +
                  "claimed world units and px/16; both were wrong.")]
        public float radius = 160f;

        [Tooltip("Light intensity [0..2].")]
        [Range(0f, 2f)]
        public float intensity = 1f;

        [Tooltip("URP Light2D falloff curve, [0..1]. Unity clamps anything outside that range, " +
                  "so the 1.6-2.2 values this field used to allow all collapsed to an identical 1.0 " +
                  "and the three presets were indistinguishable.")]
        [Range(0f, 1f)]
        public float falloff = 0.8f;

        [Tooltip("Light color.")]
        public Color color = new Color(1f, 0.78f, 0.55f, 1f);

        [Tooltip("Flicker amplitude [0..1]. 0 = no flicker.")]
        [Range(0f, 1f)]
        public float flickerAmplitude = 0.15f;

        [Tooltip("Flicker speed (Hz).")]
        public float flickerSpeed = 0.75f;

        /// <summary>How a light's intensity wobbles. Fire is aperiodic; a magic lantern is not.</summary>
        public enum FlickerStyle
        {
            /// <summary>Two octaves of Perlin noise — aperiodic, reads as a flame.</summary>
            Flame = 0,
            /// <summary>A clean sine — reads as a breathing, enchanted glow.</summary>
            Pulse = 1,
            /// <summary>No wobble at all.</summary>
            Steady = 2,
        }

        [Tooltip("Flame = aperiodic noise (fire). Pulse = clean sine (magic). Steady = no wobble.")]
        public FlickerStyle flickerStyle = FlickerStyle.Flame;

        [Tooltip("Whether this light casts 2D shadows off ShadowCaster2D geometry. " +
                  "OFF by default: the shadow pass — and the per-frame cost of every caster in " +
                  "the world — only exists once some light asks for it.")]
        public bool castsShadows = false;

        [Tooltip("How dark the cast shadow is [0..1]. 1 = fully occluded.")]
        [Range(0f, 1f)]
        public float shadowStrength = 0.75f;

        [Tooltip("Inner radius as a fraction of the outer radius [0..1] — the core that burns at " +
                  "full intensity before the falloff starts. 1.0 means inner == outer, i.e. a hard " +
                  "disc with no gradient at all.")]
        [Range(0f, 0.95f)]
        public float centerScale = 0.25f;
    }
}
