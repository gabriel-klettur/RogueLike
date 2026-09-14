using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// A named world-generation preset ("Archipielago", "Reino helado"...).
    ///
    /// <para>Only a wrapper: the generator reads <see cref="WorldGenSettings"/>, never this, so
    /// the pipeline stays testable without assets. Saving a preset from the Seed World editor
    /// is phase 2 of <c>.github/SEED_WORLD_ROADMAP.md</c>.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "WorldGenProfile", menuName = "Valkur/World Gen Profile")]
    public sealed class WorldGenProfile : ScriptableObject
    {
        [Tooltip("Shown in the Seed World editor's preset list.")]
        [SerializeField] private string displayName = "Mundo";

        [Tooltip("Every generation parameter of this preset.")]
        [SerializeField] private WorldGenSettings settings = new WorldGenSettings();

        public string DisplayName => displayName;

        /// <summary>A clamped COPY, so editing it never dirties the asset.</summary>
        public WorldGenSettings CopySettings()
        {
            if (settings == null) settings = new WorldGenSettings();
            return settings.Clone() ?? new WorldGenSettings();
        }
    }
}
