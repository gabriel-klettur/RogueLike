using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Project-wide registry of every <see cref="TilesetRuleset"/>. Lookup hub used by
    /// the runtime auto-tile solver and by the F8 Tile Editor's terrain picker.
    /// Singleton asset at <c>Assets/_Project/Data/Catalogs/Tiles/TerrainCatalog.asset</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "TerrainCatalog", menuName = "Valkur/Tiles/Terrain Catalog")]
    public class TerrainCatalog : ScriptableObject
    {
        [SerializeField] private List<TilesetRuleset> rulesets = new List<TilesetRuleset>();

        public IReadOnlyList<TilesetRuleset> Rulesets => rulesets;

        /// <summary>
        /// Returns the highest-priority base ruleset (no secondary terrain) whose
        /// primary matches <paramref name="terrain"/>, or null if none exists.
        /// </summary>
        public TilesetRuleset FindBaseRuleset(string terrain)
        {
            if (string.IsNullOrEmpty(terrain)) return null;
            TilesetRuleset best = null;
            for (int i = 0; i < rulesets.Count; i++)
            {
                var r = rulesets[i];
                if (r == null) continue;
                if (r.IsTransition) continue;
                if (r.TerrainPrimary != terrain) continue;
                if (best == null || r.Priority > best.Priority) best = r;
            }
            return best;
        }

        /// <summary>
        /// Returns a transition ruleset whose two terrains match
        /// <paramref name="terrainA"/> and <paramref name="terrainB"/> in either order,
        /// or null if no such ruleset exists.
        /// </summary>
        public TilesetRuleset FindTransitionRuleset(string terrainA, string terrainB)
        {
            if (string.IsNullOrEmpty(terrainA) || string.IsNullOrEmpty(terrainB)) return null;
            for (int i = 0; i < rulesets.Count; i++)
            {
                var r = rulesets[i];
                if (r == null) continue;
                if (!r.IsTransition) continue;
                bool match = (r.TerrainPrimary == terrainA && r.TerrainSecondary == terrainB)
                          || (r.TerrainPrimary == terrainB && r.TerrainSecondary == terrainA);
                if (match) return r;
            }
            return null;
        }

        /// <summary>
        /// Distinct terrain IDs referenced by any ruleset (base or transition).
        /// Used to populate the "Auto-tile Region" picker chips.
        /// </summary>
        public IEnumerable<string> GetUniqueTerrains()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < rulesets.Count; i++)
            {
                var r = rulesets[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(r.TerrainPrimary)) seen.Add(r.TerrainPrimary);
                if (!string.IsNullOrEmpty(r.TerrainSecondary)) seen.Add(r.TerrainSecondary);
            }
            return seen;
        }

#if UNITY_EDITOR
        public void EditorAdd(TilesetRuleset r)
        {
            if (r == null) return;
            if (rulesets.Contains(r)) return;
            rulesets.Add(r);
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public void EditorRemove(TilesetRuleset r)
        {
            if (r == null) return;
            if (rulesets.Remove(r))
                UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
