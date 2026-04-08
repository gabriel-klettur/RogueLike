using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Catalog of MonsterDefinition assets for runtime lookup by monsterKey.
    /// Populated by PythonDataMigrator.Monsters (Import Monsters / Import Neutrals).
    /// </summary>
    [CreateAssetMenu(fileName = "MonsterCatalog", menuName = "Valkur/Monster Catalog")]
    public class MonsterCatalog : ScriptableObject
    {
        [SerializeField] private List<MonsterDefinition> definitions = new List<MonsterDefinition>();

        private Dictionary<string, MonsterDefinition> _lookup;

        public IReadOnlyList<MonsterDefinition> Definitions => definitions;

        public MonsterDefinition GetByKey(string monsterKey)
        {
            if (_lookup == null) RebuildLookup();
            _lookup.TryGetValue(monsterKey, out var result);
            return result;
        }

        public void UpsertDefinition(MonsterDefinition def)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null && definitions[i].monsterKey == def.monsterKey)
                {
                    definitions[i] = def;
                    _lookup = null;
                    return;
                }
            }
            definitions.Add(def);
            _lookup = null;
        }

        private void RebuildLookup()
        {
            _lookup = new Dictionary<string, MonsterDefinition>();
            foreach (var d in definitions)
            {
                if (d != null && !string.IsNullOrEmpty(d.monsterKey))
                    _lookup[d.monsterKey] = d;
            }
        }

        private void OnEnable() => _lookup = null;
    }
}
