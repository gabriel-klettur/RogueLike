using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Indexed catalog of every <see cref="RecipeDefinition"/> and every
    /// <see cref="ProfessionDefinition"/>. Singleton asset at
    /// <c>Assets/_Project/Resources/Crafting/RecipeCatalog.asset</c>.
    ///
    /// <para>ONE ASSET FOR BOTH, because they are never useful apart: a recipe with no
    /// profession cannot be shown and a profession with no recipes is an empty tab. Keeping
    /// them in two assets would mean two loads, two null checks and two chances for one to
    /// ship without the other.</para>
    ///
    /// <para>WHY IT LIVES UNDER <c>Resources/</c>, which this project otherwise keeps minimal.
    /// <c>CraftingService</c> is reached from a panel and from a station, neither of which is a
    /// scene object anybody wires in the Inspector — the same situation that put
    /// <c>ChatAssignmentCatalog</c> and <c>ProgressionCatalog</c> there. It is loaded from the
    /// <c>Crafting</c> SUBFOLDER, never <c>LoadAll&lt;T&gt;("")</c>, which deserialises all
    /// ~7,400 assets under Resources and logs an error for each one whose script no longer
    /// resolves.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "RecipeCatalog", menuName = "Valkur/Crafting/Catalog")]
    public sealed class RecipeCatalog : ScriptableObject
    {
        /// <summary>Resources-relative path the runtime loads this from.</summary>
        public const string ResourcePath = "Crafting/RecipeCatalog";

        [SerializeField] private List<ProfessionDefinition> _professions = new List<ProfessionDefinition>();
        [SerializeField] private List<RecipeDefinition> _recipes = new List<RecipeDefinition>();

        public IReadOnlyList<ProfessionDefinition> Professions => _professions;
        public IReadOnlyList<RecipeDefinition> Recipes => _recipes;
        public int Count => _recipes?.Count ?? 0;

        [System.NonSerialized] private Dictionary<string, RecipeDefinition> _byId;
        [System.NonSerialized] private Dictionary<string, ProfessionDefinition> _professionByKey;

        private void RebuildLookups()
        {
            _byId = new Dictionary<string, RecipeDefinition>(
                _recipes.Count, System.StringComparer.OrdinalIgnoreCase);
            foreach (var r in _recipes)
            {
                if (r == null || string.IsNullOrEmpty(r.recipeId)) continue;
                _byId[r.recipeId] = r;
            }

            _professionByKey = new Dictionary<string, ProfessionDefinition>(
                _professions.Count, System.StringComparer.OrdinalIgnoreCase);
            foreach (var p in _professions)
            {
                if (p == null || string.IsNullOrEmpty(p.professionKey)) continue;
                _professionByKey[p.professionKey] = p;
            }
        }

        /// <summary>O(1) lookup by stable recipe id. Null if absent.</summary>
        public RecipeDefinition GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_byId == null) RebuildLookups();
            return _byId.TryGetValue(id, out var r) ? r : null;
        }

        /// <summary>O(1) lookup by stable profession key. Null if absent.</summary>
        public ProfessionDefinition GetProfession(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_professionByKey == null) RebuildLookups();
            return _professionByKey.TryGetValue(key, out var p) ? p : null;
        }

        /// <summary>
        /// Every well-formed recipe belonging to <paramref name="profession"/>, in catalog
        /// order. Allocates a list per call and is therefore for PANELS, not for anything that
        /// runs per frame — the panel rebuilds only when a tab changes or the bag does.
        /// </summary>
        public List<RecipeDefinition> RecipesFor(ProfessionDefinition profession)
        {
            var result = new List<RecipeDefinition>();
            if (profession == null) return result;
            for (int i = 0; i < _recipes.Count; i++)
            {
                var r = _recipes[i];
                if (r != null && r.profession == profession && r.IsWellFormed) result.Add(r);
            }
            return result;
        }

        /// <summary>Add or replace a recipe by id — used by the importer and the Skills editor.</summary>
        public void Upsert(RecipeDefinition recipe)
        {
            if (recipe == null || string.IsNullOrEmpty(recipe.recipeId)) return;
            for (int i = 0; i < _recipes.Count; i++)
            {
                if (_recipes[i] != null
                    && string.Equals(_recipes[i].recipeId, recipe.recipeId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    _recipes[i] = recipe;
                    _byId = null;
                    return;
                }
            }
            _recipes.Add(recipe);
            _byId = null;
        }

        /// <summary>Add or replace a profession by key.</summary>
        public void UpsertProfession(ProfessionDefinition profession)
        {
            if (profession == null || string.IsNullOrEmpty(profession.professionKey)) return;
            for (int i = 0; i < _professions.Count; i++)
            {
                if (_professions[i] != null
                    && string.Equals(_professions[i].professionKey, profession.professionKey,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    _professions[i] = profession;
                    _professionByKey = null;
                    return;
                }
            }
            _professions.Add(profession);
            _professionByKey = null;
        }

        /// <summary>Remove a recipe by id; true on success.</summary>
        public bool Remove(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) return false;
            for (int i = 0; i < _recipes.Count; i++)
            {
                if (_recipes[i] != null
                    && string.Equals(_recipes[i].recipeId, recipeId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    _recipes.RemoveAt(i);
                    _byId = null;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Drop nulls and duplicate keys from both lists, then re-sort the tabs.</summary>
        public void Compact()
        {
            var seenRecipes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = _recipes.Count - 1; i >= 0; i--)
            {
                var r = _recipes[i];
                if (r == null || string.IsNullOrEmpty(r.recipeId) || !seenRecipes.Add(r.recipeId))
                    _recipes.RemoveAt(i);
            }

            var seenProfessions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = _professions.Count - 1; i >= 0; i--)
            {
                var p = _professions[i];
                if (p == null || string.IsNullOrEmpty(p.professionKey)
                    || !seenProfessions.Add(p.professionKey))
                    _professions.RemoveAt(i);
            }

            // Sorted here, once, rather than by every reader. A tab strip whose order depended
            // on which panel drew it would be a different strip in the crafting screen and the
            // Skills editor.
            _professions.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));

            _byId = null;
            _professionByKey = null;
        }

        /// <summary>
        /// Every recipe that is missing its output, its profession, or an ingredient.
        ///
        /// <para>Exists so the defect is REPORTABLE rather than merely absent. A recipe whose
        /// reference went null does not throw and does not warn — it simply stops being
        /// craftable, which from inside the game is indistinguishable from a player who has not
        /// found the ingredient yet. The EditMode suite walks this over the shipped catalog.</para>
        /// </summary>
        public List<RecipeDefinition> FindMalformed()
        {
            var bad = new List<RecipeDefinition>();
            for (int i = 0; i < _recipes.Count; i++)
            {
                var r = _recipes[i];
                if (r != null && !r.IsWellFormed) bad.Add(r);
            }
            return bad;
        }
    }
}
