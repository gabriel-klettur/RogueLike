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

        [Tooltip("Light radius in world units. Python radius in px / 16.")]
        public float radius = 5f;

        [Tooltip("Light intensity [0..2].")]
        [Range(0f, 2f)]
        public float intensity = 1f;

        [Tooltip("Falloff exponent. Higher = sharper edge.")]
        public float falloff = 2f;

        [Tooltip("Light color.")]
        public Color color = new Color(1f, 0.78f, 0.55f, 1f);

        [Tooltip("Flicker amplitude [0..1]. 0 = no flicker.")]
        [Range(0f, 1f)]
        public float flickerAmplitude = 0.15f;

        [Tooltip("Flicker speed (Hz).")]
        public float flickerSpeed = 0.75f;

        [Tooltip("Center brightness scale [0..1].")]
        [Range(0f, 1f)]
        public float centerScale = 0.25f;
    }
}
