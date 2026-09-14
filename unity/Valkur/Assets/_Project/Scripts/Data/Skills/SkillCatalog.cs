using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every skill the game ships — gathering and crafting alike — at
    /// <c>Resources/Skills/SkillCatalog</c>. The order of <see cref="skills"/> is not what the
    /// table draws; <see cref="InCategory"/> is.
    ///
    /// <para>Under <c>Resources/</c> for the reason every tuning asset read by an
    /// <c>AddComponent</c>-ed reader is: <c>PlayerSkills</c> and the skills panel have no
    /// inspector slot to be wired from. It carries the index only; the definitions live in
    /// <c>Data/Catalogs/Skills/</c> and are pulled in by reference.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "SkillCatalog", menuName = "Valkur/Skills/Skill Catalog")]
    public class SkillCatalog : ScriptableObject
    {
        public const string ResourcePath = "Skills/SkillCatalog";

        public List<SkillDefinition> skills = new List<SkillDefinition>();

        public SkillDefinition Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < skills.Count; i++)
                if (skills[i] != null &&
                    string.Equals(skills[i].skillKey, key, System.StringComparison.OrdinalIgnoreCase))
                    return skills[i];
            return null;
        }

        /// <summary>
        /// Every skill in <paramref name="category"/>, by <c>sortOrder</c> then catalog order —
        /// the one ordering the skills table and anything else that lists skills should use.
        /// Allocates: for panels, not per-frame code.
        /// </summary>
        public List<SkillDefinition> InCategory(SkillCategory category)
        {
            var result = new List<SkillDefinition>();
            for (int i = 0; i < skills.Count; i++)
                if (skills[i] != null && skills[i].category == category) result.Add(skills[i]);

            // A stable sort: List.Sort is not, and two skills sharing a sortOrder would otherwise
            // swap places between two openings of the same panel.
            var indexed = new List<KeyValuePair<int, SkillDefinition>>(result.Count);
            for (int i = 0; i < result.Count; i++) indexed.Add(new KeyValuePair<int, SkillDefinition>(i, result[i]));
            indexed.Sort((a, b) =>
            {
                int c = a.Value.sortOrder.CompareTo(b.Value.sortOrder);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            for (int i = 0; i < indexed.Count; i++) result[i] = indexed[i].Value;
            return result;
        }

        // Domain Reload is OFF: a cached asset from the previous Play session may be unloaded.
        private static SkillCatalog _cached;
        private static bool _loadAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _cached = null;
            _loadAttempted = false;
        }

        /// <summary>The shipped catalog, loaded once. Null when the project ships none.</summary>
        public static SkillCatalog Shared
        {
            get
            {
                if (_cached != null) return _cached;
                if (_loadAttempted) return null;
                _loadAttempted = true;
                _cached = Resources.Load<SkillCatalog>(ResourcePath);
                return _cached;
            }
        }
    }
}
