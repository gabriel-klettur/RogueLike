using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every gathering skill the game ships, at <c>Resources/Gathering/GatheringSkillCatalog</c>.
    ///
    /// <para>Under <c>Resources/</c> for the reason every tuning asset read by an
    /// <c>AddComponent</c>-ed reader is: <c>PlayerGatheringSkills</c> and the skills panel have no
    /// inspector slot to be wired from. It carries the index only; the definitions live in
    /// <c>Data/Catalogs/Gathering/</c> and are pulled in by reference.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "GatheringSkillCatalog", menuName = "Valkur/World/Gathering Skill Catalog")]
    public class GatheringSkillCatalog : ScriptableObject
    {
        public const string ResourcePath = "Gathering/GatheringSkillCatalog";

        public List<GatheringSkillDefinition> skills = new List<GatheringSkillDefinition>();

        public GatheringSkillDefinition Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < skills.Count; i++)
                if (skills[i] != null &&
                    string.Equals(skills[i].skillKey, key, System.StringComparison.OrdinalIgnoreCase))
                    return skills[i];
            return null;
        }

        // Domain Reload is OFF: a cached asset from the previous Play session may be unloaded.
        private static GatheringSkillCatalog _cached;
        private static bool _loadAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _cached = null;
            _loadAttempted = false;
        }

        /// <summary>The shipped catalog, loaded once. Null when the project ships none.</summary>
        public static GatheringSkillCatalog Shared
        {
            get
            {
                if (_cached != null) return _cached;
                if (_loadAttempted) return null;
                _loadAttempted = true;
                _cached = Resources.Load<GatheringSkillCatalog>(ResourcePath);
                return _cached;
            }
        }
    }
}
